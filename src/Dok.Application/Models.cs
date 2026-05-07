namespace Dok.Application;

public sealed record DebtsResult(
    Plate Plate,
    IReadOnlyList<UpdatedDebt> Debts,
    DebtsSummary Summary,
    IReadOnlyList<PaymentOption> Options);

public sealed record DebtsSummary(Money TotalOriginal, Money TotalUpdated);

public sealed record CalculatorResult(IReadOnlyList<UpdatedDebt> Debts, DebtsSummary Summary);

public sealed record PaymentOption(
    string Type,
    Money Base,
    PixOption Pix,
    CreditCardOption CreditCard);

public sealed record PixOption(Money TotalWithDiscount);

public sealed record CreditCardOption(IReadOnlyList<Installment> Installments);

public sealed record Installment(int Quantity, Money Amount);
