using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MonitorSwitch;

public sealed class MonitorInfo
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string GdiName { get; init; } = "";
    public Rectangle Bounds { get; init; }
    public bool Primary { get; init; }
    public int? CurrentInput { get; set; }

    public string ShortGdiName => GdiName.Replace(@"\\.\", "");

    public string Label =>
        $"{Name}  ({ShortGdiName}, {Bounds.Width}x{Bounds.Height}{(Primary ? ", primary" : "")})";
}

/// <summary>Enumerates monitors and switches inputs over DDC/CI (VCP code 0x60).</summary>
public static class MonitorService
{
    private const byte VcpInputSource = 0x60;
    private static readonly object Gate = new();

    public static List<MonitorInfo> GetMonitors(bool readCurrentInput = false)
    {
        var result = new List<MonitorInfo>();
        WithMonitors(list =>
        {
            foreach (var (info, handle) in list)
            {
                if (readCurrentInput &&
                    Native.GetVCPFeatureAndVCPFeatureReply(handle, VcpInputSource, IntPtr.Zero, out var cur, out _))
                    info.CurrentInput = (int)(cur & 0xFF);
                result.Add(info);
            }
        });
        return result;
    }

    /// <summary>Applies a profile. Returns a list of human readable errors (empty on success).</summary>
    public static List<string> Apply(IReadOnlyList<ProfileTarget> targets)
    {
        var errors = new List<string>();
        if (targets.Count == 0)
        {
            errors.Add("Profile has no displays selected.");
            return errors;
        }

        WithMonitors(list =>
        {
            foreach (var target in targets)
            {
                var match = list.FirstOrDefault(m => string.Equals(m.Info.Key, target.MonitorKey, StringComparison.OrdinalIgnoreCase));
                if (match.Handle == IntPtr.Zero)
                {
                    // Fall back to the friendly name if it is unique (e.g. the monitor moved to another port).
                    var byName = list.Where(m => m.Info.Name == target.MonitorName).ToList();
                    if (byName.Count == 1) match = byName[0];
                }
                if (match.Handle == IntPtr.Zero)
                {
                    errors.Add($"{target.MonitorName}: not connected");
                    continue;
                }

                var ok = Native.SetVCPFeature(match.Handle, VcpInputSource, (uint)target.Source);
                if (!ok)
                {
                    Thread.Sleep(150);
                    ok = Native.SetVCPFeature(match.Handle, VcpInputSource, (uint)target.Source);
                }
                if (!ok)
                    errors.Add($"{match.Info.Name}: DDC/CI command failed (is DDC/CI enabled in the monitor menu?)");
            }
        });
        return errors;
    }

    private static void WithMonitors(Action<List<(MonitorInfo Info, IntPtr Handle)>> action)
    {
        lock (Gate)
        {
            var friendlyNames = LoadFriendlyNames();
            var hmonitors = new List<IntPtr>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
            {
                hmonitors.Add(h);
                return true;
            }, IntPtr.Zero);

            var opened = new List<(MonitorInfo Info, IntPtr Handle)>();
            try
            {
                foreach (var hmon in hmonitors)
                {
                    var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
                    if (!Native.GetMonitorInfo(hmon, ref mi)) continue;

                    var devices = GetDisplayDevices(mi.szDevice);
                    var physical = GetPhysicalMonitors(hmon);
                    if (physical == null) continue;

                    for (var i = 0; i < physical.Length; i++)
                    {
                        if (physical[i].hPhysicalMonitor == IntPtr.Zero) continue;
                        var device = i < devices.Count ? devices[i] : (Native.DISPLAY_DEVICE?)null;
                        var instance = device.HasValue ? DeviceIdToInstance(device.Value.DeviceID) : null;

                        string? name = null;
                        if (instance != null) friendlyNames.TryGetValue(instance, out name);
                        if (string.IsNullOrWhiteSpace(name)) name = physical[i].szPhysicalMonitorDescription?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) name = device?.DeviceString ?? "Unknown monitor";

                        var r = mi.rcMonitor;
                        opened.Add((new MonitorInfo
                        {
                            Key = instance ?? $"{mi.szDevice}#{i}",
                            Name = name!,
                            GdiName = mi.szDevice,
                            Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom),
                            Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                        }, physical[i].hPhysicalMonitor));
                    }
                }

                opened.Sort((a, b) => a.Info.Bounds.X != b.Info.Bounds.X
                    ? a.Info.Bounds.X.CompareTo(b.Info.Bounds.X)
                    : a.Info.Bounds.Y.CompareTo(b.Info.Bounds.Y));
                action(opened);
            }
            finally
            {
                foreach (var (_, handle) in opened) Native.DestroyPhysicalMonitor(handle);
            }
        }
    }

    // The first dxva2 call in a process occasionally fails for a monitor, so retry briefly.
    private static Native.PHYSICAL_MONITOR[]? GetPhysicalMonitors(IntPtr hmon)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) Thread.Sleep(100);
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out var count) || count == 0) continue;
            var physical = new Native.PHYSICAL_MONITOR[count];
            if (Native.GetPhysicalMonitorsFromHMONITOR(hmon, count, physical)) return physical;
        }
        return null;
    }

    private static List<Native.DISPLAY_DEVICE> GetDisplayDevices(string adapter)
    {
        var all = new List<Native.DISPLAY_DEVICE>();
        for (uint i = 0; ; i++)
        {
            var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            if (!Native.EnumDisplayDevices(adapter, i, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME)) break;
            all.Add(dd);
        }
        var active = all.Where(d => (d.StateFlags & Native.DISPLAY_DEVICE_ACTIVE) != 0).ToList();
        return active.Count > 0 ? active : all;
    }

    // "\\?\DISPLAY#DEL4123#5&abc&0&UID4352#{guid}" -> "DISPLAY\DEL4123\5&ABC&0&UID4352"
    private static string? DeviceIdToInstance(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId) || !deviceId.StartsWith(@"\\?\")) return null;
        var s = deviceId[4..];
        var guid = s.IndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0) s = s[..guid];
        return s.Replace('#', '\\').ToUpperInvariant();
    }

    private static Dictionary<string, string> LoadFriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    if (o["InstanceName"] is not string instance || o["UserFriendlyName"] is not ushort[] raw) continue;
                    var name = new string(raw.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                    if (name.Length > 0)
                        names[Regex.Replace(instance, @"_\d+$", "")] = name;
                }
            }
        }
        catch
        {
            // WMI unavailable: fall back to the generic description.
        }
        return names;
    }
}
