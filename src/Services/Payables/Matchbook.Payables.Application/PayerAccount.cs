using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application;

/// <summary>
/// The company's own account that payment runs pay from: the debtor in the bank file. Bound from the <c>Payer</c>
/// configuration section and checked when the host starts, so a mistyped IBAN stops the service rather than the
/// first download.
/// </summary>
public sealed record PayerAccount
{
    public required string Name { get; init; }

    public required string Iban { get; init; }

    public required string Bic { get; init; }

    /// <summary>True when the name is present and the IBAN and BIC are well formed.</summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Name) || Iban is null || Bic is null)
        {
            return false;
        }

        try
        {
            Domain.Iban.Parse(Iban);
            Domain.Bic.Parse(Bic);
            return true;
        }
        catch (BusinessRuleException)
        {
            return false;
        }
    }

    // A record prints every property by default, and an IBAN has no business in a log line.
    public override string ToString() => $"PayerAccount {{ Name = {Name} }}";
}
