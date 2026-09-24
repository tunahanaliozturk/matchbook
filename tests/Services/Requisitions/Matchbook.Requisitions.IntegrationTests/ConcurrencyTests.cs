using System.Net;
using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Application.Features.Requisitions;
using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Npgsql;

namespace Matchbook.Requisitions.IntegrationTests;

/// <summary>Races and constraints, against the real Postgres the service runs on.</summary>
public sealed class ConcurrencyTests(RequisitionsFixture fixture) : IClassFixture<RequisitionsFixture>
{
    [Fact]
    public async Task Two_approvers_deciding_one_step_at_once_get_one_success_and_one_conflict()
    {
        Actor frank = TestUsers.Stranger(Roles.FinanceApprover);
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita, 50_000m);
        await (await fixture.PostAsync(TestUsers.Mark, pending.Id, "approve")).ReadAsync<RequisitionView>();

        // Hold the requisition's row, so both approvals read the same version and then queue behind the lock.
        // Releasing it lets them write in turn, and the second finds the row changed underneath it. Without
        // the lock the race would depend on timing and could pass by running the two one after the other.
        await using NpgsqlConnection holder = new(fixture.Host.Database);
        await holder.OpenAsync();
        await using NpgsqlTransaction hold = await holder.BeginTransactionAsync();
        await using (NpgsqlCommand lockRow = new("select 1 from requisitions where id = @id for update", holder, hold))
        {
            lockRow.Parameters.AddWithValue("id", pending.Id);
            await lockRow.ExecuteScalarAsync();
        }

        Task<HttpResponseMessage> fiona = fixture.PostAsync(TestUsers.Fiona, pending.Id, "approve");
        Task<HttpResponseMessage> other = fixture.PostAsync(frank, pending.Id, "approve");
        await Eventually.MatchesAsync(WaitingOnLocksAsync, static waiting => waiting == 2, TimeSpan.FromSeconds(15));
        await hold.RollbackAsync();

        HttpResponseMessage[] answers = await Task.WhenAll(fiona, other);

        answers.Select(static answer => answer.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        await answers.Single(static answer => answer.StatusCode == HttpStatusCode.Conflict)
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency.conflict");

        RequisitionView approved = await answers.Single(static answer => answer.IsSuccessStatusCode).ReadAsync<RequisitionView>();
        approved.Status.ShouldBe(RequisitionStatus.Approved);
        approved.Steps.Single(static step => step.Sequence == 2).DecidedBy.ShouldBeOneOf(TestUsers.Fiona.Id, frank.Id);

        await fixture.Probe.WaitForAsync<RequisitionApproved>(message => message.RequisitionId == pending.Id);
        await Task.Delay(TimeSpan.FromSeconds(1));
        fixture.Probe.Received<RequisitionApproved>().Count(message => message.RequisitionId == pending.Id).ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_a_second_decision_by_one_person_even_past_the_domain()
    {
        RequisitionView pending = await fixture.PendingApprovalAsync(TestUsers.Rita, 50_000m);
        await (await fixture.PostAsync(TestUsers.Mark, pending.Id, "approve")).ReadAsync<RequisitionView>();

        await using NpgsqlConnection connection = new(fixture.Host.Database);
        await connection.OpenAsync();
        await using NpgsqlCommand forge = new(
            "update approval_steps set decision = 'Approved', decided_by = @mark, decided_at = now() where requisition_id = @id and sequence = 2",
            connection);
        forge.Parameters.AddWithValue("mark", TestUsers.Mark.Id);
        forge.Parameters.AddWithValue("id", pending.Id);

        PostgresException refusal = await Should.ThrowAsync<PostgresException>(() => forge.ExecuteNonQueryAsync());

        refusal.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        refusal.ConstraintName.ShouldBe("ix_approval_steps_requisition_id_decided_by");
    }

    [Fact]
    public async Task A_line_amount_rounded_any_other_way_cannot_be_stored()
    {
        RequisitionView draft = await fixture.CreateAsync(TestUsers.Rita, 1_000m);

        await using NpgsqlConnection connection = new(fixture.Host.Database);
        await connection.OpenAsync();
        await using NpgsqlCommand forge = new(
            "update requisition_lines set amount = amount + 0.01 where requisition_id = @id and line_number = 1",
            connection);
        forge.Parameters.AddWithValue("id", draft.Id);

        PostgresException refusal = await Should.ThrowAsync<PostgresException>(() => forge.ExecuteNonQueryAsync());

        refusal.ConstraintName.ShouldBe("ck_requisition_lines_amount");
    }

    private async Task<long> WaitingOnLocksAsync()
    {
        await using NpgsqlConnection connection = new(fixture.Host.Database);
        await connection.OpenAsync();
        await using NpgsqlCommand count = new(
            "select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'",
            connection);
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
