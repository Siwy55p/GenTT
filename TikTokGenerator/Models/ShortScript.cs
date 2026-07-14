namespace TikTokGenerator.Models;

public sealed class ShortScript
{
    public string Title { get; set; } = string.Empty;

    public string Hook { get; set; } = string.Empty;

    public List<ScriptScene> Scenes { get; set; } = [];

    public string Ending { get; set; } = string.Empty;
}

public sealed class ScriptScene
{
    public string Text { get; set; } = string.Empty;

    public string SearchPhrase { get; set; } = string.Empty;
}
