namespace Dok.Domain.Rules;

public interface IInterestRule
{
    DebtType Type { get; }
    UpdatedDebt Apply(Debt debt, DateOnly today);
}
