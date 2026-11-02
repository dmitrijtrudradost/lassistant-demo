using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Lassistant.Core.Config;
using Lassistant.Core.Models;
using Lassistant.Core.Services;
using Lassistant.Core.Validation;
using Lassistant.Extractors;
using Lassistant.Templates;
using Microsoft.Win32;

namespace Lassistant.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileExtractorRouter _router;
    private readonly DocxTemplateEngine _templateEngine = new();
    private readonly FieldMappingConfig _fieldMapping;
    private readonly DocumentCatalogConfig _documentCatalog;
    private readonly FieldDefinitionsConfig _fieldDefinitions;
    private readonly DocumentTemplateResolver _templateResolver;
    private ExecutorsConfig _executorsConfig;

    private DocumentKind _selectedDocumentKind = DocumentKind.Contract;
    private ClientType _selectedClientType = ClientType.Individual;
    private ServiceKind _selectedServiceKind = ServiceKind.Montage;
    private DocumentCatalogEntry? _selectedDocument;
    private ExcelRowOption? _selectedExcelRow;
    private ExecutorCard? _selectedExecutor;
    private bool _templateDrivenForm;
    private IReadOnlyList<string> _templatePlaceholders = [];

    private string _statusMessage = "Выберите тип документа и заполните поля. Данные не покидают ваш компьютер.";
    private string _validationSummary = string.Empty;
    private bool _isProcessing;

    private readonly Dictionary<string, string> _fieldValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FormFieldViewModel> _fieldViewModels = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel()
    {
        NerAnonymization = new NerAnonymizationViewModel();
        Templates     = new TemplateEditorViewModel();
        _fieldMapping = FieldMappingConfig.Load(AppPaths.FieldMappingPath);
        _documentCatalog = DocumentCatalogConfig.Load(AppPaths.DocumentCatalogPath);
        _fieldDefinitions = FieldDefinitionsConfig.Load(AppPaths.FieldDefinitionsPath);
        _templateResolver = new DocumentTemplateResolver(_documentCatalog);
        _executorsConfig = ExecutorsConfig.Load();
        _router = new FileExtractorRouter(_fieldMapping);

        LoadedFiles = new ObservableCollection<string>();
        FieldGroups = new ObservableCollection<FieldGroupViewModel>();
        ExcelRows = new ObservableCollection<ExcelRowOption>();
        ExecutorOptions = new ObservableCollection<ExecutorCard>(_executorsConfig.Executors);

        DocumentKindOptions = new ObservableCollection<EnumOption<DocumentKind>>(
            _templateResolver.GetDocumentKinds()
                .Select(k => new EnumOption<DocumentKind>(k, DisplayNames.ForDocumentKind(k))));

        RefreshClientTypeOptions();
        RefreshServiceOptions();
        ResolveSelectedDocument();
        RebuildForm();
    }

    public ObservableCollection<string> LoadedFiles { get; }
    public NerAnonymizationViewModel NerAnonymization { get; }
    public TemplateEditorViewModel Templates { get; }
    public ObservableCollection<FieldGroupViewModel> FieldGroups { get; }
    public ObservableCollection<ExcelRowOption> ExcelRows { get; }
    public ObservableCollection<EnumOption<DocumentKind>> DocumentKindOptions { get; }
    public ObservableCollection<EnumOption<ClientType>> ClientTypeOptions { get; } = [];
    public ObservableCollection<EnumOption<ServiceKind>> ServiceOptions { get; } = [];
    public ObservableCollection<ExecutorCard> ExecutorOptions { get; }

    public DocumentKind SelectedDocumentKind
    {
        get => _selectedDocumentKind;
        set
        {
            if (_selectedDocumentKind == value)
                return;
            _selectedDocumentKind = value;
            OnPropertyChanged();
            RefreshClientTypeOptions();
            EnsureValidClientType();
            RefreshServiceOptions();
            EnsureValidService();
            ResolveSelectedDocument();
            RebuildForm();
        }
    }

    public ClientType SelectedClientType
    {
        get => _selectedClientType;
        set
        {
            if (_selectedClientType == value)
                return;
            _selectedClientType = value;
            OnPropertyChanged();
            RefreshServiceOptions();
            EnsureValidService();
            ResolveSelectedDocument();
            RebuildForm();
        }
    }

    public ServiceKind SelectedServiceKind
    {
        get => _selectedServiceKind;
        set
        {
            if (_selectedServiceKind == value)
                return;
            _selectedServiceKind = value;
            OnPropertyChanged();
            ResolveSelectedDocument();
            RebuildForm();
        }
    }

    public ExecutorCard? SelectedExecutor
    {
        get => _selectedExecutor;
        set
        {
            if (ReferenceEquals(_selectedExecutor, value))
                return;
            if (_fieldViewModels.Count > 0)
                SaveFormToFieldValues();
            _selectedExecutor = value;
            OnPropertyChanged();
            RebuildForm(preserveImportedValues: true);
        }
    }

    public ExcelRowOption? SelectedExcelRow
    {
        get => _selectedExcelRow;
        set
        {
            if (_selectedExcelRow == value)
                return;
            _selectedExcelRow = value;
            OnPropertyChanged();
            if (value is not null)
                _ = ImportExcelRowAsync(value.FilePath, value.RowIndex);
        }
    }

    public string SelectedDocumentTitle =>
        _selectedDocument?.DisplayName ?? "Документ не выбран";

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        set { _validationSummary = value; OnPropertyChanged(); }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (_isProcessing == value)
                return;
            _isProcessing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanImportFiles));
        }
    }

    public bool CanImportFiles => !IsProcessing;
    public bool HasExcelRows => ExcelRows.Count > 0;

    /// <summary>После окна «Настройки»: пути, роли ComboBox и список исполнителей.</summary>
    public void ReloadAfterSettings()
    {
        UserSettingsStore.Reload();
        _executorsConfig = ExecutorsConfig.Load();
        var previousId = _selectedExecutor?.Id;
        ExecutorOptions.Clear();
        foreach (var card in _executorsConfig.Executors)
            ExecutorOptions.Add(card);
        SelectedExecutor = _executorsConfig.GetDefault(previousId);
        Templates.RefreshNerRoleOptions();
        NerAnonymization.RefreshNerRoleOptions();
        StatusMessage = "Настройки применены. Карточки: " + AppPaths.CustomerCardsDirectory;
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (IsProcessing)
            return;

        var validPaths = paths.Where(File.Exists).Distinct().ToList();
        if (validPaths.Count == 0)
            return;

        var cardPaths = validPaths
            .Where(p => Path.GetExtension(p).Equals(".md", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var extractPaths = validPaths.Except(cardPaths).ToList();

        foreach (var path in validPaths)
        {
            if (!LoadedFiles.Contains(path))
                LoadedFiles.Add(path);
        }

        var statusParts = new List<string>();

        if (cardPaths.Count > 0)
        {
            foreach (var cardPath in cardPaths)
            {
                if (ImportCustomerCardFromFile(cardPath, out var cardStatus))
                    statusParts.Add(cardStatus);
            }
        }

        if (extractPaths.Count == 0)
        {
            if (statusParts.Count > 0)
                StatusMessage = string.Join(" ", statusParts);
            return;
        }

        IsProcessing = true;
        StatusMessage = $"Обработка файлов ({extractPaths.Count})... Подождите, OCR может занять до минуты.";
        ValidationSummary = string.Empty;
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        try
        {
            var extractTask = Task.Run(() => _router.ExtractFromFiles(extractPaths));
            var completed = await Task.WhenAny(extractTask, Task.Delay(TimeSpan.FromMinutes(2)));
            if (completed != extractTask)
            {
                StatusMessage = "Превышено время ожидания (2 мин). Попробуйте Excel-шаблон или файл меньшего размера.";
                ValidationSummary = "Обработка прервана по таймауту.";
                return;
            }

            var extracted = await extractTask;
            ApplyClientData(extracted);
            await LoadExcelRowsAsync(extractPaths);

            var extractStatus = extractPaths.Any(IsImageOrPdf)
                ? $"Обработано файлов: {extractPaths.Count}. OCR мог ошибиться — проверьте поля вручную."
                : $"Обработано файлов: {extractPaths.Count}. Проверьте и при необходимости исправьте поля.";
            statusParts.Add(extractStatus);
            StatusMessage = string.Join(" ", statusParts);
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка при извлечении данных.";
            ValidationSummary = ex.Message;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void UpdateCustomerCard()
    {
        SaveFormToFieldValues();

        var hasCustomer = _fieldValues.Keys.Any(k =>
            k.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase));
        if (!hasCustomer)
        {
            ValidationSummary = "В форме нет полей заказчик_* — карточку сохранить нельзя.";
            StatusMessage = "Карточка заказчика не обновлена.";
            return;
        }

        var name = CustomerCardStore.ResolveCustomerNameForFile(_fieldValues);
        if (string.IsNullOrWhiteSpace(name) || name == "без_названия")
        {
            ValidationSummary = "Укажите краткое наименование или ФИО заказчика — по ним формируется имя файла карточки.";
            StatusMessage = "Карточка заказчика не обновлена.";
            return;
        }

        try
        {
            var result = CustomerCardStore.MergeAndSave(
                SelectedClientType,
                _fieldValues,
                ConfirmCustomerCardFieldReplace);

            var action = result.CreatedNew ? "создана" : "обновлена";
            StatusMessage =
                $"Карточка заказчика {action}: добавлено {result.Added}, " +
                $"обновлено {result.Updated}, пропущено {result.Skipped}. " +
                $"Файл: {result.FilePath}";
            ValidationSummary = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось сохранить карточку заказчика.";
            ValidationSummary = ex.Message;
        }
    }

    private bool ImportCustomerCardFromFile(string path, out string statusMessage)
    {
        statusMessage = string.Empty;
        try
        {
            if (!CustomerCardStore.LoadCard(path, out var typeLabel, out var cardFields))
            {
                ValidationSummary = "Файл карточки не найден.";
                statusMessage = "Импорт карточки заказчика не выполнен.";
                return false;
            }

            var customerFields = cardFields
                .Where(kv => CustomerCardStore.IsCardField(kv.Key))
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.OrdinalIgnoreCase);

            if (customerFields.Count == 0)
            {
                ValidationSummary =
                    $"В файле нет заполненных полей заказчика / договора / объекта: {Path.GetFileName(path)}";
                statusMessage = "Импорт карточки заказчика не выполнен.";
                return false;
            }

            SaveFormToFieldValues();

            if (CustomerCardStore.TryParseClientType(typeLabel, out var cardType)
                && cardType != SelectedClientType
                && ClientTypeOptions.Any(o => o.Value == cardType))
            {
                var switchType = MessageBox.Show(
                    $"В карточке указан тип «{CustomerCardStore.GetTypeLabel(cardType)}», " +
                    $"сейчас выбран «{CustomerCardStore.GetTypeLabel(SelectedClientType)}».\n\n" +
                    "Переключить тип клиента под карточку?",
                    "Импорт карточки заказчика",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (switchType == MessageBoxResult.Yes)
                {
                    _selectedClientType = cardType;
                    OnPropertyChanged(nameof(SelectedClientType));
                    RefreshServiceOptions();
                    EnsureValidService();
                    ResolveSelectedDocument();
                    RebuildForm(preserveImportedValues: true);
                }
            }

            var added = 0;
            var updated = 0;
            var skipped = 0;

            foreach (var (key, cardValue) in customerFields)
            {
                _fieldValues.TryGetValue(key, out var formValue);
                formValue ??= string.Empty;
                var formEmpty = string.IsNullOrWhiteSpace(formValue);

                if (formEmpty)
                {
                    ApplyCustomerFieldToForm(key, cardValue);
                    added++;
                    continue;
                }

                if (string.Equals(formValue.Trim(), cardValue, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                if (ConfirmFormFieldReplaceFromCard(key, formValue, cardValue))
                {
                    ApplyCustomerFieldToForm(key, cardValue);
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }

            ValidationSummary = string.Empty;
            statusMessage =
                $"Карточка «{Path.GetFileName(path)}»: заполнено {added}, " +
                $"обновлено {updated}, пропущено {skipped}.";
            return true;
        }
        catch (Exception ex)
        {
            ValidationSummary = ex.Message;
            statusMessage = "Не удалось загрузить карточку заказчика.";
            return false;
        }
    }

    private void ApplyCustomerFieldToForm(string key, string value)
    {
        _fieldValues[key] = value;
        if (_fieldViewModels.TryGetValue(key, out var vm))
            vm.Value = value;
    }

    private static bool ConfirmCustomerCardFieldReplace(string key, string oldValue, string newValue)
    {
        var result = MessageBox.Show(
            $"Поле «{key}» уже заполнено в карточке.\n\n" +
            $"Было:\n{Truncate(oldValue, 120)}\n\n" +
            $"Новое из формы:\n{Truncate(newValue, 120)}\n\n" +
            "Обновить значение в карточке?",
            "Карточка заказчика",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static bool ConfirmFormFieldReplaceFromCard(string key, string formValue, string cardValue)
    {
        var result = MessageBox.Show(
            $"Поле «{key}» уже заполнено в форме.\n\n" +
            $"В форме:\n{Truncate(formValue, 120)}\n\n" +
            $"В карточке:\n{Truncate(cardValue, 120)}\n\n" +
            "Подставить значение из карточки?",
            "Импорт карточки заказчика",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max] + "…";
    }

    public void GenerateDocument()
    {
        if (_selectedDocument is null)
        {
            ValidationSummary = "Выберите тип документа, клиента и услугу.";
            return;
        }

        SaveFormToFieldValues();
        var data = BuildClientData();

        if (_templateDrivenForm)
        {
            var missing = _templatePlaceholders
                .Where(k => !_fieldValues.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))
                .Take(8)
                .ToList();
            ValidationSummary = missing.Count > 0
                ? "Не заполнены поля (будут выделены красной заливкой): " + string.Join(", ", missing)
                : string.Empty;
        }
        else
        {
            var validation = SchemaDrivenValidator.Validate(data, _selectedDocument.FieldProfile, _fieldDefinitions);
            if (!validation.IsValid)
            {
                ValidationSummary = string.Join(Environment.NewLine, validation.Errors);
                StatusMessage = "Не удалось проверить данные.";
                return;
            }

            ValidationSummary = validation.FormatWarnings();
        }

        var templatePath = _templateResolver.GetTemplatePath(_selectedDocument);
        if (!File.Exists(templatePath))
        {
            ValidationSummary = $"Шаблон не найден: {templatePath}";
            StatusMessage = "Файл шаблона отсутствует.";
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Сохранить документ",
            Filter = "Word документ (*.docx)|*.docx",
            FileName = _templateResolver.BuildOutputFileName(_selectedDocument, data),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            _templateEngine.Generate(templatePath, saveDialog.FileName, data);
            StatusMessage = $"Документ сохранён: {saveDialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Не удалось создать документ.";
            ValidationSummary = ex.Message;
        }
    }

    private void RefreshClientTypeOptions()
    {
        ClientTypeOptions.Clear();
        foreach (var clientType in _templateResolver.GetClientTypes(SelectedDocumentKind))
        {
            ClientTypeOptions.Add(new EnumOption<ClientType>(clientType, DisplayNames.ForClientType(clientType)));
        }
    }

    private void RefreshServiceOptions()
    {
        ServiceOptions.Clear();
        foreach (var service in _templateResolver.GetServiceKinds(SelectedDocumentKind, SelectedClientType))
        {
            ServiceOptions.Add(new EnumOption<ServiceKind>(service, DisplayNames.ForServiceKind(service)));
        }
    }

    private void EnsureValidClientType()
    {
        if (ClientTypeOptions.Any(o => o.Value == SelectedClientType))
            return;
        if (ClientTypeOptions.Count > 0)
            SelectedClientType = ClientTypeOptions[0].Value;
    }

    private void EnsureValidService()
    {
        if (ServiceOptions.Any(o => o.Value == SelectedServiceKind))
            return;
        if (ServiceOptions.Count > 0)
            SelectedServiceKind = ServiceOptions[0].Value;
    }

    private void ResolveSelectedDocument()
    {
        _selectedDocument = _templateResolver.Resolve(SelectedDocumentKind, SelectedClientType, SelectedServiceKind);
        OnPropertyChanged(nameof(SelectedDocumentTitle));
        SyncExecutorFromDocument();
    }

    private void SyncExecutorFromDocument()
    {
        var preferred = _selectedDocument?.ExecutorId;
        var card = _executorsConfig.GetDefault(preferred);
        _selectedExecutor = card;
        OnPropertyChanged(nameof(SelectedExecutor));
    }

    private void RebuildForm(bool preserveImportedValues = false)
    {
        if (!preserveImportedValues && _fieldViewModels.Count > 0)
            SaveFormToFieldValues();

        RemoveExecutorKeysFromFieldValues();

        FieldGroups.Clear();
        _fieldViewModels.Clear();
        _templateDrivenForm = false;
        _templatePlaceholders = [];

        if (_selectedDocument is null)
            return;

        var templatePath = _templateResolver.GetTemplatePath(_selectedDocument);
        if (File.Exists(templatePath))
        {
            IReadOnlyList<string> placeholders;
            try
            {
                placeholders = TemplatePlaceholderScanner.Extract(templatePath);
            }
            catch (Exception ex)
            {
                ValidationSummary = $"Не удалось прочитать шаблон «{Path.GetFileName(templatePath)}»: {ex.Message}";
                StatusMessage = "Закройте файл шаблона в других программах (например, Word) и выберите документ заново.";
                RebuildFormFromProfile(preserveImportedValues);
                ApplySelectedExecutorToForm();
                return;
            }

            if (placeholders.Count > 0)
            {
                RebuildFormFromPlaceholders(placeholders, preserveImportedValues);
                ApplySelectedExecutorToForm();
                return;
            }
        }

        RebuildFormFromProfile(preserveImportedValues);
        ApplySelectedExecutorToForm();
    }

    private void RebuildFormFromPlaceholders(IReadOnlyList<string> placeholders, bool preserveImportedValues)
    {
        _templateDrivenForm = true;
        // Валидация пустых — только по ключам из DOCX, не по доп. полям исполнителя.
        _templatePlaceholders = placeholders;

        var formKeys = MergeWithSelectedExecutorKeys(placeholders);
        var groups = new Dictionary<string, List<FormFieldViewModel>>(StringComparer.Ordinal);
        foreach (var key in formKeys)
            AddTemplateDrivenField(groups, key);

        foreach (var label in OrderGroupLabels(groups.Keys))
            FieldGroups.Add(new FieldGroupViewModel(label, groups[label]));
    }

    private void RebuildFormFromProfile(bool preserveImportedValues)
    {
        var groups = new Dictionary<string, List<FormFieldViewModel>>(StringComparer.Ordinal);

        foreach (var (definitionGroupKey, _, fieldKeys) in _fieldDefinitions.GetProfileGroups(_selectedDocument!.FieldProfile))
        {
            foreach (var fieldKey in fieldKeys)
            {
                if (_fieldViewModels.ContainsKey(fieldKey))
                    continue;
                if (!_fieldDefinitions.Fields.TryGetValue(fieldKey, out var fieldDef))
                    continue;

                var isRequired = _fieldDefinitions.IsRequired(_selectedDocument.FieldProfile, fieldKey);
                var vm = new FormFieldViewModel(fieldKey, fieldDef.Label, isRequired)
                {
                    Value = _fieldValues.TryGetValue(fieldKey, out var existing) ? existing : string.Empty
                };
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FormFieldViewModel.Value) && !string.IsNullOrEmpty(ValidationSummary))
                        ValidationSummary = string.Empty;
                };
                _fieldViewModels[fieldKey] = vm;

                var category = TemplatePlaceholderScanner.ProfileCategoryLabel(definitionGroupKey, fieldKey);
                if (!groups.TryGetValue(category, out var list))
                {
                    list = [];
                    groups[category] = list;
                }

                list.Add(vm);
            }
        }

        foreach (var key in GetSelectedExecutorFieldKeys())
        {
            if (_fieldViewModels.ContainsKey(key))
                continue;

            var existing = _fieldValues.TryGetValue(key, out var cached) ? cached : string.Empty;
            var vm = new FormFieldViewModel(
                key,
                TemplatePlaceholderScanner.DisplayLabel(key),
                isRequired: false,
                isReadOnly: false)
            {
                Value = existing
            };
            _fieldViewModels[key] = vm;

            const string category = "Исполнитель";
            if (!groups.TryGetValue(category, out var list))
            {
                list = [];
                groups[category] = list;
            }

            list.Add(vm);
        }

        foreach (var label in OrderGroupLabels(groups.Keys))
            FieldGroups.Add(new FieldGroupViewModel(label, groups[label]));
    }

    private static IEnumerable<string> OrderGroupLabels(IEnumerable<string> labels) =>
        labels.OrderBy(l => l switch
        {
            "Исполнитель" => 0,
            "Заказчик" => 1,
            "Договор и объект" => 2,
            _ => 3
        });

    private void AddTemplateDrivenField(
        Dictionary<string, List<FormFieldViewModel>> groups,
        string key)
    {
        if (_fieldViewModels.ContainsKey(key))
            return;

        var groupLabel = TemplatePlaceholderScanner.GroupLabel(key);
        if (!groups.TryGetValue(groupLabel, out var list))
        {
            list = [];
            groups[groupLabel] = list;
        }

        var existing = _fieldValues.TryGetValue(key, out var cached) ? cached : string.Empty;
        var vm = new FormFieldViewModel(
            key,
            TemplatePlaceholderScanner.DisplayLabel(key),
            isRequired: false,
            isReadOnly: false)
        {
            Value = existing
        };
        _fieldViewModels[key] = vm;
        list.Add(vm);
    }

    private IEnumerable<string> GetSelectedExecutorFieldKeys()
    {
        if (_selectedExecutor?.Fields is null)
            yield break;

        foreach (var key in _selectedExecutor.Fields.Keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            yield return key;
    }

    private IReadOnlyList<string> MergeWithSelectedExecutorKeys(IReadOnlyList<string> placeholders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(placeholders.Count + 8);
        foreach (var key in placeholders)
        {
            if (seen.Add(key))
                result.Add(key);
        }

        foreach (var key in GetSelectedExecutorFieldKeys())
        {
            if (seen.Add(key))
                result.Add(key);
        }

        return result;
    }

    private void RemoveExecutorKeysFromFieldValues()
    {
        var stale = _fieldValues.Keys
            .Where(k => k.StartsWith("исполнитель_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in stale)
            _fieldValues.Remove(key);
    }

    private void ApplySelectedExecutorToForm()
    {
        if (_selectedExecutor is null)
            return;

        foreach (var (key, value) in _selectedExecutor.Fields)
        {
            _fieldValues[key] = value ?? string.Empty;
            if (_fieldViewModels.TryGetValue(key, out var vm))
                vm.Value = value ?? string.Empty;
        }

        if (_fieldViewModels.TryGetValue("исполнитель_наименование", out var nameVm)
            && string.IsNullOrWhiteSpace(nameVm.Value))
        {
            nameVm.Value = _selectedExecutor.DisplayName;
            _fieldValues["исполнитель_наименование"] = _selectedExecutor.DisplayName;
        }
    }

    private void SaveFormToFieldValues()
    {
        foreach (var (key, vm) in _fieldViewModels)
            _fieldValues[key] = vm.Value;
    }

    private void ApplyClientData(ClientData data)
    {
        foreach (var (key, value) in ClientDataMapper.ToFieldValues(data))
            _fieldValues[key] = value;

        if (_templateResolver.Resolve(SelectedDocumentKind, data.ClientType, SelectedServiceKind) is null)
        {
            var match = _templateResolver.GetAll().FirstOrDefault(d => d.ClientType == data.ClientType);
            if (match is not null)
            {
                _selectedDocumentKind = match.DocumentKind;
                _selectedClientType = match.ClientType;
                _selectedServiceKind = match.ServiceKind;
                OnPropertyChanged(nameof(SelectedDocumentKind));
                OnPropertyChanged(nameof(SelectedClientType));
                OnPropertyChanged(nameof(SelectedServiceKind));
                RefreshClientTypeOptions();
                RefreshServiceOptions();
            }
        }
        else if (_selectedClientType != data.ClientType)
        {
            _selectedClientType = data.ClientType;
            OnPropertyChanged(nameof(SelectedClientType));
            RefreshServiceOptions();
            EnsureValidService();
        }

        ResolveSelectedDocument();
        RebuildForm(preserveImportedValues: true);
    }

    private ClientData BuildClientData()
    {
        var data = ClientDataMapper.FromFieldValues(_fieldValues, SelectedClientType);
        data.ClientType = SelectedClientType;
        foreach (var (key, value) in _fieldValues)
            data.ExtraFields[key] = value ?? string.Empty;
        return data;
    }

    private async Task LoadExcelRowsAsync(IEnumerable<string> paths)
    {
        ExcelRows.Clear();
        SelectedExcelRow = null;
        OnPropertyChanged(nameof(HasExcelRows));

        foreach (var path in paths.Where(p => Path.GetExtension(p).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            var rows = await Task.Run(() => ExcelExtractor.ListDataRows(path, _fieldMapping));
            foreach (var row in rows)
                ExcelRows.Add(new ExcelRowOption(row.RowIndex, $"{Path.GetFileName(path)} — строка {row.RowIndex}: {row.Label}", path));
        }

        OnPropertyChanged(nameof(HasExcelRows));
        if (ExcelRows.Count == 1)
            SelectedExcelRow = ExcelRows[0];
    }

    private async Task ImportExcelRowAsync(string filePath, int rowIndex)
    {
        IsProcessing = true;
        try
        {
            var data = await Task.Run(() => ExcelExtractor.ExtractRow(filePath, _fieldMapping, rowIndex));
            ApplyClientData(data);
            ValidationSummary = string.Empty;
            StatusMessage = $"Загружена строка {rowIndex} из Excel.";
        }
        catch (Exception ex)
        {
            ValidationSummary = ex.Message;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private static bool IsImageOrPdf(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
