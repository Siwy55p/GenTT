using System.Text.Json;
using System.Text.Json.Serialization;

namespace TikTokGenerator.Models;

public sealed class ShortScript
{
    public string Title { get; set; } = string.Empty;

    public string Hook { get; set; } = string.Empty;

    public string HookOnScreenText { get; set; } = string.Empty;

    public string HookSearchPhrase { get; set; } = string.Empty;

    public List<ScriptScene> Scenes { get; set; } = [];

    public string Ending { get; set; } = string.Empty;

    public string EndingOnScreenText { get; set; } = string.Empty;

    public string EndingSearchPhrase { get; set; } = string.Empty;
}

public sealed class ScriptScene
{
    public string VoiceOver { get; set; } = string.Empty;

    public string OnScreenText { get; set; } = string.Empty;

    public string VisualDescription { get; set; } = string.Empty;

    public string SearchPhrase { get; set; } = string.Empty;

    public string AvoidVisuals { get; set; } = string.Empty;

    public string SceneGoal { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyText { get; set; }

    [JsonExtensionData]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
