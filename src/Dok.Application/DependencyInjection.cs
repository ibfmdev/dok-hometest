using Dok.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dok.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDokApplication(this IServiceCollection services)
    {
        services.AddSingleton<IInterestRule, IpvaInterestRule>();
        services.AddSingleton<IInterestRule, MultaInterestRule>();
        services.AddSingleton<IInterestRule, LicenciamentoInterestRule>();
        services.AddSingleton<IReadOnlyDictionary<DebtType, IInterestRule>>(sp =>
            sp.GetServices<IInterestRule>().ToDictionary(r => r.Type));

        // Default IDebtsClock — usa o TimeProvider injetado. Pode ser sobrescrito
        // por SetDebtsReferenceDate() para fixar a data da spec na demo.
        services.TryAddSingleton<IDebtsClock, SystemDebtsClock>();

        services.AddScoped<IDebtsCalculator, DebtsCalculator>();
        services.AddSingleton<IPaymentSimulator, PaymentSimulator>();
        services.AddScoped<IDebtsService, DebtsService>();

        return services;
    }

    /// <summary>
    /// Substitui o <see cref="IDebtsClock"/> default por um <see cref="FixedDebtsClock"/>
    /// que sempre retorna <paramref name="referenceDate"/>. Usado quando se quer fixar a
    /// data dos cálculos de juros (ex: para reproduzir os exemplos numéricos da spec na
    /// demo) sem afetar o <see cref="TimeProvider"/> global usado pelo Polly.
    /// </summary>
    public static IServiceCollection SetDebtsReferenceDate(this IServiceCollection services, DateOnly referenceDate)
    {
        services.RemoveAll<IDebtsClock>();
        services.AddSingleton<IDebtsClock>(new FixedDebtsClock(referenceDate));
        return services;
    }
}
