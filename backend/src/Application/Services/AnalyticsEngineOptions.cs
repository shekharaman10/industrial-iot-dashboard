namespace IotDashboard.Application.Services;

/// <summary>
/// Tunable parameters for AnalyticsEngine — configure via appsettings.json or
/// environment variables (Analytics__WarningSigma, etc.) without recompiling.
/// </summary>
public sealed class AnalyticsEngineOptions
{
    public const string Section = "Analytics";

    public int    WindowSize       { get; set; } = 60;
    public int    BaselineSamples  { get; set; } = 300;
    public double WarningSigma     { get; set; } = 1.5;
    public double CriticalSigma    { get; set; } = 2.5;
    public double FaultSigma       { get; set; } = 3.5;
    public double RocThresholdPct  { get; set; } = 0.40;

    /// <summary>
    /// Seconds of inactivity before a device+metric state entry is evicted.
    /// Prevents unbounded memory growth when devices are decommissioned.
    /// Default: 1 hour.
    /// </summary>
    public int StateEvictionSeconds { get; set; } = 3600;
}
