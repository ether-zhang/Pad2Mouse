using System.Windows;
using DS2Mouse.Models;
using DS2Mouse.Services;

namespace DS2Mouse;

public partial class MainWindow : Window
{
    private readonly DualSenseReader _reader;

    public MainWindow()
    {
        InitializeComponent();

        _reader = new DualSenseReader();
        _reader.ConnectionChanged += OnConnectionChanged;
        _reader.FrameReceived += OnFrameReceived;
        _reader.Start();

        Closed += (_, _) => _reader.Dispose();
    }

    private void OnConnectionChanged(ConnectionType c) => Dispatcher.BeginInvoke(() =>
    {
        ConnText.Text = c switch
        {
            ConnectionType.Usb => "Connected (USB)",
            ConnectionType.Bluetooth => "Connected (Bluetooth)",
            _ => "Disconnected",
        };
    });

    private void OnFrameReceived(DualSenseState s) => Dispatcher.BeginInvoke(() =>
    {
        StickText.Text = $"L:({s.LeftStickX,6:0.00}, {s.LeftStickY,6:0.00})  R:({s.RightStickX,6:0.00}, {s.RightStickY,6:0.00})";
        TrigText.Text  = $"L2:{s.L2Trigger:0.00}  R2:{s.R2Trigger:0.00}";
        BtnText.Text   = $"Buttons: {(s.Buttons == 0 ? "None" : s.Buttons.ToString())}";
    });
}
