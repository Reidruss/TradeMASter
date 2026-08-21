namespace TradeMASter.Core.Enums;

public enum OrderType
{
    Market = 0,
    Limit = 1,
    Stop = 2,
    StopLimit = 3
}

public enum OrderSide
{
    Buy = 0,
    Sell = 1
}

public enum OrderStatus
{
    Pending = 0,
    Open = 1,
    PartiallyFilled = 2,
    Filled = 3,
    Canceled = 4,
    Rejected = 5,
    Expired = 6
}
