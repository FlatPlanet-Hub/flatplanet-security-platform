using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IJwtService
{
    Task<string> IssueAccessTokenAsync(User user, Guid sessionId, IEnumerable<string> roles);
    /// <summary>
    /// Issues a short-lived (10 min) enrolment-only JWT. No roles or business memberships are
    /// included. The token carries an <c>enrolment_only: true</c> claim so the frontend can
    /// identify it and restrict the session to the enrollment flow.
    /// </summary>
    Task<string> IssueEnrolmentTokenAsync(User user, Guid sessionId);
    (string token, string hash) GenerateRefreshToken();
    string HashToken(string token);
}
