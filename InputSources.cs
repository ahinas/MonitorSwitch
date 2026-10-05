using System.Globalization;
using System.Text.RegularExpressions;

namespace MonitorSwitch;

/// <summary>MCCS VCP 0x60 (Input Source) values.</summary>
public static class InputSources
{
    public static readonly (int Code, string Name)[] Known =
    {
        (15, "DisplayPort 1"),
        (16, "DisplayPort 2"),
        (17, "HDMI 1"),
        (18, "HDMI 2"),
        (27, "USB-C"),
        (3, "DVI 1"),
        (4, "DVI 2"),
        (1, "VGA 1"),
        (2, "VGA 2"),
        (5, "Composite 1"),
        (6, "Composite 2"),
        (7, "S-Video 1"),
        (8, "S-Video 2"),
        (12, "Component 1"),
        (13, "Component 2"),
        (14, "Component 3"),
    };

    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dp"] = 15, ["dp1"] = 15, ["displayport"] = 15, ["dp2"] = 16,
        ["hdmi"] = 17, ["hdmi1"] = 17, ["hdmi2"] = 18,
        ["usbc"] = 27, ["usb-c"] = 27, ["typec"] = 27, ["type-c"] = 27,
        ["dvi"] = 3, ["vga"] = 1,
    };

    public static string[] Items => Known.Select(k => Format(k.Code)).ToArray();

    public static string Format(int code)
    {
        foreach (var (c, name) in Known)
            if (c == code) return $"{name} ({code})";
        return $"Code {code}";
    }

    public static bool TryParse(string? text, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        var m = Regex.Match(text, @"\((\d+)\)\s*$");
        if (m.Success) return InRange(int.Parse(m.Groups[1].Value), out code);

        m = Regex.Match(text, @"^(?:code\s*)?(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success) return InRange(int.Parse(m.Groups[1].Value), out code);

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text[2..], NumberStyles.HexNumber, null, out var hex))
            return InRange(hex, out code);

        var compact = text.Replace(" ", "");
        if (Aliases.TryGetValue(compact, out code)) return true;
        foreach (var (c, name) in Known)
            if (string.Equals(name.Replace(" ", ""), compact, StringComparison.OrdinalIgnoreCase)) { code = c; return true; }
        return false;
    }

    private static bool InRange(int value, out int code)
    {
        code = value;
        return value is >= 0 and <= 255;
    }
}
