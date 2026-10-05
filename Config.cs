using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorSwitch;

public sealed class Hotkey
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

    public uint Modifiers { get; set; }
    public int Key { get; set; }

    public bool SameAs(Hotkey? other) => other != null && other.Modifiers == Modifiers && other.Key == Key;

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(KeyName((Keys)Key));
        return string.Join("+", parts);
    }

    public static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (key - Keys.NumPad0),
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemMinus => "-",
        Keys.Oemplus => "+",
        Keys.Next => "PageDown",
        Keys.Prior => "PageUp",
        _ => key.ToString(),
    };
}

public sealed class ProfileTarget
{
    public string MonitorKey { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public int Source { get; set; }
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public Hotkey? Hotkey { get; set; }
    public List<ProfileTarget> Targets { get; set; } = new();

    public override string ToString() => Hotkey == null ? Name : $"{Name}    [{Hotkey}]";
}

public sealed class UsbTrigger
{
    public bool Enabled { get; set; }
    public string InstanceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string? ConnectProfileId { get; set; }
    public string? DisconnectProfileId { get; set; }
}

public sealed class AppConfig
{
    public List<Profile> Profiles { get; set; } = new();
    public UsbTrigger Usb { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorSwitch");

    public static string FilePath => Path.Combine(Directory, "config.json");

    public static bool Exists => File.Exists(FilePath);

    public Profile? FindProfile(string? id) => id == null ? null : Profiles.FirstOrDefault(p => p.Id == id);

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read {FilePath}:\n{ex.Message}\n\nStarting with an empty configuration.",
                "Monitor Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return new AppConfig();
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, Options), Options)!;

    /// <summary>Defaults that mirror the original watch-usb-and-switch.ps1 script.</summary>
    public static AppConfig CreateDefault(IReadOnlyList<MonitorInfo> monitors)
    {
        Profile Make(string name, int source) => new()
        {
            Name = name,
            Targets = monitors.Select(m => new ProfileTarget { MonitorKey = m.Key, MonitorName = m.Name, Source = source }).ToList(),
        };

        var dp = Make("DisplayPort", 15);
        var hdmi2 = Make("HDMI 2", 18);
        return new AppConfig
        {
            Profiles = { dp, hdmi2 },
            Usb = new UsbTrigger
            {
                Enabled = true,
                InstanceId = @"USB\VID_0451&PID_2036\7&131C09EC&1&2",
                ConnectProfileId = dp.Id,
                DisconnectProfileId = hdmi2.Id,
            },
        };
    }
}
