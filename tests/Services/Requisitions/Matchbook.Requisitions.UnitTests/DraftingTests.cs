using Matchbook.Requisitions.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.UnitTests;

public sealed class DraftingTests
{
    [Fact]
    public void A_draft_belongs_to_its_requester_and_waits_as_a_draft()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 42, A.Requester, A.Details(), A.Now);

        requisition.Status.ShouldBe(RequisitionStatus.Draft);
        requisition.RequesterId.ShouldBe(A.Requester.Id);
        requisition.RequesterName.ShouldBe("rita");
        requisition.CreatedAt.ShouldBe(A.Now);
        requisition.FiscalYear.ShouldBeNull();
        requisition.Steps.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(42, "REQ-2026-000042")]
    [InlineData(999_999, "REQ-2026-999999")]
    [InlineData(1_234_567, "REQ-2026-1234567")]
    public void The_number_is_the_year_of_creation_and_the_serial(long serial, string number) =>
        Requisition.Draft(Guid.CreateVersion7(), serial, A.Requester, A.Details(), A.Now).Number.ShouldBe(number);

    [Fact]
    public void The_year_in_the_number_is_the_utc_year()
    {
        DateTimeOffset newYearsEveInNewYork = new(2026, 12, 31, 20, 0, 0, TimeSpan.FromHours(-5));
        RequisitionDetails details = A.Details() with { NeededBy = new DateOnly(2027, 1, 15) };

        Requisition.Draft(Guid.CreateVersion7(), 7, A.Requester, details, newYearsEveInNewYork).Number.ShouldBe("REQ-2027-000007");
    }

    [Fact]
    public void The_amount_is_the_sum_of_the_line_amounts()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(),
            1, A.Requester, A.Details(A.Line(2m, 1_500m), A.Line(3.5m, 12.3456m), A.Line(1m, 0m)), A.Now);

        requisition.Lines.Select(line => line.Amount).ShouldBe([3_000m, 43.21m, 0m]);
        requisition.Amount.ShouldBe(3_043.21m);
    }

    [Fact]
    public void A_line_amount_rounds_half_away_from_zero()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(A.Line(1m, 0.125m)), A.Now);

        requisition.Amount.ShouldBe(0.13m);
    }

    [Fact]
    public void Lines_are_numbered_from_one_in_the_order_given()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(),
            1, A.Requester, A.Details(A.Line(description: "Laptop"), A.Line(description: "Dock"), A.Line(description: "Bag")), A.Now);

        requisition.Lines.Select(line => (line.LineNumber, line.Description)).ShouldBe([(1, "Laptop"), (2, "Dock"), (3, "Bag")]);
    }

    [Fact]
    public void Text_is_trimmed_and_the_cost_centre_code_upper_cased()
    {
        RequisitionDetails details = A.Details(new LineInput("  Laptop  ", 1m, " EA ", 10m)) with
        {
            CostCentreCode = " eng-platform ",
            Justification = "  Two new engineers  ",
        };

        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, details, A.Now);

        requisition.CostCentreCode.ShouldBe("ENG-PLATFORM");
        requisition.Justification.ShouldBe("Two new engineers");
        requisition.Lines[0].Description.ShouldBe("Laptop");
        requisition.Lines[0].UnitOfMeasure.ShouldBe("EA");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Requisition.MaxLines)]
    public void A_requisition_takes_from_one_to_fifty_lines(int count)
    {
        LineInput[] lines = [.. Enumerable.Repeat(A.Line(), count)];

        Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(lines), A.Now).Lines.Count.ShouldBe(count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Requisition.MaxLines + 1)]
    public void A_requisition_with_no_lines_or_more_than_fifty_is_refused(int count)
    {
        RequisitionDetails details = A.Details() with { Lines = [.. Enumerable.Repeat(A.Line(), count)] };

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, details, A.Now), RequisitionCodes.LineCountInvalid, ViolationKind.Invalid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0005")]
    [InlineData("1000000000000000")]
    public void A_quantity_must_be_positive_with_at_most_three_decimals(string quantity)
    {
        LineInput line = A.Line(quantity: decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture));

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(A.Line(), line), A.Now), RequisitionCodes.LineInvalid, ViolationKind.Invalid)
            .Message.ShouldContain("Line 2");
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.00001")]
    [InlineData("100000000000000")]
    public void A_unit_price_must_be_zero_or_more_with_at_most_four_decimals(string unitPrice)
    {
        LineInput line = A.Line(unitPrice: decimal.Parse(unitPrice, System.Globalization.CultureInfo.InvariantCulture));

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(line), A.Now), RequisitionCodes.LineInvalid, ViolationKind.Invalid);
    }

    [Fact]
    public void The_finest_quantity_and_price_the_columns_hold_are_accepted()
    {
        Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(A.Line(0.001m, 0.0001m)), A.Now);

        requisition.Lines[0].Quantity.ShouldBe(0.001m);
        requisition.Lines[0].UnitPrice.ShouldBe(0.0001m);
        requisition.Amount.ShouldBe(0m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_line_needs_a_description(string description) =>
        A.Refused(
            () => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(A.Line(description: description)), A.Now),
            RequisitionCodes.LineInvalid,
            ViolationKind.Invalid);

    [Fact]
    public void A_description_longer_than_the_column_is_refused()
    {
        LineInput line = A.Line(description: new string('x', RequisitionLine.MaxDescriptionLength + 1));

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(line), A.Now), RequisitionCodes.LineInvalid, ViolationKind.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A unit of measure that is far too long")]
    public void A_line_needs_a_short_unit_of_measure(string unitOfMeasure) =>
        A.Refused(
            () => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(new LineInput("Laptop", 1m, unitOfMeasure, 10m)), A.Now),
            RequisitionCodes.LineInvalid,
            ViolationKind.Invalid);

    [Fact]
    public void A_line_too_large_for_the_amount_column_is_refused_rather_than_overflowing()
    {
        LineInput line = A.Line(RequisitionLine.MaxQuantity, RequisitionLine.MaxUnitPrice);

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(line), A.Now), RequisitionCodes.AmountTooLarge, ViolationKind.Invalid);
    }

    [Fact]
    public void Lines_that_together_exceed_the_amount_column_are_refused()
    {
        // Each line comes to 10^15, which fits; fifty of them do not.
        LineInput[] lines = [.. Enumerable.Repeat(A.Line(1_000m, 1_000_000_000_000m), Requisition.MaxLines)];

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(lines), A.Now), RequisitionCodes.AmountTooLarge, ViolationKind.Invalid);
    }

    [Fact]
    public void A_needed_by_date_that_has_passed_is_refused()
    {
        RequisitionDetails details = A.Details() with { NeededBy = DateOnly.FromDateTime(A.Now.UtcDateTime).AddDays(-1) };

        A.Refused(() => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, details, A.Now), RequisitionCodes.NeededByInPast, ViolationKind.Invalid);
    }

    [Fact]
    public void Needed_today_is_accepted()
    {
        RequisitionDetails details = A.Details() with { NeededBy = DateOnly.FromDateTime(A.Now.UtcDateTime) };

        Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, details, A.Now).NeededBy.ShouldBe(details.NeededBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ENG-PLATFORM-AND-MORE")]
    public void A_requisition_needs_a_cost_centre_code(string code) =>
        A.Refused(
            () => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details() with { CostCentreCode = code }, A.Now),
            RequisitionCodes.CostCentreInvalid,
            ViolationKind.Invalid);

    [Fact]
    public void A_requisition_needs_a_supplier() =>
        A.Refused(
            () => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details() with { SupplierId = Guid.Empty }, A.Now),
            RequisitionCodes.SupplierInvalid,
            ViolationKind.Invalid);

    [Theory]
    [InlineData(0)]
    [InlineData(Requisition.MaxJustificationLength + 1)]
    public void A_requisition_needs_a_justification_that_fits(int length) =>
        A.Refused(
            () => Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details() with { Justification = new string('x', length) }, A.Now),
            RequisitionCodes.JustificationInvalid,
            ViolationKind.Invalid);
}
