namespace Lassistant.Templates;

public sealed class MappingReplacement
{
    public required string Placeholder { get; init; }
    public required string Original { get; init; }
    public required string Category { get; init; }
    public required string Confidence { get; init; }
}

public sealed class AnonymizationMapping
{
    public string Version { get; init; } = "1.0";
    public required string SourceFile { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string AppVersion { get; init; } = "1.1.0";
    public required string SearchMode { get; init; }
    public string Language { get; init; } = "ru";
    public required IList<MappingReplacement> Replacements { get; init; }
}
