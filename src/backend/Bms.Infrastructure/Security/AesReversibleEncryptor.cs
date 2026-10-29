using System.Security.Cryptography;
using System.Text;
using Bms.Application.Abstractions.Security;
using Microsoft.Extensions.Options;

namespace Bms.Infrastructure.Security;

/// <summary>
/// AES-256-GCM. Output packs nonce + auth tag + ciphertext into one
/// base64 string so nothing extra needs to be stored alongside it. See
/// IReversibleEncryptor for why this exists instead of a one-way hash.
/// </summary>
public sealed class AesReversibleEncryptor : IReversibleEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IOptions<EncryptionOptions> _options;

    // Key validation used to happen in the constructor — correct in
    // isolation, but this type is a Singleton sitting behind
    // IAlertEmailNotifier -> IEmailSender in IngestionPipeline's
    // constructor chain (added for SMTP password decryption), which .NET
    // DI builds eagerly the moment IngestionPipeline itself is resolved —
    // i.e. on every single ingested telemetry message. A missing
    // Encryption:Key (never previously needed by Bms.Ingestion, which has
    // no reason to view MQTT credentials or SMTP settings) meant every
    // message failed before SendAsync's own "SMTP not configured, skip"
    // check ever got a chance to run — confirmed live: real telemetry was
    // being silently dropped by Bms.Ingestion's TCP worker. Deferring the
    // check to first actual use means construction always succeeds, and
    // only a genuine Encrypt/Decrypt call (on-demand, admin-triggered, or
    // gated behind SMTP actually being configured) can hit this error.
    private byte[]? _key;

    public AesReversibleEncryptor(IOptions<EncryptionOptions> options)
    {
        _options = options;
    }

    private byte[] Key
    {
        get
        {
            if (_key is not null) return _key;

            var keyB64 = _options.Value.Key;
            if (string.IsNullOrWhiteSpace(keyB64))
                throw new InvalidOperationException(
                    "Encryption:Key is not configured. Set it via the Encryption__Key environment variable " +
                    "(a base64-encoded 32-byte value, e.g. from `openssl rand -base64 32`) — never in appsettings.json.");

            var key = Convert.FromBase64String(keyB64);
            if (key.Length != 32)
                throw new InvalidOperationException($"Encryption:Key must decode to exactly 32 bytes (AES-256); got {key.Length}.");

            return _key = key;
        }
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSize);
        ciphertext.CopyTo(combined, NonceSize + TagSize);
        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertextB64)
    {
        var combined = Convert.FromBase64String(ciphertextB64);
        var nonce = combined.AsSpan(0, NonceSize);
        var tag = combined.AsSpan(NonceSize, TagSize);
        var ciphertext = combined.AsSpan(NonceSize + TagSize);

        var plaintextBytes = new byte[ciphertext.Length];
        using var aes = new AesGcm(Key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
