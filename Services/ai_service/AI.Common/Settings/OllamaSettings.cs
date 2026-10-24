namespace AI.Common.Settings;

public sealed class OllamaSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int ContextLength { get; set; }
    public int MaximumOutputTokens { get; set; }
    public int MaximumPromptCharacters { get; set; }
    public int KeepAliveMinutes { get; set; }
}
