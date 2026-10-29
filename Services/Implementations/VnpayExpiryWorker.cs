using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ClothingAPI.Services.Implementations;

public sealed class VnpayExpiryWorker(IServiceScopeFactory scopes, IOptions<VnpaySettings> settings,
    ILogger<VnpayExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!settings.Value.WorkerEnabled || !settings.Value.IsConfigured) continue;
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IVnpayService>().ProcessExpiredOrdersAsync();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "VNPAY expiration check failed; will retry");
            }
        }
    }
}
