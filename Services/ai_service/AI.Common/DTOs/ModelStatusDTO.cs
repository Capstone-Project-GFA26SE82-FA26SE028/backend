namespace AI.Common.DTOs;

public sealed record ModelStatusDTO(bool RuntimeAvailable, bool Installed, bool Loaded, bool Ready, string Status);
