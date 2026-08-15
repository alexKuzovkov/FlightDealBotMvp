using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class AmadeusClient
{
    private readonly HttpClient _httpClient;
    private readonly AppOptions _options;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;

    public AmadeusClient(HttpClient httpClient, AppOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<FlightDeal?> SearchLowestOfferAsync(
        string origin,
        string destination,
        DateOnly departureDate,
        DateOnly returnDate,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = $"{_options.AmadeusBaseUrl.TrimEnd('/')}/v2/shopping/flight-offers" +
                  $"?originLocationCode={Uri.EscapeDataString(origin)}" +
                  $"&destinationLocationCode={Uri.EscapeDataString(destination)}" +
                  $"&departureDate={departureDate:yyyy-MM-dd}" +
                  $"&returnDate={returnDate:yyyy-MM-dd}" +
                  "&adults=1&currencyCode=USD&max=20";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Amadeus flight search failed ({(int)response.StatusCode}): {responseBody}");

        var payload = System.Text.Json.JsonSerializer.Deserialize<AmadeusFlightOffersResponse>(
            responseBody, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (payload?.Data is null || payload.Data.Count == 0)
            return null;

        var cheapest = payload.Data
            .Select(x => new
            {
                Offer = x,
                Price = ParsePrice(x.Price?.Total),
                Currency = x.Price?.Currency
            })
            .Where(x => x.Price.HasValue && string.Equals(x.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Price)
            .FirstOrDefault();

        return cheapest?.Price is decimal price
            ? new FlightDeal(origin, destination, departureDate, returnDate, price, cheapest.Offer.Id)
            : null;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAtUtc)
            return _accessToken;

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAtUtc)
                return _accessToken;

            var url = $"{_options.AmadeusBaseUrl.TrimEnd('/')}/v1/security/oauth2/token";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.AmadeusClientId,
                ["client_secret"] = _options.AmadeusClientSecret
            });

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Amadeus token request failed ({(int)response.StatusCode}): {responseBody}");

            var token = System.Text.Json.JsonSerializer.Deserialize<AmadeusTokenResponse>(
                responseBody, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
                throw new InvalidOperationException("Amadeus returned an empty access token.");

            _accessToken = token.AccessToken;
            var safeLifetimeSeconds = Math.Max(30, token.ExpiresIn - 60);
            _accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(safeLifetimeSeconds);
            return _accessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static decimal? ParsePrice(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }
}
