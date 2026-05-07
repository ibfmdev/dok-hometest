using Dok.Application.Abstractions;

namespace Dok.Application;

public sealed class FixedDebtsClock(DateOnly today) : IDebtsClock
{
    public DateOnly Today { get; } = today;
}
