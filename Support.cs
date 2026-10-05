using System.Management;
using Microsoft.Win32;

namespace MonitorSwitch;

public sealed record UsbDevice(string InstanceId, string Name)
{
    public override string ToString() => $"{Name}  —  {InstanceId}";
}

public static class UsbDevices
{
    public static bool IsPresent(string? instanceId) =>
        !string.IsNullOrWhiteSpace(instanceId) && Native.CM_Locate_DevNodeW(out _, instanceId.Trim(), 0) == 0;

    public static List<UsbDevice> ListPresent()
    {
        var list = new List<UsbDevice>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    if (o["PNPDeviceID"] is string id && id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
                        list.Add(new UsbDevice(id, o["Name"] as string ?? "(unknown)"));
                }
            }
        }
        catch
        {
            // Ignore; the user can still paste an instance id manually.
        }
        return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonitorSwitch";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, false);
    }
}

/// <summary>Hidden top-level window that receives global hotkeys and device change notifications.</summary>
public sealed class MessageWindow : NativeWindow, IDisposable
{
    private IntPtr _notification;

    public event Action<int>? HotkeyPressed;
    public event Action? DevicesChanged;

    public MessageWindow()
    {
        CreateHandle(new CreateParams { Caption = "MonitorSwitch.MessageWindow" });
        var filter = new Native.DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = System.Runtime.InteropServices.Marshal.SizeOf<Native.DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = Native.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = Native.GUID_DEVINTERFACE_USB_DEVICE,
        };
        _notification = Native.RegisterDeviceNotification(Handle, ref filter, 0);
    }

    public bool RegisterHotkey(int id, Hotkey hotkey) =>
        Native.RegisterHotKey(Handle, id, hotkey.Modifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key);

    public void UnregisterHotkey(int id) => Native.UnregisterHotKey(Handle, id);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY) HotkeyPressed?.Invoke((int)m.WParam);
        else if (m.Msg == Native.WM_DEVICECHANGE) DevicesChanged?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_notification != IntPtr.Zero)
        {
            Native.UnregisterDeviceNotification(_notification);
            _notification = IntPtr.Zero;
        }
        DestroyHandle();
    }
}

/// <summary>TextBox that captures a key combination.</summary>
public sealed class HotkeyBox : TextBox
{
    private Hotkey? _value;

    public event EventHandler? ValueChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        PlaceholderText = "Click and press a key combination";
        UpdateText();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Hotkey? Value
    {
        get => _value;
        set { _value = value; UpdateText(); }
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Let Alt+key combos reach OnKeyDown instead of being treated as mnemonics.
        if ((keyData & Keys.Alt) != 0 && msg.Msg is 0x0104) { OnKeyDown(new KeyEventArgs(keyData)); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        var key = e.KeyCode;
        var win = Native.GetKeyState(0x5B) < 0 || Native.GetKeyState(0x5C) < 0;
        uint mods = 0;
        if (e.Control) mods |= Hotkey.Ctrl;
        if (e.Alt) mods |= Hotkey.Alt;
        if (e.Shift) mods |= Hotkey.Shift;
        if (win) mods |= Hotkey.Win;

        if (mods == 0 && key is Keys.Back or Keys.Delete or Keys.Escape)
        {
            Value = null;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return;

        var isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
        if (mods == 0 && !isFunctionKey)
        {
            Text = "Use a modifier (Ctrl/Alt/Shift/Win) or an F-key";
            return;
        }

        Value = new Hotkey { Modifiers = mods, Key = (int)key };
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateText() => Text = _value?.ToString() ?? "";
}
