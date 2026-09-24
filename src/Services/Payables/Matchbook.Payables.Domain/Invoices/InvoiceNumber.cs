using System.Text;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Domain.Invoices;

/// <summary>
/// A supplier's invoice number as written, and the normalised form that decides whether two numbers are the same
/// invoice.
/// </summary>
public sealed record InvoiceNumber
{
    /// <summary>Long enough for any numbering scheme seen in practice, and well inside pain.001's 140-character remittance field.</summary>
    public const int MaxLength = 50;

    private InvoiceNumber(string value, string normalised)
    {
        Value = value;
        Normalised = normalised;
    }

    /// <summary>The number as the supplier wrote it, trimmed. This is what goes back to the supplier on the payment.</summary>
    public string Value { get; }

    public string Normalised { get; }

    public static InvoiceNumber Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string trimmed = value.Trim();
        string normalised = Normalise(trimmed);
        if (trimmed.Length > MaxLength || normalised.Length == 0)
        {
            throw new BusinessRuleException(
                "invoice.number_invalid",
                $"The invoice number must contain a letter or a digit and be at most {MaxLength} characters.",
                ViolationKind.Invalid);
        }

        return new InvoiceNumber(trimmed, normalised);
    }

    /// <summary>
    /// Upper case, letters and digits only, then leading zeros dropped from each run of digits: <c>INV-0042</c> and
    /// <c>inv42</c> are the same number, and so are <c>A-007-B</c> and <c>a7b</c>.
    /// </summary>
    /// <remarks>
    /// Runs of digits are the runs left once punctuation is gone, so only a letter ends one. That is what makes
    /// normalising twice change nothing. A run of zeros keeps one zero, so <c>INV-0</c> is not <c>INV</c>. Digits
    /// are ASCII only; any other character, a non-Latin digit included, is punctuation.
    /// </remarks>
    public static string Normalise(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var result = new StringBuilder(value.Length);
        var run = DigitRun.None;

        foreach (char c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                if (run == DigitRun.Significant || c != '0')
                {
                    result.Append(c);
                    run = DigitRun.Significant;
                }
                else
                {
                    run = DigitRun.LeadingZeros;
                }
            }
            else if (char.IsLetter(c))
            {
                if (run == DigitRun.LeadingZeros)
                {
                    result.Append('0');
                }

                run = DigitRun.None;
                result.Append(char.ToUpperInvariant(c));
            }
        }

        if (run == DigitRun.LeadingZeros)
        {
            result.Append('0');
        }

        return result.ToString();
    }

    private enum DigitRun
    {
        None,
        LeadingZeros,
        Significant,
    }
}
