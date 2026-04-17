namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class GenerateBackupCodesResponse
{
    /// <summary>Plain-text codes shown once. The user must store these — they cannot be retrieved again.</summary>
    public IEnumerable<string> Codes { get; set; } = [];
    public int Count { get; set; }
}
