using System.Security.Cryptography;
using Matchbook.BuildingBlocks.Security;

namespace Matchbook.BuildingBlocks.Tests;

public sealed class ColumnProtectorTests
{
    private static readonly byte[] First = [.. Enumerable.Range(1, 32).Select(static value => (byte)value)];
    private static readonly byte[] Second = [.. Enumerable.Range(101, 32).Select(static value => (byte)value)];

    private static ColumnProtector Protector(string active, params (string Id, byte[] Key)[] keys) =>
        new(keys.ToDictionary(static key => key.Id, static key => key.Key), active);

    [Fact]
    public void A_value_survives_the_round_trip()
    {
        ColumnProtector protector = Protector("k1", ("k1", First));

        protector.Unprotect(protector.Protect("DE89 3704 0044 0532 0130 00")).ShouldBe("DE89 3704 0044 0532 0130 00");
    }

    [Fact]
    public void The_same_value_encrypts_differently_every_time()
    {
        ColumnProtector protector = Protector("k1", ("k1", First));

        protector.Protect("DE89370400440532013000").ShouldNotBe(protector.Protect("DE89370400440532013000"));
    }

    [Fact]
    public void The_stored_form_does_not_contain_the_value()
    {
        string stored = Protector("k1", ("k1", First)).Protect("DE89370400440532013000");

        stored.ShouldStartWith("v1.k1.");
        stored.ShouldNotContain("370400440532013000");
    }

    [Fact]
    public void A_tampered_value_is_refused()
    {
        ColumnProtector protector = Protector("k1", ("k1", First));
        string stored = protector.Protect("DE89370400440532013000");
        char[] characters = stored.ToCharArray();
        characters[^3] = characters[^3] == 'A' ? 'B' : 'A';

        Should.Throw<CryptographicException>(() => protector.Unprotect(new string(characters)));
    }

    [Fact]
    public void After_rotation_old_values_still_decrypt_and_new_ones_use_the_new_key()
    {
        string old = Protector("k1", ("k1", First)).Protect("old account");
        ColumnProtector rotated = Protector("k2", ("k1", First), ("k2", Second));

        rotated.Unprotect(old).ShouldBe("old account");
        rotated.Protect("new account").ShouldStartWith("v1.k2.");
    }

    [Fact]
    public void A_value_relabelled_to_another_key_is_refused()
    {
        ColumnProtector both = Protector("k1", ("k1", First), ("k2", First));
        string relabelled = both.Protect("account").Replace("v1.k1.", "v1.k2.", StringComparison.Ordinal);

        Should.Throw<CryptographicException>(() => both.Unprotect(relabelled));
    }

    [Fact]
    public void A_value_under_a_removed_key_names_the_key()
    {
        string stored = Protector("k1", ("k1", First)).Protect("account");

        Should.Throw<CryptographicException>(() => Protector("k2", ("k2", Second)).Unprotect(stored))
            .Message.ShouldContain("'k1'");
    }

    [Fact]
    public void A_short_key_is_refused_at_startup() =>
        Should.Throw<ArgumentException>(() => Protector("k1", ("k1", new byte[16])));
}
