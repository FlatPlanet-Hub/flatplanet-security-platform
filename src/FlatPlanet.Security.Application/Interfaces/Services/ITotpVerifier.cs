namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ITotpVerifier
{
    /// <summary>
    /// Verifies a TOTP code against the given secret.
    /// Returns true if valid, and sets <paramref name="matchedStep"/> to the matched
    /// time step (Unix timestamp / period) so callers can detect replay attempts.
    /// </summary>
    bool Verify(byte[] secret, string code, out long matchedStep);
}
