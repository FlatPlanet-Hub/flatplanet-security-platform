namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ITotpSecretEncryptor
{
    string Encrypt(byte[] secret);
    byte[] Decrypt(string ciphertext);
}
