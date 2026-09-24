using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchbook.Suppliers.Infrastructure.Configurations;

/// <summary>
/// The IBAN column's one mapping, and the seam for encryption at rest: compose it with the column protector's
/// string converter (<c>new IbanConverter().ComposeWith(...)</c>) and neither the domain nor any query changes.
/// </summary>
internal sealed class IbanConverter() : ValueConverter<Iban, string>(
    static iban => iban.Value,
    static value => Iban.Parse(value));
