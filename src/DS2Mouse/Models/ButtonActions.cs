namespace DS2Mouse.Models;

/// <summary>String IDs for the user-selectable mapping target on each
/// configurable input. Stored verbatim in config.json.</summary>
public static class ButtonActions
{
    public const string None        = "None";
    public const string LeftClick   = "LeftClick";
    public const string RightClick  = "RightClick";
    public const string MiddleClick = "MiddleClick";
    public const string LeftHold    = "LeftHold";
    public const string RightHold   = "RightHold";
    public const string MiddleHold  = "MiddleHold";
    public const string Enter       = "Enter";
    public const string Escape      = "Escape";
    public const string Space       = "Space";

    public static readonly string[] All =
    {
        None, LeftClick, RightClick, MiddleClick,
        LeftHold, RightHold, MiddleHold,
        Enter, Escape, Space,
    };

    public static bool IsHold(string id) =>
        id == LeftHold || id == RightHold || id == MiddleHold;
}

public sealed class ButtonMappings
{
    /// <summary>R2 trigger and Cross share one slot — both contribute to the
    /// same action via OR aggregate, mirroring the original drag behavior.</summary>
    public string R2OrCross { get; set; } = ButtonActions.LeftHold;
    public string L2        { get; set; } = ButtonActions.RightHold;
    public string Circle    { get; set; } = ButtonActions.RightClick;
    public string Square    { get; set; } = ButtonActions.MiddleClick;
    public string Triangle  { get; set; } = ButtonActions.Enter;
}
