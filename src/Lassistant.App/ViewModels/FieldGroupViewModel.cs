using System.Collections.ObjectModel;
using Lassistant.Templates;

namespace Lassistant.App.ViewModels;

public sealed class FieldSubGroupViewModel
{
    public FieldSubGroupViewModel(string label, IEnumerable<FormFieldViewModel> fields)
    {
        Label = label;
        Fields = new ObservableCollection<FormFieldViewModel>(fields);
    }

    public string Label { get; }
    public ObservableCollection<FormFieldViewModel> Fields { get; }
}

public sealed class FieldGroupViewModel
{
    public FieldGroupViewModel(string label, IEnumerable<FormFieldViewModel> fields)
    {
        Label = label;
        var list = fields.ToList();
        Fields = new ObservableCollection<FormFieldViewModel>(list);

        var ordered = list
            .OrderBy(f => TemplatePlaceholderScanner.SubGroupOrder(f.Key))
            .ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .GroupBy(f => TemplatePlaceholderScanner.SubGroupLabel(f.Key));

        SubGroups = new ObservableCollection<FieldSubGroupViewModel>(
            ordered.Select(g => new FieldSubGroupViewModel(g.Key, g)));
    }

    public string Label { get; }
    public ObservableCollection<FormFieldViewModel> Fields { get; }
    public ObservableCollection<FieldSubGroupViewModel> SubGroups { get; }
}
