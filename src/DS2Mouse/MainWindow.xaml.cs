using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DS2Mouse.Models;
using DS2Mouse.Services;

namespace DS2Mouse;

public partial class MainWindow : Window
{
    private readonly DualSenseReader _reader;
    private readonly MapperEngine _mapper;
    private readonly AppConfig _config;
    private readonly FullscreenGuard _guard;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        _reader = App.Current.Reader;
        _mapper = App.Current.Mapper;
        _config = App.Current.Config;
        _guard  = App.Current.Guard;

        // Reflect current config into UI before wiring change handlers.
        SensSlider.Value   = _config.LeftStick.Sensitivity;
        DzSlider.Value     = _config.LeftStick.Deadzone;
        ScrollSlider.Value = _config.RightStick.Speed;
        SensVal.Text   = $"{_config.LeftStick.Sensitivity:0}";
        DzVal.Text     = $"{_config.LeftStick.Deadzone:0.00}";
        ScrollVal.Text = $"{_config.RightStick.Speed:0}";
        EnableCheck.IsChecked = _mapper.Enabled;
        RefreshWhitelistBox();
        SetConnectionLabel(_reader.ConnectionType);
        _initialized = true;

        _reader.ConnectionChanged += OnConnectionChanged;
        _reader.FrameReceived     += OnFrameReceived;
        _mapper.EnabledChanged    += OnEnabledChanged;
        _guard.StateChanged       += OnGuardStateChanged;
        UpdateGuardStatus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hide to tray instead of exiting; tray menu's Exit triggers real shutdown.
        e.Cancel = true;
        Hide();

        _reader.ConnectionChanged -= OnConnectionChanged;
        _reader.FrameReceived     -= OnFrameReceived;
        _mapper.EnabledChanged    -= OnEnabledChanged;
        _guard.StateChanged       -= OnGuardStateChanged;
        base.OnClosing(e);
    }

    private void OnGuardStateChanged() => Dispatcher.BeginInvoke(UpdateGuardStatus);

    private void UpdateGuardStatus()
    {
        var fg = _guard.ForegroundName ?? "—";
        var fs = _guard.IsFullscreen ? "yes" : "no";
        var sup = _guard.Suppressed ? "yes" : "no";
        GuardStatusText.Text = $"Foreground: {fg}    Fullscreen: {fs}    Suppressed: {sup}";
    }

    private void OnConnectionChanged(ConnectionType c) => Dispatcher.BeginInvoke(() => SetConnectionLabel(c));

    private void SetConnectionLabel(ConnectionType c)
    {
        ConnText.Text = c switch
        {
            ConnectionType.Usb => "Connected (USB)",
            ConnectionType.Bluetooth => "Connected (Bluetooth)",
            _ => "Disconnected",
        };
    }

    private void OnFrameReceived(DualSenseState s) => Dispatcher.BeginInvoke(() =>
    {
        StickText.Text = $"L:({s.LeftStickX,6:0.00}, {s.LeftStickY,6:0.00})  R:({s.RightStickX,6:0.00}, {s.RightStickY,6:0.00})";
        TrigText.Text  = $"L2:{s.L2Trigger:0.00}  R2:{s.R2Trigger:0.00}";
        BtnText.Text   = $"Buttons: {(s.Buttons == 0 ? "None" : s.Buttons.ToString())}";
    });

    private void OnEnabledChanged(bool enabled) => Dispatcher.BeginInvoke(() => EnableCheck.IsChecked = enabled);

    private void OnEnableToggle(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _mapper.Enabled = EnableCheck.IsChecked == true;
    }

    private void OnSensChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.LeftStick.Sensitivity = (float)e.NewValue;
        SensVal.Text = $"{e.NewValue:0}";
    }

    private void OnDzChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.LeftStick.Deadzone = (float)e.NewValue;
        DzVal.Text = $"{e.NewValue:0.00}";
    }

    private void OnScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.RightStick.Speed = (float)e.NewValue;
        ScrollVal.Text = $"{e.NewValue:0}";
    }

    private void OnWhitelistAdd(object sender, RoutedEventArgs e) => AddWhitelistFromInput();

    private void OnWhitelistInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddWhitelistFromInput();
    }

    private void AddWhitelistFromInput()
    {
        var name = WhitelistInput.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!_config.FullscreenWhitelist.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _config.FullscreenWhitelist.Add(name);
            RefreshWhitelistBox();
        }
        WhitelistInput.Text = string.Empty;
    }

    private void OnWhitelistRemove(object sender, RoutedEventArgs e)
    {
        if (WhitelistBox.SelectedItem is string s)
        {
            _config.FullscreenWhitelist.Remove(s);
            RefreshWhitelistBox();
        }
    }

    private void RefreshWhitelistBox()
    {
        WhitelistBox.ItemsSource = null;
        WhitelistBox.ItemsSource = _config.FullscreenWhitelist;
    }
}
