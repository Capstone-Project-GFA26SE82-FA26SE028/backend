namespace AI.Common.DTOs;

public sealed record ChatContextDTO
{
    public IReadOnlyList<Guid> PresentedItems { get; init; } = [];
    public IReadOnlyList<Guid> ExcludedItems { get; init; } = [];
    public IReadOnlyList<string> ExcludedTerms { get; init; } = [];
    public bool RequiresLightMeal { get; init; }
    public decimal? MaximumPrice { get; init; }
    public bool StrictPriceLimit { get; init; }
    public string Preference { get; init; } = "none";
    public string Language { get; init; } = "vi";
    public bool HasDietaryRestriction { get; init; }
    public bool HasAllergyConcern { get; init; }
}
