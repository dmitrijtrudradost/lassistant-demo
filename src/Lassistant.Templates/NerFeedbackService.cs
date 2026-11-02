using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lassistant.Templates;

/// <summary>
/// Локально сохраняет результат NER до и после ручного ревью.
/// Файлы предназначены для последующей проверки и экспорта в gold-набор.
/// </summary>
public static class NerFeedbackService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static NerFeedbackSession Start(
        string sourcePath,
        ContractNerDocument document,
        IReadOnlyList<ContractNerEntity> entities,
        string processingMode,
        double minScore,
        double autoAcceptScore)
    {
        var sessionId =
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"[..28];
        var directory = Path.Combine(GetApplicationDirectory(), "feedback", "contract-ner", sessionId);
        Directory.CreateDirectory(directory);

        var before = entities
            .Select(entity => Snapshot(entity, entity.Role, entity.Text, entity.Start, entity.End,
                entity.Score >= autoAcceptScore))
            .ToList();
        var metadata = new
        {
            schema_version = 1,
            session_id = sessionId,
            created_utc = DateTime.UtcNow,
            source_file = Path.GetFileName(sourcePath),
            source_sha256 = Sha256(sourcePath),
            processing_mode = processingMode,
            min_score = minScore,
            auto_accept_score = autoAcceptScore,
            input_length = document.Text.Length,
            entity_count = entities.Count
        };

        WriteJson(Path.Combine(directory, "metadata.json"), metadata);
        AtomicWrite(Path.Combine(directory, "input.txt"), document.Text);
        WriteJson(Path.Combine(directory, "input.fragments.json"), document.Fragments);
        WriteJson(Path.Combine(directory, "before.json"), before);

        return new NerFeedbackSession(sessionId, directory);
    }

    public static string Complete(
        NerFeedbackSession session,
        IReadOnlyList<NerFeedbackReview> reviews,
        string templatePath)
    {
        var after = reviews.Select(review => new
        {
            role = review.FinalRole,
            text = review.FinalText,
            start = review.FinalStart,
            end = review.FinalEnd,
            accepted = review.IsAccepted,
            score = review.Before.Score,
            paragraph_index = review.Before.ParagraphIndex,
            party_hint = review.Before.PartyHint,
            table = review.Before.Table,
            row = review.Before.Row,
            cell = review.Before.Cell,
            column = review.Before.Column,
            correction_reason = EmptyToNull(review.CorrectionReason),
            comment = EmptyToNull(review.Comment)
        }).ToList();

        var corrections = reviews
            .Select(review =>
            {
                var initialAccepted = review.Before.Score >= review.AutoAcceptScore;
                var changes = new List<string>();
                if (initialAccepted != review.IsAccepted) changes.Add("acceptance_changed");
                if (!string.Equals(review.Before.Role, review.FinalRole, StringComparison.Ordinal))
                    changes.Add("role_changed");
                if (review.Before.Start != review.FinalStart || review.Before.End != review.FinalEnd)
                    changes.Add("boundary_changed");
                return new
                {
                    changed = changes.Count > 0
                              || !string.IsNullOrWhiteSpace(review.CorrectionReason)
                              || !string.IsNullOrWhiteSpace(review.Comment),
                    payload = new
                    {
                        changes,
                        before = Snapshot(
                            review.Before,
                            review.Before.Role,
                            review.Before.Text,
                            review.Before.Start,
                            review.Before.End,
                            initialAccepted),
                        after = new
                        {
                            role = review.FinalRole,
                            text = review.FinalText,
                            start = review.FinalStart,
                            end = review.FinalEnd,
                            accepted = review.IsAccepted
                        },
                        reason = EmptyToNull(review.CorrectionReason),
                        comment = EmptyToNull(review.Comment)
                    }
                };
            })
            .Where(item => item.changed)
            .Select(item => item.payload)
            .ToList();

        WriteJson(Path.Combine(session.DirectoryPath, "after.json"), after);
        WriteJson(Path.Combine(session.DirectoryPath, "corrections.json"), corrections);
        WriteJson(Path.Combine(session.DirectoryPath, "completion.json"), new
        {
            completed_utc = DateTime.UtcNow,
            template_file = Path.GetFileName(templatePath),
            reviewed_count = reviews.Count,
            accepted_count = reviews.Count(review => review.IsAccepted),
            rejected_count = reviews.Count(review => !review.IsAccepted),
            correction_count = corrections.Count
        });
        return session.DirectoryPath;
    }

    private static object Snapshot(
        ContractNerEntity entity,
        string role,
        string text,
        int start,
        int end,
        bool accepted) => new
    {
        role,
        text,
        score = entity.Score,
        start,
        end,
        paragraph_index = entity.ParagraphIndex,
        party_hint = entity.PartyHint,
        table = entity.Table,
        row = entity.Row,
        cell = entity.Cell,
        column = entity.Column,
        accepted
    };

    private static string GetApplicationDirectory()
    {
        var processPath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(processPath)
            ? Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void WriteJson<T>(string path, T value) =>
        AtomicWrite(path, JsonSerializer.Serialize(value, JsonOptions));

    private static void AtomicWrite(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed record NerFeedbackSession(
    string SessionId,
    string DirectoryPath);

public sealed class NerFeedbackReview
{
    public required ContractNerEntity Before { get; init; }
    public required double AutoAcceptScore { get; init; }
    public required string FinalRole { get; init; }
    public required string FinalText { get; init; }
    public required int FinalStart { get; init; }
    public required int FinalEnd { get; init; }
    public required bool IsAccepted { get; init; }
    public string? CorrectionReason { get; init; }
    public string? Comment { get; init; }
}
