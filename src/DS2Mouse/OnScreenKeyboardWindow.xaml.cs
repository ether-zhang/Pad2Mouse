using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DS2Mouse.Services;

namespace DS2Mouse;

/// <summary>
/// Compact translucent QWERTY keyboard. Critical property: the window must
/// never take keyboard focus, so that clicks on its buttons do not pull focus
/// away from the application the user is actually typing into.
/// Achieved with WS_EX_NOACTIVATE + WM_MOUSEACTIVATE → MA_NOACTIVATE.
/// </summary>
public partial class OnScreenKeyboardWindow : Window
{
    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;

    private bool _shiftLatched;
    private bool _ctrlLatched;
    private bool _positioned;

    /// <summary>VK → (unshifted-label, shifted-label). Letters use lowercase by
    /// default and uppercase when Shift is latched; digits show their US-layout
    /// shift-symbol when Shift is latched.</summary>
    private static readonly Dictionary<ushort, (string Off, string On)> KeyLabels = BuildKeyLabels();

    public OnScreenKeyboardWindow()
    {
        InitializeComponent();
        Loaded += OnFirstLoaded;
    }

    /// <summary>Thread-safe show/hide — invoked from the mapper's timer thread.</summary>
    public void Toggle()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible) Hide();
            else Show();
        });
    }

    public void HideKeyboard() => Dispatcher.Invoke(Hide);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        if (!_positioned)
        {
            // Bottom-center of primary work area (above taskbar) on first show.
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - ActualWidth) / 2;
            Top = wa.Bottom - ActualHeight - 40;
            _positioned = true;
        }
    }

    private void OnDragAreaPressed(object sender, MouseButtonEventArgs e)
    {
        // Only drag when the user grabs the translucent border itself, not a button.
        if (ReferenceEquals(e.OriginalSource, DragArea))
        {
            try { DragMove(); } catch { /* DragMove can throw if mouse is up already */ }
        }
    }

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        if (!ushort.TryParse(tag.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vk))
            return;

        // Send modifiers down → key tap → modifiers up. Both modifiers can be
        // active at once for combos like Ctrl+Shift+T. Both auto-deactivate
        // after one keystroke (single-shot latch).
        if (_ctrlLatched)  InputSimulator.KeyDown(VK_CONTROL);
        if (_shiftLatched) InputSimulator.KeyDown(VK_SHIFT);
        InputSimulator.KeyTap(vk);
        if (_shiftLatched) InputSimulator.KeyUp(VK_SHIFT);
        if (_ctrlLatched)  InputSimulator.KeyUp(VK_CONTROL);

        if (_shiftLatched) SetShift(false);
        if (_ctrlLatched)  SetCtrl(false);
    }

    private void OnShiftClick(object sender, RoutedEventArgs e) => SetShift(!_shiftLatched);
    private void OnCtrlClick(object sender, RoutedEventArgs e)  => SetCtrl(!_ctrlLatched);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void SetShift(bool on)
    {
        _shiftLatched = on;
        ShiftBtn.Background = on ? AccentBrush : DefaultBrush;
        RefreshKeyLabels();
    }

    private void SetCtrl(bool on)
    {
        _ctrlLatched = on;
        CtrlBtn.Background = on ? AccentBrush : DefaultBrush;
    }

    private void RefreshKeyLabels()
    {
        foreach (var btn in EnumerateKeyButtons(this))
        {
            if (btn.Tag is not string tag) continue;
            if (!tag.StartsWith("0x", StringComparison.Ordinal)) continue;
            if (!ushort.TryParse(tag.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vk))
                continue;
            if (KeyLabels.TryGetValue(vk, out var labels))
            {
                btn.Content = _shiftLatched ? labels.On : labels.Off;
            }
        }
    }

    private static IEnumerable<Button> EnumerateKeyButtons(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b) yield return b;
            else
                foreach (var sub in EnumerateKeyButtons(child))
                    yield return sub;
        }
    }

    private static Dictionary<ushort, (string Off, string On)> BuildKeyLabels()
    {
        var map = new Dictionary<ushort, (string, string)>();
        // Letters: A (0x41) .. Z (0x5A)
        for (ushort vk = 0x41; vk <= 0x5A; vk++)
        {
            char up = (char)vk;
            char lo = char.ToLowerInvariant(up);
            map[vk] = (lo.ToString(), up.ToString());
        }
        // Digit row: 1!, 2@, 3#, 4$, 5%, 6^, 7&, 8*, 9(, 0)  (US layout)
        var digitShift = new[]
        {
            ((ushort)0x31, "1", "!"),
            ((ushort)0x32, "2", "@"),
            ((ushort)0x33, "3", "#"),
            ((ushort)0x34, "4", "$"),
            ((ushort)0x35, "5", "%"),
            ((ushort)0x36, "6", "^"),
            ((ushort)0x37, "7", "&"),
            ((ushort)0x38, "8", "*"),
            ((ushort)0x39, "9", "("),
            ((ushort)0x30, "0", ")"),
        };
        foreach (var (vk, off, on) in digitShift) map[vk] = (off, on);
        // Comma / period: ,< and .>
        map[0xBC] = (",", "<");
        map[0xBE] = (".", ">");
        return map;
    }

    private static readonly SolidColorBrush AccentBrush  =
        new(Color.FromArgb(0xCC, 0x4F, 0xC3, 0xF7));
    private static readonly SolidColorBrush DefaultBrush =
        new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    // ----- Win32 -----
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_NOACTIVATE  = 0x08000000;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;
    private const int WM_MOUSEACTIVATE  = 0x0021;
    private const int MA_NOACTIVATE     = 0x0003;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
