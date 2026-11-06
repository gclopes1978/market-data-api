namespace MarketData.Infrastructure;

/// <summary>
/// Circuit breaker settings shared by every upstream HTTP client, bound from <c>Resilience:CircuitBreaker</c>.
/// Each upstream still gets its own breaker, so one failing provider never blocks the others.
/// </summary>
public sealed class CircuitBreakerSettings
{
    /// <summary>Name of the configuration section these settings are bound from.</summary>
    public const string Section = "Resilience:CircuitBreaker";

    /// <summary>Turns the circuit breakers off. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Share of failed calls (0 to 1] within the sampling window that opens the circuit. Defaults to 0.5.</summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>Minimum number of calls in the sampling window before the ratio is evaluated (at least 2). Defaults to 5.</summary>
    public int MinimumThroughput { get; set; } = 5;

    /// <summary>Length of the sliding window used to compute the failure ratio, in seconds. Defaults to 30.</summary>
    public int SamplingDurationSeconds { get; set; } = 30;

    /// <summary>How long the circuit stays open (failing fast) before a trial call is allowed, in seconds. Defaults to 30.</summary>
    public int BreakDurationSeconds { get; set; } = 30;

    /// <summary>Throws <see cref="InvalidOperationException"/> when a value would be rejected by the resilience pipeline.</summary>
    public void Validate()
    {
        if (FailureRatio is <= 0 or > 1)
            throw new InvalidOperationException($"{Section}:FailureRatio must be in (0, 1].");
        if (MinimumThroughput < 2)
            throw new InvalidOperationException($"{Section}:MinimumThroughput must be at least 2.");
        if (SamplingDurationSeconds < 1)
            throw new InvalidOperationException($"{Section}:SamplingDurationSeconds must be at least 1.");
        if (BreakDurationSeconds < 1)
            throw new InvalidOperationException($"{Section}:BreakDurationSeconds must be at least 1.");
    }
}
