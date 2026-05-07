using Dok.Application;

namespace Dok.Api.Extensions;

internal static class TimeProviderExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registra o <see cref="TimeProvider"/> da aplicação como
        /// <see cref="TimeProvider.System"/> (sempre relógio real). Quando
        /// <c>Domain:ReferenceDate</c> estiver configurado, em vez de fixar o
        /// <see cref="TimeProvider"/> global, fixa apenas o <c>IDebtsClock</c>
        /// usado pelo <c>DebtsCalculator</c> — assim os cálculos de juros batem
        /// com a data da spec, mas o Polly continua medindo tempo real para a
        /// sliding window do circuit breaker.
        /// </summary>
        public IServiceCollection AddDokTimeProvider(IConfiguration config)
        {
            services.AddSingleton(TimeProvider.System);

            var fixedDate = config.GetValue<DateTimeOffset?>("Domain:ReferenceDate");
            if (fixedDate is { } d)
                services.SetDebtsReferenceDate(DateOnly.FromDateTime(d.UtcDateTime));

            return services;
        }
    }
}
