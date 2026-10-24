using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TiendaOnline.Services;

// Cada minuto cuenta en Redis las sesiones con actividad en los últimos 5 minutos
// y publica ese número como ActiveCarts en CloudWatch (namespace URPOnline).
public class ActiveCartsPublisher(IServiceScopeFactory scopeFactory, ILogger<ActiveCartsPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var metrics = scope.ServiceProvider.GetRequiredService<BusinessMetrics>();
                await metrics.PublishActiveCartsMetricAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo publicar ActiveCarts en CloudWatch");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}