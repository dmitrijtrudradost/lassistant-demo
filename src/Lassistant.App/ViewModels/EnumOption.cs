namespace Lassistant.App.ViewModels;

public sealed class EnumOption<T> where T : struct, Enum
{
    public EnumOption(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public T Value { get; }
    public string DisplayName { get; }
}
