using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

public sealed class AmendingTests
{
    [Fact]
    public void A_buyer_can_change_the_quantity_and_price_of_a_draft_line()
    {
        PurchaseOrder order = Orders.Draft();

        order.AmendLine(People.Bruno, 2, 6m, 89.5m);

        order.Line(2).Quantity.ShouldBe(6m);
        order.Line(2).UnitPrice.ShouldBe(89.5m);
        order.Line(2).Amount.ShouldBe(537.00m);
        order.Amount.ShouldBe(125.00m + 537.00m);
    }

    [Fact]
    public void A_line_set_to_zero_stays_on_the_order_and_can_be_brought_back()
    {
        PurchaseOrder order = Orders.Draft();

        order.AmendLine(People.Bruno, 1, 0m, 12.50m);

        order.Lines.Count.ShouldBe(2);
        order.Line(1).Amount.ShouldBe(0m);
        order.Amount.ShouldBe(399.96m);

        order.AmendLine(People.Bruno, 1, 10m, 12.50m);

        order.Amount.ShouldBe(524.96m);
    }

    [Theory]
    [InlineData(-1, 1, "purchase_order.negative_quantity")]
    [InlineData(1.0005, 1, "purchase_order.quantity_precision")]
    [InlineData(1, -1, "purchase_order.negative_unit_price")]
    [InlineData(1, 1.23456, "purchase_order.unit_price_precision")]
    public void A_value_below_zero_or_finer_than_its_column_is_refused_and_changes_nothing(
        decimal quantity, decimal unitPrice, string code)
    {
        PurchaseOrder order = Orders.Draft();

        ShouldBeRefused(() => order.AmendLine(People.Bruno, 1, quantity, unitPrice), code, ViolationKind.Invalid);

        order.Line(1).Quantity.ShouldBe(10m);
        order.Line(1).UnitPrice.ShouldBe(12.50m);
        order.Amount.ShouldBe(524.96m);
    }

    [Fact]
    public void A_line_amount_too_large_for_its_column_is_refused_without_overflowing()
    {
        PurchaseOrder order = Orders.Draft();

        // The largest values each column holds: their product does not fit a decimal, let alone numeric(18,2).
        ShouldBeRefused(
            () => order.AmendLine(People.Bruno, 1, 999_999_999_999_999.999m, 99_999_999_999_999.9999m),
            "purchase_order.amount_too_large",
            ViolationKind.Invalid);
    }

    [Fact]
    public void An_amendment_that_would_take_the_order_total_past_its_column_is_refused_and_changes_nothing()
    {
        PurchaseOrder order = Orders.Draft((600_000_000m, 10_000_000m), (1m, 1m));

        ShouldBeRefused(
            () => order.AmendLine(People.Bruno, 2, 600_000_000m, 10_000_000m),
            "purchase_order.amount_too_large",
            ViolationKind.Invalid);

        order.Line(2).Amount.ShouldBe(1m);
    }

    [Fact]
    public void An_unknown_line_is_refused() =>
        ShouldBeRefused(
            () => Orders.Draft().AmendLine(People.Bruno, 3, 1m, 1m), "purchase_order.unknown_line", ViolationKind.Invalid);

    [Fact]
    public void Only_a_buyer_may_amend_a_draft()
    {
        ShouldBeRefused(
            () => Orders.Draft().AmendLine(People.Rosa, 1, 1m, 1m), "purchase_order.not_a_buyer", ViolationKind.Forbidden);
        ShouldBeRefused(
            () => Orders.Draft().AmendLine(People.Audrey, 1, 1m, 1m), "purchase_order.not_a_buyer", ViolationKind.Forbidden);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("issued")]
    [InlineData("short-closed")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    public void Only_a_draft_can_be_amended(string state) =>
        ShouldBeRefused(
            () => Orders.InState(state).AmendLine(People.Bruno, 1, 1m, 1m), "purchase_order.not_draft", ViolationKind.Conflict);
}
