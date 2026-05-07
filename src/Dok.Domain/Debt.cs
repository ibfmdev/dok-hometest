namespace Dok.Domain;

public sealed record Debt(DebtType Type, Money OriginalAmount, DateOnly DueDate);
