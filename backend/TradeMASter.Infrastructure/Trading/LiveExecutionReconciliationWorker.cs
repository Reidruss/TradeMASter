using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeMASter.Core.Interfaces;

namespace TradeMASter.Infrastructure.Trading;

public sealed class LiveExecutionReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LiveExecutionReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ILiveExecutionReconciliationService>();
                await reconciler.ReconcileActiveBatchesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background live-order reconciliation failed closed; the next cycle will retry observation only.");
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
