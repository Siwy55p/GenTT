namespace TikTokGenerator.Models;

public sealed class ScriptConceptSelection
{
    public List<ScriptConcept> Directions { get; set; } = [];

    public string SelectedDirectionId { get; set; } = string.Empty;

    public string SelectedReason { get; set; } = string.Empty;

    public ScriptConcept? SelectedDirection =>
        Directions.FirstOrDefault(direction => direction.Id.Equals(SelectedDirectionId, StringComparison.OrdinalIgnoreCase))
        ?? Directions.OrderByDescending(direction => direction.TotalScore).FirstOrDefault();
}

public sealed class ScriptConcept
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Structure { get; set; } = string.Empty;

    public string HookAngle { get; set; } = string.Empty;

    public string Payoff { get; set; } = string.Empty;

    public ConceptScores Scores { get; set; } = new();

    public int TotalScore =>
        Scores.Usefulness
        + Scores.Specificity
        + Scores.Freshness
        + Scores.SourceAlignment
        + Scores.VisualPotential
        + Scores.HookStrength;
}

public sealed class ConceptScores
{
    public int Usefulness { get; set; }

    public int Specificity { get; set; }

    public int Freshness { get; set; }

    public int SourceAlignment { get; set; }

    public int VisualPotential { get; set; }

    public int HookStrength { get; set; }
}
