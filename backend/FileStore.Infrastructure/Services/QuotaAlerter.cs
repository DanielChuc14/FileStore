using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Infrastructure.Services;

/// <summary>
/// Encola un aviso cuando un cliente cruza el 80% o el 95% de su cuota.
///
/// La marca <c>QuotaAlertPercent</c> es lo que evita el spam: sin ella, un
/// cliente al 96% recibiria un correo en cada ciclo del job.
/// </summary>
public class QuotaAlerter(
    IApplicationDbContext context,
    IEmailQueue emailQueue,
    IAppUrlProvider urls,
    ILogger<QuotaAlerter> logger)
    : IQuotaAlerter
{
    private const int WarningPercent = 80;
    private const int CriticalPercent = 95;

    public async Task<int> CheckAsync(CancellationToken cancellationToken = default)
    {
        var clients = await context.Clients
            .Where(c => !c.IsDeleted && c.IsActive && c.QuotaBytes > 0)
            .ToListAsync(cancellationToken);

        var queued = 0;
        var changed = false;

        foreach (var client in clients)
        {
            var percent = (int)(client.UsedBytes * 100 / client.QuotaBytes);
            var threshold = ThresholdFor(percent);

            if (threshold is null)
            {
                // Por debajo del 80% se limpia la marca, para que si vuelve a
                // llenarse el aviso salga de nuevo. Sin esto, quien libera
                // espacio y se llena otra vez no recibiria nada la segunda vez.
                if (client.QuotaAlertPercent is not null)
                {
                    client.QuotaAlertPercent = null;
                    changed = true;
                }

                continue;
            }

            // Solo se avisa al SUBIR de umbral. Pasar del 95% al 85% no dispara
            // el aviso del 80%: ya se advirtio de algo mas grave.
            if (client.QuotaAlertPercent >= threshold)
            {
                continue;
            }

            emailQueue.Enqueue(
                EmailTemplates.QuotaAlert(
                    client.Email,
                    client.Name,
                    threshold.Value,
                    client.UsedBytes,
                    client.QuotaBytes,
                    urls.PanelUrl),
                client.Id);

            client.QuotaAlertPercent = threshold;
            queued++;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        if (queued > 0)
        {
            logger.LogInformation("Encolados {Count} avisos de cuota.", queued);
        }

        return queued;
    }

    private static int? ThresholdFor(int percent) => percent switch
    {
        >= CriticalPercent => CriticalPercent,
        >= WarningPercent => WarningPercent,
        _ => null
    };
}
