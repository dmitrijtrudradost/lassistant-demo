using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Lassistant.Core.Services;
using Lassistant.Templates;
using Microsoft.Win32;

namespace Lassistant.App.ViewModels;

/// <summary>
/// Вкладка «Анонимизация»: детект как в «Шаблонах»,
/// замена на {{роль}}, сохранение DOCX + mapping отдельно.
/// </summary>
public sealed class NerAnonymizationViewModel : INotifyPropertyChanged
{
    private string _sourceFilePath = string.Empty;
    private string _statusMessage = "Загрузите договор .docx, нажмите «Найти сведения», затем «Анонимизировать» → «Сохранить».";
    private bool _isProcessing;
    private NerProcessingModeOption _selectedNerProcessingMode;
    private NerScoreThresholdOption _selectedNerScoreThreshold;
    private ContractNerDocument? _lastNerDocument;
    private string? _pendingAnonTempPath;
    private AnonymizationMapping? _pendingMapping;
    private bool _canSave;

    public NerAnonymizationViewModel()
    {
        NerEntities = new ObservableCollection<NerEntityRowViewModel>();
        NerRoleOptions = new ObservableCollection<string>();
        RefreshNerRoleOptions();
        PlaceholderRolesStore.Changed += (_, _) =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                RefreshNerRoleOptions();
            else
                dispatcher.Invoke(RefreshNerRoleOptions);
        };
        NerScoreThresholds = NerScoreThresholdOption.All;
        NerProcessingModes =
        [
            new(
                ContractNerProcessingModes.Production,
                "Рабочий (рекомендуется)",
                "NER → Python-постобработка → C#-агрегатор."),
            new(
                ContractNerProcessingModes.PythonPostprocess,
                "Диагностика: без C#-агрегатора",
                "NER → Python-постобработка без C#-агрегатора."),
            new(
                ContractNerProcessingModes.Raw,
                "Голая NER-модель",
                "Только модель, без постобработки и агрегатора.")
        ];

        var nerSettings = ContractNerService.LoadSettings();
        var configuredMode = ContractNerProcessingModes.Normalize(nerSettings.ProcessingMode);
        _selectedNerProcessingMode = NerProcessingModes.First(o => o.Value == configuredMode);
        _selectedNerScoreThreshold = NerScoreThresholds
            .OrderBy(o => Math.Abs(o.Value - nerSettings.MinScore))
            .First();
    }

    public ObservableCollection<NerEntityRowViewModel> NerEntities { get; }

    public bool HasNerEntities => NerEntities.Count > 0;

    public bool? AllNerAccepted
    {
        get
        {
            if (NerEntities.Count == 0) return false;
            var accepted = NerEntities.Count(row => row.IsAccepted);
            if (accepted == 0) return false;
            if (accepted == NerEntities.Count) return true;
            return null;
        }
        set
        {
            if (!value.HasValue) return;
            foreach (var row in NerEntities)
                row.IsAccepted = value.Value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> NerRoleOptions { get; }

    public void RefreshNerRoleOptions()
    {
        var roles = LoadNerRoleOptions();
        NerRoleOptions.Clear();
        foreach (var role in roles)
            NerRoleOptions.Add(role);
        NerRoleCatalog.SetKnownRoles(roles);
    }

    public IReadOnlyList<NerProcessingModeOption> NerProcessingModes { get; }

    public IReadOnlyList<NerScoreThresholdOption> NerScoreThresholds { get; }

    public NerProcessingModeOption SelectedNerProcessingMode
    {
        get => _selectedNerProcessingMode;
        set
        {
            if (value is null || value.Value == _selectedNerProcessingMode.Value) return;
            _selectedNerProcessingMode = value;
            OnPropertyChanged();
            ClearNerEntities();
            ClearPendingAnonymization();
            OnPropertyChanged(nameof(HasNerEntities));
            StatusMessage = $"Выбран режим NER: {value.Title}. Запустите разбор заново.";
        }
    }

    public NerScoreThresholdOption SelectedNerScoreThreshold
    {
        get => _selectedNerScoreThreshold;
        set
        {
            if (value is null || Math.Abs(value.Value - _selectedNerScoreThreshold.Value) < 0.0001)
                return;
            _selectedNerScoreThreshold = value;
            OnPropertyChanged();
            ClearNerEntities();
            ClearPendingAnonymization();
            OnPropertyChanged(nameof(HasNerEntities));
            StatusMessage = $"Выбрана минимальная уверенность {value.Value:0.00}. Запустите разбор NER заново.";
        }
    }

    public string SourceFilePath
    {
        get => _sourceFilePath;
        private set
        {
            _sourceFilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSourceFile));
            OnPropertyChanged(nameof(CanOperate));
            OnPropertyChanged(nameof(CanAnalyzeNer));
            OnPropertyChanged(nameof(CanAnonymize));
        }
    }

    public bool HasSourceFile => !string.IsNullOrEmpty(SourceFilePath);

    /// <summary>Плоский текст договора, по которому считались смещения NER.</summary>
    public string? NerDocumentText => _lastNerDocument?.Text;

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            _isProcessing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOperate));
            OnPropertyChanged(nameof(CanAnalyzeNer));
            OnPropertyChanged(nameof(CanAnonymize));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool CanOperate => !IsProcessing;

    public bool CanAnalyzeNer => !IsProcessing && HasSourceFile;

    public bool CanAnonymize => !IsProcessing && HasSourceFile && HasNerEntities;

    public bool CanSave
    {
        get => _canSave && !IsProcessing && _pendingAnonTempPath is not null && File.Exists(_pendingAnonTempPath);
        private set
        {
            _canSave = value;
            OnPropertyChanged();
        }
    }

    public void BrowseSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите договор .docx",
            Filter = "Word документы|*.docx|Все файлы|*.*"
        };

        if (dialog.ShowDialog() == true)
            LoadFile(dialog.FileName);
    }

    public void LoadFile(string path)
    {
        if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Поддерживаются только файлы .docx.";
            return;
        }

        SourceFilePath = path;
        _lastNerDocument = null;
        ClearNerEntities();
        ClearPendingAnonymization();
        OnPropertyChanged(nameof(HasNerEntities));
        OnPropertyChanged(nameof(CanAnonymize));
        StatusMessage = $"Загружен: {Path.GetFileName(path)}. Нажмите «Найти сведения».";
    }

    public async void AnalyzeWithContractNerAsync()
    {
        if (!HasSourceFile)
        {
            MessageBox.Show("Сначала загрузите договор .docx.",
                "Анонимизация", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var avail = ContractNerService.CheckAvailability();
        if (!avail.IsAvailable)
        {
            MessageBox.Show(
                (avail.Reason ?? "Модуль NER недоступен.") + "\n\n" +
                "См. scripts/contract_ner/README.md",
                "Модуль NER не установлен", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsProcessing = true;
        StatusMessage = "Извлечение текста договора…";
        ClearPendingAnonymization();

        try
        {
            var nerDocument = DocxPlainTextExtractor.ExtractForNer(SourceFilePath);
            if (string.IsNullOrWhiteSpace(nerDocument.Text))
            {
                StatusMessage = "В документе нет текста.";
                MessageBox.Show("Не удалось извлечь текст из .docx.",
                    "Анонимизация", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusMessage = "Локальный NER… (первый запуск может занять 1–2 минуты)";
            var entities = await ContractNerService.AnalyzeDocumentAsync(
                nerDocument,
                SelectedNerProcessingMode.Value,
                SelectedNerScoreThreshold.Value);

            _lastNerDocument = nerDocument;
            ClearNerEntities();
            foreach (var e in entities)
                AddNerEntity(new NerEntityRowViewModel(
                    e,
                    avail.Settings.AutoAcceptScore,
                    nerDocument.Text));

            OnPropertyChanged(nameof(HasNerEntities));
            OnPropertyChanged(nameof(AllNerAccepted));
            OnPropertyChanged(nameof(CanAnonymize));

            var acceptedCount = NerEntities.Count(e => e.IsAccepted);
            var weakCount = NerEntities.Count - acceptedCount;
            StatusMessage = NerEntities.Count > 0
                ? $"{SelectedNerProcessingMode.Title}, уверенность ≥ {SelectedNerScoreThreshold.Value:0.00}: " +
                  $"показано {NerEntities.Count}, выбрано {acceptedCount}, слабых — {weakCount}. " +
                  "Проверьте галочки и нажмите «Анонимизировать»."
                : "NER не нашёл сущностей.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка NER: {ex.Message}";
            MessageBox.Show($"Локальный NER не выполнен:\n{ex.Message}",
                "Ошибка NER", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void Anonymize()
    {
        if (!HasSourceFile || _lastNerDocument is null)
        {
            MessageBox.Show("Сначала загрузите файл и нажмите «Найти сведения».",
                "Анонимизация", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var accepted = NerEntities
            .Where(e => e.IsAccepted && !string.IsNullOrWhiteSpace(e.EditableRole))
            .ToList();

        if (accepted.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одну сущность (галочка «принять»).",
                "Анонимизация", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsProcessing = true;
            StatusMessage = "Анонимизация…";

            var spans = new List<(int Start, int End, string Role)>();
            var replacements = new List<MappingReplacement>();

            foreach (var row in accepted)
            {
                if (!row.TryResolveFinalSpan(
                        _lastNerDocument.Text,
                        out var start,
                        out var end,
                        out var error))
                {
                    throw new InvalidOperationException(
                        $"Не удалось применить «{row.EditableText}»: {error}");
                }

                var role = row.EditableRole.Trim();
                var original = _lastNerDocument.Text[start..end];
                spans.Add((start, end, role));
                replacements.Add(new MappingReplacement
                {
                    Placeholder = "{{" + role + "}}",
                    Original = original,
                    Category = role,
                    Confidence = row.Score >= row.AutoAcceptScore ? "high" : "low"
                });
            }

            // Как в NerSpanToTemplateConverter: один текст → одна замена; mapping — по placeholder.
            var mappingReplacements = replacements
                .GroupBy(r => r.Original, StringComparer.Ordinal)
                .Select(g => g.First())
                .GroupBy(r => r.Placeholder, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            ClearPendingAnonymization();
            _pendingAnonTempPath = Path.Combine(
                Path.GetTempPath(),
                "lassistant_ner_anon_" + Guid.NewGuid().ToString("N") + ".docx");

            NerSpanToTemplateConverter.Convert(
                SourceFilePath,
                _pendingAnonTempPath,
                _lastNerDocument,
                spans);

            _pendingMapping = new AnonymizationMapping
            {
                SourceFile = Path.GetFileName(SourceFilePath),
                CreatedAt = DateTimeOffset.Now,
                AppVersion = AppInfo.Version,
                SearchMode = "ContractNer",
                Replacements = mappingReplacements
            };

            CanSave = true;
            StatusMessage =
                $"Анонимизировано в памяти: {mappingReplacements.Count} плейсхолдеров {{{{роль}}}}. " +
                "Нажмите «Сохранить».";
        }
        catch (Exception ex)
        {
            ClearPendingAnonymization();
            StatusMessage = $"Ошибка анонимизации: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка анонимизации", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void Save()
    {
        if (!CanSave || _pendingAnonTempPath is null || _pendingMapping is null)
        {
            MessageBox.Show("Сначала выполните «Анонимизировать».",
                "Анонимизация", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить анонимизированный документ",
            Filter = "Word|*.docx",
            FileName = Path.GetFileNameWithoutExtension(SourceFilePath) + "_anon_ner.docx",
            InitialDirectory = Path.GetDirectoryName(SourceFilePath)
                               ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true)
            return;

        var outputPath = dialog.FileName;
        var mappingPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(outputPath) + ".mapping.json");

        try
        {
            IsProcessing = true;
            AppPaths.EnsureDirectory(outputPath);
            File.Copy(_pendingAnonTempPath, outputPath, overwrite: true);

            var mapping = new AnonymizationMapping
            {
                Version = _pendingMapping.Version,
                SourceFile = _pendingMapping.SourceFile,
                CreatedAt = DateTimeOffset.Now,
                AppVersion = AppInfo.Version,
                SearchMode = _pendingMapping.SearchMode,
                Language = _pendingMapping.Language,
                Replacements = _pendingMapping.Replacements
            };
            MappingFileService.Save(mapping, mappingPath);

            StatusMessage = $"Сохранено: {outputPath}";
            MessageBox.Show(
                $"Документ: {outputPath}\nMapping: {mappingPath}\n\n" +
                "Плейсхолдеры в формате {{роль}} — как во вкладке «Шаблоны».",
                "Анонимизация",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void RestoreFromMapping()
    {
        var fileDialog = new OpenFileDialog
        {
            Filter = "Word|*.docx",
            Title = "Анонимизированный документ"
        };
        if (fileDialog.ShowDialog() != true)
            return;

        var mappingDialog = new OpenFileDialog
        {
            Filter = "Mapping JSON|*.mapping.json;*.json",
            FileName = Path.GetFileNameWithoutExtension(fileDialog.FileName) + ".mapping.json",
            Title = "Файл mapping"
        };
        if (mappingDialog.ShowDialog() != true)
            return;

        var saveDialog = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(fileDialog.FileName) + "_restored.docx",
            Filter = "Word|*.docx",
            Title = "Куда сохранить восстановленный документ"
        };
        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            IsProcessing = true;
            DocxDeAnonymizer.Restore(
                fileDialog.FileName,
                saveDialog.FileName,
                MappingFileService.Load(mappingDialog.FileName));
            StatusMessage = $"Восстановлено: {saveDialog.FileName}";
            MessageBox.Show($"Документ восстановлен:\n{saveDialog.FileName}",
                "Деанонимизация", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка восстановления", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = $"Ошибка восстановления: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void AddNerEntity(NerEntityRowViewModel row)
    {
        row.PropertyChanged += NerEntity_PropertyChanged;
        NerEntities.Add(row);
    }

    private void ClearNerEntities()
    {
        foreach (var row in NerEntities)
            row.PropertyChanged -= NerEntity_PropertyChanged;
        NerEntities.Clear();
        OnPropertyChanged(nameof(AllNerAccepted));
    }

    private void NerEntity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NerEntityRowViewModel.IsAccepted))
            OnPropertyChanged(nameof(AllNerAccepted));
    }

    private void ClearPendingAnonymization()
    {
        if (_pendingAnonTempPath is not null)
        {
            try
            {
                if (File.Exists(_pendingAnonTempPath))
                    File.Delete(_pendingAnonTempPath);
            }
            catch
            {
                // temp cleanup best-effort
            }
        }

        _pendingAnonTempPath = null;
        _pendingMapping = null;
        CanSave = false;
    }

    private static IReadOnlyList<string> LoadNerRoleOptions()
    {
        var roles = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            var labelsPath = ResolveNerLabelsPath();
            if (labelsPath is not null && File.Exists(labelsPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(labelsPath));
                if (doc.RootElement.TryGetProperty("label2id", out var label2id))
                {
                    foreach (var property in label2id.EnumerateObject())
                    {
                        var label = property.Name;
                        if (label is "O" or "o") continue;
                        if (label.StartsWith("B-", StringComparison.Ordinal)
                            || label.StartsWith("I-", StringComparison.Ordinal))
                            label = label[2..];
                        if (!string.IsNullOrWhiteSpace(label))
                            roles.Add(label);
                    }
                }
            }
        }
        catch
        {
            // fallback / placeholder-roles below
        }

        foreach (var role in PlaceholderRolesStore.LoadCanonRoles())
            roles.Add(role);
        foreach (var role in PlaceholderRolesStore.LoadRoles())
            roles.Add(role);

        if (roles.Count == 0)
        {
            foreach (var role in FallbackNerRoles)
                roles.Add(role);
        }

        var list = roles.ToList();
        list.Add(TemplateEditorViewModel.AddNewNerRoleOption);
        return list;
    }

    private static string? ResolveNerLabelsPath()
    {
        var avail = ContractNerService.CheckAvailability();
        if (!string.IsNullOrWhiteSpace(avail.ModelPath))
        {
            var fromAvail = Path.Combine(avail.ModelPath, "labels.json");
            if (File.Exists(fromAvail)) return fromAvail;
        }

        var settings = ContractNerService.LoadSettings();
        var configured = settings.ModelPath;
        if (string.IsNullOrWhiteSpace(configured)) return null;

        var candidates = new[]
        {
            Path.IsPathRooted(configured)
                ? Path.Combine(configured, "labels.json")
                : Path.Combine(AppPaths.BaseDirectory, configured, "labels.json"),
            Path.Combine(AppPaths.BaseDirectory, "models", "contract-ner", "ner_model_final", "labels.json"),
            Path.GetFullPath(Path.Combine(AppPaths.BaseDirectory, "..", "..", "..", "..", "..",
                "models", "contract-ner", "ner_model_final", "labels.json")),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static readonly string[] FallbackNerRoles =
    [
        "дата_договора",
        "договор_штраф_сумма",
        "заказчик_email",
        "заказчик_адрес_юридический",
        "заказчик_банк",
        "заказчик_бик",
        "заказчик_дата_огрнип",
        "заказчик_дата_рождения",
        "заказчик_должность",
        "заказчик_должность_падеж",
        "заказчик_инн",
        "заказчик_источник_финансирования",
        "заказчик_корр_счет",
        "заказчик_наименование",
        "заказчик_наименование_кратко",
        "заказчик_наименование_кратко_падеж",
        "заказчик_наименование_падеж",
        "заказчик_огрн",
        "заказчик_огрнип",
        "заказчик_окпо",
        "заказчик_паспорт",
        "заказчик_расчетный_счет",
        "заказчик_телефон",
        "заказчик_фио",
        "заказчик_фио_кратко",
        "идентификационный_код_закупки",
        "исполнитель_email",
        "исполнитель_банк",
        "исполнитель_бик",
        "исполнитель_город_банка",
        "исполнитель_дата_лицензии",
        "исполнитель_директор_фио_падеж",
        "исполнитель_директор_фио_кратко",
        "исполнитель_директор_фио_падеж",
        "исполнитель_инн",
        "исполнитель_корр_счет",
        "исполнитель_кпп",
        "исполнитель_лицензия_кем_выдана",
        "исполнитель_наименование",
        "исполнитель_наименование_кратко",
        "исполнитель_номер_лицензии",
        "исполнитель_огрн",
        "исполнитель_окпо",
        "исполнитель_адрес_почтовый",
        "исполнитель_расчетный_счет",
        "исполнитель_регион_лицензии",
        "исполнитель_срок_действия_лицензии",
        "исполнитель_старый_номер_лицензии",
        "исполнитель_телефон",
        "исполнитель_телефон_дежурного",
        "исполнитель_фио_кратко",
        "исполнитель_адрес_юридический",
        "номер_договора",
        "объект_адрес",
        "объект_дата_отключения",
        "объект_дата_подключения",
        "объект_номер_пульта",
        "объект_телефон",
        "объект_наименование",
        "объект_уполномоч_лицо_телефон",
        "объект_уполномоч_лицо_фио",
        "цена_ндс_прописью",
        "цена_ндс_числом_1",
        "цена_прописью",
        "цена_числом"
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
