namespace Matchbook.SharedKernel;

/// <summary>
/// Encrypts and decrypts one sensitive value, a bank account number above all, under the payment-data key.
/// Suppliers and Payables share the key, so an IBAN can travel between them encrypted (ADR 0007).
/// </summary>
public interface IFieldProtector
{
    string Protect(string plaintext);

    string Unprotect(string stored);
}
