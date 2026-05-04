using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DS2Mouse.Models;
using DS2Mouse.Services;

namespace DS2Mouse;

public partial class MainWindow : Window
{
    private const string SentinelPickKey = "__pick_key__";

    private readonly IControllerReader _reader;
    private readonly MapperEngine _mapper;
    private readonly AppConfig _config;
    private readonly FullscreenGuard _guard;
    private readonly LocalizationService _loc;
    private bool _initialized;
    private bool _populatingCombos;

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
        SetMappingLabels(_reader.Kind);
        UpdateGuardStatus();
        _initialized = true;

        _reader.ConnectionChanged += OnConnectionChanged;
        _reader.KindChanged       += OnControllerKindChanged;
        _mapper.EnabledChanged    += OnEnabledChanged;
        _guard.StateChanged       += OnGuardStateChanged;
        _loc.LanguageChanged      += OnLanguageRefresh;
    }

    private void OnControllerKindChanged(ControllerKind kind) =>
        Dispatcher.BeginInvoke(() => SetMappingLabels(kind));

    private void SetMappingLabels(ControllerKind kind)
    {
        // Show PS-style alone when only DualSense is connected, Xbox-style alone
        // when only Xbox, and "PS / Xbox" combined when both are present. With
        // nothing connected we fall back to PS naming, since the rest of the
        // app's text already uses that style.
        bool ds   = (kind & ControllerKind.DualSense) != 0;
        bool xbox = (kind & ControllerKind.Xbox)      != 0;
        LblR2.Text       = LabelFor("R2",      "RT",        ds, xbox);
        LblL2.Text       = LabelFor("L2",      "LT",        ds, xbox);
        LblCross.Text    = LabelFor("✕",       "A",         ds, xbox);
        LblCircle.Text   = LabelFor("○",       "B",         ds, xbox);
        LblSquare.Text   = LabelFor("□",       "X",         ds, xbox);
        LblTriangle.Text = LabelFor("△",       "Y",         ds, xbox);
        LblL3R3.Text     = LabelFor("L3 + R3", "LSB + RSB", ds, xbox);
        LblL1R1.Text     = LabelFor("L1 + R1", "LB + RB",   ds, xbox);
    }

    private static string LabelFor(string ps, string xbox, bool dsConnected, bool xboxConnected)
    {
        if (dsConnected && xboxConnected) return $"{ps} / {xbox}";
        if (xboxConnected)                return xbox;
        return ps; // DS only or nothing connected
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
        // Dynamic Key items embed the localized "Key:" prefix as plain text,
        // so rebuild the combos to pick up the new language.
        PopulateMappingCombos();
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
        _populatingCombos = true;
        try
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
                // Sentinel that triggers the on-screen-keyboard pick flow.
                var pick = new ComboBoxItem { Tag = SentinelPickKey };
                pick.SetResourceReference(ContentControl.ContentProperty, "Mapping.PickKey");
                combo.Items.Add(pick);

                var slot = (string)combo.Tag;
                var current = ReadMapping(slot);
                SelectActionInCombo(combo, current);
            }
        }
        finally
        {
            _populatingCombos = false;
        }
    }

    private IEnumerable<ComboBox> MappingCombos()
    {
        yield return MapR2Combo;
        yield return MapL2Combo;
        yield return MapCrossCombo;
        yield return MapCircleCombo;
        yield return MapSquareCombo;
        yield return MapTriangleCombo;
    }

    private string ReadMapping(string slot) => slot switch
    {
        "R2"       => _config.Mappings.R2,
        "Cross"    => _config.Mappings.Cross,
        "L2"       => _config.Mappings.L2,
        "Circle"   => _config.Mappings.Circle,
        "Square"   => _config.Mappings.Square,
        "Triangle" => _config.Mappings.Triangle,
        _ => ButtonActions.None,
    };

    private void WriteMapping(string slot, string action)
    {
        switch (slot)
        {
            case "R2":       _config.Mappings.R2       = action; break;
            case "Cross":    _config.Mappings.Cross    = action; break;
            case "L2":       _config.Mappings.L2       = action; break;
            case "Circle":   _config.Mappings.Circle   = action; break;
            case "Square":   _config.Mappings.Square   = action; break;
            case "Triangle": _config.Mappings.Triangle = action; break;
        }
    }

    private void SelectActionInCombo(ComboBox combo, string actionId)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (string)item.Tag == actionId)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        // Captured-key mapping not represented yet — synthesize the dynamic item.
        if (actionId.StartsWith(ButtonActions.KeyPrefix, StringComparison.Ordinal))
        {
            InsertDynamicKeyItem(combo, actionId);
            foreach (var obj in combo.Items)
            {
                if (obj is ComboBoxItem item && (string)item.Tag == actionId)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }
        combo.SelectedIndex = 0;
    }

    private void InsertDynamicKeyItem(ComboBox combo, string keyAction)
    {
        if (!ButtonActions.TryParseKey(keyAction, out var vk)) return;
        var prefix = _loc.Get("Mapping.KeyPrefix");
        var item = new ComboBoxItem
        {
            Tag = keyAction,
            Content = prefix + KeyFriendlyName(vk),
        };
        // Insert before the sentinel (always last item).
        int idx = Math.Max(0, combo.Items.Count - 1);
        combo.Items.Insert(idx, item);
    }

    private static void RemoveDynamicKeyItems(ComboBox combo)
    {
        for (int i = combo.Items.Count - 1; i >= 0; i--)
        {
            if (combo.Items[i] is ComboBoxItem item
                && item.Tag is string tag
                && tag.StartsWith(ButtonActions.KeyPrefix, StringComparison.Ordinal))
            {
                combo.Items.RemoveAt(i);
            }
        }
    }

    private static string KeyFriendlyName(ushort vk) => vk switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0xBC => ",",
        0xBE => ".",
        _ when vk >= 0x30 && vk <= 0x39 => ((char)vk).ToString(),
        _ when vk >= 0x41 && vk <= 0x5A => ((char)vk).ToString(),
        _ => $"0x{vk:X2}",
    };

    private void OnMappingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _populatingCombos) return;
        if (sender is not ComboBox combo) return;
        if (combo.SelectedItem is not ComboBoxItem item) return;
        if (combo.Tag is not string slot || item.Tag is not string actionId) return;

        if (actionId == SentinelPickKey)
        {
            // Don't write the sentinel — revert to current real mapping and
            // launch the keyboard in pick mode. Result lands asynchronously.
            var current = ReadMapping(slot);
            _populatingCombos = true;
            try { SelectActionInCombo(combo, current); }
            finally { _populatingCombos = false; }
            App.Current.Keyboard.BeginCapture(vk =>
                Dispatcher.Invoke(() => CompleteKeyCapture(combo, slot, vk)));
            return;
        }

        // Release whatever the OLD action was holding before swapping in the
        // new one — otherwise a hold mid-swap would never receive its up event.
        _mapper.ReleaseHeldInputs();
        WriteMapping(slot, actionId);
        App.Current.SaveConfig();
    }

    private void CompleteKeyCapture(ComboBox combo, string slot, ushort? vk)
    {
        if (vk is null) return; // user cancelled
        var keyAction = ButtonActions.EncodeKey(vk.Value);

        _mapper.ReleaseHeldInputs();
        WriteMapping(slot, keyAction);
        App.Current.SaveConfig();

        _populatingCombos = true;
        try
        {
            RemoveDynamicKeyItems(combo);
            InsertDynamicKeyItem(combo, keyAction);
            SelectActionInCombo(combo, keyAction);
        }
        finally
        {
            _populatingCombos = false;
        }
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
