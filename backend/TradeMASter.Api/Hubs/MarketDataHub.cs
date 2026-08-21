using Microsoft.AspNetCore.SignalR;
using TradeMASter.Core.ValueObjects;

namespace TradeMASter.Api.Hubs;

public interface IMarketDataClient
{
    Task ReceiveMarketTick(string symbol, decimal price, decimal change24h, decimal changePercent24h, decimal volume, string timestamp);
    Task ReceiveOrderUpdate(object orderUpdate);
}

public class MarketDataHub : Hub<IMarketDataClient>
{
    public async Task SubscribeTicker(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }

    public async Task UnsubscribeTicker(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }
}
