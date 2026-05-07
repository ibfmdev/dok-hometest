namespace Dok.Application.Abstractions;

public interface IPaymentSimulator
{
    IReadOnlyList<PaymentOption> Simulate(IReadOnlyList<UpdatedDebt> debts);
}
