using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class IgnavFlightProvider : IFlightProvider
{
    private readonly HttpClient _http;
    private readonly AppOptions _options;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public IgnavFlightProvider(HttpClient http, AppOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Name => "Ignav";

    public async Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest request, CancellationToken cancellationToken)
    {
        ValidateSearch(request);
        var endpoint = SelectEndpoint(request);
        var payload = BuildSearchPayload(request, endpoint);
        using var response = await SendAsync(endpoint, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        return ExtractItineraries(document.RootElement).Select(MapOffer).Where(x => x is not null).Cast<FlightOffer>().ToList();
    }

    public async Task<BookingResolution> ResolveBookingAsync(FlightOffer offer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(offer.ProviderOfferId)) return new BookingResolution(null, []);
        using var response = await SendAsync("/fares/booking-links", new { ignav_id = offer.ProviderOfferId }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        FlightOffer? refreshed = TryGet(root, "itinerary", out var itinerary) ? MapOffer(itinerary, offer.ProviderOfferId) : null;
        var options = new List<BookingOption>();
        if (TryGet(root, "booking_options", out var bookingOptions) && bookingOptions.ValueKind == JsonValueKind.Array)
            foreach (var option in bookingOptions.EnumerateArray()) options.Add(MapBookingOption(option));
        return new BookingResolution(refreshed, options);
    }

    public static string SelectEndpoint(FlightSearchRequest request)
    {
        if (request.Legs.Count == 1) return "/fares/one-way";
        var a = request.Legs[0]; var b = request.Legs[1];
        return a.Origin == b.Destination && a.Destination == b.Origin ? "/fares/round-trip" : "/fares/search";
    }
    private object BuildSearchPayload(FlightSearchRequest request, string endpoint)
    {
        var common = new { adults = request.Adults, cabin_class = request.CabinClass, market = request.Market };
        if (endpoint == "/fares/one-way")
        {
            var leg = request.Legs[0];
            return new { origin = leg.Origin, destination = leg.Destination, departure_date = leg.DepartureDate.ToString("yyyy-MM-dd"), common.adults, common.cabin_class, common.market, max_stops = leg.MaxStops };
        }
        if (endpoint == "/fares/round-trip")
        {
            var outbound = request.Legs[0]; var inbound = request.Legs[1];
            return new { origin = outbound.Origin, destination = outbound.Destination, departure_date = outbound.DepartureDate.ToString("yyyy-MM-dd"), return_date = inbound.DepartureDate.ToString("yyyy-MM-dd"), common.adults, common.cabin_class, common.market, max_stops = outbound.MaxStops };
        }
        return new
        {
            legs = request.Legs.Select(x => new { origin = x.Origin, destination = x.Destination, departure_date = x.DepartureDate.ToString("yyyy-MM-dd"), max_stops = x.MaxStops }).ToArray(),
            common.adults, common.cabin_class, common.market
        };
    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _options.IgnavBaseUrl.TrimEnd('/') + endpoint);
                request.Headers.Add("X-Api-Key", _options.IgnavApiKey);
                request.Content = JsonContent.Create(payload, options: _json);
                var response = await _http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode) return response;
                var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!transient || attempt >= _options.IgnavMaxRetries)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    response.Dispose();
                    throw new HttpRequestException($"Ignav {endpoint} failed ({(int)response.StatusCode}): {Shorten(body, 600)}");
                }
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await DelayAsync(attempt, retryAfter, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < _options.IgnavMaxRetries)
            {
                await DelayAsync(attempt, null, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _options.IgnavMaxRetries)
            {
                await DelayAsync(attempt, null, cancellationToken);
            }
        }
    }

    private static Task DelayAsync(int attempt, TimeSpan? retryAfter, CancellationToken ct)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(4000, 250 * Math.Pow(2, attempt)) + Random.Shared.Next(25, 175));
        return Task.Delay(delay, ct);
    }

    private static void ValidateSearch(FlightSearchRequest request)
    {
        if (request.Legs.Count is < 1 or > 2) throw new ArgumentException("Ignav MVP supports one or two ordered legs.");
        if (request.Adults < 1) throw new ArgumentException("Adults must be positive.");
    }
    private static IEnumerable<JsonElement> ExtractItineraries(JsonElement root)
    {
        if (TryGet(root, "itineraries", out var items) && items.ValueKind == JsonValueKind.Array) return items.EnumerateArray().ToArray();
        if (TryGet(root, "data", out var data) && data.ValueKind == JsonValueKind.Array) return data.EnumerateArray().ToArray();
        if (TryGet(root, "itinerary", out var itinerary)) return [itinerary];
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root];
    }

    private static FlightOffer? MapOffer(JsonElement item) => MapOffer(item, ReadString(item, "ignav_id", "id") ?? string.Empty);

    private static FlightOffer? MapOffer(JsonElement item, string providerOfferId)
    {
        if (!TryGet(item, "price", out var priceElement)) return null;
        var price = MapPrice(priceElement);
        var legs = new List<FlightLeg>();
        if (TryGet(item, "legs", out var legsElement) && legsElement.ValueKind == JsonValueKind.Array)
            legs.AddRange(legsElement.EnumerateArray().Select(MapLeg));
        else
        {
            if (TryGet(item, "outbound", out var outbound)) legs.Add(MapLeg(outbound));
            if (TryGet(item, "inbound", out var inbound)) legs.Add(MapLeg(inbound));
        }
        if (legs.Count == 0) return null;
        return new FlightOffer("Ignav", providerOfferId, price, ReadString(item, "cabin_class") ?? "economy", ReadBool(item, "requires_self_transfer"), legs);
    }

    private static FlightLeg MapLeg(JsonElement leg)
    {
        var segments = TryGet(leg, "segments", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(MapSegment).ToList() : [];
        return new FlightLeg(ReadString(leg, "carrier") ?? segments.FirstOrDefault()?.OperatingCarrierName ?? "Unknown", ReadInt(leg, "duration_minutes"), segments);
    }
    private static FlightSegment MapSegment(JsonElement s) => new(
        ReadString(s, "marketing_carrier_code") ?? string.Empty,
        ReadString(s, "flight_number") ?? string.Empty,
        ReadString(s, "operating_carrier_name") ?? string.Empty,
        ReadString(s, "departure_airport") ?? string.Empty,
        ReadDateTime(s, "departure_time_local"),
        ReadString(s, "departure_timezone") ?? string.Empty,
        ReadDateTimeOffset(s, "departure_time_utc"),
        ReadString(s, "arrival_airport") ?? string.Empty,
        ReadDateTime(s, "arrival_time_local"),
        ReadString(s, "arrival_timezone") ?? string.Empty,
        ReadDateTimeOffset(s, "arrival_time_utc"),
        ReadInt(s, "duration_minutes"),
        ReadString(s, "aircraft"));

    private static FlightPrice MapPrice(JsonElement p) => new(ReadDecimal(p, "amount"), ReadString(p, "currency") ?? string.Empty, ReadString(p, "status") ?? string.Empty);

    private static BookingOption MapBookingOption(JsonElement option)
    {
        var legs = TryGet(option, "legs", out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList() : [];
        var links = new List<BookingLink>();
        if (TryGet(option, "links", out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var link in array.EnumerateArray())
                links.Add(new BookingLink(ReadString(link, "provider_name") ?? "Seller", ReadString(link, "provider_type") ?? "third_party", TryGet(link, "price", out var p) ? MapPrice(p) : new FlightPrice(0, string.Empty, string.Empty), ReadString(link, "url") ?? string.Empty));
        return new BookingOption(legs, links);
    }
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }
    private static string? ReadString(JsonElement e, params string[] names) { foreach (var n in names) if (TryGet(e, n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString(); return null; }
    private static bool ReadBool(JsonElement e, string n) => TryGet(e, n, out var v) && v.ValueKind == JsonValueKind.True;
    private static int ReadInt(JsonElement e, string n) => TryGet(e, n, out var v) && v.TryGetInt32(out var result) ? result : 0;
    private static decimal ReadDecimal(JsonElement e, string n)
    {
        if (!TryGet(e, n, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
    private static DateTime ReadDateTime(JsonElement e, string n) => DateTime.TryParse(ReadString(e, n), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : default;
    private static DateTimeOffset ReadDateTimeOffset(JsonElement e, string n) => DateTimeOffset.TryParse(ReadString(e, n), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : default;
    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
