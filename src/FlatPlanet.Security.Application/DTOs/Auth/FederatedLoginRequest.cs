namespace FlatPlanet.Security.Application.DTOs.Auth;

public class FederatedLoginRequest
{
    /// <summary>Identity provider. Currently only "microsoft" is supported.</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>id_token returned by the identity provider after successful sign-in.</summary>
    public string IdToken { get; set; } = string.Empty;
    /// <summary>SP app slug the user is logging into (e.g. "finvoice").</summary>
    public string AppSlug { get; set; } = string.Empty;
}
