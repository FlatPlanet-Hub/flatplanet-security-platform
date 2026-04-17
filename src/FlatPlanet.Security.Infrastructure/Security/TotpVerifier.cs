using FlatPlanet.Security.Application.Interfaces.Services;
using OtpNet;

namespace FlatPlanet.Security.Infrastructure.Security;

public class TotpVerifier : ITotpVerifier
{
    public bool Verify(byte[] secret, string code, out long matchedStep)
    {
        var totp = new Totp(secret);
        return totp.VerifyTotp(code, out matchedStep, new VerificationWindow(1, 1));
    }
}
