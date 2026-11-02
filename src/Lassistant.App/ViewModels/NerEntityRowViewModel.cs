using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Lassistant.Templates;

namespace Lassistant.App.ViewModels;

/// <summary>Строка ревью сущности Contract NER.</summary>
public sealed class NerEntityRowViewModel : INotifyPropertyChanged
{
    private bool _isAccepted;
    private string _editableRole;
    private string _editableText;
    private string _correctionReason = "";
    private string _comment = "";
    private bool _suppressNewRoleConfirm;

    public static IReadOnlyList<string> AvailableCorrectionReasons { get; } =
    [
        "",
        "неверная роль",
        "новая роль",
        "неверная сторона",
        "лишняя сущность",
        "неверные границы",
        "обрезано начало",
        "обрезан конец",
        "дубль",
        "другое"
    ];

    public NerEntityRowViewModel(
        ContractNerEntity entity,
        double autoAcceptScore,
        string documentText)
    {
        Entity = entity;
        _editableRole = entity.Role;
        _editableText = entity.Text;
        OriginalText = entity.Text;
        ContextPreview = BuildContextPreview(documentText, entity.Start, entity.End);
        Score = entity.Score;
        AutoAcceptScore = autoAcceptScore;
        ParagraphIndex = entity.ParagraphIndex;
        IsLowConfidence = entity.Score < autoAcceptScore;
        _isAccepted = !IsLowConfidence;
    }

    public ContractNerEntity Entity { get; }

    public string OriginalText { get; }

    /// <summary>
    /// Итоговый текст сущности. Фрагмент ищется в исходном абзаце и в соседних.
    /// Перевод строки в договоре совпадает с пробелом в ячейке.
    /// </summary>
    public string EditableText
    {
        get => _editableText;
        set
        {
            if (_editableText == value) return;
            _editableText = value;
            SuggestReason("неверные границы");
            OnPropertyChanged();
        }
    }

    /// <summary>Строка исходного NER-текста вокруг найденной сущности.</summary>
    public string ContextPreview { get; }

    public double Score { get; }

    public double AutoAcceptScore { get; }

    public string ScoreDisplay => Score.ToString("0.000");

    /// <summary>
    /// Сторона определяется прежде всего по роли. Контекстный hint — только запасной вариант:
    /// в таблицах DOCX он может «залипнуть» от предыдущего заголовка секции.
    /// </summary>
    public string PartyDisplay => ResolveParty(EditableRole, Entity.PartyHint);

    public bool IsLowConfidence { get; }

    public int ParagraphIndex { get; }

    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            if (_isAccepted == value) return;
            _isAccepted = value;
            if (!value) SuggestReason("лишняя сущность");
            OnPropertyChanged();
        }
    }

    /// <summary>Ключ плейсхолдера (роль), редактируемый.</summary>
    public string EditableRole
    {
        get => _editableRole;
        set
        {
            var next = (value ?? "").Trim();
            if (string.Equals(next, TemplateEditorViewModel.AddNewNerRoleOption, StringComparison.Ordinal))
                next = "";

            // Пока восстанавливаем роль после «Да», игнорируем null/пусто от сброса ComboBox.
            if (_suppressNewRoleConfirm && string.IsNullOrEmpty(next))
                return;

            if (_editableRole == next) return;
            _editableRole = next;
            SuggestReason("неверная роль");
            OnPropertyChanged();
            OnPropertyChanged(nameof(PartyDisplay));

            if (_suppressNewRoleConfirm || string.IsNullOrEmpty(next))
                return;

            _suppressNewRoleConfirm = true;
            try
            {
                if (!NerRoleCatalog.ConfirmPersistNewRole(next))
                {
                    _editableRole = "";
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PartyDisplay));
                }
                else
                {
                    // Refresh списка мог сбросить SelectedItem — вернуть введённую роль.
                    _editableRole = next;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PartyDisplay));
                }
            }
            finally
            {
                _suppressNewRoleConfirm = false;
            }
        }
    }

    public IReadOnlyList<string> CorrectionReasonOptions => AvailableCorrectionReasons;

    public string CorrectionReason
    {
        get => _correctionReason;
        set
        {
            if (_correctionReason == value) return;
            _correctionReason = value;
            OnPropertyChanged();
        }
    }

    public string Comment
    {
        get => _comment;
        set
        {
            if (_comment == value) return;
            _comment = value;
            OnPropertyChanged();
        }
    }

    public bool TryResolveFinalSpan(
        string documentText,
        out int start,
        out int end,
        out string? error)
    {
        start = Entity.Start;
        end = Entity.End;
        error = null;

        if (EditableText == OriginalText)
            return true;
        if (string.IsNullOrEmpty(EditableText))
        {
            error = "Итоговый текст сущности не может быть пустым.";
            return false;
        }

        var lineStart = documentText.LastIndexOf('\n', Math.Max(0, Entity.Start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = documentText.IndexOf('\n', Entity.End);
        if (lineEnd < 0) lineEnd = documentText.Length;

        if (TryFindExact(documentText, lineStart, lineEnd, EditableText, Entity.Start, out start, out end))
            return true;

        var windowStart = lineStart;
        if (lineStart > 0)
        {
            var prevBreak = documentText.LastIndexOf('\n', lineStart - 2);
            windowStart = prevBreak < 0 ? 0 : prevBreak + 1;
        }

        var windowEnd = documentText.Length;
        if (lineEnd < documentText.Length)
        {
            var nextBreak = documentText.IndexOf('\n', lineEnd + 1);
            windowEnd = nextBreak < 0 ? documentText.Length : nextBreak;
        }

        if (TryFindFlexible(documentText, windowStart, windowEnd, EditableText, Entity.Start, out start, out end))
            return true;

        error = $"Текст «{EditableText.Trim()}» не найден в исходном абзаце и соседних.";
        return false;
    }

    private static bool TryFindExact(
        string documentText,
        int windowStart,
        int windowEnd,
        string needle,
        int preferNear,
        out int start,
        out int end)
    {
        var candidates = new List<int>();
        var offset = windowStart;
        while (offset <= windowEnd - needle.Length)
        {
            var found = documentText.IndexOf(needle, offset, windowEnd - offset, StringComparison.Ordinal);
            if (found < 0) break;
            candidates.Add(found);
            offset = found + 1;
        }

        if (candidates.Count == 0)
        {
            start = end = 0;
            return false;
        }

        start = candidates.MinBy(candidate => Math.Abs(candidate - preferNear));
        end = start + needle.Length;
        return true;
    }

    /// <summary>
    /// Ищет текст, считая перевод строки и несколько пробелов одним пробелом.
    /// Границы результата — позиции в исходном тексте, включая перевод строки между абзацами.
    /// </summary>
    private static bool TryFindFlexible(
        string documentText,
        int windowStart,
        int windowEnd,
        string needle,
        int preferNear,
        out int start,
        out int end)
    {
        start = end = 0;
        needle = CollapseWhitespace(needle).Trim();
        if (needle.Length == 0) return false;

        var normalized = new StringBuilder();
        var map = new List<int>();
        var inWhitespace = false;
        for (var i = windowStart; i < windowEnd; i++)
        {
            if (char.IsWhiteSpace(documentText[i]))
            {
                if (normalized.Length == 0 || inWhitespace) continue;
                inWhitespace = true;
                normalized.Append(' ');
                map.Add(i);
                continue;
            }

            inWhitespace = false;
            normalized.Append(documentText[i]);
            map.Add(i);
        }

        while (normalized.Length > 0 && normalized[^1] == ' ')
        {
            normalized.Length--;
            map.RemoveAt(map.Count - 1);
        }

        var haystack = normalized.ToString();
        var candidates = new List<int>();
        var offset = 0;
        while (offset <= haystack.Length - needle.Length)
        {
            var found = haystack.IndexOf(needle, offset, StringComparison.Ordinal);
            if (found < 0) break;
            candidates.Add(found);
            offset = found + 1;
        }

        if (candidates.Count == 0) return false;

        var best = candidates.MinBy(candidate => Math.Abs(map[candidate] - preferNear));
        start = map[best];
        end = map[best + needle.Length - 1] + 1;
        return true;
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var inWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (builder.Length == 0 || inWhitespace) continue;
                inWhitespace = true;
                builder.Append(' ');
                continue;
            }

            inWhitespace = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string ResolveParty(string role, string? partyHint)
    {
        if (role.StartsWith("исполнитель_", StringComparison.OrdinalIgnoreCase))
            return "исполнитель";
        if (role.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase))
            return "заказчик";
        if (role.StartsWith("объект_", StringComparison.OrdinalIgnoreCase) ||
            role.StartsWith("цена_", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("дата_договора", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("номер_договора", StringComparison.OrdinalIgnoreCase))
            return "общая";

        if (string.Equals(partyHint, "исполнитель", StringComparison.OrdinalIgnoreCase))
            return "исполнитель";
        if (string.Equals(partyHint, "заказчик", StringComparison.OrdinalIgnoreCase))
            return "заказчик";

        return "общая";
    }

    private static string BuildContextPreview(string text, int start, int end)
    {
        if (string.IsNullOrEmpty(text)
            || start < 0
            || end <= start
            || end > text.Length)
            return "";

        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        const int sideLength = 60;
        var previewStart = Math.Max(lineStart, start - sideLength);
        var previewEnd = Math.Min(lineEnd, end + sideLength);
        var prefix = previewStart > lineStart ? "… " : "";
        var suffix = previewEnd < lineEnd ? " …" : "";

        var before = text[previewStart..start].Replace('\t', ' ');
        var entity = text[start..end].Replace('\t', ' ');
        var after = text[end..previewEnd].Replace('\t', ' ');
        return $"{prefix}{before}⟦{entity}⟧{after}{suffix}";
    }

    private void SuggestReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(_correctionReason))
        {
            _correctionReason = reason;
            OnPropertyChanged(nameof(CorrectionReason));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
