using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lassistant.App.ViewModels;

public sealed class FormFieldViewModel : INotifyPropertyChanged
{
    private string _value = string.Empty;
    private bool _isReadOnly;

    private static readonly HashSet<string> MultilineKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "LegalAddress", "RegistrationAddress", "ObjectAddress",
        "заказчик_адрес_юридический", "исполнитель_адрес_юридический", "объект_адрес",
        "адрес_регистрации", "юр_адрес", "registration_address", "legal_address"
    };

    public FormFieldViewModel(string key, string label, bool isRequired, bool isReadOnly = false)
    {
        Key = key;
        Label = label;
        IsRequired = isRequired;
        _isReadOnly = isReadOnly;
        IsMultiline = MultilineKeys.Contains(key)
                      || key.Contains("адрес", StringComparison.OrdinalIgnoreCase)
                      || key.Contains("address", StringComparison.OrdinalIgnoreCase);
    }

    public string Key { get; }
    public string Label { get; }
    public bool IsRequired { get; }
    public bool IsMultiline { get; }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value) return;
            _isReadOnly = value;
            OnPropertyChanged();
        }
    }

    public string DisplayLabel => IsRequired ? $"{Label} *" : Label;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
