using Matchbook.Budgets.Domain;
using Matchbook.SharedKernel;

namespace Matchbook.Budgets.UnitTests;

public sealed class CostCentreTests
{
    [Fact]
    public void A_new_cost_centre_is_active_at_version_one()
    {
        CostCentre costCentre = CostCentre.Create("ENG-PLATFORM", "  Platform engineering ", Some.Manager);

        costCentre.IsActive.ShouldBeTrue();
        costCentre.Version.ShouldBe(1);
        costCentre.Name.ShouldBe("Platform engineering");
    }

    [Theory]
    [InlineData("EN-01")]
    [InlineData("ENGIN-ABCDEFGHIJ12")]
    [InlineData("MKT-GROWTH")]
    public void Codes_at_the_edges_of_the_pattern_are_accepted(string code) =>
        CostCentre.Create(code, "Name", Some.Manager).Code.ShouldBe(code);

    [Theory]
    [InlineData("eng-platform")]
    [InlineData("E-PLATFORM")]
    [InlineData("ENGINE-PLATFORM")]
    [InlineData("ENG-P")]
    [InlineData("ENG-ABCDEFGHIJKLM")]
    [InlineData("ENG_PLATFORM")]
    [InlineData(" ENG-PLATFORM")]
    [InlineData("ENG-PLATFORM\n")]
    [InlineData("")]
    public void Codes_off_the_pattern_are_refused(string code) =>
        new Func<CostCentre>(() => CostCentre.Create(code, "Name", Some.Manager))
            .ShouldBreak("cost_centre.code_invalid", ViolationKind.Invalid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name) =>
        new Func<CostCentre>(() => CostCentre.Create("ENG-OPS", name, Some.Manager))
            .ShouldBreak("cost_centre.name_invalid", ViolationKind.Invalid);

    [Fact]
    public void A_name_longer_than_the_column_is_refused() =>
        new Func<CostCentre>(() => CostCentre.Create("ENG-OPS", new string('x', CostCentre.NameMaxLength + 1), Some.Manager))
            .ShouldBreak("cost_centre.name_invalid", ViolationKind.Invalid);

    [Fact]
    public void A_cost_centre_without_a_manager_is_refused() =>
        new Func<CostCentre>(() => CostCentre.Create("ENG-OPS", "Operations", Guid.Empty))
            .ShouldBreak("cost_centre.manager_required", ViolationKind.Invalid);

    [Fact]
    public void Every_real_change_moves_the_version_on_by_one()
    {
        CostCentre costCentre = Some.CostCentre();

        costCentre.Change(1, "Platform", Some.Manager, isActive: true).ShouldBeTrue();
        costCentre.Change(2, "Platform", Guid.CreateVersion7(), isActive: true).ShouldBeTrue();
        costCentre.Change(3, "Platform", costCentre.ManagerId, isActive: false).ShouldBeTrue();

        costCentre.Version.ShouldBe(4);
        costCentre.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_change_that_changes_nothing_keeps_the_version()
    {
        CostCentre costCentre = Some.CostCentre();

        costCentre.Change(1, " Platform engineering ", Some.Manager, isActive: true).ShouldBeFalse();

        costCentre.Version.ShouldBe(1);
    }

    [Fact]
    public void A_change_made_against_an_older_version_is_a_conflict()
    {
        CostCentre costCentre = Some.CostCentre();
        costCentre.Change(1, "Platform", Some.Manager, isActive: true);

        new Func<bool>(() => costCentre.Change(1, "Another name", Some.Manager, isActive: true))
            .ShouldBreak("concurrency.conflict", ViolationKind.Conflict);
        costCentre.Name.ShouldBe("Platform");
    }
}
