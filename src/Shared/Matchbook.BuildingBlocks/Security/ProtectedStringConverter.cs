using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchbook.BuildingBlocks.Security;

/// <summary>
/// Encrypts a string property on its way into the database and decrypts it on the way out. Encryption is
/// randomised, so a protected column cannot be searched or indexed by value; look rows up by something else.
/// </summary>
public sealed class ProtectedStringConverter(ColumnProtector protector)
    : ValueConverter<string, string>(
        value => protector.Protect(value),
        stored => protector.Unprotect(stored));
