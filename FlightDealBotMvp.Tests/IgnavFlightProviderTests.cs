using System.Net;
using System.Text.Json;
using FlightDealBotMvp.Models;
using FlightDealBotMvp.Services;

namespace FlightDealBotMvp.Tests;

public sealed class IgnavFlightProviderTests
{
    [Fact]
    public async Task Search_WawBcn_MapsItineraryAndRequest()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, Fixture("waw-bcn-search.json"));
        var provider = CreateProvider(handler);
        var request = new FlightSearchRequest([new("WAW", "BCN", new DateOnly(2026, 9, 20), 0)], 1, "economy", "PL");

        var offers = await provider.SearchAsync(request, CancellationToken.None);

        var offer = Assert.Single(offers);
        Assert.Equal("ignav-waw-bcn-001", offer.ProviderOfferId);
        Assert.Equal(149.90m, offer.Price.Amount);
        Assert.Equal("USD", offer.Price.Currency);
        var segment = Assert.Single(Assert.Single(offer.Legs).Segments);
        Assert.Equal("WAW", segment.DepartureAirport);
        Assert.Equal("BCN", segment.ArrivalAirport);
        Assert.Equal("LO", segment.MarketingCarrierCode);
        Assert.Equal("437", segment.FlightNumber);
        Assert.Equal("https://unit.test/fares/one-way", handler.LastRequestUri!.ToString());
        Assert.Equal("test-key", handler.LastApiKey);
        using var json = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("WAW", json.RootElement.GetProperty("origin").GetString());
        Assert.Equal("BCN", json.RootElement.GetProperty("destination").GetString());
        Assert.Equal("2026-09-20", json.RootElement.GetProperty("departure_date").GetString());
        Assert.Equal("PL", json.RootElement.GetProperty("market").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("max_stops").GetInt32());
    }

    [Fact]
    public async Task ResolveBooking_MapsAirlineAndOtaLinks()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, Fixture("booking-links.json"));
        var provider = CreateProvider(handler);
        var offer = Offer("ignav-waw-bcn-001");

        var booking = await provider.ResolveBookingAsync(offer, CancellationToken.None);

        var option = Assert.Single(booking.Options);
        Assert.Equal(2, option.Links.Count);
        Assert.Contains(option.Links, x => x.ProviderType == "airline" && x.ProviderName == "LOT");
        Assert.Contains(option.Links, x => x.ProviderType == "ota" && x.ProviderName == "Example OTA");
        Assert.Contains("ignav-waw-bcn-001", handler.LastRequestBody!);
    }
    [Fact]
    public async Task ResolveBooking_EmptyOptions_ReturnsEmptyCollection()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"booking_options\":[]}");
        var booking = await CreateProvider(handler).ResolveBookingAsync(Offer("offer-1"), CancellationToken.None);
        Assert.Empty(booking.Options);
    }

    [Fact]
    public async Task Search_500Then200_RetriesAndSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{\"error\":\"temporary\"}");
        handler.Enqueue(HttpStatusCode.OK, Fixture("waw-bcn-search.json"));

        var offers = await CreateProvider(handler, retries: 1).SearchAsync(OneWay(), CancellationToken.None);

        Assert.Single(offers);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Search_400_DoesNotRetry()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, "{\"error\":\"bad request\"}");        var provider = CreateProvider(handler, retries: 3);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync(OneWay(), CancellationToken.None));

        Assert.Contains("(400)", error.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Search_Cancellation_IsPropagatedWithoutRetry()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProvider(handler, retries: 3).SearchAsync(OneWay(), cts.Token));

        Assert.Equal(1, handler.RequestCount);
    }

    private static IgnavFlightProvider CreateProvider(StubHttpMessageHandler handler, int retries = 0) =>
        new(new HttpClient(handler), new AppOptions { IgnavApiKey = "test-key", IgnavBaseUrl = "https://unit.test", IgnavMarket = "PL", IgnavMaxRetries = retries });

    private static FlightSearchRequest OneWay() => new([new("WAW", "BCN", new DateOnly(2026, 9, 20))], Market: "PL");

    private static FlightOffer Offer(string id) => new("Ignav", id, new FlightPrice(149.90m, "USD", "live"), "economy", false, []);
    private static string Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }
}