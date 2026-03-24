using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Security.Infrastructure.ExternalServices;

public class SupabaseAuthClient : ISupabaseAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;

    public SupabaseAuthClient(HttpClient httpClient, IOptions<SupabaseOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<SupabaseAuthResult?> SignInAsync(string email, string password)
    {
        var payload = new { email, password };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.Url}/auth/v1/token?grant_type=password")
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add("apikey", _options.ServiceRoleKey);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadFromJsonAsync<SupabaseTokenResponse>();

        if (body?.User is null || !Guid.TryParse(body.User.Id, out var userId))
            return null;

        return new SupabaseAuthResult
        {
            UserId = userId,
            Email = body.User.Email
        };
    }

    private class SupabaseTokenResponse
    {
        [JsonPropertyName("user")]
        public SupabaseUser? User { get; set; }
    }

    private class SupabaseUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }
}
