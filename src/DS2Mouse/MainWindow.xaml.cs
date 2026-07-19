using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
    private bool _updatingSensitivityUi;
    private bool _showXboxController;
    private string _selectedMappingSlot = "Cross";

    private readonly record struct SensitivityPreset(
        float Sensitivity,
        float Deadzone,
        float AccelMax,
        float AccelRamp,
        float ScrollSpeed,
        float ScrollAccelMax,
        float ScrollAccelRamp);

    private static readonly SensitivityPreset PrecisePreset = new(8f, 0.12f, 1.8f, 1.2f, 6f, 1.8f, 1.2f);
    private static readonly SensitivityPreset BalancedPreset = new(12f, 0.10f, 2.5f, 1.0f, 8f, 2.5f, 1.0f);
    private static readonly SensitivityPreset FastPreset = new(18f, 0.08f, 3.5f, 0.7f, 12f, 3.5f, 0.7f);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref uint value, uint size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        uint round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

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
        PopulateMappingCombo();
        RefreshWhitelistBox();
        SetConnectionLabel(_reader.ConnectionType);
        SelectControllerForKind(_reader.Kind);
        SelectMappingSlot(_selectedMappingSlot, openDropDown: false);
        RefreshSensitivityPresetState();
        UpdateGuardStatus();
        _initialized = true;

        _reader.ConnectionChanged += OnConnectionChanged;
        _reader.KindChanged       += OnControllerKindChanged;
        _mapper.EnabledChanged    += OnEnabledChanged;
        _guard.StateChanged       += OnGuardStateChanged;
        _loc.LanguageChanged      += OnLanguageRefresh;
    }

    private void OnControllerKindChanged(ControllerKind kind)
    {
        // Keep the last visible controller when everything disconnects.
        if (kind == ControllerKind.None) return;
        Dispatcher.BeginInvoke(() => SelectControllerForKind(kind));
    }

    private void SelectControllerForKind(ControllerKind kind)
    {
        if (kind == ControllerKind.Xbox) SetControllerMode(showXbox: true);
        else if (kind == ControllerKind.DualSense) SetControllerMode(showXbox: false);
        else SetControllerMode(_showXboxController); // both connected: preserve manual selection
    }

    private void OnControllerModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string mode })
            SetControllerMode(mode == "Xbox");
    }

    private void SetControllerMode(bool showXbox)
    {
        _showXboxController = showXbox;
        PsViewButton.IsChecked = !showXbox;
        XboxViewButton.IsChecked = showXbox;
        PsControllerView.Visibility = showXbox ? Visibility.Collapsed : Visibility.Visible;
        XboxControllerView.Visibility = showXbox ? Visibility.Visible : Visibility.Collapsed;

        FixedToggleComboLabel.Text = showXbox ? "LSB + RSB" : "L3 + R3";
        FixedKeyboardComboLabel.Text = showXbox ? "LB + RB" : "L1 + R1";
        FixedCenterComboLabel.Text = showXbox ? "View + Menu" : "Create + Options";
        RefreshMappingHotspots();
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

    private void OnMinClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaxClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // Goes through OnClosing → hide-to-tray. Tray menu's Exit owns real shutdown.
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Swap the maximize button glyph to the "restore" pair when maximized.
        // Both glyphs are Segoe MDL2 Assets code points.
        MaxBtn.Content = WindowState == WindowState.Maximized
            ? ""   // restore (overlapping squares)
            : "";  // maximize (single square)
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
        var devices = _reader.ConnectedDevices;
        if (devices.Count == 0)
        {
            ConnText.Text = _loc.Get("Status.Disconnected");
            return;
        }
        // One line per physical device, e.g. "DualSense Edge(蓝牙)\nXbox Controller(USB)".
        var lines = devices.Select(d =>
        {
            var tag = d.ConnectionType switch
            {
                ConnectionType.Bluetooth => _loc.Get("Conn.Bt"),
                ConnectionType.Usb       => _loc.Get("Conn.Usb"),
                _                        => string.Empty,
            };
            return string.IsNullOrEmpty(tag) ? d.Name : $"{d.Name}({tag})";
        });
        ConnText.Text = string.Join('\n', lines);
    }

    private void OnEnabledChanged(bool enabled) => Dispatcher.BeginInvoke(() => EnableCheck.IsChecked = enabled);

    private void OnLanguageRefresh()
    {
        // DynamicResource handles XAML labels; refresh strings we set in code.
        SetConnectionLabel(_reader.ConnectionType);
        UpdateGuardStatus();
        // Dynamic Key items and hotspot tooltips embed localized text.
        PopulateMappingCombo();
        RefreshMappingHotspots();
    }

    private void OnEnableToggle(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _mapper.Enabled = EnableCheck.IsChecked == true;
    }

    private void OnSensChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.LeftStick.Sensitivity = (float)e.NewValue;
        SensVal.Text = $"{e.NewValue:0}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnDzChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.LeftStick.Deadzone = (float)e.NewValue;
        DzVal.Text = $"{e.NewValue:0.00}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.RightStick.Speed = (float)e.NewValue;
        ScrollVal.Text = $"{e.NewValue:0}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnAccelMaxChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.LeftStick.AccelMaxFactor = (float)e.NewValue;
        AccelMaxVal.Text = $"{e.NewValue:0.0}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnAccelRampChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.LeftStick.AccelRampSeconds = (float)e.NewValue;
        AccelRampVal.Text = $"{e.NewValue:0.0}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnScrollAccelMaxChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.RightStick.AccelMaxFactor = (float)e.NewValue;
        ScrollAccelMaxVal.Text = $"{e.NewValue:0.0}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnScrollAccelRampChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingSensitivityUi) return;
        _config.RightStick.AccelRampSeconds = (float)e.NewValue;
        ScrollAccelRampVal.Text = $"{e.NewValue:0.0}";
        MarkSensitivityCustom();
        App.Current.SaveConfig();
    }

    private void OnSensitivityPresetClick(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not ToggleButton { Tag: string presetId }) return;

        if (presetId == "Custom")
        {
            SetSensitivityPresetSelection("Custom");
            CustomSensitivityPanel.Visibility = CustomSensitivityPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            return;
        }

        var preset = presetId switch
        {
            "Precise" => PrecisePreset,
            "Fast" => FastPreset,
            _ => BalancedPreset,
        };
        ApplySensitivityPreset(presetId, preset);
    }

    private void ApplySensitivityPreset(string presetId, SensitivityPreset preset)
    {
        _config.LeftStick.Sensitivity = preset.Sensitivity;
        _config.LeftStick.Deadzone = preset.Deadzone;
        _config.LeftStick.AccelMaxFactor = preset.AccelMax;
        _config.LeftStick.AccelRampSeconds = preset.AccelRamp;
        _config.RightStick.Speed = preset.ScrollSpeed;
        _config.RightStick.AccelMaxFactor = preset.ScrollAccelMax;
        _config.RightStick.AccelRampSeconds = preset.ScrollAccelRamp;

        _updatingSensitivityUi = true;
        try
        {
            SyncSensitivityControls();
        }
        finally
        {
            _updatingSensitivityUi = false;
        }

        SetSensitivityPresetSelection(presetId);
        CustomSensitivityPanel.Visibility = Visibility.Collapsed;
        App.Current.SaveConfig();
    }

    private void SyncSensitivityControls()
    {
        SensSlider.Value = _config.LeftStick.Sensitivity;
        DzSlider.Value = _config.LeftStick.Deadzone;
        AccelMaxSlider.Value = _config.LeftStick.AccelMaxFactor;
        AccelRampSlider.Value = _config.LeftStick.AccelRampSeconds;
        ScrollSlider.Value = _config.RightStick.Speed;
        ScrollAccelMaxSlider.Value = _config.RightStick.AccelMaxFactor;
        ScrollAccelRampSlider.Value = _config.RightStick.AccelRampSeconds;
        SensVal.Text = $"{_config.LeftStick.Sensitivity:0}";
        DzVal.Text = $"{_config.LeftStick.Deadzone:0.00}";
        AccelMaxVal.Text = $"{_config.LeftStick.AccelMaxFactor:0.0}";
        AccelRampVal.Text = $"{_config.LeftStick.AccelRampSeconds:0.0}";
        ScrollVal.Text = $"{_config.RightStick.Speed:0}";
        ScrollAccelMaxVal.Text = $"{_config.RightStick.AccelMaxFactor:0.0}";
        ScrollAccelRampVal.Text = $"{_config.RightStick.AccelRampSeconds:0.0}";
    }

    private void RefreshSensitivityPresetState()
    {
        var presetId = MatchesPreset(PrecisePreset) ? "Precise"
            : MatchesPreset(BalancedPreset) ? "Balanced"
            : MatchesPreset(FastPreset) ? "Fast"
            : "Custom";
        SetSensitivityPresetSelection(presetId);
        CustomSensitivityPanel.Visibility = Visibility.Collapsed;
    }

    private bool MatchesPreset(SensitivityPreset preset) =>
        NearlyEqual(_config.LeftStick.Sensitivity, preset.Sensitivity)
        && NearlyEqual(_config.LeftStick.Deadzone, preset.Deadzone)
        && NearlyEqual(_config.LeftStick.AccelMaxFactor, preset.AccelMax)
        && NearlyEqual(_config.LeftStick.AccelRampSeconds, preset.AccelRamp)
        && NearlyEqual(_config.RightStick.Speed, preset.ScrollSpeed)
        && NearlyEqual(_config.RightStick.AccelMaxFactor, preset.ScrollAccelMax)
        && NearlyEqual(_config.RightStick.AccelRampSeconds, preset.ScrollAccelRamp);

    private static bool NearlyEqual(float left, float right) => MathF.Abs(left - right) < 0.001f;

    private void MarkSensitivityCustom() => SetSensitivityPresetSelection("Custom");

    private void SetSensitivityPresetSelection(string presetId)
    {
        PrecisePresetButton.IsChecked = presetId == "Precise";
        BalancedPresetButton.IsChecked = presetId == "Balanced";
        FastPresetButton.IsChecked = presetId == "Fast";
        CustomPresetButton.IsChecked = presetId == "Custom";
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

    private void PopulateMappingCombo()
    {
        _populatingCombos = true;
        try
        {
            MappingActionCombo.Items.Clear();
            foreach (var id in ButtonActions.All)
            {
                var item = new ComboBoxItem { Tag = id };
                item.SetResourceReference(ContentControl.ContentProperty, $"Mapping.{id}");
                MappingActionCombo.Items.Add(item);
            }
            var pick = new ComboBoxItem { Tag = SentinelPickKey };
            pick.SetResourceReference(ContentControl.ContentProperty, "Mapping.PickKey");
            MappingActionCombo.Items.Add(pick);

            MappingActionCombo.Tag = _selectedMappingSlot;
            SelectActionInCombo(MappingActionCombo, ReadMapping(_selectedMappingSlot));
        }
        finally
        {
            _populatingCombos = false;
        }
    }

    private void OnMappingHotspotClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string slot })
            SelectMappingSlot(slot, openDropDown: true);
    }

    private void SelectMappingSlot(string slot, bool openDropDown)
    {
        _selectedMappingSlot = slot;
        MappingActionCombo.Tag = slot;

        _populatingCombos = true;
        try
        {
            SelectActionInCombo(MappingActionCombo, ReadMapping(slot));
        }
        finally
        {
            _populatingCombos = false;
        }

        RefreshMappingHotspots();
        if (openDropDown)
            Dispatcher.BeginInvoke(() => MappingActionCombo.IsDropDownOpen = true);
    }

    private void RefreshMappingHotspots()
    {
        SelectedMappingName.Text = MappingSlotDisplayName(_selectedMappingSlot);
        foreach (var hotspot in MappingHotspots())
        {
            if (hotspot.Tag is not string slot) continue;
            hotspot.IsChecked = slot == _selectedMappingSlot;
            hotspot.ToolTip = $"{hotspot.Content} · {MappingActionDisplayName(ReadMapping(slot))}";
        }
    }

    private IEnumerable<ToggleButton> MappingHotspots() =>
        PsHotspotCanvas.Children.OfType<ToggleButton>()
            .Concat(XboxHotspotCanvas.Children.OfType<ToggleButton>());

    private string MappingSlotDisplayName(string slot) => (_showXboxController, slot) switch
    {
        (true, "L2") => "LT",
        (true, "R2") => "RT",
        (true, "Cross") => "A",
        (true, "Circle") => "B",
        (true, "Square") => "X",
        (true, "Triangle") => "Y",
        (true, "L3") => "LSB",
        (true, "R3") => "RSB",
        (false, "Cross") => "✕",
        (false, "Circle") => "○",
        (false, "Square") => "□",
        (false, "Triangle") => "△",
        _ => slot,
    };

    private string MappingActionDisplayName(string actionId)
    {
        if (ButtonActions.TryParseKey(actionId, out var vk))
            return _loc.Get("Mapping.KeyPrefix") + KeyFriendlyName(vk);
        return _loc.Get($"Mapping.{actionId}");
    }

    private string ReadMapping(string slot) => slot switch
    {
        "R2"       => _config.Mappings.R2,
        "Cross"    => _config.Mappings.Cross,
        "L2"       => _config.Mappings.L2,
        "Circle"   => _config.Mappings.Circle,
        "Square"   => _config.Mappings.Square,
        "Triangle" => _config.Mappings.Triangle,
        "L3"       => _config.Mappings.L3,
        "R3"       => _config.Mappings.R3,
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
            case "L3":       _config.Mappings.L3       = action; break;
            case "R3":       _config.Mappings.R3       = action; break;
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
        RefreshMappingHotspots();
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
        RefreshMappingHotspots();
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
