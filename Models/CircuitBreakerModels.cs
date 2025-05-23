using Polly.CircuitBreaker;

namespace CircuitBreakerService.Models;

public class CircuitBreakerConfiguration
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan DurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(1);
    public int MinimumThroughput { get; set; } = 3;
}

public class CircuitInfo
{
    public string CircuitName { get; set; } = string.Empty;
    public CircuitState State { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? LastFailureTime { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
}

public class KafkaMessage
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ProcessingResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }

    public static ProcessingResult Success() => new() { IsSuccess = true };
    public static ProcessingResult Failure(string errorMessage, Exception? exception = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, Exception = exception };
}