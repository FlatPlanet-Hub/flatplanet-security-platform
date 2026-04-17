using System.Security.Cryptography;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Security.Infrastructure.Security;

public class TotpSecretEncryptor : ITotpSecretEncryptor
{
    private readonly byte[] _key;

    public TotpSecretEncryptor(IOptions<MfaOptions> options)
    {
        var keyBase64 = options.Value.TotpEncryptionKey;
        if (string.IsNullOrEmpty(keyBase64))
            throw new InvalidOperationException("Mfa:TotpEncryptionKey is not configured.");

        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException("Mfa:TotpEncryptionKey must be a 32-byte (256-bit) Base64 value.");
    }

    public string Encrypt(byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag   = new byte[16];
        var ciphertext = new byte[secret.Length];

        using var aesGcm = new AesGcm(_key, 16);
        aesGcm.Encrypt(nonce, secret, ciphertext, tag);

        // Format: base64(nonce) + "." + base64(tag) + "." + base64(ciphertext)
        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(ciphertext)}";
    }

    public byte[] Decrypt(string ciphertext)
    {
        var parts = ciphertext.Split('.');
        if (parts.Length != 3)
            throw new FormatException("Invalid TOTP secret format.");

        var nonce      = Convert.FromBase64String(parts[0]);
        var tag        = Convert.FromBase64String(parts[1]);
        var encrypted  = Convert.FromBase64String(parts[2]);
        var plaintext  = new byte[encrypted.Length];

        using var aesGcm = new AesGcm(_key, 16);
        aesGcm.Decrypt(nonce, encrypted, tag, plaintext);

        return plaintext;
    }
}
