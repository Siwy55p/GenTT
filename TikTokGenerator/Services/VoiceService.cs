namespace TikTokGenerator.Services;

public sealed class VoiceService
{
    private readonly HttpClient _httpClient;

    public VoiceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GenerateVoiceOverAsync(
        string script,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var voiceOverPath = Path.Combine(outputDirectory, "voiceover.txt");
        await File.WriteAllTextAsync(voiceOverPath, script, cancellationToken);

        return voiceOverPath;
    }
}
