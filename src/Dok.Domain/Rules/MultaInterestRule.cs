namespace Dok.Domain.Rules;

public sealed class MultaInterestRule : IInterestRule
{
    /// <summary>1,00% ao dia, sem teto, conforme HomeTest §1 — Juros por atraso (MULTA).</summary>
    private const decimal DailyInterestRate = 0.01m;

    public DebtType Type => DebtType.Multa;

    public UpdatedDebt Apply(Debt debt, DateOnly today)
    {
        var days = today.DayNumber - debt.DueDate.DayNumber;
        if (days <= 0)
            return new UpdatedDebt(debt.Type, debt.OriginalAmount, debt.OriginalAmount, debt.DueDate, 0);

        var interest = debt.OriginalAmount.Value * DailyInterestRate * days;
        var updated = Money.Of(debt.OriginalAmount.Value + interest);
        return new UpdatedDebt(debt.Type, debt.OriginalAmount, updated, debt.DueDate, days);
    }
}
