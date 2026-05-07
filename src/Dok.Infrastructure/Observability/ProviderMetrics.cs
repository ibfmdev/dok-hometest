using System.Diagnostics.Metrics;

namespace Dok.Infrastructure.Observability;

/// <summary>
/// Counters nativos via <see cref="System.Diagnostics.Metrics"/> para o pipeline de
/// providers. Sem dependência de OpenTelemetry — observáveis via
/// <c>dotnet-counters monitor -n Dok.Api Dok.Providers</c> e/ou exportador OTel
/// configurado externamente.
/// </summary>
public sealed class ProviderMetrics : IDisposable
{
    public const string MeterName = "Dok.Providers";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _fallback;
    private readonly Counter<long> _allUnavailable;

    public ProviderMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName, MeterVersion);

        _requests = _meter.CreateCounter<long>(
            "dok.providers.requests",
            unit: "{request}",
            description: "Number of provider requests, tagged by provider and outcome (success|failure).");

        _failures = _meter.CreateCounter<long>(
            "dok.providers.failures",
            unit: "{failure}",
            description: "Number of provider failures, tagged by provider and exception type.");

        _fallback = _meter.CreateCounter<long>(
            "dok.providers.fallback",
            unit: "{fallback}",
            description: "Number of times the chain fell back from one provider to the next.");

        _allUnavailable = _meter.CreateCounter<long>(
            "dok.providers.all_unavailable",
            unit: "{event}",
            description: "Number of times every provider in the chain failed for a single request (results in 503).");
    }

    public void RecordSuccess(string provider) =>
        _requests.Add(1, new KeyValuePair<string, object?>("provider", provider),
                         new KeyValuePair<string, object?>("outcome", "success"));

    public void RecordFailure(string provider, string exceptionType)
    {
        _requests.Add(1, new KeyValuePair<string, object?>("provider", provider),
                         new KeyValuePair<string, object?>("outcome", "failure"));
        _failures.Add(1, new KeyValuePair<string, object?>("provider", provider),
                         new KeyValuePair<string, object?>("exception_type", exceptionType));
    }

    public void RecordFallback(string fromProvider) =>
        _fallback.Add(1, new KeyValuePair<string, object?>("from_provider", fromProvider));

    public void RecordAllUnavailable() => _allUnavailable.Add(1);

    public void Dispose() => _meter.Dispose();
}
