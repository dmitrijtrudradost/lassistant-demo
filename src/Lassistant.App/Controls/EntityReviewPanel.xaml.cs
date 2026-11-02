using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lassistant.App.ViewModels;

namespace Lassistant.App.Controls;

public partial class EntityReviewPanel : UserControl
{
    private TemplateEditorViewModel? _observedViewModel;

    public EntityReviewPanel()
    {
        InitializeComponent();
        TemplateDropZone.DragOver += DropZone_DragOver;
        TemplateDropZone.Drop     += DropZone_Drop;
        DataContextChanged += EntityReviewPanel_DataContextChanged;
    }

    private TemplateEditorViewModel? ViewModel =>
        DataContext as TemplateEditorViewModel;

    private void EntityReviewPanel_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _observedViewModel = e.NewValue as TemplateEditorViewModel;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;

        UpdateResultsLayout();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TemplateEditorViewModel.HasColoredEntities))
            UpdateResultsLayout();
    }

    private void UpdateResultsLayout()
    {
        var showColoredResults = _observedViewModel?.HasColoredEntities == true;

        ResultsSplitter.Visibility = showColoredResults
            ? Visibility.Visible
            : Visibility.Collapsed;
        ColoredResultsPanel.Visibility = showColoredResults
            ? Visibility.Visible
            : Visibility.Collapsed;

        ResultsSplitterRow.Height = showColoredResults
            ? new GridLength(6)
            : new GridLength(0);
        ColoredResultsRow.MinHeight = showColoredResults ? 100 : 0;
        ColoredResultsRow.Height = showColoredResults
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        NerResultsRow.Height = showColoredResults
            ? new GridLength(2, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        var docx = files.FirstOrDefault(f =>
            f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));

        if (docx is not null)
            ViewModel?.LoadFile(docx);
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.BrowseSourceFile();

    private void SaveTemplate_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.SaveTemplate();

    private void AnalyzeNer_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.AnalyzeWithContractNerAsync();

    private void NerContext_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NerEntityRowViewModel row)
            return;

        ContextPreviewWindow.Show(Window.GetWindow(this), row, ViewModel?.NerDocumentText);
        e.Handled = true;
    }
}
