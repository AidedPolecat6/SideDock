using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using SideDock.Native;

namespace SideDock;

public partial class HandleWindow : Window, INotifyPropertyChanged
{
    private readonly Action _toggleDrawer;
    private readonly Action _openDrawer;
    private readonly Action _pointerEntered;
    private readonly DispatcherTimer _hoverTimer;
    private string _chevron = "<";

    internal HandleWindow(Action toggleDrawer, Action openDrawer, Action pointerEntered, double width)
    {
        _toggleDrawer = toggleDrawer;
        _openDrawer = openDrawer;
        _pointerEntered = pointerEntered;
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _hoverTimer.Tick += HoverTimer_Tick;
        InitializeComponent();
        Width = width;
        DataContext = this;
        SourceInitialized += (_, _) =>
        {
            NativeMethods.ApplyToolWindowStyle(new WindowInteropHelper(this).Handle);
        };
        Closed += (_, _) => _hoverTimer.Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Chevron
    {
        get => _chevron;
        private set
        {
            if (_chevron == value) return;
            _chevron = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chevron)));
        }
    }

    internal void Position(double right, double top, double workHeight)
    {
        Left = right - Width;
        Top = top + ((workHeight - Height) / 2);
    }

    internal void SetDrawerOpen(bool isOpen)
    {
        Chevron = isOpen ? ">" : "<";
    }

    private void HandleButton_Click(object sender, RoutedEventArgs e) => _toggleDrawer();

    private void HandleButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pointerEntered();
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void HandleButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => _hoverTimer.Stop();

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (IsMouseOver)
        {
            _openDrawer();
        }
    }
}
