using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Matchbook.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Matchbook.BuildingBlocks.Security;

/// <summary>
/// Encrypts individual column values, bank account numbers above all, with AES-256-GCM under a named key.
/// </summary>
/// <remarks>
/// <para>
/// A stored value is <c>v1.{keyId}.{base64url(nonce ‖ tag ‖ ciphertext)}</c>. The key id travels with the value,
/// so rotating means adding a key and making it active: new writes use it, old rows still decrypt, and a
/// background rewrite can move them over whenever convenient. Removing a key that rows still use is the one way
/// to lose data, which is why decryption names the missing key in its error.
/// </para>
/// <para>
/// The nonce is random per value. GCM tolerates about 2^32 random nonces per key before collisions become a
/// concern, far beyond the number of bank accounts this system will ever write under one key.
/// </para>
/// </remarks>
public sealed class ColumnProtector : IFieldProtector
{
    private const string Version = "v1";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    public ColumnProtector(IReadOnlyDictionary<string, byte[]> keys, string activeKeyId)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);

        foreach ((string id, byte[] key) in keys)
        {
            if (key.Length != KeySize)
            {
                throw new ArgumentException($"Encryption key '{id}' must be {KeySize} bytes.", nameof(keys));
            }

            if (id.Contains('.', StringComparison.Ordinal))
            {
                throw new ArgumentException($"Encryption key id '{id}' may not contain a dot.", nameof(keys));
            }
        }

        if (!keys.ContainsKey(activeKeyId))
        {
            throw new ArgumentException($"The active encryption key '{activeKeyId}' is not configured.", nameof(activeKeyId));
        }

        _keys = keys.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray(), StringComparer.Ordinal);
        _activeKeyId = activeKeyId;
    }

    /// <summary>
    /// Reads <c>Encryption:ActiveKey</c> and every <c>Encryption:Keys:{id}</c> (base64, 32 bytes) from configuration.
    /// </summary>
    public static ColumnProtector FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Encryption");
        string activeKey = section["ActiveKey"]
            ?? throw new InvalidOperationException("Encryption:ActiveKey is not configured.");

        Dictionary<string, byte[]> keys = new(StringComparer.Ordinal);

        foreach (IConfigurationSection key in section.GetSection("Keys").GetChildren())
        {
            keys[key.Key] = Convert.FromBase64String(key.Value ?? "");
        }

        return new ColumnProtector(keys, activeKey);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] key = _keys[_activeKeyId];
        int length = Encoding.UTF8.GetByteCount(plaintext);
        byte[] payload = new byte[NonceSize + TagSize + length];

        Span<byte> nonce = payload.AsSpan(0, NonceSize);
        Span<byte> tag = payload.AsSpan(NonceSize, TagSize);
        Span<byte> ciphertext = payload.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        Span<byte> clear = length <= 256 ? stackalloc byte[length] : new byte[length];
        Encoding.UTF8.GetBytes(plaintext, clear);

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, clear, ciphertext, tag, AssociatedData(_activeKeyId));
        }

        CryptographicOperations.ZeroMemory(clear);
        return $"{Version}.{_activeKeyId}.{Base64Url.EncodeToString(payload)}";
    }

    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        string[] parts = stored.Split('.');

        if (parts.Length != 3 || parts[0] != Version)
        {
            throw new CryptographicException("The value is not in the protected column format.");
        }

        if (!_keys.TryGetValue(parts[1], out byte[]? key))
        {
            throw new CryptographicException($"The value was encrypted under key '{parts[1]}', which is not configured.");
        }

        byte[] payload = Base64Url.DecodeFromChars(parts[2]);

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The protected value is truncated.");
        }

        byte[] clear = new byte[payload.Length - NonceSize - TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize),
                clear,
                AssociatedData(parts[1]));
        }

        return Encoding.UTF8.GetString(clear);
    }

    // Binding the key id into the tag means a value cannot be relabelled to be decrypted under another key.
    private static byte[] AssociatedData(string keyId)
    {
        byte[] data = new byte[4 + Encoding.UTF8.GetByteCount(keyId)];
        BinaryPrimitives.WriteInt32BigEndian(data, 1);
        Encoding.UTF8.GetBytes(keyId, data.AsSpan(4));
        return data;
    }
}
