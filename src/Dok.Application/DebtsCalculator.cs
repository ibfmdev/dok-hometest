using Dok.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dok.Application;

public sealed class DebtsCalculator(
    IDebtProviderChain providers,
    IReadOnlyDictionary<DebtType, IInterestRule> rules,
    IDebtsClock clock,
    ILogger<DebtsCalculator> logger) : IDebtsCalculator
{
    public async Task<CalculatorResult> CalculateAsync(Plate plate, CancellationToken ct)
    {
        var debts = await providers.FetchDebtsAsync(plate, ct);
        var today = clock.Today;

        var updated = new List<UpdatedDebt>(debts.Count);
        foreach (var debt in debts)
        {
            if (!rules.TryGetValue(debt.Type, out var rule))
                throw new UnknownDebtTypeException(DebtTypeMapper.ToWire(debt.Type));

            // Anomalia observável: provider devolveu vencimento futuro (typo / clock skew).
            // Comportamento permanece conforme spec (juros = 0); apenas sinalizamos para auditoria.
            if (debt.DueDate.DayNumber > today.DayNumber)
                logger.LogWarning(
                    "Provider returned future due date for {DebtType}: {DueDate} (today {Today}) — possible typo or clock skew",
                    debt.Type, debt.DueDate, today);

            try
            {
                updated.Add(rule.Apply(debt, today));
            }
            catch (Exception ex) when (ex is not DomainException)
            {
                logger.LogError(ex,
                    "Rule {RuleType} failed for debt {DebtType} due {DueDate} amount {Amount}",
                    rule.GetType().Name, debt.Type, debt.DueDate, debt.OriginalAmount.Value);
                throw;
            }
        }

        var totalOriginal = Money.Of(updated.Sum(d => d.OriginalAmount.Value));
        var totalUpdated = Money.Of(updated.Sum(d => d.UpdatedAmount.Value));

        return new CalculatorResult(updated, new DebtsSummary(totalOriginal, totalUpdated));
    }
}
