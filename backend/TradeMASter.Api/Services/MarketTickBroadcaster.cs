using Microsoft.AspNetCore.SignalR;
using TradeMASter.Api.Hubs;
using TradeMASter.Infrastructure.MarketData;

namespace TradeMASter.Api.Services;

public class MarketTickBroadcaster : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<MarketDataHub, IMarketDataClient> _hubContext;
    private readonly ILogger<MarketTickBroadcaster> _logger;

    private static readonly string[] MonitoredTickers = { "NVDA", "AAPL", "MSFT", "TSLA", "BTC-USD", "ETH-USD", "SPY", "QQQ" };

    public MarketTickBroadcaster(
        IServiceProvider serviceProvider,
        IHubContext<MarketDataHub, IMarketDataClient> hubContext,
        ILogger<MarketTickBroadcaster> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketTickBroadcaster started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var marketData = scope.ServiceProvider.GetRequiredService<IMarketDataService>();

                foreach (var ticker in MonitoredTickers)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var quoteRes = await marketData.GetQuoteAsync(ticker, stoppingToken);
                    if (quoteRes.IsSuccess)
                    {
                        var tick = quoteRes.Value;
                        // Broadcast to ticker specific group and global subscribers
                        await _hubContext.Clients.All.ReceiveMarketTick(
                            tick.Symbol,
                            tick.Price,
                            tick.Change24h,
                            tick.ChangePercent24h,
                            tick.Volume,
                            tick.Timestamp.ToString("o")
                        );
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Tick broadcaster iteration exception: {Message}", ex.Message);
            }

            await Task.Delay(4000, stoppingToken);
        }
    }
}
