using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Lassistant.App.ViewModels;

namespace Lassistant.App;

public partial class ContextPreviewWindow : Window
{
    private static ContextPreviewWindow? _open;

    public ContextPreviewWindow()
    {
        InitializeComponent();
    }

    public static void Show(Window? owner, NerEntityRowViewModel row, string? documentText)
    {
        if (string.IsNullOrEmpty(documentText))
        {
            MessageBox.Show(
                owner,
                "Полный текст договора недоступен. Запустите «Найти сведения» ещё раз.",
                "Контекст",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_open is null)
        {
            _open = new ContextPreviewWindow();
            if (owner is not null)
                _open.Owner = owner;
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }

        _open.Title = BuildTitle(row);
        _open.Load(documentText, row.Entity.Start, row.Entity.End);
        _open.Activate();
    }

    private static string BuildTitle(NerEntityRowViewModel row)
    {
        var snippet = row.EditableText.Replace('\n', ' ').Trim();
        if (snippet.Length > 80)
            snippet = snippet[..80] + "…";

        var role = row.EditableRole.Trim();
        return string.IsNullOrEmpty(role)
            ? "Контекст: " + snippet
            : "Контекст: " + role + " — " + snippet;
    }

    private void Load(string text, int start, int end)
    {
        if (start < 0) start = 0;
        if (end < start) end = start;
        if (start > text.Length) start = text.Length;
        if (end > text.Length) end = text.Length;

        var document = new FlowDocument
        {
            PagePadding = new Thickness(16),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14
        };

        Run? highlight = null;
        var index = 0;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            AppendLine(paragraph, line, index, start, end, ref highlight);
            document.Blocks.Add(paragraph);
            index += line.Length + 1;
        }

        Viewer.Document = document;
        if (highlight is null)
            return;

        var marked = highlight;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            var rect = marked.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
                marked.BringIntoView();
            else
                Viewer.ScrollToVerticalOffset(Math.Max(0, rect.Top - 48));
        });
    }

    private static void AppendLine(
        Paragraph paragraph,
        string line,
        int lineStart,
        int start,
        int end,
        ref Run? highlight)
    {
        var lineEnd = lineStart + line.Length;
        if (line.Length == 0 || end <= lineStart || start >= lineEnd)
        {
            paragraph.Inlines.Add(new Run(line));
            return;
        }

        var localStart = Math.Max(0, start - lineStart);
        var localEnd = Math.Min(line.Length, end - lineStart);
        if (localStart > 0)
            paragraph.Inlines.Add(new Run(line[..localStart]));

        var marked = new Run(line[localStart..localEnd])
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF2, 0x99)),
            FontWeight = FontWeights.SemiBold
        };
        paragraph.Inlines.Add(marked);
        highlight ??= marked;

        if (localEnd < line.Length)
            paragraph.Inlines.Add(new Run(line[localEnd..]));
    }
}
