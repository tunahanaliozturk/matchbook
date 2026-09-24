namespace Matchbook.Purchasing.Api;

/// <summary>Who may call what. The domain checks the same roles again, so a policy wired wrongly fails closed.</summary>
internal static class Policies
{
    /// <summary>Buyers and receivers work with orders; auditors read everything and change nothing.</summary>
    public const string Read = "purchasing.read";

    public const string Buy = "purchasing.buy";

    public const string Receive = "purchasing.receive";
}
