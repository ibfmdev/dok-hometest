using Dok.Infrastructure.Abstractions;
using Dok.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Dok.Infrastructure.Providers;

public sealed class DebtProviderChain(
    IEnumerable<IDebtProvider> providers,
    ProviderUsage usage,
    ProviderMetrics metrics,
    ILogger<DebtProviderChain> logger) : IDebtProviderChain
{
    public async Task<IReadOnlyList<Debt>> FetchDebtsAsync(Plate plate, CancellationToken ct)
    {
        var failures = new List<Exception>();
        IDebtProvider? previous = null;
        foreach (var provider in providers)
        {
            if (previous is not null)
                metrics.RecordFallback(previous.Name);

            try
            {
                logger.LogInformation("Querying {Provider} for {@Plate}", provider.Name, plate);
                var result = await provider.FetchAsync(plate, ct);
                logger.LogInformation(
                    "{Provider} returned {Count} debts for {@Plate}",
                    provider.Name, result.Count, plate);
                usage.Mark(provider.Name);
                metrics.RecordSuccess(provider.Name);
                return result;
            }
            catch (UnknownDebtTypeException)
            {
                // Erro de domínio: não silenciar nem cair pro próximo provider.
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento pelo cliente: propaga, não é falha de provider.
                throw;
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                logger.LogWarning(ex,
                    "{Provider} failed ({ExceptionType}) for {@Plate} — trying next provider",
                    provider.Name, ex.GetType().Name, plate);
                metrics.RecordFailure(provider.Name, ex.GetType().Name);
                failures.Add(ex);
                previous = provider;
            }
        }
        metrics.RecordAllUnavailable();
        throw new AllProvidersUnavailableException(failures);
    }

    /// <summary>
    /// Falhas esperadas de provider que disparam fallback. Bugs (NullReference, InvalidOperation
    /// não-relacionado a XML/parsing, etc.) propagam para visibilidade no monitoring.
    /// </summary>
    private static bool IsProviderFailure(Exception ex) => ex is
        HttpRequestException or
        System.Text.Json.JsonException or
        System.Xml.XmlException or
        TimeoutRejectedException or
        BrokenCircuitException or
        TaskCanceledException;
}
