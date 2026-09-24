using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.UnitTests;

public sealed class MovementTests
{
    [Fact]
    public void A_reservation_consumes_its_amount() => Movement.Reserve(120m).Consumes.ShouldBe(120m);

    [Fact]
    public void A_commitment_consumes_only_what_the_order_adds_to_its_reservation()
    {
        Movement.Commit(reservationHeld: 800m, orderAmount: 1_000m).Consumes.ShouldBe(200m);
        Movement.Commit(reservationHeld: 1_000m, orderAmount: 800m).Consumes.ShouldBe(-200m);
    }

    [Fact]
    public void An_invoice_consumes_only_its_excess_over_the_relieved_commitment()
    {
        Movement.Invoice(relief: 500m, amount: 500m).Consumes.ShouldBe(0m);
        Movement.Invoice(relief: 500m, amount: 530m).Consumes.ShouldBe(30m);
    }

    [Fact]
    public void Raising_the_allotment_frees_funds() => Movement.Allot(300m).Consumes.ShouldBe(-300m);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    public void A_reservation_needs_a_positive_amount_in_cents(string amount) =>
        new Func<Movement>(() => Movement.Reserve(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)))
            .ShouldBreak("budget.amount_invalid", ViolationKind.Invalid);

    [Fact]
    public void A_commitment_needs_a_positive_order_amount() =>
        new Func<Movement>(() => Movement.Commit(reservationHeld: 100m, orderAmount: 0m))
            .ShouldBreak("budget.amount_invalid", ViolationKind.Invalid);

    [Fact]
    public void Amounts_up_to_the_column_limit_are_accepted_and_beyond_it_refused()
    {
        Money.Positive(9_999_999_999_999_999.99m, "Amount").ShouldBe(9_999_999_999_999_999.99m);
        new Func<decimal>(() => Money.Positive(10_000_000_000_000_000m, "Amount"))
            .ShouldBreak("budget.amount_invalid", ViolationKind.Invalid);
    }

    [Fact]
    public void A_ledger_entry_stores_its_times_in_utc()
    {
        var local = new DateTimeOffset(2026, 3, 2, 12, 30, 0, TimeSpan.FromHours(3));

        LedgerEntry entry = LedgerEntry.Record(
            Some.BudgetId, Guid.CreateVersion7(), LedgerStep.Reserve, Movement.Reserve(1m), local, local);

        entry.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        entry.RecordedAt.Offset.ShouldBe(TimeSpan.Zero);
        entry.OccurredAt.ShouldBe(local);
    }
}
