using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lassistant.App.ViewModels;
using Microsoft.Win32;

namespace Lassistant.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        DropZone.DragOver += DropZone_DragOver;
        DropZone.Drop += DropZone_Drop;

        NerAnonDropZone.DragOver += NerAnonDropZone_DragOver;
        NerAnonDropZone.Drop += NerAnonDropZone_Drop;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        await _viewModel.ImportFilesAsync(files);
    }

    private async void BrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Документы|*.xlsx;*.xlsm;*.docx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.md|Карточка заказчика (*.md)|*.md|Все файлы|*.*"
        };

        if (dialog.ShowDialog() == true)
            await _viewModel.ImportFilesAsync(dialog.FileNames);
    }

    private void GenerateDocument_Click(object sender, RoutedEventArgs e) =>
        _viewModel.GenerateDocument();

    private void UpdateCustomerCard_Click(object sender, RoutedEventArgs e) =>
        _viewModel.UpdateCustomerCard();

    private void NerAnonDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void NerAnonDropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var file = files.FirstOrDefault(f =>
            f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));

        if (file is not null)
            _viewModel.NerAnonymization.LoadFile(file);
    }

    private void BrowseNerAnonymizationFile_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NerAnonymization.BrowseSourceFile();

    private void AnalyzeNerAnonymization_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NerAnonymization.AnalyzeWithContractNerAsync();

    private void AnonymizeNerAnonymization_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NerAnonymization.Anonymize();

    private void SaveNerAnonymization_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NerAnonymization.Save();

    private void RestoreNerAnonymization_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NerAnonymization.RestoreFromMapping();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow { Owner = this };
        if (window.ShowDialog() == true)
            _viewModel.ReloadAfterSettings();
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void NerContext_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NerEntityRowViewModel row)
            return;

        ContextPreviewWindow.Show(this, row, _viewModel.NerAnonymization.NerDocumentText);
        e.Handled = true;
    }
}
