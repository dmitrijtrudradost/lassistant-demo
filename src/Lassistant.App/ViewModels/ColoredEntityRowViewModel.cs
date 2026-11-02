using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lassistant.Templates;

namespace Lassistant.App.ViewModels;

/// <summary>
/// Строка DataGrid во вкладке «Шаблоны»: одно цветное выделение + редактируемый плейсхолдер.
/// </summary>
public sealed class ColoredEntityRowViewModel : INotifyPropertyChanged
{
    private string _editablePlaceholder;
    private string _editableText;
    private bool _isAccepted = true;
    private bool _suppressNewRoleConfirm;

    public ColoredEntityRowViewModel(ColoredEntity entity, string suggestedPlaceholder)
    {
        Entity = entity;
        SuggestedPlaceholder = suggestedPlaceholder;
        _editablePlaceholder = suggestedPlaceholder;
        _editableText = entity.Text;
        ContextPreview = BuildContextPreview(entity.ParagraphContext, entity.Text);
    }

    public ColoredEntity Entity { get; }

    /// <summary>Включать выделение в шаблон при сохранении.</summary>
    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            if (_isAccepted == value) return;
            _isAccepted = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Исходный текст выделения из документа.</summary>
    public string OriginalText => Entity.Text;

    /// <summary>Текст выделения (редактируемый).</summary>
    public string EditableText
    {
        get => _editableText;
        set
        {
            var next = value ?? string.Empty;
            if (_editableText == next) return;
            _editableText = next;
            OnPropertyChanged();
        }
    }

    /// <summary>Совместимость: текущий текст выделения.</summary>
    public string Text => EditableText;

    /// <summary>WPF-имя цвета для фона строки.</summary>
    public string ColorBrush => Entity.Color switch
    {
        HighlightColor.Cyan => "#B2EBF2",
        HighlightColor.Yellow => "#FFF9C4",
        HighlightColor.Green => "#C8E6C9",
        _ => "Transparent"
    };

    /// <summary>Автоматически предложенный ключ плейсхолдера.</summary>
    public string SuggestedPlaceholder { get; }

    /// <summary>Контекст абзаца (до 100 символов вокруг выделения).</summary>
    public string ContextPreview { get; }

    /// <summary>Ключ плейсхолдера, который будет записан в шаблон.</summary>
    public string EditablePlaceholder
    {
        get => _editablePlaceholder;
        set
        {
            var next = (value ?? "").Trim();
            if (string.Equals(next, TemplateEditorViewModel.AddNewNerRoleOption, StringComparison.Ordinal))
                next = "";

            // Пока восстанавливаем роль после «Да», игнорируем null/пусто от сброса ComboBox.
            if (_suppressNewRoleConfirm && string.IsNullOrEmpty(next))
                return;

            if (_editablePlaceholder == next) return;
            _editablePlaceholder = next;
            OnPropertyChanged();

            if (_suppressNewRoleConfirm || string.IsNullOrEmpty(next))
                return;

            _suppressNewRoleConfirm = true;
            try
            {
                if (!NerRoleCatalog.ConfirmPersistNewRole(next))
                {
                    _editablePlaceholder = "";
                    OnPropertyChanged();
                }
                else
                {
                    _editablePlaceholder = next;
                    OnPropertyChanged();
                }
            }
            finally
            {
                _suppressNewRoleConfirm = false;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string BuildContextPreview(string paragraphContext, string entityText)
    {
        if (string.IsNullOrEmpty(paragraphContext))
            return string.Empty;

        const int sideLength = 100;
        var normalized = paragraphContext.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        var entity = (entityText ?? string.Empty).Replace('\t', ' ');

        var idx = string.IsNullOrEmpty(entity)
            ? -1
            : normalized.IndexOf(entity, StringComparison.Ordinal);
        if (idx < 0)
        {
            return normalized.Length > sideLength
                ? normalized[..sideLength] + "…"
                : normalized;
        }

        var end = idx + entity.Length;
        var previewStart = Math.Max(0, idx - sideLength);
        var previewEnd = Math.Min(normalized.Length, end + sideLength);
        var prefix = previewStart > 0 ? "… " : "";
        var suffix = previewEnd < normalized.Length ? " …" : "";
        var before = normalized[previewStart..idx];
        var after = normalized[end..previewEnd];
        return $"{prefix}{before}⟦{entity}⟧{after}{suffix}";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
