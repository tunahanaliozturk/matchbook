using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.UnitTests.Properties;

public sealed class AmountProperties
{
    private static readonly Gen<LineInput> Lines =
        from description in Gen.Elements("Laptop", "Dock", "Monitor", "Chair")
        from thousandths in Gen.Choose(1, 100_000_000)
        from tenThousandths in Gen.Choose(0, int.MaxValue)
        select new LineInput(description, thousandths / 1_000m, "EA", tenThousandths / 10_000m);

    private static readonly Gen<LineInput[]> LineLists =
        Gen.Choose(1, Requisition.MaxLines).SelectMany(static count => Gen.ArrayOf(Lines, count));

    [Property]
    public Property The_amount_is_the_sum_of_the_line_amounts_each_within_half_a_cent_of_quantity_times_price() =>
        Prop.ForAll(LineLists.ToArbitrary(), static lines =>
        {
            Requisition requisition = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(lines), A.Now);

            requisition.Amount.ShouldBe(requisition.Lines.Sum(static line => line.Amount));
            foreach (RequisitionLine line in requisition.Lines)
            {
                decimal.Round(line.Amount, 2).ShouldBe(line.Amount);
                Math.Abs(line.Amount - (line.Quantity * line.UnitPrice)).ShouldBeLessThanOrEqualTo(0.005m);
            }
        });

    [Property]
    public Property Editing_a_draft_to_any_lines_leaves_what_drafting_those_lines_would() =>
        Prop.ForAll(LineLists.ToArbitrary(), LineLists.ToArbitrary(), static (first, second) =>
        {
            Requisition edited = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(first), A.Now);
            edited.Edit(A.Requester, A.Details(second), A.Now);

            Requisition drafted = Requisition.Draft(Guid.CreateVersion7(), 1, A.Requester, A.Details(second), A.Now);

            Snapshot(edited).ShouldBe(Snapshot(drafted));
            edited.Amount.ShouldBe(drafted.Amount);
        });

    private static (int, string, decimal, string, decimal, decimal)[] Snapshot(Requisition requisition) =>
        [.. requisition.Lines
            .OrderBy(static line => line.LineNumber)
            .Select(static line => (line.LineNumber, line.Description, line.Quantity, line.UnitOfMeasure, line.UnitPrice, line.Amount))];
}
