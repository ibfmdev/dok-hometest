using System.ComponentModel.DataAnnotations;

namespace Dok.Infrastructure.Options;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    [Range(1, 600)] public int TotalTimeoutSeconds { get; init; } = 10;
    [Range(1, 60)]  public int PerAttemptTimeoutSeconds { get; init; } = 3;
    [Range(0, 10)]  public int RetryCount { get; init; } = 2;
    [Range(50, 5000)] public int RetryBaseDelayMs { get; init; } = 200;
    [Range(1, 100)] public int CircuitBreakerFailures { get; init; } = 5;
    [Range(1, 600)] public int CircuitBreakerWindowSeconds { get; init; } = 30;
    [Range(1, 600)] public int CircuitBreakerBreakDurationSeconds { get; init; } = 30;
}
