using FluentAssertions;
using TradeMASter.Core.Common;
using TradeMASter.Core.Entities;
using TradeMASter.Core.Enums;
using TradeMASter.Core.ValueObjects;
using Xunit;

namespace TradeMASter.Tests.Domain;

public class DomainEntityTests
{
    [Fact]
    public void Ticker_WithValidSymbol_CreatesSuccessfully()
    {
        var ticker = new Ticker("nvda");
        ticker.Symbol.Should().Be("NVDA");
        ticker.ToString().Should().Be("NVDA");
    }

    [Fact]
    public void Ticker_WithEmptySymbol_ThrowsArgumentException()
    {
        var act = () => new Ticker("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Money_Arithmetic_CalculatesCorrectly()
    {
        var m1 = new Money(100m, "USD");
        var m2 = new Money(50m, "USD");

        var sum = m1 + m2;
        sum.Amount.Should().Be(150m);
        sum.Currency.Should().Be("USD");

        var diff = m1 - m2;
        diff.Amount.Should().Be(50m);
    }

    [Fact]
    public void Result_Monad_HandlesSuccessAndFailure()
    {
        var success = Result.Success(42);
        success.IsSuccess.Should().BeTrue();
        success.Value.Should().Be(42);

        var failure = Result.Failure<int>("Invalid operation");
        failure.IsFailure.Should().BeTrue();
        failure.Error.Should().Be("Invalid operation");
    }

    [Fact]
    public void Portfolio_DepositAndWithdraw_UpdatesBalance()
    {
        var portfolio = new Portfolio("Test Fund", 50_000m);
        portfolio.CashBalance.Should().Be(50_000m);

        portfolio.Deposit(10_000m);
        portfolio.CashBalance.Should().Be(60_000m);

        portfolio.Withdraw(20_000m);
        portfolio.CashBalance.Should().Be(40_000m);

        var act = () => portfolio.Withdraw(100_000m);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Order_MarkFilled_TransitionsStatusAndRecordsFilledQuantity()
    {
        var order = new Order(Guid.NewGuid(), "AAPL", OrderSide.Buy, OrderType.Limit, 10m, 200m);
        order.Status.Should().Be(OrderStatus.Pending);

        order.MarkFilled(199.50m, 10m);
        order.Status.Should().Be(OrderStatus.Filled);
        order.FilledQuantity.Should().Be(10m);
        order.FilledPrice.Should().Be(199.50m);
        order.FilledAt.Should().NotBeNull();
    }
}
