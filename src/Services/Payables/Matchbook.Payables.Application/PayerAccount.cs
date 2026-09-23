namespace Matchbook.Payables.Application;

/// <summary>The company's own account that payment runs pay from: the debtor in the bank file. Bound from configuration.</summary>
public sealed record PayerAccount
{
    public required string CompanyName { get; init; }

    public required string Iban { get; init; }

    public required string Bic { get; init; }

    // A record prints every property by default, and an IBAN has no business in a log line.
    public override string ToString() => $"PayerAccount {{ CompanyName = {CompanyName} }}";
}
