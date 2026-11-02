using System.Collections.Frozen;
using System.Reflection;

namespace Lassistant.Core.Models;

public static class ClientDataMapper
{
    private static readonly FrozenDictionary<string, PropertyInfo> Properties =
        typeof(ClientData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite)
            .ToFrozenDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> ToFieldValues(ClientData data)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, prop) in Properties)
            values[name] = (string?)prop.GetValue(data) ?? string.Empty;

        foreach (var (key, value) in data.ExtraFields)
            values[key] = value;

        return values;
    }

    public static ClientData FromFieldValues(Dictionary<string, string> values, ClientType clientType = ClientType.Individual)
    {
        var data = new ClientData { ClientType = clientType };

        foreach (var (name, prop) in Properties)
        {
            if (values.TryGetValue(name, out var value))
                prop.SetValue(data, value ?? string.Empty);
        }

        foreach (var (key, value) in values)
        {
            if (!Properties.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
                data.ExtraFields[key] = value;
        }

        return data;
    }

    public static void ApplyFieldValues(ClientData target, Dictionary<string, string> values)
    {
        foreach (var (name, prop) in Properties)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                prop.SetValue(target, value);
        }

        foreach (var (key, value) in values)
        {
            if (!Properties.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
                target.ExtraFields[key] = value;
        }
    }
}
