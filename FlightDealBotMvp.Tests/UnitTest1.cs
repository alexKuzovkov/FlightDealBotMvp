using FlightDealBotMvp.Models;
using FlightDealBotMvp.Services;

namespace FlightDealBotMvp.Tests;

public sealed class ProviderSelectionTests
{
    [Fact]
    public void OneLeg_UsesOneWay()
    {
        var request = new FlightSearchRequest([new("WAW", "BCN", new DateOnly(2026, 9, 20))]);
        Assert.Equal("/fares/one-way", IgnavFlightProvider.SelectEndpoint(request));
    }

    [Fact]
    public void SymmetricReturn_UsesRoundTrip()
    {
        var request = new FlightSearchRequest([new("WAW", "BCN", new DateOnly(2026, 9, 20)), new("BCN", "WAW", new DateOnly(2026, 9, 27))]);
        Assert.Equal("/fares/round-trip", IgnavFlightProvider.SelectEndpoint(request));
    }

    [Fact]
    public void OpenJaw_UsesFlexibleSearch()
    {
        var request = new FlightSearchRequest([new("WAW", "BCN", new DateOnly(2026, 9, 20)), new("BCN", "BER", new DateOnly(2026, 9, 27))]);
        Assert.Equal("/fares/search", IgnavFlightProvider.SelectEndpoint(request));
    }
}
