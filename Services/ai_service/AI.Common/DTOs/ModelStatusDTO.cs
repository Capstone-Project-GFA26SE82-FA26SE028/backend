namespace AI.Common.DTOs;

public sealed record ModelStatusDTO(string Provider, string Model, bool Configured, bool Available, bool Ready, string Status);
