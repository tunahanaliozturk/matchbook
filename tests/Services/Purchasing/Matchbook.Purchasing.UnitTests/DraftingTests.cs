using Matchbook.Purchasing.Domain;
using Matchbook.SharedKernel;
using static Matchbook.Purchasing.UnitTests.Refusal;

namespace Matchbook.Purchasing.UnitTests;

public sealed class DraftingTests
{
    [Fact]
    public void An_approved_requisition_becomes_a_draft_with_its_lines_and_amount()
    {
        ApprovedRequisition requisition = Orders.Requisition(Orders.TwoLines);

        PurchaseOrder order = PurchaseOrder.Draft(requisition, 42, Orders.Now);

        order.Status.ShouldBe(PurchaseOrderStatus.Draft);
        order.RequisitionId.ShouldBe(requisition.RequisitionId);
        order.SupplierId.ShouldBe(requisition.SupplierId);
        order.CostCentreCode.ShouldBe("ENG-PLATFORM");
        order.FiscalYear.ShouldBe(2026);
        order.DraftedAt.ShouldBe(Orders.Now);
        order.CommitmentAttempt.ShouldBe(0);
        order.IssuedBy.ShouldBeNull();
        order.Lines.Select(static line => (line.LineNumber, line.Quantity, line.UnitPrice, line.Amount))
            .ShouldBe([(1, 10m, 12.50m, 125.00m), (2, 4m, 99.99m, 399.96m)]);
        order.Lines.ShouldAllBe(static line => line.ReceivedQuantity == 0 && line.InvoicedQuantity == 0);
        order.Amount.ShouldBe(524.96m);
    }

    [Theory]
    [InlineData(42, "PO-2026-000042")]
    [InlineData(999_999, "PO-2026-999999")]
    [InlineData(1_000_000, "PO-2026-1000000")]
    public void The_number_is_the_year_drafted_and_the_sequence_in_at_least_six_digits(long sequence, string number) =>
        PurchaseOrder.Draft(Orders.Requisition(Orders.TwoLines), sequence, Orders.Now).Number.ShouldBe(number);

    [Fact]
    public void The_year_in_the_number_is_the_utc_year_whatever_offset_the_clock_reports()
    {
        // 23:30 on New Year's Eve two hours west of Greenwich is already 2027 in UTC.
        DateTimeOffset lateOnNewYearsEve = new(2026, 12, 31, 23, 30, 0, TimeSpan.FromHours(-2));

        PurchaseOrder.Draft(Orders.Requisition(Orders.TwoLines), 1, lateOnNewYearsEve).Number.ShouldBe("PO-2027-000001");
    }

    [Fact]
    public void A_line_amount_is_rounded_half_away_from_zero_like_every_other_service()
    {
        PurchaseOrder order = Orders.Draft((1m, 0.125m));

        order.Line(1).Amount.ShouldBe(0.13m);
        order.Amount.ShouldBe(Amounts.Line(1m, 0.125m));
    }

    [Fact]
    public void A_requisition_without_lines_cannot_be_drafted() =>
        ShouldBeRefused(
            () => PurchaseOrder.Draft(Orders.Requisition(), 1, Orders.Now),
            "purchase_order.no_lines",
            ViolationKind.Invalid);

    [Fact]
    public void A_line_number_may_appear_only_once()
    {
        ApprovedRequisition requisition = Orders.Requisition(Orders.TwoLines) with
        {
            Lines = [new DraftLine(1, "Pens", 1m, "box", 2m), new DraftLine(1, "Paper", 1m, "ream", 4m)],
        };

        ShouldBeRefused(
            () => PurchaseOrder.Draft(requisition, 1, Orders.Now), "purchase_order.duplicate_line", ViolationKind.Invalid);
    }

    [Theory]
    [InlineData(0, "Pens", "box", 1, 1, "purchase_order.invalid_line_number")]
    [InlineData(1, " ", "box", 1, 1, "purchase_order.description_required")]
    [InlineData(1, "Pens", "", 1, 1, "purchase_order.unit_of_measure_required")]
    [InlineData(1, "Pens", "box", -1, 1, "purchase_order.negative_quantity")]
    [InlineData(1, "Pens", "box", 1.0001, 1, "purchase_order.quantity_precision")]
    [InlineData(1, "Pens", "box", 1, -0.01, "purchase_order.negative_unit_price")]
    [InlineData(1, "Pens", "box", 1, 0.00001, "purchase_order.unit_price_precision")]
    public void A_line_that_breaks_a_rule_is_refused(
        int lineNumber, string description, string unitOfMeasure, decimal quantity, decimal unitPrice, string code)
    {
        ApprovedRequisition requisition = Orders.Requisition(Orders.TwoLines) with
        {
            Lines = [new DraftLine(lineNumber, description, quantity, unitOfMeasure, unitPrice)],
        };

        ShouldBeRefused(() => PurchaseOrder.Draft(requisition, 1, Orders.Now), code, ViolationKind.Invalid);
    }

    [Fact]
    public void Lines_whose_total_would_not_fit_the_amount_column_are_refused()
    {
        // Each line fits numeric(18,2) on its own; together they do not.
        ShouldBeRefused(
            () => Orders.Draft((600_000_000m, 10_000_000m), (600_000_000m, 10_000_000m)),
            "purchase_order.amount_too_large",
            ViolationKind.Invalid);
    }
}
