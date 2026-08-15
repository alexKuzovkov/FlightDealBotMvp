using System.Text.Json.Serialization;

namespace FlightDealBotMvp.Models;

public sealed class AmadeusTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class AmadeusFlightOffersResponse
{
    [JsonPropertyName("data")]
    public List<AmadeusFlightOffer> Data { get; set; } = [];
}

public sealed class AmadeusFlightOffer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("price")]
    public AmadeusPrice? Price { get; set; }
}

public sealed class AmadeusPrice
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("total")]
    public string? Total { get; set; }
}
