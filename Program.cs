using System.Text.Json;
using FlightDealBotMvp.Infrastructure;
using FlightDealBotMvp.Models;
using FlightDealBotMvp.Services;

namespace FlightDealBotMvp;

public static class Program
{
    public static async Task<int> Main()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            var options = await LoadOptionsAsync(cts.Token); options.Validate();
            using var telegramHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            using var ignavHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(options.IgnavRequestTimeoutSeconds) };
            var store = new JsonFileAlertStore(Path.Combine(Directory.GetCurrentDirectory(), "data", "alerts.json"));
            await store.InitializeAsync(cts.Token);
            var telegram = new TelegramApiClient(telegramHttp, options.TelegramBotToken);
            IFlightProvider provider = new IgnavFlightProvider(ignavHttp, options);
            var commands = new BotCommandHandler(telegram, store, provider);
            var polling = new TelegramPollingRunner(telegram, commands);
            var monitor = new PriceMonitor(store, provider, telegram, options);
            Console.WriteLine($"FlightDealBot MVP starting with {provider.Name}. Press Ctrl+C to stop.");
            await Task.WhenAll(polling.RunAsync(cts.Token), monitor.RunAsync(cts.Token));
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task<AppOptions> LoadOptionsAsync(CancellationToken ct)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (!File.Exists(path)) throw new FileNotFoundException("appsettings.json not found. Copy appsettings.example.json and fill credentials.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppOptions>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct)
               ?? throw new InvalidOperationException("Unable to deserialize appsettings.json.");
    }
}
