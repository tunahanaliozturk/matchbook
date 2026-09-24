using Matchbook.Payables.Application;

namespace Matchbook.Payables.UnitTests;

public sealed class PagingTests
{
    [Theory]
    [InlineData(0, 50)]
    [InlineData(-3, 50)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(10_000, 200)]
    public void A_limit_defaults_to_fifty_and_is_capped_at_two_hundred(int asked, int used) =>
        Paging.Clamp(asked).ShouldBe(used);

    [Fact]
    public void A_page_fetched_one_row_long_has_a_next_cursor_at_its_last_row()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        Page<Guid> page = Paging.ToPage([.. ids], limit: 2, id => id);

        page.Items.ShouldBe([ids[0], ids[1]]);
        page.Next.ShouldBe(ids[1]);
    }

    [Fact]
    public void The_last_page_has_no_next_cursor()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];

        Page<Guid> page = Paging.ToPage([.. ids], limit: 2, id => id);

        page.Items.Count.ShouldBe(2);
        page.Next.ShouldBeNull();
    }
}
