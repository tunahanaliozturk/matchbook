using Matchbook.BuildingBlocks.Security;
using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchbook.Suppliers.Infrastructure.Configurations;

/// <summary>
/// The IBAN column's one mapping: the value object to its electronic form, then encrypted under the payment-data
/// key. Reading parses the decrypted value again, so a row can never hand the domain an IBAN it would refuse.
/// </summary>
internal static class IbanConverter
{
    private static readonly ValueConverter<Iban, string> Plain = new(
        static iban => iban.Value,
        static value => Iban.Parse(value));

    public static ValueConverter EncryptedWith(ColumnProtector protector) =>
        Plain.ComposeWith(new ProtectedStringConverter(protector));
}
