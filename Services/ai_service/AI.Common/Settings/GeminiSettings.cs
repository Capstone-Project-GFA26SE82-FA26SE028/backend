namespace AI.Common.Settings;

public sealed class GeminiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int MaximumOutputTokens { get; set; }
    public int MaximumPromptCharacters { get; set; }
    public double Temperature { get; set; }
    public string ThinkingLevel { get; set; } = string.Empty;
}
