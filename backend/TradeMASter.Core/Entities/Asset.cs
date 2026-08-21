using TradeMASter.Core.Common;
using TradeMASter.Core.Enums;

namespace TradeMASter.Core.Entities;

public class Asset : BaseEntity
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetType Type { get; set; }
    public string Exchange { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public bool IsTradable { get; set; } = true;
    public decimal LastPrice { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal Change24h { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Volume24h { get; set; }
    public DateTime? LastPriceUpdatedUtc { get; set; }

    public Asset() { }

    public Asset(
        string symbol,
        string name,
        AssetType type,
        string exchange = "NASDAQ",
        string currency = "USD",
        bool isTradable = true,
        decimal initialPrice = 0m)
    {
        Symbol = symbol.ToUpperInvariant();
        Name = name;
        Type = type;
        Exchange = exchange;
        Currency = currency;
        IsTradable = isTradable;
        LastPrice = initialPrice;
        PreviousClose = initialPrice;
        LastPriceUpdatedUtc = DateTime.UtcNow;
    }

    public void UpdatePrice(decimal newPrice, decimal volume = 0)
    {
        if (LastPrice > 0)
        {
            Change24h = newPrice - LastPrice;
            ChangePercent24h = LastPrice != 0 ? (Change24h / LastPrice) * 100m : 0m;
        }

        LastPrice = newPrice;
        if (volume > 0) Volume24h = volume;
        LastPriceUpdatedUtc = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
