namespace Dok.Domain;

public sealed record UpdatedDebt(
    DebtType Type,
    Money OriginalAmount,
    Money UpdatedAmount,
    DateOnly DueDate,
    int DaysOverdue);
