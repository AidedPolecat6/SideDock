using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SideDock.Models;

public sealed class DockTab : INotifyPropertyChanged
{
    private string _name = "Favorites";
    private bool _useFullWidthRows;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public string Accent { get; set; } = "#38BDF8";
    public string FolderPath { get; set; } = "";
    public string? SourceShortcutPath { get; set; }

    public bool UseFullWidthRows
    {
        get => _useFullWidthRows;
        set
        {
            if (_useFullWidthRows == value) return;
            _useFullWidthRows = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseFullWidthRows)));
        }
    }

    [JsonIgnore]
    public bool IsLinkedFolder => !string.IsNullOrWhiteSpace(SourceShortcutPath);

    public ObservableCollection<DockItem> Items { get; set; } = [];
}
