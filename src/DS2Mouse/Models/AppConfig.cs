namespace DS2Mouse.Models;

public sealed class StickConfig
{
    public float Deadzone { get; set; } = 0.10f;
    public float Sensitivity { get; set; } = 12.0f;  // px per tick at full deflection (~1500 px/s @ 125Hz)
    public float Exponent { get; set; } = 2.0f;
}

public sealed class ScrollConfig
{
    public float Deadzone { get; set; } = 0.20f;
    public float Speed { get; set; } = 8.0f; // notches per second at full deflection
    public bool InvertVertical { get; set; } = false;
}

public sealed class AppConfig
{
    public bool Enabled { get; set; } = true;
    public StickConfig LeftStick { get; set; } = new();
    public ScrollConfig RightStick { get; set; } = new();
    public float TriggerThreshold { get; set; } = 0.20f;
    public List<string> FullscreenWhitelist { get; set; } = new();

    public static AppConfig Default() => new();
}
