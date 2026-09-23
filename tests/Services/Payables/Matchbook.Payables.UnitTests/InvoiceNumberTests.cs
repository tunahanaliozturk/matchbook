using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

public sealed class InvoiceNumberTests
{
    [Theory]
    [InlineData("INV-0042", "inv42")]
    [InlineData("A-007-B", "a7b")]
    [InlineData("  INV 0042 ", "INV42")]
    [InlineData("INV-000", "inv0")]
    [InlineData("rechnung/Nr.0010", "RECHNUNGNR10")]
    public void Numbers_that_differ_only_in_case_punctuation_and_leading_zeros_are_the_same_number(string one, string other) =>
        InvoiceNumber.Normalise(one).ShouldBe(InvoiceNumber.Normalise(other));

    [Theory]
    [InlineData("INV-0", "INV")]
    [InlineData("A7B", "A70B")]
    [InlineData("INV-10", "INV-1")]
    [InlineData("INV-42A", "INV-42B")]
    public void Numbers_that_differ_in_a_letter_or_a_significant_digit_are_different(string one, string other) =>
        InvoiceNumber.Normalise(one).ShouldNotBe(InvoiceNumber.Normalise(other));

    [Fact]
    public void The_normal_form_is_upper_case_letters_and_digits_without_leading_zeros() =>
        InvoiceNumber.Normalise("inv-0042/b-00").ShouldBe("INV42B0");

    [Fact]
    public void Only_a_letter_ends_a_run_of_digits_so_punctuation_between_digits_joins_them() =>
        InvoiceNumber.Normalise("2026-0042").ShouldBe("20260042");

    [Fact]
    public void Digits_other_than_ascii_are_treated_as_punctuation() =>
        InvoiceNumber.Normalise("INV-٤٢").ShouldBe("INV");

    [Fact]
    public void The_number_is_kept_as_written_apart_from_surrounding_spaces()
    {
        InvoiceNumber number = InvoiceNumber.Parse("  inv-0042 ");

        number.Value.ShouldBe("inv-0042");
        number.Normalised.ShouldBe("INV42");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--/--")]
    public void A_number_without_a_letter_or_a_digit_is_refused(string value)
    {
        var refusal = Should.Throw<BusinessRuleException>(() => InvoiceNumber.Parse(value));

        refusal.Code.ShouldBe("invoice.number_invalid");
        refusal.Kind.ShouldBe(ViolationKind.Invalid);
    }

    [Fact]
    public void A_number_longer_than_fifty_characters_is_refused() =>
        Should.Throw<BusinessRuleException>(() => InvoiceNumber.Parse(new string('7', 51))).Code.ShouldBe("invoice.number_invalid");

    [Property(MaxTest = 500)]
    public Property Normalising_twice_changes_nothing() =>
        Prop.ForAll(AnyText.ToArbitrary(), text =>
        {
            string once = InvoiceNumber.Normalise(text);

            InvoiceNumber.Normalise(once).ShouldBe(once);
        });

    [Property(MaxTest = 500)]
    public Property Case_punctuation_and_leading_zeros_never_change_the_normal_form() =>
        Prop.ForAll(Spellings.ToArbitrary(), spelling =>
        {
            InvoiceNumber.Normalise(spelling.Written).ShouldBe(spelling.Canonical);
            InvoiceNumber.Normalise(spelling.Canonical).ShouldBe(spelling.Canonical);
        });

    /// <summary>Anything a person or a scanner might type: ASCII, zeros, punctuation, and letters with awkward casing.</summary>
    private static readonly Gen<string> AnyText =
        Gen.Frequency(
                (6, Gen.Elements("abcxyzABCXYZ0000123456789".ToCharArray())),
                (2, Gen.Elements(" -/._#".ToCharArray())),
                (1, Gen.Elements("éßİıΣσςǅǆﬁÅ".ToCharArray())),
                (1, Gen.Choose(0, char.MaxValue).Select(code => (char)code)))
            .ArrayOf()
            .Select(chars => new string(chars));

    private sealed record Spelling(string Written, string Canonical);

    /// <summary>
    /// A number built from alternating runs of letters and digits, written with random case, punctuation anywhere,
    /// and extra zeros in front of each digit run, alongside the normal form it must come to.
    /// </summary>
    private static readonly Gen<Spelling> Spellings =
        from startsWithLetters in Gen.Elements(true, false)
        from runs in Gen.NonEmptyListOf(Gen.Zip(LetterRun, DigitRun))
        from caseFlips in Gen.ArrayOf(Gen.Elements(true, false), 200)
        from zeros in Gen.ArrayOf(Gen.Choose(0, 3), 200)
        from separators in Gen.ArrayOf(Gen.Frequency((3, Gen.Constant(string.Empty)), (1, Gen.Elements("-", " ", "/", "."))), 400)
        select Spell(startsWithLetters, runs, caseFlips, zeros, separators);

    private static Gen<string> LetterRun =>
        Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray()).NonEmptyListOf().Select(letters => new string([.. letters]));

    private static Gen<string> DigitRun =>
        Gen.Frequency(
            (1, Gen.Constant("0")),
            (5, from first in Gen.Elements("123456789".ToCharArray())
                from rest in Gen.Elements("0123456789".ToCharArray()).ListOf()
                select first + new string([.. rest])));

    private static Spelling Spell(
        bool startsWithLetters,
        List<(string Letters, string Digits)> runs,
        bool[] caseFlips,
        int[] zeros,
        string[] separators)
    {
        IEnumerable<(bool IsDigits, string Text)> ordered = runs.SelectMany(run => startsWithLetters
            ? new[] { (false, run.Letters), (true, run.Digits) }
            : new[] { (true, run.Digits), (false, run.Letters) });

        var written = new StringBuilder();
        var canonical = new StringBuilder();
        int position = 0;
        foreach ((bool isDigits, string text) in ordered)
        {
            canonical.Append(text);
            string spelled = isDigits
                ? new string('0', zeros[position % zeros.Length]) + text
                : new string([.. text.Select((letter, i) => caseFlips[(position + i) % caseFlips.Length] ? char.ToLowerInvariant(letter) : letter)]);
            foreach (char c in spelled)
            {
                written.Append(separators[position++ % separators.Length]).Append(c);
            }
        }

        written.Append(separators[position % separators.Length]);
        return new Spelling(written.ToString(), canonical.ToString());
    }
}
