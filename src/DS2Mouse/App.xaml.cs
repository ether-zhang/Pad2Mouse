using System.Windows;
using System.Windows.Controls;
using DS2Mouse.Models;
using DS2Mouse.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace DS2Mouse;

public partial class App : Application
{
    private TaskbarIcon? _tray;
    private DualSenseReader? _reader;
    private MapperEngine? _mapper;
    private AppConfig? _config;
    private ConfigStore? _store;
    private FullscreenGuard? _guard;
    private LocalizationService? _loc;
    private MainWindow? _mainWindow;
    private OnScreenKeyboardWindow? _keyboard;
    private MenuItem? _toggleMenuItem;
    private MenuItem? _showMenuItem;
    private MenuItem? _exitMenuItem;

    public AppConfig Config => _config!;
    public DualSenseReader Reader => _reader!;
    public MapperEngine Mapper => _mapper!;
    public FullscreenGuard Guard => _guard!;
    public LocalizationService Loc => _loc!;
    public OnScreenKeyboardWindow Keyboard => _keyboard!;

    public void SaveConfig() => _store?.Save(_config!);

    public new static App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _store  = new ConfigStore();
        _config = _store.Load();
        // Persist defaults on first run so the file is discoverable for editing.
        if (!System.IO.File.Exists(_store.FilePath)) _store.Save(_config);

        _loc = new LocalizationService(this);
        _loc.SetLanguage(_config.Language);
        _loc.LanguageChanged += UpdateTrayMenuLabels;

        _reader = new DualSenseReader();
        _mapper = new MapperEngine(_reader, _config);
        _guard = new FullscreenGuard(() => _config.FullscreenWhitelist);
        _mapper.Gate = _guard.ShouldSuppress;
        _mapper.EnabledChanged += OnMapperEnabledChanged;

        // Eager-create the keyboard so its first show is instant, but keep it hidden.
        _keyboard = new OnScreenKeyboardWindow();
        _mapper.OnSystemKeyboardToggle = _keyboard.Toggle;

        _reader.Start();
        _mapper.Start();
        _guard.Start();

        InitTray();
    }

    private void InitTray()
    {
        _tray = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/tray.ico", UriKind.Absolute)),
            ToolTipText = "Pad2Mouse",
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        var menu = new ContextMenu();
        _toggleMenuItem = new MenuItem
        {
            IsCheckable = true,
            IsChecked = _mapper!.Enabled,
        };
        // Click on a checkable MenuItem flips IsChecked before firing Click,
        // so we read the new state straight from the item.
        _toggleMenuItem.Click += (_, _) => _mapper!.Enabled = _toggleMenuItem!.IsChecked;
        menu.Items.Add(_toggleMenuItem);

        _showMenuItem = new MenuItem();
        _showMenuItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(_showMenuItem);

        menu.Items.Add(new Separator());

        _exitMenuItem = new MenuItem();
        _exitMenuItem.Click += (_, _) => ExitApp();
        menu.Items.Add(_exitMenuItem);

        _tray.ContextMenu = menu;
        UpdateTrayMenuLabels();
    }

    private void UpdateTrayMenuLabels()
    {
        if (_toggleMenuItem != null) _toggleMenuItem.Header = Loc.Get("Action.EnableMapping");
        if (_showMenuItem   != null) _showMenuItem.Header   = Loc.Get("Action.ShowWindow");
        if (_exitMenuItem   != null) _exitMenuItem.Header   = Loc.Get("Action.Exit");
    }

    private void OnMapperEnabledChanged(bool enabled) => Dispatcher.BeginInvoke(() =>
    {
        if (_toggleMenuItem != null)
            _toggleMenuItem.IsChecked = enabled;
        if (_config != null) _config.Enabled = enabled;
        SaveConfig();

        if (_tray != null && _config?.EnableNotifications == true)
        {
            _tray.ShowBalloonTip(
                Loc.Get("Toast.Title"),
                Loc.Get(enabled ? "Toast.Enabled" : "Toast.Disabled"),
                BalloonIcon.Info);
        }
    });

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApp()
    {
        _mapper?.Stop();
        _guard?.Stop();
        _mapper?.Dispose();
        _guard?.Dispose();
        _reader?.Dispose();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveConfig();
        _mapper?.Stop();
        _guard?.Stop();
        _mapper?.Dispose();
        _guard?.Dispose();
        _reader?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
