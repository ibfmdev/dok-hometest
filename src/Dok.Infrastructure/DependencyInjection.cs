using Dok.Infrastructure.Abstractions;
using Dok.Infrastructure.Observability;
using Dok.Infrastructure.Options;
using Dok.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Dok.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDokInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ProvidersOptions>()
            .Bind(config.GetSection(ProvidersOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ResilienceOptions>()
            .Bind(config.GetSection(ResilienceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var resilience = config.GetSection(ResilienceOptions.SectionName).Get<ResilienceOptions>()
                         ?? new ResilienceOptions();

        services.AddHttpClient<ProviderAJsonAdapter>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<ProvidersOptions>>().Value;
                client.BaseAddress = new Uri(opts.ProviderAUrl);
            })
            .AddStandardResilienceHandler(o => ApplyResilience(o, resilience));

        services.AddHttpClient<ProviderBXmlAdapter>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<ProvidersOptions>>().Value;
                client.BaseAddress = new Uri(opts.ProviderBUrl);
            })
            .AddStandardResilienceHandler(o => ApplyResilience(o, resilience));

        // Ordem importa: A antes de B para o fallback
        services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderAJsonAdapter>());
        services.AddTransient<IDebtProvider>(sp => sp.GetRequiredService<ProviderBXmlAdapter>());

        services.AddScoped<ProviderUsage>();
        services.AddSingleton<ProviderMetrics>();
        services.AddScoped<IDebtProviderChain, DebtProviderChain>();

        return services;
    }

    private static void ApplyResilience(HttpStandardResilienceOptions options, ResilienceOptions cfg)
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(cfg.TotalTimeoutSeconds);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(cfg.PerAttemptTimeoutSeconds);

        options.Retry.MaxRetryAttempts = cfg.RetryCount;
        options.Retry.Delay = TimeSpan.FromMilliseconds(cfg.RetryBaseDelayMs);
        options.Retry.UseJitter = true;

        // FailureRatio = 1.0 + MinimumThroughput = N → "abre quando todas as N chamadas em SamplingDuration falham"
        // Aproximação razoável de "5 falhas em 30s" do enunciado.
        options.CircuitBreaker.FailureRatio = 1.0;
        options.CircuitBreaker.MinimumThroughput = cfg.CircuitBreakerFailures;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(cfg.CircuitBreakerWindowSeconds);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(cfg.CircuitBreakerBreakDurationSeconds);
    }
}
