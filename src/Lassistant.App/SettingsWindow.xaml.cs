using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Lassistant.App.ViewModels;
using Lassistant.Core.Config;
using Lassistant.Core.Services;
using Lassistant.Templates;
using Microsoft.Win32;

namespace Lassistant.App;

public sealed class ExecutorFieldRow : INotifyPropertyChanged
{
    private string _key = "";
    private string _value = "";

    public string Key
    {
        get => _key;
        set { _key = value ?? ""; OnPropertyChanged(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value ?? ""; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class SettingsWindow : Window
{
    private readonly ContractNerSettings _nerSettings;
    private readonly ObservableCollection<ExecutorCard> _editorExecutors = [];
    private readonly ObservableCollection<ExecutorFieldRow> _editorFields = [];

    public SettingsWindow()
    {
        InitializeComponent();
        _nerSettings = ContractNerService.LoadSettings();
        ExecutorFieldsGrid.ItemsSource = _editorFields;
        ExecutorsList.ItemsSource = _editorExecutors;
        LoadUi();
    }

    private void LoadUi()
    {
        UserRootHint.Text = AppPaths.UserRootDirectory;
        AppVersionText.Text = "Версия программы: " + GetAppVersion();
        var user = UserSettingsStore.Current;
        RolesPathBox.Text = UserSettingsStore.ResolvePath(user.RolesFile, AppPaths.DefaultNerUserRolesRelative);
        CardsPathBox.Text = UserSettingsStore.ResolvePath(user.CustomerCardsDirectory, AppPaths.DefaultCustomerCardsRelative);
        ModeUserOnlyRadio.IsChecked = user.ExecutorsMode == UserSettings.ModeUserOnly;
        ModeMergeRadio.IsChecked = !ModeUserOnlyRadio.IsChecked;

        ModelVersionText.Text = ContractNerService.ResolveDisplayedModelVersion(_nerSettings);
        AppNerVersionText.Text = "Версия модуля NER: " + ModelVersionText.Text;
        var models = ContractNerService.ListAvailableModels();
        ModelCombo.ItemsSource = models;
        var currentFolder = Path.GetFileName(_nerSettings.ModelPath.Replace('\\', '/').TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(currentFolder) &&
            models.Contains(currentFolder, StringComparer.OrdinalIgnoreCase))
        {
            ModelCombo.SelectedItem = models.First(m =>
                string.Equals(m, currentFolder, StringComparison.OrdinalIgnoreCase));
        }
        else if (models.Count > 0)
        {
            ModelCombo.SelectedIndex = 0;
        }

        UpdateModelPathHint();
        ModelCombo.SelectionChanged += (_, _) =>
        {
            UpdateModelPathHint();
            RefreshVersionPreview();
        };

        MinScoreCombo.ItemsSource = NerScoreThresholdOption.All;
        AutoAcceptCombo.ItemsSource = NerScoreThresholdOption.All;
        MinScoreCombo.SelectedItem = Nearest(_nerSettings.MinScore);
        AutoAcceptCombo.SelectedItem = Nearest(_nerSettings.AutoAcceptScore);

        ReloadExecutorsList();
    }

    private void ReloadExecutorsList()
    {
        var selectedId = (ExecutorsList.SelectedItem as ExecutorCard)?.Id;
        _editorExecutors.Clear();
        foreach (var card in ExecutorsConfig.Load().Executors)
            _editorExecutors.Add(card.Clone());
        if (_editorExecutors.Count == 0)
            return;
        var match = _editorExecutors.FirstOrDefault(e =>
            string.Equals(e.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        ExecutorsList.SelectedItem = match ?? _editorExecutors[0];
    }

    private void ExecutorsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ExecutorsList.SelectedItem is not ExecutorCard card)
            return;
        ExecutorIdBox.Text = card.Id;
        ExecutorNameBox.Text = card.DisplayName;
        _editorFields.Clear();
        foreach (var kv in card.Fields.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            _editorFields.Add(new ExecutorFieldRow { Key = kv.Key, Value = kv.Value });
    }

    private void AddExecutor_Click(object sender, RoutedEventArgs e)
    {
        var id = "user_" + DateTime.Now.ToString("HHmmss");
        var card = new ExecutorCard
        {
            Id = id,
            DisplayName = "Новый исполнитель",
            Fields = ExecutorsConfig.CreateEmptyStandardFields()
        };
        _editorExecutors.Add(card);
        ExecutorsList.SelectedItem = card;
    }

    private void DeleteExecutor_Click(object sender, RoutedEventArgs e)
    {
        if (ExecutorsList.SelectedItem is not ExecutorCard card)
            return;
        var user = ExecutorsConfig.LoadUser();
        user.Executors.RemoveAll(x => string.Equals(x.Id, card.Id, StringComparison.OrdinalIgnoreCase));
        ExecutorsConfig.SaveUser(user);
        ReloadExecutorsList();
    }

    private void AddFieldRow_Click(object sender, RoutedEventArgs e)
    {
        CommitExecutorGrid();
        var row = new ExecutorFieldRow { Key = "исполнитель_" };
        _editorFields.Add(row);
        ExecutorFieldsGrid.SelectedItem = row;
        ExecutorFieldsGrid.ScrollIntoView(row);
        ExecutorFieldsGrid.CurrentCell = new DataGridCellInfo(row, ExecutorFieldsGrid.Columns[0]);
        ExecutorFieldsGrid.BeginEdit();
    }

    private void DeleteFieldRow_Click(object sender, RoutedEventArgs e)
    {
        CommitExecutorGrid();
        if (ExecutorFieldsGrid.SelectedItem is not ExecutorFieldRow row)
            return;
        _editorFields.Remove(row);
    }

    private void CommitExecutorGrid()
    {
        ExecutorFieldsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ExecutorFieldsGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void SaveExecutor_Click(object sender, RoutedEventArgs e)
    {
        CommitExecutorGrid();
        var id = (ExecutorIdBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show(this, "Укажите код (id) исполнителя.", "Настройки",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _editorFields)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
                continue;
            fields[row.Key.Trim()] = row.Value?.Trim() ?? "";
        }

        var card = new ExecutorCard
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(ExecutorNameBox.Text)
                ? id
                : ExecutorNameBox.Text.Trim(),
            Fields = fields
        };

        var user = ExecutorsConfig.LoadUser();
        user.Executors.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        user.Executors.Add(card);
        if (string.IsNullOrWhiteSpace(user.DefaultExecutorId))
            user.DefaultExecutorId = id;
        ExecutorsConfig.SaveUser(user);
        ReloadExecutorsList();
        var match = _editorExecutors.FirstOrDefault(x =>
            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            ExecutorsList.SelectedItem = match;
        MessageBox.Show(this, "Исполнитель сохранён в user/settings/executors.json.", "Настройки",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetUserExecutors_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            this,
            "Удалить личный файл исполнителей? Останутся только карточки из поставки (config).",
            "Настройки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
            return;
        ExecutorsConfig.DeleteUserFile();
        ReloadExecutorsList();
    }

    private void BrowseRolesFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Файл пользовательских ролей NER",
            Filter = "JSON (*.json)|*.json",
            FileName = "ner-user-roles.json",
            InitialDirectory = Directory.Exists(AppPaths.UserSettingsDirectory)
                ? AppPaths.UserSettingsDirectory
                : AppPaths.BaseDirectory
        };
        if (dialog.ShowDialog(this) == true)
            RolesPathBox.Text = dialog.FileName;
    }

    private void BrowseCardsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Папка карточек заказчика",
            InitialDirectory = Directory.Exists(AppPaths.CustomerCardsDirectory)
                ? AppPaths.CustomerCardsDirectory
                : AppPaths.BaseDirectory
        };
        if (dialog.ShowDialog(this) == true)
            CardsPathBox.Text = dialog.FolderName;
    }

    private void OpenUserFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.UserRootDirectory);
        Process.Start(new ProcessStartInfo(AppPaths.UserRootDirectory) { UseShellExecute = true });
    }

    private void OpenAppFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.BaseDirectory) { UseShellExecute = true });

    private void RefreshVersionPreview()
    {
        var folder = ModelCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var preview = new ContractNerSettings
        {
            ModelPath = BuildModelPath(folder),
            ModelsRoot = _nerSettings.ModelsRoot,
            ModelVersion = GuessVersionFromFolder(folder, _nerSettings.ModelVersion)
        };
        ModelVersionText.Text = ContractNerService.ResolveDisplayedModelVersion(preview);
        AppNerVersionText.Text = "Версия модуля NER: " + ModelVersionText.Text;
    }

    private static string GetAppVersion()
    {
        var asm = typeof(App).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "1.6.6.2";
    }

    private string BuildModelPath(string folder)
    {
        var root = string.IsNullOrWhiteSpace(_nerSettings.ModelsRoot)
            ? "models/contract-ner"
            : _nerSettings.ModelsRoot.Trim().Replace('\\', '/').TrimEnd('/');
        return $"{root}/{folder}".Replace('/', Path.DirectorySeparatorChar);
    }

    private void UpdateModelPathHint()
    {
        var folder = ModelCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(folder))
        {
            ModelPathHint.Text = "Каталоги ner_model_* не найдены в models/contract-ner.";
            return;
        }

        ModelPathHint.Text = BuildModelPath(folder).Replace('\\', '/');
    }

    private static NerScoreThresholdOption Nearest(double value) =>
        NerScoreThresholdOption.All.OrderBy(o => Math.Abs(o.Value - value)).First();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var folder = ModelCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "Выберите каталог модели ner_model_*.", "Настройки",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rolesPath = (RolesPathBox.Text ?? "").Trim();
        var cardsPath = (CardsPathBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(rolesPath) || string.IsNullOrWhiteSpace(cardsPath))
        {
            MessageBox.Show(this, "Укажите путь к файлу ролей и папке карточек.", "Настройки",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UserSettingsStore.Save(new UserSettings
        {
            RolesFile = UserSettingsStore.ToStoredPath(rolesPath),
            CustomerCardsDirectory = UserSettingsStore.ToStoredPath(cardsPath),
            ExecutorsMode = ModeUserOnlyRadio.IsChecked == true
                ? UserSettings.ModeUserOnly
                : UserSettings.ModeMerge
        });
        UserDataBootstrap.Ensure();

        _nerSettings.ModelPath = BuildModelPath(folder);
        _nerSettings.ModelVersion = GuessVersionFromFolder(folder, _nerSettings.ModelVersion);
        if (MinScoreCombo.SelectedItem is NerScoreThresholdOption min)
            _nerSettings.MinScore = min.Value;
        if (AutoAcceptCombo.SelectedItem is NerScoreThresholdOption auto)
            _nerSettings.AutoAcceptScore = auto.Value;
        _nerSettings.ModelVersion = ContractNerService.ResolveDisplayedModelVersion(_nerSettings);
        ContractNerService.SaveSettings(_nerSettings);

        DialogResult = true;
        Close();
    }

    private static string GuessVersionFromFolder(string folder, string fallback)
    {
        if (folder.Contains("1.1", StringComparison.OrdinalIgnoreCase))
            return "1.1.1";
        if (folder.Contains("1.0", StringComparison.OrdinalIgnoreCase)
            || folder.Contains("final", StringComparison.OrdinalIgnoreCase))
            return "1.0";
        return string.IsNullOrWhiteSpace(fallback) ? "1.1.1" : fallback;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
