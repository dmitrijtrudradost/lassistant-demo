using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Lassistant.Core.Config;
using Lassistant.Core.Services;
using Lassistant.Templates;
using Microsoft.Win32;

namespace Lassistant.App.ViewModels;

public sealed record NerProcessingModeOption(
    string Value,
    string Title,
    string Description);

public sealed record NerScoreThresholdOption(double Value, string DisplayName)
{
    /// <summary>Единый набор порогов для «Настроек», «Шаблонов» и «Анонимизации».</summary>
    public static IReadOnlyList<NerScoreThresholdOption> All { get; } =
    [
        new(0.00, "0,00 — без отсечения"),
        new(0.05, "0,05 — максимум результатов"),
        new(0.10, "0,10 — слабый фильтр"),
        new(0.15, "0,15 — умеренный фильтр"),
        new(0.20, "0,20 — средний фильтр"),
        new(0.30, "0,30 — уверенные"),
        new(0.50, "0,50 — высокая уверенность"),
        new(0.75, "0,75 — очень высокая уверенность"),
        new(0.90, "0,90 — почти без сомнений")
    ];
}

/// <summary>
/// ViewModel вкладки «Шаблоны».
/// Управляет чтением цветных выделений из .docx и сохранением готового шаблона.
/// </summary>
public sealed class TemplateEditorViewModel : INotifyPropertyChanged
{
    private string _sourceFilePath = string.Empty;
    private string _statusMessage  = "Загрузите договор .docx и нажмите «Найти сведения».";
    private bool   _isProcessing;
    private NerProcessingModeOption _selectedNerProcessingMode;
    private NerScoreThresholdOption _selectedNerScoreThreshold;
    private ContractNerDocument? _lastNerDocument;
    private NerFeedbackSession? _nerFeedbackSession;

    public TemplateEditorViewModel()
    {
        Entities       = new ObservableCollection<ColoredEntityRowViewModel>();
        NerEntities    = new ObservableCollection<NerEntityRowViewModel>();
        PlaceholderOptions = LoadPlaceholderOptions();
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
                "NER → Python-постобработка → C#-агрегатор. Исправляет границы и стороны, " +
                "удаляет мусор и дубли, оставляет лучшие значения ролей."),
            new(
                ContractNerProcessingModes.PythonPostprocess,
                "Диагностика: без C#-агрегатора",
                "NER → Python-постобработка. Показывает все найденные вхождения после правил " +
                "для ИНН, счетов, банков, адресов и сторон; дубли сохраняются."),
            new(
                ContractNerProcessingModes.Raw,
                "Голая NER-модель",
                "Только token-classification модели: без Python-постобработки, без C#-агрегатора " +
                "Порог уверенности задаётся отдельно; при 0,00 выводятся все найденные сущности.")
        ];

        var nerSettings = ContractNerService.LoadSettings();
        var configuredMode = ContractNerProcessingModes.Normalize(
            nerSettings.ProcessingMode);
        _selectedNerProcessingMode = NerProcessingModes.First(
            option => option.Value == configuredMode);
        _selectedNerScoreThreshold = NerScoreThresholds
            .OrderBy(option => Math.Abs(option.Value - nerSettings.MinScore))
            .First();
    }

    // ─── Свойства, отображаемые в UI ─────────────────────────────────────────

    public ObservableCollection<ColoredEntityRowViewModel> Entities { get; }

    public bool HasColoredEntities => Entities.Count > 0;

    /// <summary>Сущности локального Contract NER.</summary>
    public ObservableCollection<NerEntityRowViewModel> NerEntities { get; }

    public bool HasNerEntities => NerEntities.Count > 0;

    /// <summary>
    /// Состояние флажка «Все» в заголовке NER: true/false или null при смешанном выборе.
    /// </summary>
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

    /// <summary>
    /// Состояние флажка «Все» в таблице цветных выделений.
    /// </summary>
    public bool? AllColoredAccepted
    {
        get
        {
            if (Entities.Count == 0) return false;
            var accepted = Entities.Count(row => row.IsAccepted);
            if (accepted == 0) return false;
            if (accepted == Entities.Count) return true;
            return null;
        }
        set
        {
            if (!value.HasValue) return;
            foreach (var row in Entities)
                row.IsAccepted = value.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>Список всех ключей плейсхолдеров для ComboBox.</summary>
    public IReadOnlyList<PlaceholderOption> PlaceholderOptions { get; }

    /// <summary>
    /// Роли/ключи NER-модели для ComboBox «Роль / ключ», плюс пункт «добавить новую».
    /// </summary>
    public ObservableCollection<string> NerRoleOptions { get; }

    /// <summary>Пункт ComboBox для ввода произвольного ключа.</summary>
    public const string AddNewNerRoleOption = "— добавить новую —";

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
            _nerFeedbackSession = null;
            OnPropertyChanged(nameof(HasNerEntities));
            try
            {
                ContractNerService.SaveProcessingMode(value.Value);
                StatusMessage = $"Выбран режим NER: {value.Title}. Запустите разбор заново.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    $"Режим «{value.Title}» выбран на текущий сеанс, но не сохранён: {ex.Message}";
            }
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
            _nerFeedbackSession = null;
            OnPropertyChanged(nameof(HasNerEntities));
            try
            {
                ContractNerService.SaveMinScore(value.Value);
                StatusMessage =
                    $"Выбрана минимальная уверенность {value.Value:0.00}. Запустите разбор NER заново.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    $"Уверенность {value.Value:0.00} выбрана на текущий сеанс, но не сохранена: {ex.Message}";
            }
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
        }
    }

    public bool CanOperate => !IsProcessing && HasSourceFile;

    public bool CanAnalyzeNer => CanOperate;

    // ─── Команды ─────────────────────────────────────────────────────────────

    public void BrowseSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title  = "Выберите договор .docx",
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
        _nerFeedbackSession = null;
        ClearNerEntities();
        OnPropertyChanged(nameof(HasNerEntities));
        ReadColoredEntities();
        StatusMessage = HasSourceFile
            ? $"Загружен: {Path.GetFileName(path)}. Рекомендуется «Найти сведения»."
            : StatusMessage;
    }

    public void SaveTemplate()
    {
        if (!HasSourceFile)
        {
            MessageBox.Show("Сначала загрузите договор .docx.",
                "Сохранить шаблон", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var acceptedNer = NerEntities
            .Where(e => e.IsAccepted && !string.IsNullOrWhiteSpace(e.EditableRole))
            .ToList();
        var acceptedColored = Entities
            .Where(e => e.IsAccepted && !string.IsNullOrWhiteSpace(e.EditablePlaceholder))
            .ToList();

        if (acceptedNer.Count == 0 && acceptedColored.Count == 0)
        {
            MessageBox.Show(
                "Нет выбранных строк для шаблона.\n\n" +
                "Отметьте галочками сущности NER или цветные выделения, " +
                "либо нажмите «Найти сведения» / разметьте поля цветом.",
                "Сохранить шаблон", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(SourceFilePath)
                           .Replace("_anon", "", StringComparison.OrdinalIgnoreCase)
                           .TrimEnd('_');

        var saveDialog = new SaveFileDialog
        {
            Title            = "Сохранить шаблон",
            Filter           = "Word документ (*.docx)|*.docx",
            FileName         = baseName + "_template.docx",
            InitialDirectory = Path.GetDirectoryName(SourceFilePath)
                               ?? AppPaths.TemplatesDirectory
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            IsProcessing = true;
            StatusMessage = "Сохранение шаблона…";
            string? feedbackMessage = null;

            if (acceptedNer.Count > 0)
            {
                if (_lastNerDocument is null)
                    throw new InvalidOperationException(
                        "Исходный NER-текст недоступен. Выполните локальный разбор повторно.");

                var spans = new List<(int Start, int End, string Role)>();
                foreach (var row in acceptedNer)
                {
                    if (!row.TryResolveFinalSpan(
                            _lastNerDocument.Text,
                            out var start,
                            out var end,
                            out var error))
                    {
                        throw new InvalidOperationException(
                            $"Не удалось применить исправление «{row.EditableText}»: {error}");
                    }
                    spans.Add((start, end, row.EditableRole.Trim()));
                }
                NerSpanToTemplateConverter.Convert(
                    SourceFilePath,
                    saveDialog.FileName,
                    _lastNerDocument,
                    spans);
                StatusMessage = $"Шаблон (NER) сохранён: {Path.GetFileName(saveDialog.FileName)} ({spans.Count} полей)";

                if (_nerFeedbackSession is not null)
                {
                    try
                    {
                        var reviews = NerEntities.Select(row =>
                        {
                            var resolved = row.TryResolveFinalSpan(
                                _lastNerDocument.Text,
                                out var finalStart,
                                out var finalEnd,
                                out _);
                            return new NerFeedbackReview
                            {
                                Before = row.Entity,
                                AutoAcceptScore = row.AutoAcceptScore,
                                FinalRole = row.EditableRole.Trim(),
                                FinalText = resolved ? row.EditableText : row.OriginalText,
                                FinalStart = resolved ? finalStart : row.Entity.Start,
                                FinalEnd = resolved ? finalEnd : row.Entity.End,
                                IsAccepted = row.IsAccepted,
                                CorrectionReason = row.CorrectionReason,
                                Comment = row.Comment
                            };
                        }).ToList();
                        var feedbackPath = NerFeedbackService.Complete(
                            _nerFeedbackSession,
                            reviews,
                            saveDialog.FileName);
                        feedbackMessage = $"Обратная связь сохранена:\n{feedbackPath}";
                    }
                    catch (Exception feedbackException)
                    {
                        feedbackMessage =
                            $"Шаблон сохранён, но журнал обратной связи записать не удалось:\n" +
                            feedbackException.Message;
                    }
                }
            }
            else
            {
                var mappings = acceptedColored
                    .Select(r => (r.OriginalText, r.Entity.Color, r.EditablePlaceholder))
                    .ToList();
                ColoredDocxToTemplateConverter.Convert(SourceFilePath, saveDialog.FileName, mappings);
                StatusMessage =
                    $"Шаблон (цветная разметка) сохранён: {Path.GetFileName(saveDialog.FileName)} " +
                    $"({mappings.Count} из {Entities.Count} выделений)";
            }

            var completionMessage = $"Шаблон успешно сохранён:\n{saveDialog.FileName}";
            if (!string.IsNullOrWhiteSpace(feedbackMessage))
                completionMessage += $"\n\n{feedbackMessage}";
            MessageBox.Show(completionMessage,
                "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка при сохранении шаблона.";
            MessageBox.Show($"Не удалось сохранить шаблон:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Локальный разбор договора через Contract NER (без отправки ПДн в интернет).
    /// </summary>
    public async void AnalyzeWithContractNerAsync()
    {
        if (!HasSourceFile)
        {
            MessageBox.Show("Сначала загрузите договор .docx.",
                "Contract NER", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var avail = ContractNerService.CheckAvailability();
        if (!avail.IsAvailable)
        {
            MessageBox.Show(
                (avail.Reason ?? "Модуль NER недоступен.") + "\n\n" +
                "См. scripts/contract_ner/README.md\n" +
                "Нужны: models/contract-ner/ner_model_final и Python с requirements-infer.txt.",
                "Модуль NER не установлен", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsProcessing = true;
        StatusMessage = "Извлечение текста договора…";

        try
        {
            var nerDocument = DocxPlainTextExtractor.ExtractForNer(SourceFilePath);
            if (string.IsNullOrWhiteSpace(nerDocument.Text))
            {
                StatusMessage = "В документе нет текста.";
                MessageBox.Show("Не удалось извлечь текст из .docx.",
                    "Contract NER", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusMessage = "Локальный NER… (первый запуск может занять 1–2 минуты)";
            var entities = await ContractNerService.AnalyzeDocumentAsync(
                nerDocument,
                SelectedNerProcessingMode.Value,
                SelectedNerScoreThreshold.Value);

            _lastNerDocument = nerDocument;
            ClearNerEntities();
            string? feedbackWarning = null;
            try
            {
                _nerFeedbackSession = NerFeedbackService.Start(
                    SourceFilePath,
                    nerDocument,
                    entities.ToList(),
                    SelectedNerProcessingMode.Value,
                    SelectedNerScoreThreshold.Value,
                    avail.Settings.AutoAcceptScore);
            }
            catch (Exception feedbackException)
            {
                _nerFeedbackSession = null;
                feedbackWarning = $" Журнал обратной связи не создан: {feedbackException.Message}";
            }
            foreach (var e in entities)
                AddNerEntity(new NerEntityRowViewModel(
                    e,
                    avail.Settings.AutoAcceptScore,
                    nerDocument.Text));
            OnPropertyChanged(nameof(HasNerEntities));
            OnPropertyChanged(nameof(AllNerAccepted));

            var acceptedCount = NerEntities.Count(e => e.IsAccepted);
            var weakCount = NerEntities.Count - acceptedCount;
            StatusMessage = NerEntities.Count > 0
                ? $"{SelectedNerProcessingMode.Title}, уверенность ≥ {SelectedNerScoreThreshold.Value:0.00}: " +
                  $"показано {NerEntities.Count} строк, " +
                  $"автоматически выбрано {acceptedCount}, " +
                  $"слабых для ручной проверки — {weakCount}. ПДн не отправлялись в интернет." +
                  feedbackWarning
                : "NER не нашёл сущностей. Проверьте тип договора (охрана, заказчик=ИП).";

            if (NerEntities.Count == 0)
                MessageBox.Show(
                    "Сущности не найдены.\n\n" +
                    "Модуль обучен на договорах охраны с заказчиком-ИП " +
                    "и заголовками ИСПОЛНИТЕЛЬ / ЗАКАЗЧИК.",
                    "Contract NER", MessageBoxButton.OK, MessageBoxImage.Information);
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
            OnPropertyChanged(nameof(CanOperate));
            OnPropertyChanged(nameof(CanAnalyzeNer));
        }
    }

    /// <summary>
    /// Запускает natasha NER для строк с неуверенным плейсхолдером
    /// и обновляет EditablePlaceholder там, где NER дал более точный ответ.
    /// </summary>
    public async void RefineWithNer()
    {
        var uncertain = Entities.Where(IsUncertain).ToList();
        if (uncertain.Count == 0)
        {
            MessageBox.Show("Нет строк для уточнения — все ключи уже определены.",
                "Уточнить NER", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsProcessing  = true;
        StatusMessage = $"NER-анализ: обрабатываем {uncertain.Count} строк…";

        try
        {
            var requests = uncertain.Select(r => new NerSuggester.NerRequest(
                r.Text,
                r.Entity.ParagraphContext,
                r.Entity.Color.ToString().ToLowerInvariant()
            )).ToList();

            var results = await NerSuggester.RefineAsync(requests);

            if (results is null)
            {
                MessageBox.Show(
                    "natasha не найдена или Python не установлен.\n\n" +
                    "Установите зависимости:\n  pip install -r requirements_ner.txt\n\n" +
                    "и убедитесь, что команда python доступна в PATH.",
                    "NER недоступен", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int refined = 0;
            for (int i = 0; i < uncertain.Count && i < results.Count; i++)
            {
                var key = results[i].Key;
                if (!string.IsNullOrEmpty(key) && key != "custom_value")
                {
                    uncertain[i].EditablePlaceholder = key;
                    refined++;
                }
            }

            StatusMessage = refined > 0
                ? $"NER уточнил {refined} из {uncertain.Count} строк."
                : $"NER не смог уточнить строки ({uncertain.Count} шт.) — проверьте вручную.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка NER: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            OnPropertyChanged(nameof(CanOperate));
        }
    }

    /// <summary>
    /// Строка считается «неуверенной» (кандидат для NER) если:
    /// — ключ пустой или равен "custom_value"
    /// — ИЛИ ключ "наименование_орг", но текст не содержит явных org-маркеров
    /// </summary>
    private static bool IsUncertain(ColoredEntityRowViewModel row)
    {
        var key  = row.EditablePlaceholder;
        var text = row.Text;

        if (string.IsNullOrWhiteSpace(key) || key == "custom_value")
            return true;

        if (key == "наименование_орг" &&
            !text.Contains("ООО",      StringComparison.Ordinal) &&
            !text.Contains("ОАО",      StringComparison.Ordinal) &&
            !text.Contains("ЗАО",      StringComparison.Ordinal) &&
            !text.Contains(" АО ",     StringComparison.Ordinal) &&
            !text.Contains("ИП ",      StringComparison.Ordinal) &&
            !text.Contains("Общество", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    // ─── Внутренняя логика ────────────────────────────────────────────────────

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

    private void ReadColoredEntities()
    {
        foreach (var row in Entities)
            row.PropertyChanged -= ColoredEntity_PropertyChanged;
        Entities.Clear();
        OnPropertyChanged(nameof(AllColoredAccepted));
        StatusMessage = "Чтение цветных выделений…";
        IsProcessing  = true;

        try
        {
            var entities = ColoredDocxReader.Read(SourceFilePath);

            foreach (var entity in entities)
            {
                var suggested = HighlightPlaceholderMapper.Suggest(entity);
                var row = new ColoredEntityRowViewModel(entity, suggested);
                row.PropertyChanged += ColoredEntity_PropertyChanged;
                Entities.Add(row);
            }

            if (Entities.Count == 0)
                StatusMessage = "Цветных выделений не найдено. Убедитесь, что юрист пометил фразы в Word.";
            else
                StatusMessage = $"Найдено выделений: {Entities.Count} — отметьте нужные, проверьте ключи, затем «Сохранить шаблон».";

            OnPropertyChanged(nameof(HasColoredEntities));
            OnPropertyChanged(nameof(AllColoredAccepted));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка чтения файла: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            OnPropertyChanged(nameof(HasColoredEntities));
            OnPropertyChanged(nameof(CanOperate));
        }
    }

    private void ColoredEntity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColoredEntityRowViewModel.IsAccepted))
            OnPropertyChanged(nameof(AllColoredAccepted));
    }

    private static IReadOnlyList<PlaceholderOption> LoadPlaceholderOptions()
    {
        try
        {
            var cfg = FieldDefinitionsConfig.Load(AppPaths.FieldDefinitionsPath);
            return cfg.Fields.Values
                .Select(f => new PlaceholderOption(f.Label, f.Placeholder))
                .OrderBy(o => o.Key)
                .ToList();
        }
        catch
        {
            return [];
        }
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
            // ниже — fallback / placeholder-roles
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
        list.Add(AddNewNerRoleOption);
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
            Path.Combine(AppPaths.BaseDirectory, "models", "contract-ner", "ner_model_1.1", "labels.json"),
            Path.Combine(AppPaths.BaseDirectory, "models", "contract-ner", "ner_model_1.0", "labels.json"),
            Path.Combine(AppPaths.BaseDirectory, "models", "contract-ner", "ner_model_final", "labels.json"),
            Path.GetFullPath(Path.Combine(AppPaths.BaseDirectory, "..", "..", "..", "..", "..",
                "models", "contract-ner", "ner_model_1.1", "labels.json")),
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

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Элемент ComboBox с ключом плейсхолдера и читаемой подписью.</summary>
public sealed record PlaceholderOption(string Label, string Key)
{
    public string DisplayName => $"{Label} — {Key}";
}
