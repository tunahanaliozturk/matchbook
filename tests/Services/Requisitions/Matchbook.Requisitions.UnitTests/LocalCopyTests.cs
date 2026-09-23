using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.UnitTests;

public sealed class LocalCopyTests
{
    [Fact]
    public void A_newer_cost_centre_snapshot_replaces_the_copy()
    {
        CostCentre copy = new("ENG-PLATFORM", 3, "Platform", A.Manager.Id, true);

        copy.Apply(4, "Platform and tooling", A.OtherManager.Id, false).ShouldBeTrue();

        copy.Version.ShouldBe(4);
        copy.Name.ShouldBe("Platform and tooling");
        copy.ManagerId.ShouldBe(A.OtherManager.Id);
        copy.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void An_older_or_repeated_cost_centre_snapshot_is_ignored(long version)
    {
        CostCentre copy = new("ENG-PLATFORM", 3, "Platform", A.Manager.Id, true);

        copy.Apply(version, "Old name", A.OtherManager.Id, false).ShouldBeFalse();

        copy.Version.ShouldBe(3);
        copy.ManagerId.ShouldBe(A.Manager.Id);
        copy.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void A_newer_supplier_snapshot_replaces_the_copy()
    {
        Supplier copy = new(A.SupplierId, 1, "Acme", true);

        copy.Apply(2, "Acme BV", false).ShouldBeTrue();

        copy.Version.ShouldBe(2);
        copy.LegalName.ShouldBe("Acme BV");
        copy.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    public void An_older_or_repeated_supplier_snapshot_is_ignored(long version)
    {
        Supplier copy = new(A.SupplierId, 5, "Acme BV", false);

        copy.Apply(version, "Acme", true).ShouldBeFalse();

        copy.Version.ShouldBe(5);
        copy.IsActive.ShouldBeFalse();
    }
}
