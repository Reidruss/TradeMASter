using TradeMASter.Core.Common;

namespace TradeMASter.Core.ValueObjects;

public class Ticker : ValueObject
{
    public string Symbol { get; }

    public Ticker(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Ticker symbol cannot be empty.", nameof(symbol));

        Symbol = symbol.Trim().ToUpperInvariant();
    }

    public override string ToString() => Symbol;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Symbol;
    }

    public static implicit operator string(Ticker ticker) => ticker.Symbol;
    public static implicit operator Ticker(string symbol) => new(symbol);
}

public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "USD")
    {
        Amount = amount;
        Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant();
    }

    public static Money Usd(decimal amount) => new(amount, "USD");
    public static Money Zero(string currency = "USD") => new(0m, currency);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot add amounts with different currencies ({Currency} vs {other.Currency}).");

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot subtract amounts with different currencies ({Currency} vs {other.Currency}).");

        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public override string ToString() => $"{Amount:N2} {Currency}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
    public static Money operator *(Money left, decimal right) => left.Multiply(right);
}

public class PriceTick : ValueObject
{
    public string Symbol { get; }
    public decimal Price { get; }
    public decimal Volume { get; }
    public decimal? Bid { get; }
    public decimal? Ask { get; }
    public decimal Change24h { get; }
    public decimal ChangePercent24h { get; }
    public DateTime Timestamp { get; }

    public PriceTick(
        string symbol,
        decimal price,
        decimal volume,
        DateTime timestamp,
        decimal? bid = null,
        decimal? ask = null,
        decimal change24h = 0m,
        decimal changePercent24h = 0m)
    {
        Symbol = symbol.ToUpperInvariant();
        Price = price;
        Volume = volume;
        Timestamp = timestamp;
        Bid = bid;
        Ask = ask;
        Change24h = change24h;
        ChangePercent24h = changePercent24h;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Symbol;
        yield return Price;
        yield return Volume;
        yield return Timestamp;
    }
}
