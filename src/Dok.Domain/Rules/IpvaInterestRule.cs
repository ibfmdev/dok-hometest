namespace Dok.Domain.Rules;

public sealed class IpvaInterestRule : IInterestRule
{
    /// <summary>0,33% ao dia conforme HomeTest §1 — Juros por atraso (IPVA).</summary>
    private const decimal DailyInterestRate = 0.0033m;

    /// <summary>20% do valor original aplicado ao <em>valor de juros</em>, não ao total.</summary>
    private const decimal InterestCapRatio = 0.20m;

    public DebtType Type => DebtType.Ipva;

    public UpdatedDebt Apply(Debt debt, DateOnly today)
    {
        var days = today.DayNumber - debt.DueDate.DayNumber;
        if (days <= 0)
            return new UpdatedDebt(debt.Type, debt.OriginalAmount, debt.OriginalAmount, debt.DueDate, 0);

        var raw = debt.OriginalAmount.Value * DailyInterestRate * days;
        var capValue = debt.OriginalAmount.Value * InterestCapRatio;
        var interest = Math.Min(raw, capValue);
        var updated = Money.Of(debt.OriginalAmount.Value + interest);
        return new UpdatedDebt(debt.Type, debt.OriginalAmount, updated, debt.DueDate, days);
    }
}
