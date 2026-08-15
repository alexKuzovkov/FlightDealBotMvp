using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class TelegramPollingRunner
{
    private readonly TelegramApiClient _telegram;
    private readonly BotCommandHandler _commandHandler;

    public TelegramPollingRunner(TelegramApiClient telegram, BotCommandHandler commandHandler)
    {
        _telegram = telegram;
        _commandHandler = commandHandler;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _telegram.DeleteWebhookAsync(cancellationToken);
        long offset = 0;
        Console.WriteLine("Telegram long polling started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _telegram.GetUpdatesAsync(offset, cancellationToken);
                foreach (var update in updates)
                {
                    offset = Math.Max(offset, update.UpdateId + 1);
                    if (update.Message is { Text: not null } message)
                    {
                        try
                        {
                            await _commandHandler.HandleAsync(message, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Command error: {ex}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Polling error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}
