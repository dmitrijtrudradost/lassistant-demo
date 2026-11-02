namespace Lassistant.App.ViewModels;

public sealed class ExcelRowOption
{
    public ExcelRowOption(int rowIndex, string displayName, string filePath)
    {
        RowIndex = rowIndex;
        DisplayName = displayName;
        FilePath = filePath;
    }

    public int RowIndex { get; }
    public string FilePath { get; }
    public string DisplayName { get; }
}
