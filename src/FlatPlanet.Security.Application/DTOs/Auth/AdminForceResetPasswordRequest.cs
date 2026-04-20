namespace FlatPlanet.Security.Application.DTOs.Auth;

// No fields required — the reset link is built using the platform's configured
// AppOptions.BaseUrl. Since this is an SSO service, the reset URL is always
// the same central endpoint regardless of which app triggered the reset.
public class AdminForceResetPasswordRequest { }
