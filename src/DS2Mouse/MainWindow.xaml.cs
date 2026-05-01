using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
    private readonly LocalizationService _loc;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        _reader = App.Current.Reader;
        _mapper = App.Current.Mapper;
        _config = App.Current.Config;
        _guard  = App.Current.Guard;
        _loc    = App.Current.Loc;

        // Reflect current config into UI before wiring change handlers.
        SensSlider.Value      = _config.LeftStick.Sensitivity;
        DzSlider.Value        = _config.LeftStick.Deadzone;
        AccelMaxSlider.Value  = _config.LeftStick.AccelMaxFactor;
        AccelRampSlider.Value = _config.LeftStick.AccelRampSeconds;
        ScrollSlider.Value    = _config.RightStick.Speed;
        SensVal.Text      = $"{_config.LeftStick.Sensitivity:0}";
        DzVal.Text        = $"{_config.LeftStick.Deadzone:0.00}";
        AccelMaxVal.Text  = $"{_config.LeftStick.AccelMaxFactor:0.0}";
        AccelRampVal.Text = $"{_config.LeftStick.AccelRampSeconds:0.0}";
        ScrollVal.Text    = $"{_config.RightStick.Speed:0}";
        EnableCheck.IsChecked = _mapper.Enabled;
        SelectLanguageInCombo(_loc.CurrentLanguage);
        RefreshWhitelistBox();
        SetConnectionLabel(_reader.ConnectionType);
        UpdateGuardStatus();
        _initialized = true;

        _reader.ConnectionChanged += OnConnectionChanged;
        _reader.FrameReceived     += OnFrameReceived;
        _mapper.EnabledChanged    += OnEnabledChanged;
        _guard.StateChanged       += OnGuardStateChanged;
        _loc.LanguageChanged      += OnLanguageRefresh;
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
        _loc.LanguageChanged      -= OnLanguageRefresh;
        base.OnClosing(e);
    }

    private void OnGuardStateChanged() => Dispatcher.BeginInvoke(UpdateGuardStatus);

    private void UpdateGuardStatus()
    {
        var fg = _guard.ForegroundName ?? "—";
        var fs = _guard.IsFullscreen ? _loc.Get("Stat.Yes") : _loc.Get("Stat.No");
        var sup = _guard.Suppressed ? _loc.Get("Stat.Yes") : _loc.Get("Stat.No");
        GuardStatusText.Text =
            $"{_loc.Get("Stat.Foreground")}: {fg}    " +
            $"{_loc.Get("Stat.Fullscreen")}: {fs}    " +
            $"{_loc.Get("Stat.Suppressed")}: {sup}";
    }

    private void OnConnectionChanged(ConnectionType c) => Dispatcher.BeginInvoke(() => SetConnectionLabel(c));

    private void SetConnectionLabel(ConnectionType c)
    {
        ConnText.Text = c switch
        {
            ConnectionType.Usb       => _loc.Get("Status.UsbConnected"),
            ConnectionType.Bluetooth => _loc.Get("Status.BtConnected"),
            _                        => _loc.Get("Status.Disconnected"),
        };
    }

    private void OnFrameReceived(DualSenseState s) => Dispatcher.BeginInvoke(() =>
    {
        StickText.Text = $"L:({s.LeftStickX,6:0.00}, {s.LeftStickY,6:0.00})  R:({s.RightStickX,6:0.00}, {s.RightStickY,6:0.00})";
        TrigText.Text  = $"L2:{s.L2Trigger:0.00}  R2:{s.R2Trigger:0.00}";
        var btnLabel   = _loc.Get("Stat.Buttons");
        var btnValue   = s.Buttons == 0 ? _loc.Get("Stat.None") : s.Buttons.ToString();
        BtnText.Text   = $"{btnLabel} {btnValue}";
    });

    private void OnEnabledChanged(bool enabled) => Dispatcher.BeginInvoke(() => EnableCheck.IsChecked = enabled);

    private void OnLanguageRefresh()
    {
        // DynamicResource handles XAML labels; refresh the strings we set in code.
        SetConnectionLabel(_reader.ConnectionType);
        UpdateGuardStatus();
        // BtnText updates on next frame.
    }

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
        App.Current.SaveConfig();
    }

    private void OnDzChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.LeftStick.Deadzone = (float)e.NewValue;
        DzVal.Text = $"{e.NewValue:0.00}";
        App.Current.SaveConfig();
    }

    private void OnScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.RightStick.Speed = (float)e.NewValue;
        ScrollVal.Text = $"{e.NewValue:0}";
        App.Current.SaveConfig();
    }

    private void OnAccelMaxChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.LeftStick.AccelMaxFactor = (float)e.NewValue;
        AccelMaxVal.Text = $"{e.NewValue:0.0}";
        App.Current.SaveConfig();
    }

    private void OnAccelRampChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.LeftStick.AccelRampSeconds = (float)e.NewValue;
        AccelRampVal.Text = $"{e.NewValue:0.0}";
        App.Current.SaveConfig();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string code)
        {
            _loc.SetLanguage(code);
            _config.Language = _loc.CurrentLanguage;
            App.Current.SaveConfig();
        }
    }

    private void SelectLanguageInCombo(string code)
    {
        foreach (var obj in LanguageCombo.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag && tag == code)
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }
        LanguageCombo.SelectedIndex = 0;
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
            App.Current.SaveConfig();
        }
        WhitelistInput.Text = string.Empty;
    }

    private void OnWhitelistRemove(object sender, RoutedEventArgs e)
    {
        if (WhitelistBox.SelectedItem is string s)
        {
            _config.FullscreenWhitelist.Remove(s);
            RefreshWhitelistBox();
            App.Current.SaveConfig();
        }
    }

    private void RefreshWhitelistBox()
    {
        WhitelistBox.ItemsSource = null;
        WhitelistBox.ItemsSource = _config.FullscreenWhitelist;
    }
}
