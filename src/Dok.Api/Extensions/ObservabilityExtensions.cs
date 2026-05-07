using Dok.Infrastructure.Observability;
using OpenTelemetry.Metrics;

namespace Dok.Api.Extensions;

internal static class ObservabilityExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra o pipeline OpenTelemetry de métricas e habilita o exporter Prometheus.
        /// O endpoint HTTP <c>/metrics</c> é montado depois via <see cref="MapDokMetrics"/>.
        /// Coleta o meter <c>Dok.Providers</c> definido em <see cref="ProviderMetrics"/>.
        /// </summary>
        public IServiceCollection AddDokObservability()
        {
            services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddMeter(ProviderMetrics.MeterName)
                    .AddPrometheusExporter());
            return services;
        }
    }

    extension(IEndpointRouteBuilder app)
    {
        /// <summary>
        /// Mapeia <c>GET /metrics</c> no formato Prometheus text exposition.
        /// Os contadores de <see cref="ProviderMetrics"/> aparecem com prefixo <c>dok_providers_</c>.
        /// </summary>
        public IEndpointRouteBuilder MapDokMetrics()
        {
            app.MapPrometheusScrapingEndpoint();
            return app;
        }
    }
}
