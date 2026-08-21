namespace TradeMASter.Core.Enums;

public enum AgentRole
{
    TechnicalAnalyst = 0,
    FundamentalAnalyst = 1,
    SentimentAnalyst = 2,
    RiskAuditor = 3,
    PortfolioArbiter = 4,
    ExecutionStrategist = 5
}

public enum SignalDirection
{
    Neutral = 0,
    Bullish = 1,
    Bearish = 2,
    StrongBuy = 3,
    StrongSell = 4
}

public enum DecisionVerdict
{
    Hold = 0,
    Buy = 1,
    Sell = 2,
    Vetoed = 3
}

public enum TimeFrame
{
    OneMinute = 0,
    FiveMinutes = 1,
    FifteenMinutes = 2,
    OneHour = 3,
    FourHours = 4,
    OneDay = 5,
    OneWeek = 6
}
