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
    private MainWindow? _mainWindow;
    private MenuItem? _toggleMenuItem;

    public AppConfig Config => _config!;
    public DualSenseReader Reader => _reader!;
    public MapperEngine Mapper => _mapper!;
    public FullscreenGuard Guard => _guard!;

    public void SaveConfig() => _store?.Save(_config!);

    public new static App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _store  = new ConfigStore();
        _config = _store.Load();
        // Persist defaults on first run so the file is discoverable for editing.
        if (!System.IO.File.Exists(_store.FilePath)) _store.Save(_config);

        _reader = new DualSenseReader();
        _mapper = new MapperEngine(_reader, _config);
        _guard = new FullscreenGuard(() => _config.FullscreenWhitelist);
        _mapper.Gate = _guard.ShouldSuppress;
        _mapper.EnabledChanged += OnMapperEnabledChanged;

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
            ToolTipText = "DS2Mouse",
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        var menu = new ContextMenu();
        _toggleMenuItem = new MenuItem
        {
            Header = "Enable mapping",
            IsCheckable = true,
            IsChecked = _mapper!.Enabled,
        };
        // Click on a checkable MenuItem flips IsChecked before firing Click,
        // so we read the new state straight from the item.
        _toggleMenuItem.Click += (_, _) => _mapper!.Enabled = _toggleMenuItem!.IsChecked;
        menu.Items.Add(_toggleMenuItem);

        var showItem = new MenuItem { Header = "Show window" };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _tray.ContextMenu = menu;
    }

    private void OnMapperEnabledChanged(bool enabled) => Dispatcher.BeginInvoke(() =>
    {
        if (_toggleMenuItem != null)
            _toggleMenuItem.IsChecked = enabled;
        if (_config != null) _config.Enabled = enabled;
        SaveConfig();
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
