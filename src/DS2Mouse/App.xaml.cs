using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DS2Mouse.Models;
using DS2Mouse.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace DS2Mouse;

public partial class App : Application
{
    private TaskbarIcon? _tray;
    private IControllerReader? _reader;
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
    public IControllerReader Reader => _reader!;
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

        _reader = new ControllerSource();
        _mapper = new MapperEngine(_reader, _config);
        _guard = new FullscreenGuard(() => _config.FullscreenWhitelist);
        _mapper.Gate = _guard.ShouldSuppress;
        _mapper.EnabledChanged += OnMapperEnabledChanged;
        _reader.KindChanged += OnControllerKindChanged;
        UpdateAccentBrush(_reader.Kind);

        // Eager-create the keyboard so its first show is instant, but keep it hidden.
        _keyboard = new OnScreenKeyboardWindow();
        _mapper.OnSystemKeyboardToggle = _keyboard.Toggle;

        _reader.Start();
        _mapper.Start();
        _guard.Start();
        ShellInputSuppressor.Start();

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

    // The window background is a muted, controller-tinted dark; the accent is
    // the saturated version used for slider fills, focus borders, tab
    // indicators, etc. Both are swapped on KindChanged. None keeps the
    // previous color so the UI doesn't snap back to neutral when the user
    // briefly unplugs.
    private static readonly Color PsBackground   = Color.FromRgb(0x02, 0x74, 0xC9);
    private static readonly Color XboxBackground = Color.FromRgb(0x1E, 0x30, 0x1E);
    private static readonly Color PsAccent       = Color.FromRgb(0x4F, 0xC3, 0xF7);
    private static readonly Color XboxAccent     = Color.FromRgb(0x8B, 0xC3, 0x4A);

    private void OnControllerKindChanged(ControllerKind kind) =>
        Dispatcher.BeginInvoke(() => UpdateAccentBrush(kind));

    private void UpdateAccentBrush(ControllerKind kind)
    {
        (Brush? bg, Brush? accent) = kind switch
        {
            ControllerKind.DualSense                       => ((Brush?)Solid(PsBackground),   (Brush?)Solid(PsAccent)),
            ControllerKind.Xbox                            => ((Brush?)Solid(XboxBackground), (Brush?)Solid(XboxAccent)),
            ControllerKind.DualSense | ControllerKind.Xbox => ((Brush?)Gradient(PsBackground, XboxBackground),
                                                               (Brush?)Gradient(PsAccent,     XboxAccent)),
            _                                              => ((Brush?)null, (Brush?)null), // keep previous
        };
        if (bg     != null) Resources["WindowBackground"] = bg;
        if (accent != null) Resources["AccentBrush"]      = accent;

        static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        static LinearGradientBrush Gradient(Color a, Color b)
        {
            var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 0));
            g.Freeze();
            return g;
        }
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
        ShellInputSuppressor.Stop();
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
        ShellInputSuppressor.Stop();
        _mapper?.Stop();
        _guard?.Stop();
        _mapper?.Dispose();
        _guard?.Dispose();
        _reader?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
