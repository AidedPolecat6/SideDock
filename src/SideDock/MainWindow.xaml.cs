using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using SideDock.Models;
using SideDock.Native;
using SideDock.Services;

namespace SideDock;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string DragItemFormat = "SideDock.ItemId";
    private static readonly object LoadingMarker = new();

    private readonly ConfigurationStore _configurationStore;
    private readonly FolderBrowser _folderBrowser;
    private readonly TabFolderService _tabFolderService;
    private readonly OutsideClickMonitor _outsideClickMonitor;
    private readonly Stopwatch _drawerAnimationClock = new();
    private readonly DispatcherTimer _hoverRegionTimer;
    private readonly DispatcherTimer _folderSyncTimer;
    private readonly HashSet<ContextMenu> _openMenus = [];
    private readonly List<FileSystemWatcher> _folderWatchers = [];
    private HwndSource? _windowSource;
    private HandleWindow? _handleWindow;
    private HowToWindow? _howToWindow;
    private bool _isOpen;
    private bool _isEditing;
    private int _openMenuCount;
    private double _openLeft;
    private double _closedLeft;
    private double _animationDestination;
    private int _openLeftPixel;
    private int _closedLeftPixel;
    private int _animationStartLeftPixel;
    private int _animationTargetLeftPixel;
    private int _animationTopPixel;
    private bool _isDrawerAnimating;
    private bool _closeOnMouseLeaveArmed;
    private long? _pointerLeftTimestamp;
    private Point _dragStart;

    internal MainWindow(
        SideDockConfiguration configuration,
        ConfigurationStore configurationStore,
        FolderBrowser folderBrowser,
        TabFolderService tabFolderService)
    {
        Configuration = configuration;
        _configurationStore = configurationStore;
        _folderBrowser = folderBrowser;
        _tabFolderService = tabFolderService;
        _isEditing = !configuration.IsLocked;

        InitializeComponent();
        _outsideClickMonitor = new OutsideClickMonitor(Dispatcher, HandleOutsideClick);
        _hoverRegionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(25),
            DispatcherPriority.Background,
            HoverRegionTimer_Tick,
            Dispatcher);
        _hoverRegionTimer.Stop();
        _folderSyncTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            FolderSyncTimer_Tick,
            Dispatcher);
        _folderSyncTimer.Stop();
        RebuildFolderWatchers();
        DataContext = this;
        Width = Configuration.DrawerWidth;
        Height = Configuration.DrawerHeight;
        PinButtonText.Text = Configuration.IsPinnedOpen ? "UNPIN" : "PIN";
        EditButtonText.Text = IsEditing ? "LOCK" : "EDIT";
        SourceInitialized += Window_SourceInitialized;
        Closed += Window_Closed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SideDockConfiguration Configuration { get; }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            Configuration.IsLocked = !value;
            EditButtonText.Text = value ? "LOCK" : "EDIT";
            OnPropertyChanged();
            SaveConfiguration();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource.CompositionTarget.BackgroundColor = Colors.Transparent;
        NativeMethods.ApplyBlurBehind(handle);
        NativeMethods.ApplyLeftRoundedRegion(handle, 32);
        NativeMethods.ApplyToolWindowStyle(handle);
        _windowSource.AddHook(WindowMessageHook);
        if (!NativeMethods.RegisterHotKey(
                handle,
                NativeMethods.HotKeyId,
                Configuration.HotKeyModifiers,
                Configuration.HotKeyVirtualKey))
        {
            SetStatus("Ctrl+Alt+Space is already used by another app");
        }
        _handleWindow = new HandleWindow(ToggleDrawerFromHandle, OpenDrawerFromHover, ArmMouseLeaveClose, Configuration.HandleWidth)
        {
            Owner = this
        };
        _handleWindow.Show();
        PositionAtCursorMonitor(false);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        StopDrawerAnimation();
        _hoverRegionTimer.Stop();
        _folderSyncTimer.Stop();
        foreach (var watcher in _folderWatchers) watcher.Dispose();
        _folderWatchers.Clear();
        _outsideClickMonitor.Dispose();
        if (_windowSource != null)
        {
            NativeMethods.UnregisterHotKey(_windowSource.Handle, NativeMethods.HotKeyId);
            _windowSource.RemoveHook(WindowMessageHook);
        }
        _handleWindow?.Close();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_windowSource != null)
        {
            NativeMethods.ApplyLeftRoundedRegion(_windowSource.Handle, 32);
        }
    }

    private IntPtr WindowMessageHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotKey && wParam.ToInt32() == NativeMethods.HotKeyId)
        {
            ToggleDrawer();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void PositionAtCursorMonitor(bool keepCurrentState)
    {
        StopDrawerAnimation();
        var pixelArea = NativeMethods.GetCursorMonitorWorkArea();
        if (pixelArea.IsEmpty || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            var workArea = SystemParameters.WorkArea;
            Width = Configuration.DrawerWidth;
            Height = Math.Min(Configuration.DrawerHeight, workArea.Height - 32);
            Top = workArea.Top + ((workArea.Height - Height) / 2);
            _openLeft = workArea.Right - Width;
            _closedLeft = workArea.Right;
            _handleWindow?.Position(workArea.Right, workArea.Top, workArea.Height);
        }
        else
        {
            var fromDevice = target.TransformFromDevice;
            var topLeft = fromDevice.Transform(new Point(pixelArea.Left, pixelArea.Top));
            var bottomRight = fromDevice.Transform(new Point(pixelArea.Right, pixelArea.Bottom));
            var workHeight = bottomRight.Y - topLeft.Y;
            Width = Configuration.DrawerWidth;
            Height = Math.Min(Configuration.DrawerHeight, workHeight - 32);
            Top = topLeft.Y + ((workHeight - Height) / 2);
            _openLeft = bottomRight.X - Width;
            _closedLeft = bottomRight.X;
            _handleWindow?.Position(bottomRight.X, topLeft.Y, workHeight);
        }

        Left = keepCurrentState && _isOpen ? _openLeft : _closedLeft;
        if (_windowSource is not null)
        {
            var bounds = NativeMethods.GetWindowBounds(_windowSource.Handle);
            if (!bounds.IsEmpty)
            {
                if (keepCurrentState && _isOpen)
                {
                    _openLeftPixel = bounds.Left;
                    _closedLeftPixel = bounds.Right;
                }
                else
                {
                    _closedLeftPixel = bounds.Left;
                    _openLeftPixel = bounds.Left - bounds.Width;
                }
            }
        }
    }

    private void ToggleDrawer()
    {
        if (_isOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }

    private void ToggleDrawerFromHandle()
    {
        if (_isOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer(true, true);
        }
    }

    private void OpenDrawer() => OpenDrawer(true, false);

    private void OpenDrawerFromHover() => OpenDrawer(false, true);

    private void OpenDrawer(bool activate, bool armMouseLeave)
    {
        if (_isOpen) return;

        PositionAtCursorMonitor(false);
        _isOpen = true;
        _closeOnMouseLeaveArmed = armMouseLeave;
        _pointerLeftTimestamp = null;
        _hoverRegionTimer.Start();
        _outsideClickMonitor.Start();
        _handleWindow?.SetDrawerOpen(true);
        SetStatus("Ready");
        if (activate)
        {
            ActivateDrawer();
        }
        AnimateTo(true);
    }

    private void ActivateDrawer()
    {
        ShowActivated = true;
        Activate();
        DrawerTabs.Focus();
    }

    private void CloseDrawer()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _hoverRegionTimer.Stop();
        _closeOnMouseLeaveArmed = false;
        _pointerLeftTimestamp = null;
        _outsideClickMonitor.Stop();
        foreach (var menu in _openMenus.ToArray())
        {
            menu.IsOpen = false;
        }
        _handleWindow?.SetDrawerOpen(false);
        ShowActivated = false;
        AnimateTo(false);
    }

    private void AnimateTo(bool opening)
    {
        var destination = opening ? _openLeft : _closedLeft;
        if (_windowSource is null)
        {
            Left = destination;
            return;
        }

        var bounds = NativeMethods.GetWindowBounds(_windowSource.Handle);
        if (bounds.IsEmpty)
        {
            Left = destination;
            return;
        }

        _animationStartLeftPixel = bounds.Left;
        _animationTargetLeftPixel = opening ? _openLeftPixel : _closedLeftPixel;
        _animationTopPixel = bounds.Top;
        _animationDestination = destination;
        _drawerAnimationClock.Restart();

        if (_isDrawerAnimating) return;
        _isDrawerAnimating = true;
        CompositionTarget.Rendering += DrawerAnimation_Rendering;
    }

    private void DrawerAnimation_Rendering(object? sender, EventArgs e)
    {
        if (_windowSource is null)
        {
            StopDrawerAnimation();
            return;
        }

        var progress = Math.Clamp(
            _drawerAnimationClock.Elapsed.TotalMilliseconds / Configuration.AnimationMilliseconds,
            0,
            1);
        var easedProgress = (1 - Math.Cos(Math.PI * progress)) / 2;
        var left = (int)Math.Round(
            _animationStartLeftPixel
            + ((_animationTargetLeftPixel - _animationStartLeftPixel) * easedProgress));
        _ = NativeMethods.SetWindowPosition(_windowSource.Handle, left, _animationTopPixel);
        NativeMethods.FlushDesktopComposition();

        if (progress < 1) return;

        _ = NativeMethods.SetWindowPosition(_windowSource.Handle, _animationTargetLeftPixel, _animationTopPixel);
        StopDrawerAnimation();
        Left = _animationDestination;
    }

    private void StopDrawerAnimation()
    {
        if (!_isDrawerAnimating) return;
        CompositionTarget.Rendering -= DrawerAnimation_Rendering;
        _drawerAnimationClock.Stop();
        _isDrawerAnimating = false;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!Configuration.CloseOnDeactivate || Configuration.IsPinnedOpen || !_isOpen || _openMenuCount > 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(100);
            if (_openMenuCount == 0 && !IsActive && !Configuration.IsPinnedOpen)
            {
                CloseDrawer();
            }
        });
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        ArmMouseLeaveClose();
        if (_isOpen && !IsActive)
        {
            ActivateDrawer();
        }
    }

    private void ArmMouseLeaveClose()
    {
        _closeOnMouseLeaveArmed = true;
        _pointerLeftTimestamp = null;
    }

    private void HoverRegionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isOpen || !_closeOnMouseLeaveArmed || Configuration.IsPinnedOpen)
        {
            _pointerLeftTimestamp = null;
            return;
        }

        if (NativeMethods.IsCursorOverProcess(Environment.ProcessId))
        {
            _pointerLeftTimestamp = null;
            return;
        }

        _pointerLeftTimestamp ??= Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_pointerLeftTimestamp.Value).TotalMilliseconds >= 150)
        {
            CloseDrawer();
        }
    }

    private void HandleOutsideClick()
    {
        if (!Configuration.CloseOnDeactivate || Configuration.IsPinnedOpen || !_isOpen || _openMenuCount > 0)
        {
            return;
        }

        CloseDrawer();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox { Name: "TabNameEditor" }) return;

        if (e.Key == Key.Escape)
        {
            CloseDrawer();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Configuration.Tabs.Count > 1)
            {
                DrawerTabs.SelectedIndex = (DrawerTabs.SelectedIndex + 1) % Configuration.Tabs.Count;
            }
            e.Handled = true;
        }
    }

    private void TabItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is TabItem { IsSelected: false } tabItem
            && Keyboard.FocusedElement is not TextBox { Name: "TabNameEditor" })
        {
            tabItem.IsSelected = true;
        }
    }

    private void TileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DockItem item } button) return;
        if (item.Kind == DockItemKind.Folder)
        {
            OpenFolderMenu(button, item);
            return;
        }

        TryLaunch(item.Path);
    }

    private void OpenFolderMenu(Button owner, DockItem item)
    {
        var menu = CreateContextMenu(owner);
        var rootPath = NormalizePath(item.EffectiveBrowsePath);
        PopulateFolderMenu(menu.Items, item.EffectiveBrowsePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootPath });
        menu.IsOpen = true;
    }

    private void PopulateFolderMenu(ItemCollection items, string folderPath, HashSet<string> ancestors)
    {
        items.Clear();
        var openFolder = new MenuItem { Header = "Open folder in Explorer", Tag = folderPath };
        openFolder.Click += (_, _) => TryLaunch(folderPath, false);
        items.Add(openFolder);
        items.Add(new Separator());

        var entries = _folderBrowser.Enumerate(folderPath);
        if (entries.Count == 0)
        {
            items.Add(new MenuItem { Header = "This folder is empty or unavailable", IsEnabled = false });
            return;
        }

        foreach (var entry in entries)
        {
            var menuItem = new MenuItem
            {
                Header = CreateMenuHeader(entry.Name, entry.Path),
                ToolTip = entry.Path,
                Tag = entry
            };
            System.Windows.Automation.AutomationProperties.SetName(menuItem, entry.Name);

            if (entry.IsFolder)
            {
                var normalizedTarget = NormalizePath(entry.EffectiveBrowsePath);
                if (ancestors.Contains(normalizedTarget))
                {
                    menuItem.Click += (_, _) => TryLaunch(entry.Path);
                }
                else
                {
                    menuItem.Items.Add(LoadingMarker);
                    var childAncestors = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase)
                    {
                        normalizedTarget
                    };
                    menuItem.SubmenuOpened += (_, _) =>
                    {
                        if (menuItem.Items.Count == 1 && ReferenceEquals(menuItem.Items[0], LoadingMarker))
                        {
                            PopulateFolderMenu(menuItem.Items, entry.EffectiveBrowsePath, childAncestors);
                        }
                    };
                }
            }
            else
            {
                menuItem.Click += (_, _) => TryLaunch(entry.Path);
            }
            items.Add(menuItem);
        }
    }

    private static object CreateMenuHeader(string label, string path)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Image { Source = ShellIconService.GetIcon(path), Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) },
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };
    }

    private ContextMenu CreateContextMenu(UIElement owner)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("GlassContextMenuStyle"),
            PlacementTarget = owner,
            Placement = PlacementMode.Left,
            HorizontalOffset = -6,
            MaxHeight = Math.Max(320, ActualHeight - 40),
            Background = new SolidColorBrush(Color.FromRgb(21, 21, 21)),
            Foreground = new SolidColorBrush(Color.FromRgb(247, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 98, 94)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2)
        };
        menu.Resources[typeof(MenuItem)] = FindResource("DarkMenuItemStyle");
        menu.Resources[typeof(Separator)] = FindResource("DarkSeparatorStyle");
        menu.Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(Color.FromRgb(21, 21, 21));
        menu.Resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(Color.FromRgb(247, 250, 253));
        menu.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(Color.FromRgb(72, 72, 72));
        menu.Resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(Color.FromRgb(247, 250, 253));
        menu.Resources[SystemColors.ControlBrushKey] = new SolidColorBrush(Color.FromRgb(21, 21, 21));
        menu.Resources[SystemColors.ControlTextBrushKey] = new SolidColorBrush(Color.FromRgb(247, 250, 253));
        _openMenus.Add(menu);
        _openMenuCount++;
        menu.Closed += (_, _) =>
        {
            _openMenus.Remove(menu);
            _openMenuCount = Math.Max(0, _openMenuCount - 1);
            if (_openMenuCount == 0 && !IsActive && !Configuration.IsPinnedOpen)
            {
                CloseDrawer();
            }
        };
        return menu;
    }

    private void TryLaunch(string target, bool closeAfterLaunch = true)
    {
        try
        {
            LaunchService.Launch(target);
            SetStatus($"Opened {ItemClassifier.GetLabel(target)}");
            if (closeAfterLaunch && !Configuration.IsPinnedOpen) CloseDrawer();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Configuration.IsPinnedOpen = !Configuration.IsPinnedOpen;
        PinButtonText.Text = Configuration.IsPinnedOpen ? "UNPIN" : "PIN";
        SaveConfiguration();
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => IsEditing = !IsEditing;

    private void TabName_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || !IsEditing || sender is not TextBlock { Tag: TextBox editor }) return;

        editor.Tag = editor.Text;
        editor.Visibility = Visibility.Visible;
        editor.Focus();
        editor.SelectAll();
        e.Handled = true;
    }

    private void TabNameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor) return;

        if (e.Key == Key.Enter)
        {
            CommitTabName(editor);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTabName(editor);
            e.Handled = true;
        }
    }

    private void TabNameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Visibility: Visibility.Visible } editor)
        {
            CommitTabName(editor);
        }
    }

    private void CommitTabName(TextBox editor)
    {
        if (editor.DataContext is not DockTab tab) return;

        var name = editor.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Tab names cannot be empty");
            CancelTabName(editor);
            return;
        }

        try
        {
            _tabFolderService.RenameFolder(tab, name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
            CancelTabName(editor);
            return;
        }

        tab.Name = name;
        editor.Text = name;
        EndTabNameEdit(editor);
        SaveConfiguration();
        RebuildFolderWatchers();
        SetStatus($"Renamed tab to {name}");
    }

    private static void CancelTabName(TextBox editor)
    {
        if (editor.Tag is string originalName)
        {
            editor.Text = originalName;
        }
        EndTabNameEdit(editor);
    }

    private static void EndTabNameEdit(TextBox editor)
    {
        editor.Visibility = Visibility.Collapsed;
        editor.Tag = null;
        if (editor.Parent is Panel panel)
        {
            panel.Children.OfType<TextBlock>().FirstOrDefault()?.Focus();
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = CreateContextMenu(button);

        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = StartupService.IsEnabled() };
        startup.Click += (_, _) =>
        {
            StartupService.SetEnabled(startup.IsChecked);
            Configuration.StartWithWindows = startup.IsChecked;
            SaveConfiguration();
        };
        menu.Items.Add(startup);

        var import = new MenuItem { Header = "Refresh current tab folder" };
        import.Click += (_, _) => RefreshCurrentTabFolder();
        menu.Items.Add(import);

        var shortcutsFolder = new MenuItem { Header = "Open current tab folder" };
        shortcutsFolder.Click += (_, _) => OpenCurrentTabFolder();
        menu.Items.Add(shortcutsFolder);

        if (CurrentTab() is { } currentTab)
        {
            var fullWidthRows = new MenuItem
            {
                Header = "Full-width rows for this tab",
                IsCheckable = true,
                IsChecked = currentTab.UseFullWidthRows
            };
            fullWidthRows.Click += (_, _) =>
            {
                currentTab.UseFullWidthRows = fullWidthRows.IsChecked;
                SaveConfiguration();
            };
            menu.Items.Add(fullWidthRows);
        }

        var configFolder = new MenuItem { Header = "Open configuration folder" };
        configFolder.Click += (_, _) => LaunchService.Launch(_configurationStore.ProfileDirectory);
        menu.Items.Add(configFolder);

        var howTo = new MenuItem { Header = "How to use" };
        howTo.Click += (_, _) => OpenHowToWindow();
        menu.Items.Add(howTo);

        var removeTab = new MenuItem { Header = "Remove current tab", IsEnabled = IsEditing && Configuration.Tabs.Count > 1 };
        removeTab.Click += (_, _) => RemoveCurrentTab();
        menu.Items.Add(removeTab);
        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit SideDock" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);
        menu.IsOpen = true;
    }

    private void OpenHowToWindow()
    {
        if (_howToWindow == null)
        {
            _howToWindow = new HowToWindow();
            _howToWindow.Closed += (_, _) => _howToWindow = null;
        }
        _howToWindow.Show();
        _howToWindow.Activate();
    }

    private void RefreshCurrentTabFolder()
    {
        var tab = CurrentTab();
        if (tab == null) return;
        try
        {
            _tabFolderService.Sync(tab);
            SaveConfiguration();
            SetStatus($"Refreshed {tab.Name}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
        }
    }

    private void OpenCurrentTabFolder()
    {
        if (CurrentTab() is not { } tab) return;
        try
        {
            Directory.CreateDirectory(tab.FolderPath);
            TryLaunch(tab.FolderPath, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
        }
    }

    private void RemoveCurrentTab()
    {
        if (!IsEditing || Configuration.Tabs.Count <= 1 || CurrentTab() is not { } tab) return;
        if (tab.IsLinkedFolder)
        {
            var removeLinked = MessageBox.Show(
                this,
                $"Remove the linked tab '{tab.Name}' and delete only its descriptor shortcut?\n\nThe target folder and all files inside it will remain untouched.",
                "Remove linked SideDock tab",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (removeLinked != MessageBoxResult.Yes) return;
        }
        else
        {
            var choice = MessageBox.Show(
                this,
                $"Delete the folder '{tab.FolderPath}' too?\n\nYes: delete folder and tab\nNo: keep folder and remove tab\nCancel: keep both",
                "Remove SideDock tab",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (choice is not MessageBoxResult.Yes and not MessageBoxResult.No) return;
            if (choice == MessageBoxResult.No)
            {
                Configuration.Tabs.Remove(tab);
                DrawerTabs.SelectedIndex = 0;
                RebuildFolderWatchers();
                SaveConfiguration();
                return;
            }
        }

        try
        {
            _tabFolderService.DeleteFolder(tab);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
            return;
        }
        Configuration.Tabs.Remove(tab);
        DrawerTabs.SelectedIndex = 0;
        RebuildFolderWatchers();
        SaveConfiguration();
    }

    private void RemoveTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditing || sender is not Button { Tag: DockItem item } || CurrentTab() is not { } tab) return;
        if (tab.IsLinkedFolder)
        {
            SetStatus("Linked folder tabs are read-only");
            return;
        }
        try
        {
            _tabFolderService.RemoveReference(tab, item);
            SaveConfiguration();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
        }
        e.Handled = true;
    }

    private void Tile_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: DockItem item } button) return;
        var menu = CreateContextMenu(button);

        if (item.Kind == DockItemKind.Folder)
        {
            var open = new MenuItem { Header = "Open in Explorer" };
            open.Click += (_, _) => TryLaunch(item.EffectiveBrowsePath, false);
            menu.Items.Add(open);
        }

        var location = new MenuItem { Header = "Open containing folder" };
        location.Click += (_, _) => LaunchService.ShowInExplorer(item.Path);
        menu.Items.Add(location);

        var remove = new MenuItem { Header = "Remove tile", IsEnabled = IsEditing && CurrentTab() is { IsLinkedFolder: false } };
        remove.Click += (_, _) =>
        {
            if (CurrentTab() is not { } tab) return;
            try
            {
                _tabFolderService.RemoveReference(tab, item);
                SaveConfiguration();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SetStatus(exception.Message);
            }
        };
        menu.Items.Add(remove);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsEditing
            || CurrentTab() is not { IsLinkedFolder: false }
            || e.LeftButton != MouseButtonState.Pressed
            || sender is not Button { Tag: DockItem item }) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(DragItemFormat, item.Id.ToString());
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
    }

    private void Tile_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CurrentTab() is { IsLinkedFolder: true }
            ? DragDropEffects.None
            : e.Data.GetDataPresent(DragItemFormat) && IsEditing
                ? DragDropEffects.Move
                : GetExternalDropEffect(e.Data);
        e.Handled = true;
    }

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: DockItem target }
            && IsEditing
            && CurrentTab() is { IsLinkedFolder: false }
            && e.Data.GetData(DragItemFormat) is string idText
            && Guid.TryParse(idText, out var id)
            && CurrentTab() is { } tab)
        {
            var source = tab.Items.FirstOrDefault(item => item.Id == id);
            if (source != null && source != target)
            {
                var targetIndex = tab.Items.IndexOf(target);
                tab.Items.Move(tab.Items.IndexOf(source), targetIndex);
                SaveConfiguration();
            }
            e.Handled = true;
            return;
        }

        AddDroppedItems(e.Data);
        e.Handled = true;
    }

    private void Drawer_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CurrentTab() is { IsLinkedFolder: true }
            ? DragDropEffects.None
            : GetExternalDropEffect(e.Data);
        e.Handled = true;
    }

    private void Drawer_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragItemFormat)) AddDroppedItems(e.Data);
        e.Handled = true;
    }

    private static DragDropEffects GetExternalDropEffect(IDataObject data)
    {
        return data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.UnicodeText)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void AddDroppedItems(IDataObject data)
    {
        if (CurrentTab() is not { } tab) return;
        if (tab.IsLinkedFolder)
        {
            SetStatus("Linked folder tabs are read-only");
            return;
        }
        var paths = new List<string>();
        if (data.GetData(DataFormats.FileDrop) is string[] files)
        {
            paths.AddRange(files);
        }
        else if (data.GetData(DataFormats.UnicodeText) is string text
                 && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            paths.Add(uri.AbsoluteUri);
        }

        var added = 0;
        foreach (var path in paths)
        {
            try
            {
                var count = tab.Items.Count;
                _tabFolderService.AddReference(tab, path);
                if (tab.Items.Count > count) added++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
            {
                SetStatus(exception.Message);
            }
        }
        if (added > 0)
        {
            SaveConfiguration();
            SetStatus($"Added {added} item{(added == 1 ? "" : "s")}");
        }
    }

    private DockTab? CurrentTab() => DrawerTabs.SelectedItem as DockTab;

    private void FolderWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            _folderSyncTimer.Stop();
            _folderSyncTimer.Start();
        });
    }

    private void FolderSyncTimer_Tick(object? sender, EventArgs e)
    {
        _folderSyncTimer.Stop();
        try
        {
            _tabFolderService.Refresh(Configuration);
            RebuildFolderWatchers();
            SaveConfiguration();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message);
        }
    }

    private void RebuildFolderWatchers()
    {
        foreach (var watcher in _folderWatchers) watcher.Dispose();
        _folderWatchers.Clear();

        AddFolderWatcher(_tabFolderService.RootPath, true);
        var watchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(_tabFolderService.RootPath)
        };
        foreach (var path in Configuration.Tabs
                     .Where(tab => tab.IsLinkedFolder && Directory.Exists(tab.FolderPath))
                     .Select(tab => Path.GetFullPath(tab.FolderPath)))
        {
            if (watchedPaths.Add(path)) AddFolderWatcher(path, false);
        }
    }

    private void AddFolderWatcher(string path, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        watcher.Created += FolderWatcher_Changed;
        watcher.Changed += FolderWatcher_Changed;
        watcher.Deleted += FolderWatcher_Changed;
        watcher.Renamed += FolderWatcher_Changed;
        watcher.EnableRaisingEvents = true;
        _folderWatchers.Add(watcher);
    }

    private void SaveConfiguration()
    {
        try
        {
            _configurationStore.Save(Configuration);
        }
        catch (IOException exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void SetStatus(string text) => StatusLabel.Text = text;

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
