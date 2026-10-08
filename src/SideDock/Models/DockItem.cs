using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SideDock.Models;

public sealed class DockItem : INotifyPropertyChanged
{
    private string _label = "";
    private string _accent = "#334155";
    private bool _isPdfTarget;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public string Path { get; set; } = "";
    public string? BrowsePath { get; set; }
    public DockItemKind Kind { get; set; }

    public string Accent
    {
        get => _accent;
        set => SetField(ref _accent, value);
    }

    [JsonIgnore]
    public string EffectiveBrowsePath => BrowsePath ?? Path;

    [JsonIgnore]
    public bool UseModernFolderIcon { get; set; }

    [JsonIgnore]
    public bool IsReadOnly { get; set; }

    [JsonIgnore]
    public bool IsPdf
    {
        get => _isPdfTarget || System.IO.Path.GetExtension(Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        internal set => _isPdfTarget = value;
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
