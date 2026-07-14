namespace TikTokGenerator.Models;

public sealed class VideoProject
{
    public required string Topic { get; init; }

    public required string Country { get; init; }

    public required string Category { get; init; }

    public required string Script { get; init; }

    public required string VoiceOverPath { get; init; }

    public required string BackgroundDescription { get; init; }

    public required string OutputPath { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
