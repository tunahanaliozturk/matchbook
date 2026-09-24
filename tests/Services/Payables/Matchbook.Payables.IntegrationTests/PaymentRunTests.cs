using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Matchbook.Contracts.Payables;
using Matchbook.Payables.Application.Features.Invoices;
using Matchbook.Payables.Application.Features.PaymentRuns;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// Paying at most once, against real Postgres. Every test in the class pays one supplier, so whatever an earlier test
/// left payable can join a later run without changing what these tests check.
/// </summary>
public sealed class PaymentRunTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>, IAsyncLifetime
{
    private const string SupplierIban = "GB82WEST12345698765432";
    private static readonly XNamespace Pain = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    private readonly Scenario _given = new(fixture);
    private Guid _supplier;

    public async ValueTask InitializeAsync() => _supplier = await _given.SupplierAsync(SupplierIban, id: SupplierId);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Fixed, so every test's SupplierAsync after the first is a redelivery of the same snapshot.
    private static readonly Guid SupplierId = Guid.Parse("5c000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Two_treasurers_drafting_at_the_same_moment_never_put_one_invoice_in_both_runs()
    {
        InvoiceView[] invoices = [await PayableAsync(100m), await PayableAsync(200m), await PayableAsync(300m)];

        // Both drafts have read the same payable invoices when the lock goes; they meet at the index on active items.
        HttpResponseMessage[] responses = await Race.AtAsync(
            _given.Host.Database,
            "lock table payment_run_items in share row exclusive mode",
            () => _given.PostDraftAsync(TestUsers.Tess, Scenario.Today),
            () => _given.PostDraftAsync(TestUsers.Trevor, Scenario.Today));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        await responses.Single(response => response.StatusCode != HttpStatusCode.Created)
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payment_run.invoice_taken");

        Guid[] ids = [.. invoices.Select(invoice => invoice.Id)];
        List<Guid> runsHoldingThem = await _given.DbAsync(db => db.PaymentRunItems
            .Where(item => ids.Contains(item.InvoiceId))
            .Select(item => item.PaymentRunId)
            .ToListAsync());
        runsHoldingThem.Count.ShouldBe(3);
        runsHoldingThem.Distinct().ShouldHaveSingleItem();
        foreach (InvoiceView invoice in invoices)
        {
            (await _given.InvoiceAsync(invoice.Id)).Status.ShouldBe(InvoiceStatus.Scheduled);
        }
    }

    [Fact]
    public async Task The_treasurer_who_drafted_a_run_cannot_release_it()
    {
        await PayableAsync(50m);
        PaymentRunView run = await _given.DraftAsync(TestUsers.Tess);

        await (await _given.PostReleaseAsync(TestUsers.Tess, run.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "payment_run.same_treasurer");

        await _given.ReleaseAsync(TestUsers.Trevor, run.Id);
    }

    [Fact]
    public async Task Releasing_a_run_twice_at_once_pays_each_invoice_once_and_the_file_carries_exactly_those_payments()
    {
        InvoiceView[] invoices = [await PayableAsync(1234.56m, "RE-2026/001"), await PayableAsync(99.99m, "RE-2026/002")];
        PaymentRunView draft = await _given.DraftAsync(TestUsers.Tess);

        // Both releases have read the draft when the lock goes; they meet at the run's row version.
        HttpResponseMessage[] responses = await Race.AtAsync(
            _given.Host.Database,
            $"select 1 from payment_runs where id = '{draft.Id}' for update",
            () => _given.PostReleaseAsync(TestUsers.Trevor, draft.Id),
            () => _given.PostReleaseAsync(TestUsers.Trevor, draft.Id));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        await responses.Single(response => response.StatusCode != HttpStatusCode.OK)
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency.conflict");

        foreach (InvoiceView invoice in invoices)
        {
            await _given.Probe.WaitForAsync<InvoicePaid>(paid => paid.InvoiceId == invoice.Id);
            (await _given.InvoiceAsync(invoice.Id)).Status.ShouldBe(InvoiceStatus.Paid);
        }

        // A second payment would be published at the same moment as the first; a short wait shows there was none.
        await Task.Delay(TimeSpan.FromSeconds(2));
        foreach (InvoiceView invoice in invoices)
        {
            _given.Probe.Received<InvoicePaid>().Where(paid => paid.InvoiceId == invoice.Id)
                .ShouldHaveSingleItem().Amount.ShouldBe(invoice.Total);
        }

        await (await _given.PostReleaseAsync(TestUsers.Trevor, draft.Id)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "payment_run.not_draft");

        await ShouldCarryExactlyTheRunAsync(draft.Id);
    }

    [Fact]
    public async Task Only_treasurers_download_the_bank_file_and_only_once_the_run_is_released()
    {
        await PayableAsync(10m);
        PaymentRunView draft = await _given.DraftAsync(TestUsers.Tess);
        var file = new Uri($"/payment-runs/{draft.Id}/file", UriKind.Relative);

        await (await _given.As(TestUsers.Trevor).GetAsync(file)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "payment_run.not_released");
        await _given.ReleaseAsync(TestUsers.Trevor, draft.Id);

        (await _given.As(TestUsers.Alice).GetAsync(file)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _given.As(TestUsers.Audrey).GetAsync(file)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        HttpResponseMessage download = await _given.As(TestUsers.Tess).GetAsync(file);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        download.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"pain001-{draft.Id:N}.xml");
    }

    [Fact]
    public async Task No_account_number_is_stored_in_the_clear_anywhere_in_the_database()
    {
        await PayableAsync(75m);
        PaymentRunView run = await _given.ReleaseAsync(TestUsers.Trevor, (await _given.DraftAsync(TestUsers.Tess)).Id);
        await ShouldCarryExactlyTheRunAsync(run.Id);

        await using var connection = new NpgsqlConnection(_given.Host.Database);
        await connection.OpenAsync();

        // Every text column of every table, the outbox included: the IBAN in the clear appears in none of them.
        var columns = new List<(string Table, string Column)>();
        await using (var list = new NpgsqlCommand(
            "select table_name, column_name from information_schema.columns " +
            "where table_schema = 'public' and data_type in ('text', 'character varying')",
            connection))
        await using (NpgsqlDataReader reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        columns.ShouldContain(("suppliers", "account_protected_iban"));
        foreach ((string table, string column) in columns)
        {
            await using var search = new NpgsqlCommand($"select count(*) from \"{table}\" where \"{column}\" like @iban", connection);
            search.Parameters.AddWithValue("iban", $"%{SupplierIban}%");
            ((long)(await search.ExecuteScalarAsync())!).ShouldBe(0, $"{table}.{column}");
        }

        await using var stored = new NpgsqlCommand(
            "select account_protected_iban from suppliers union all select protected_iban from payment_run_creditors",
            connection);
        await using NpgsqlDataReader values = await stored.ExecuteReaderAsync();
        while (await values.ReadAsync())
        {
            values.GetString(0).ShouldStartWith("v1.test.");
        }
    }

    [Fact]
    public async Task A_draft_repeated_with_its_id_returns_the_same_run_and_a_cancelled_run_frees_its_invoices()
    {
        InvoiceView invoice = await PayableAsync(42m);
        Guid runId = Guid.CreateVersion7();

        PaymentRunView first = await _given.DraftAsync(TestUsers.Tess, runId);
        PaymentRunView repeated = await _given.DraftAsync(TestUsers.Tess, runId);

        repeated.Id.ShouldBe(first.Id);
        repeated.ItemCount.ShouldBe(first.ItemCount);
        await (await _given.PostDraftAsync(TestUsers.Tess, Scenario.Today.AddDays(1), runId))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "request.id_reused");
        (await _given.InvoiceAsync(invoice.Id)).Status.ShouldBe(InvoiceStatus.Scheduled);

        PaymentRunView cancelled = await Scenario.ReadAsync<PaymentRunView>(
            await _given.As(TestUsers.Tess).PostAsync(new Uri($"/payment-runs/{runId}/cancel", UriKind.Relative), null),
            HttpStatusCode.OK);

        cancelled.Status.ShouldBe(PaymentRunStatus.Cancelled);
        (await _given.InvoiceAsync(invoice.Id)).Status.ShouldBe(InvoiceStatus.Payable);
        await (await _given.PostReleaseAsync(TestUsers.Trevor, runId)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "payment_run.not_draft");
    }

    private Task<InvoiceView> PayableAsync(decimal amount, string? number = null) =>
        _given.PayableInvoiceAsync(_supplier, amount, number);

    /// <summary>Downloads the run's file and checks it against the run and the invoices it pays.</summary>
    private async Task ShouldCarryExactlyTheRunAsync(Guid runId)
    {
        PaymentRunView run = (await _given.As(TestUsers.Audrey).GetFromJsonAsync<PaymentRunView>(
            new Uri($"/payment-runs/{runId}", UriKind.Relative), Scenario.Json))!;
        HttpResponseMessage response = await _given.As(TestUsers.Trevor).GetAsync(new Uri($"/payment-runs/{runId}/file", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        XDocument file = XDocument.Load(await response.Content.ReadAsStreamAsync());

        XElement payment = file.Descendants(Pain + "PmtInf").Single();
        XElement[] transactions = [.. payment.Elements(Pain + "CdtTrfTxInf")];
        transactions.Length.ShouldBe(run.PaidCount);
        payment.Element(Pain + "NbOfTxs")!.Value.ShouldBe(run.PaidCount.ToString(CultureInfo.InvariantCulture));
        decimal.Parse(payment.Element(Pain + "CtrlSum")!.Value, CultureInfo.InvariantCulture).ShouldBe(run.PaidTotal);
        payment.Element(Pain + "DbtrAcct")!.Value.ShouldBe(PayablesFixture.PayerIban);

        List<PaymentRunItem> items = await _given.DbAsync(db => db.PaymentRunItems
            .AsNoTracking()
            .Where(item => item.PaymentRunId == runId && item.Status == PaymentRunItemStatus.Paid)
            .ToListAsync());
        foreach (XElement transaction in transactions)
        {
            string endToEnd = transaction.Descendants(Pain + "EndToEndId").Single().Value;
            PaymentRunItem item = items.Single(candidate => candidate.InvoiceId.ToString("N") == endToEnd);
            InvoiceView invoice = await _given.InvoiceAsync(item.InvoiceId);

            decimal.Parse(transaction.Descendants(Pain + "InstdAmt").Single().Value, CultureInfo.InvariantCulture).ShouldBe(invoice.Total);
            transaction.Descendants(Pain + "Ustrd").Single().Value.ShouldBe(invoice.SupplierInvoiceNumber);
            transaction.Element(Pain + "CdtrAcct")!.Value.ShouldBe(SupplierIban);
            invoice.Status.ShouldBe(InvoiceStatus.Paid);
        }
    }
}
