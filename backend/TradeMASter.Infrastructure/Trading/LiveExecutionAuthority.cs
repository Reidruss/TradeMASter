using Microsoft.Extensions.Configuration;
using TradeMASter.Core.Common;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Trading;

public sealed class LiveExecutionAuthority(IConfiguration configuration) : ILiveExecutionAuthority
{
    public Result Verify(LivePortfolioPolicySnapshot policy)
    {
        if (policy.EmergencyHaltActive)
            return Result.Failure($"Emergency halt blocks live submission: {policy.EmergencyHaltReason}");
        if (!policy.LiveTradingEnabled)
            return Result.Failure("Persisted live-trading authority is disabled and has no dashboard/API enable path.");
        if (!configuration.GetValue<bool>("Robinhood:LiveTradingEnabled"))
            return Result.Failure("Application live-trading authority is disabled.");
        return Result.Success();
    }
}
