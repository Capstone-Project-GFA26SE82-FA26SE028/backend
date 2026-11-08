namespace AI.Common.Settings;

public sealed class AISettings
{
    public string Channel { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; }
    public int DependencyTimeoutSeconds { get; set; }
    public int MaximumConcurrentInferences { get; set; }
    public int MaximumMenuItems { get; set; }
    public int MenuPageSize { get; set; }
    public int ModelCandidateCount { get; set; }
    public int MaximumSuggestions { get; set; }
    public int MaximumMessageLength { get; set; }
    public int MaximumHistoryTurns { get; set; }
    public string? DuplicatePolicy { get; set; }
}
