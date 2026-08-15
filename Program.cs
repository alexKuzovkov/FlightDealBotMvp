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
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var options = await LoadOptionsAsync(cts.Token);
            options.Validate();

            using var telegramHttp = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(40)
            };
            using var amadeusHttp = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var workingDirectory = Directory.GetCurrentDirectory();
            var store = new JsonFileAlertStore(Path.Combine(workingDirectory, "data", "alerts.json"));
            await store.InitializeAsync(cts.Token);

            var telegram = new TelegramApiClient(telegramHttp, options.TelegramBotToken);
            var amadeus = new AmadeusClient(amadeusHttp, options);
            var commands = new BotCommandHandler(telegram, store, amadeus);
            var polling = new TelegramPollingRunner(telegram, commands);
            var monitor = new PriceMonitor(store, amadeus, telegram, options);

            Console.WriteLine("FlightDealBot MVP starting. Press Ctrl+C to stop.");

            await Task.WhenAll(
                polling.RunAsync(cts.Token),
                monitor.RunAsync(cts.Token));

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<AppOptions> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "appsettings.json not found. Copy appsettings.example.json to appsettings.json and fill in the credentials.",
                configPath);
        }

        await using var stream = File.OpenRead(configPath);
        var options = await JsonSerializer.DeserializeAsync<AppOptions>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);

        return options ?? throw new InvalidOperationException("Unable to deserialize appsettings.json.");
    }
}
