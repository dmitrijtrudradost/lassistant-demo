using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Lassistant.Core.Services;
using Lassistant.Templates;

namespace Lassistant.App;

public partial class AboutWindow : Window
{
    private static readonly string[] GuideCandidates =
    [
        "rukovodstvo_polzovatelya.html",
        "Руководство_пользователя.pdf",
        "rukovodstvo_polzovatelya.md"
    ];

    private static readonly string[] DictionaryCandidates =
    [
        "slovar_poley_shablona.html"
    ];

    public AboutWindow()
    {
        InitializeComponent();

        ProductText.Text = AppInfo.ProductName;
        VersionText.Text = "Версия: " + AppInfo.Version;
        NerVersionText.Text = "Версия модуля NER: " + TryGetNerVersion();

        BuildDocLinks();
    }

    private static string TryGetNerVersion()
    {
        try
        {
            return ContractNerService.ResolveDisplayedModelVersion(ContractNerService.LoadSettings());
        }
        catch
        {
            return "не определена";
        }
    }

    private void BuildDocLinks()
    {
        var docsDir = Path.Combine(AppPaths.BaseDirectory, "docs", "руководства");

        AddDocLink("Руководство пользователя", Resolve(docsDir, GuideCandidates));
        AddDocLink("Словарь полей шаблона", Resolve(docsDir, DictionaryCandidates));
        AddDocLink("Инструкция по установке", ResolveInstall(docsDir));

        if (Directory.Exists(docsDir))
            AddDocLink("Папка с документацией", docsDir);
    }

    private static string? Resolve(string docsDir, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(docsDir, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string? ResolveInstall(string docsDir)
    {
        if (!Directory.Exists(docsDir))
            return null;

        // Инструкция называется по версии (INSTALL_v1.6.6.html) — берём самую свежую.
        foreach (var pattern in new[] { "INSTALL_*.html", "INSTALL_*.md" })
        {
            var found = Directory.GetFiles(docsDir, pattern)
                .OrderByDescending(InstallVersion)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (found is not null)
                return found;
        }

        return null;
    }

    private static Version InstallVersion(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "INSTALL_v";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(name[prefix.Length..], out var version))
            return version;

        return new Version(0, 0);
    }

    private void AddDocLink(string title, string? path)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };

        if (path is null)
        {
            line.Text = title + " — файл не найден";
            line.Foreground = System.Windows.Media.Brushes.Gray;
            DocsPanel.Children.Add(line);
            return;
        }

        var link = new Hyperlink(new Run(title)) { NavigateUri = new Uri(path), ToolTip = path };
        link.RequestNavigate += Link_RequestNavigate;
        line.Inlines.Add(link);
        DocsPanel.Children.Add(line);
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.LocalPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть файл:\n" + ex.Message, "О программе",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
