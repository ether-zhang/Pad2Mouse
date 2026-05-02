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
        SensSlider.Value            = _config.LeftStick.Sensitivity;
        DzSlider.Value              = _config.LeftStick.Deadzone;
        AccelMaxSlider.Value        = _config.LeftStick.AccelMaxFactor;
        AccelRampSlider.Value       = _config.LeftStick.AccelRampSeconds;
        ScrollSlider.Value          = _config.RightStick.Speed;
        ScrollAccelMaxSlider.Value  = _config.RightStick.AccelMaxFactor;
        ScrollAccelRampSlider.Value = _config.RightStick.AccelRampSeconds;
        SensVal.Text            = $"{_config.LeftStick.Sensitivity:0}";
        DzVal.Text              = $"{_config.LeftStick.Deadzone:0.00}";
        AccelMaxVal.Text        = $"{_config.LeftStick.AccelMaxFactor:0.0}";
        AccelRampVal.Text       = $"{_config.LeftStick.AccelRampSeconds:0.0}";
        ScrollVal.Text          = $"{_config.RightStick.Speed:0}";
        ScrollAccelMaxVal.Text  = $"{_config.RightStick.AccelMaxFactor:0.0}";
        ScrollAccelRampVal.Text = $"{_config.RightStick.AccelRampSeconds:0.0}";
        EnableCheck.IsChecked = _mapper.Enabled;
        NotifyCheck.IsChecked = _config.EnableNotifications;
        AutoStartCheck.IsChecked = StartupRegistration.IsRegistered();
        SelectLanguageInCombo(_loc.CurrentLanguage);
        PopulateMappingCombos();
        RefreshWhitelistBox();
        SetConnectionLabel(_reader.ConnectionType);
        UpdateGuardStatus();
        _initialized = true;

        _reader.ConnectionChanged += OnConnectionChanged;
        _mapper.EnabledChanged    += OnEnabledChanged;
        _guard.StateChanged       += OnGuardStateChanged;
        _loc.LanguageChanged      += OnLanguageRefresh;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hide to tray instead of exiting; tray menu's Exit triggers real shutdown.
        // Keep event subscriptions live — the instance is reused on the next
        // ShowMainWindow, and we want it to receive language/connection/etc.
        // updates while hidden so the next open shows current state.
        e.Cancel = true;
        Hide();
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
        var baseLabel = c switch
        {
            ConnectionType.Usb       => _loc.Get("Status.UsbConnected"),
            ConnectionType.Bluetooth => _loc.Get("Status.BtConnected"),
            _                        => _loc.Get("Status.Disconnected"),
        };
        var device = _reader.DeviceName;
        ConnText.Text = string.IsNullOrEmpty(device) ? baseLabel : $"{baseLabel} — {device}";
    }

    private void OnEnabledChanged(bool enabled) => Dispatcher.BeginInvoke(() => EnableCheck.IsChecked = enabled);

    private void OnLanguageRefresh()
    {
        // DynamicResource handles XAML labels; refresh strings we set in code.
        SetConnectionLabel(_reader.ConnectionType);
        UpdateGuardStatus();
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

    private void OnScrollAccelMaxChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.RightStick.AccelMaxFactor = (float)e.NewValue;
        ScrollAccelMaxVal.Text = $"{e.NewValue:0.0}";
        App.Current.SaveConfig();
    }

    private void OnScrollAccelRampChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        _config.RightStick.AccelRampSeconds = (float)e.NewValue;
        ScrollAccelRampVal.Text = $"{e.NewValue:0.0}";
        App.Current.SaveConfig();
    }

    private void OnNotifyToggle(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _config.EnableNotifications = NotifyCheck.IsChecked == true;
        App.Current.SaveConfig();
    }

    private void OnAutoStartToggle(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (AutoStartCheck.IsChecked == true) StartupRegistration.Register();
        else                                  StartupRegistration.Unregister();
    }

    private void PopulateMappingCombos()
    {
        foreach (var combo in MappingCombos())
        {
            combo.Items.Clear();
            foreach (var id in ButtonActions.All)
            {
                var item = new ComboBoxItem { Tag = id };
                // SetResourceReference makes the displayed label track the
                // current language dictionary, so it updates on language swap.
                item.SetResourceReference(ContentControl.ContentProperty, $"Mapping.{id}");
                combo.Items.Add(item);
            }

            var slot = (string)combo.Tag;
            var current = ReadMapping(slot);
            SelectActionInCombo(combo, current);
        }
    }

    private IEnumerable<ComboBox> MappingCombos()
    {
        yield return MapR2CrossCombo;
        yield return MapL2Combo;
        yield return MapCircleCombo;
        yield return MapSquareCombo;
        yield return MapTriangleCombo;
    }

    private string ReadMapping(string slot) => slot switch
    {
        "R2OrCross" => _config.Mappings.R2OrCross,
        "L2"        => _config.Mappings.L2,
        "Circle"    => _config.Mappings.Circle,
        "Square"    => _config.Mappings.Square,
        "Triangle"  => _config.Mappings.Triangle,
        _ => ButtonActions.None,
    };

    private void WriteMapping(string slot, string action)
    {
        switch (slot)
        {
            case "R2OrCross": _config.Mappings.R2OrCross = action; break;
            case "L2":        _config.Mappings.L2        = action; break;
            case "Circle":    _config.Mappings.Circle    = action; break;
            case "Square":    _config.Mappings.Square    = action; break;
            case "Triangle":  _config.Mappings.Triangle  = action; break;
        }
    }

    private static void SelectActionInCombo(ComboBox combo, string actionId)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (string)item.Tag == actionId)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void OnMappingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (sender is not ComboBox combo) return;
        if (combo.SelectedItem is not ComboBoxItem item) return;
        if (combo.Tag is not string slot || item.Tag is not string actionId) return;

        // Release whatever the OLD action was holding before swapping in the
        // new one — otherwise a hold mid-swap would never receive its up event.
        _mapper.ReleaseHeldInputs();
        WriteMapping(slot, actionId);
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
