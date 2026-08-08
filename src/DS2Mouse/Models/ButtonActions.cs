using System.Globalization;

namespace DS2Mouse.Models;

/// <summary>String IDs for the user-selectable mapping target on each
/// configurable input. Stored verbatim in config.json.
/// In addition to the predefined IDs below, mappings can store arbitrary
/// virtual-keys captured from the on-screen keyboard, encoded as
/// <c>Key:0xNN</c> (e.g. <c>Key:0x41</c> for VK_A).</summary>
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
    public const string Tab         = "Tab";
    public const string Backspace   = "Backspace";
    public const string Ctrl        = "Ctrl";
    public const string Shift       = "Shift";
    public const string Alt         = "Alt";

    public const string KeyPrefix = "Key:";

    public static readonly string[] All =
    {
        None, LeftClick, RightClick, MiddleClick,
        LeftHold, RightHold, MiddleHold,
        Enter, Escape, Space, Tab, Backspace,
        Ctrl, Shift, Alt,
    };

    public static bool IsHold(string id) =>
        id == LeftHold || id == RightHold || id == MiddleHold
        || id == Ctrl || id == Shift || id == Alt
        || id.StartsWith(KeyPrefix, StringComparison.Ordinal);

    public static string EncodeKey(ushort vk) =>
        $"{KeyPrefix}0x{vk:X2}";

    public static bool TryParseKey(string id, out ushort vk)
    {
        vk = 0;
        if (!id.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;
        var hex = id.AsSpan(KeyPrefix.Length);
        if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex[2..];
        return ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk);
    }
}

public sealed class ButtonMappings
{
    public string R2       { get; set; } = ButtonActions.LeftHold;
    public string Cross    { get; set; } = ButtonActions.LeftHold;
    public string L2       { get; set; } = ButtonActions.Ctrl;
    public string Circle   { get; set; } = ButtonActions.RightClick;
    public string Square   { get; set; } = ButtonActions.MiddleClick;
    public string Triangle { get; set; } = ButtonActions.Enter;
    public string L3       { get; set; } = ButtonActions.Tab;
    public string R3       { get; set; } = ButtonActions.MiddleClick;
    public string TouchpadLeft   { get; set; } = ButtonActions.LeftClick;
    public string TouchpadCenter { get; set; } = ButtonActions.MiddleClick;
    public string TouchpadRight  { get; set; } = ButtonActions.RightClick;
}
