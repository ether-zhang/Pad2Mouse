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
    private const ushort VK_SHIFT = 0x10;

    private bool _shiftLatched;
    private bool _positioned;

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

        if (_shiftLatched)
        {
            InputSimulator.KeyDown(VK_SHIFT);
            InputSimulator.KeyTap(vk);
            InputSimulator.KeyUp(VK_SHIFT);
            SetShift(false);
        }
        else
        {
            InputSimulator.KeyTap(vk);
        }
    }

    private void OnShiftClick(object sender, RoutedEventArgs e)
    {
        SetShift(!_shiftLatched);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SetShift(bool on)
    {
        _shiftLatched = on;
        ShiftBtn.Background = on
            ? new SolidColorBrush(Color.FromArgb(0xCC, 0x4F, 0xC3, 0xF7))  // accent blue
            : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); // default
    }

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
