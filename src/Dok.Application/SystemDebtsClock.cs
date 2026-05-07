using Dok.Application.Abstractions;

namespace Dok.Application;

public sealed class SystemDebtsClock(TimeProvider clock) : IDebtsClock
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
