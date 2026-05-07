using Dok.Application.Abstractions;

namespace Dok.Application;

public sealed class PaymentSimulator : IPaymentSimulator
{
    private const decimal PixDiscount = 0.05m;
    private const decimal CardMonthlyRate = 0.025m;
    private static readonly int[] InstallmentCounts = [1, 6, 12];

    public IReadOnlyList<PaymentOption> Simulate(IReadOnlyList<UpdatedDebt> debts)
    {
        if (debts.Count == 0)
            return [];

        var options = new List<PaymentOption>();

        // TOTAL
        var totalBase = Money.Of(debts.Sum(d => d.UpdatedAmount.Value));
        options.Add(BuildOption("TOTAL", totalBase));

        // SOMENTE_<TIPO> (singular, agrupa por tipo mesmo com múltiplos débitos do mesmo tipo)
        var byType = debts
            .GroupBy(d => d.Type)
            .OrderBy(g => g.Key);

        foreach (var group in byType)
        {
            var typeBase = Money.Of(group.Sum(d => d.UpdatedAmount.Value));
            var tag = $"SOMENTE_{DebtTypeMapper.ToWire(group.Key)}";
            options.Add(BuildOption(tag, typeBase));
        }

        return options;
    }

    private static PaymentOption BuildOption(string tag, Money baseAmount)
    {
        var pix = new PixOption(Money.Of(baseAmount.Value * (1m - PixDiscount)));
        var card = new CreditCardOption(BuildInstallments(baseAmount));
        return new PaymentOption(tag, baseAmount, pix, card);
    }

    private static IReadOnlyList<Installment> BuildInstallments(Money baseAmount)
    {
        var installments = new List<Installment>(InstallmentCounts.Length);
        foreach (var n in InstallmentCounts)
        {
            if (n == 1)
            {
                installments.Add(new Installment(1, baseAmount));
                continue;
            }

            // Price/PMT em decimal puro (sem conversão para double):
            // valor_parcela = base * i * (1+i)^n / ((1+i)^n − 1)
            var i = CardMonthlyRate;
            var factor = Pow(1m + i, n);
            var pmt = baseAmount.Value * i * factor / (factor - 1m);
            installments.Add(new Installment(n, Money.Of(pmt)));
        }
        return installments;
    }

    /// <summary>
    /// Eleva <paramref name="value"/> à potência inteira <paramref name="exponent"/> mantendo
    /// o cálculo 100% em <see cref="decimal"/> — sem conversão para <see cref="double"/>,
    /// evitando imprecisão de ponto flutuante. Suficiente para expoentes pequenos (1x/6x/12x).
    /// </summary>
    private static decimal Pow(decimal value, int exponent)
    {
        if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent));
        decimal result = 1m;
        for (var i = 0; i < exponent; i++) result *= value;
        return result;
    }
}
