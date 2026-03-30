using System.Net.Http.Headers;
using System.Text;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Security.Infrastructure.ExternalServices;

public class TwilioSmsSender : ISmsSender
{
    private readonly SmsOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public TwilioSmsSender(IOptions<SmsOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendAsync(string to, string body)
    {
        var client = _httpClientFactory.CreateClient();
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("From", _options.FromNumber),
            new KeyValuePair<string, string>("To", to),
            new KeyValuePair<string, string>("Body", body)
        });

        var response = await client.PostAsync(url, formContent);
        response.EnsureSuccessStatusCode();
    }
}
