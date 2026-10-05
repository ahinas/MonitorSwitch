namespace MonitorSwitch;

public sealed class TrayApp : ApplicationContext
{
    private AppConfig _config;
    private readonly NotifyIcon _tray;
    private readonly MessageWindow _messages;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly Dictionary<int, Profile> _hotkeys = new();
    private readonly Icon _icon;
    private SettingsForm? _settings;
    private bool _usbPresent;
    private int _nextHotkeyId = 1;

    public TrayApp()
    {
        if (AppConfig.Exists)
        {
            _config = AppConfig.Load();
        }
        else
        {
            _config = AppConfig.CreateDefault(MonitorService.GetMonitors());
            _config.Save();
        }

        _icon = AppIcon.Create();
        _tray = new NotifyIcon { Icon = _icon, Text = "Monitor Switch", Visible = true };
        _tray.DoubleClick += (_, _) => OpenSettings();
        BuildMenu();

        _messages = new MessageWindow();
        _messages.HotkeyPressed += OnHotkey;
        _messages.DevicesChanged += () => { _debounceTimer!.Stop(); _debounceTimer.Start(); };
        RegisterHotkeys(showErrors: true);

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); CheckUsb(); };

        // Safety net in case a device notification is missed.
        _pollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _pollTimer.Tick += (_, _) => CheckUsb();
        _pollTimer.Start();

        // Matches the original script: if the device is already connected at startup, apply the "connected" profile.
        _usbPresent = false;
        CheckUsb();
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        if (_config.Profiles.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("(no profiles)") { Enabled = false });

        foreach (var profile in _config.Profiles)
        {
            var p = profile;
            var item = new ToolStripMenuItem(p.Name, null, (_, _) => ApplyProfile(p))
            {
                ShortcutKeyDisplayString = p.Hotkey?.ToString(),
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var settings = new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings());
        settings.Font = new Font(settings.Font, FontStyle.Bold);
        menu.Items.Add(settings);

        var usb = new ToolStripMenuItem("Automatic USB switching")
        {
            Checked = _config.Usb.Enabled,
            Enabled = !string.IsNullOrWhiteSpace(_config.Usb.InstanceId),
        };
        usb.Click += (_, _) =>
        {
            _config.Usb.Enabled = !_config.Usb.Enabled;
            _usbPresent = UsbDevices.IsPresent(_config.Usb.InstanceId);
            _config.Save();
            BuildMenu();
        };
        menu.Items.Add(usb);

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartupManager.IsEnabled };
        startup.Click += (_, _) => { StartupManager.Set(!StartupManager.IsEnabled); BuildMenu(); };
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private void RegisterHotkeys(bool showErrors)
    {
        UnregisterHotkeys();
        var failed = new List<string>();
        foreach (var profile in _config.Profiles.Where(p => p.Hotkey != null))
        {
            var id = _nextHotkeyId++;
            if (_messages.RegisterHotkey(id, profile.Hotkey!)) _hotkeys[id] = profile;
            else failed.Add($"{profile.Hotkey} ({profile.Name})");
        }
        if (showErrors && failed.Count > 0)
            Notify("Some hotkeys could not be registered", "Already in use by another app:\n" + string.Join("\n", failed), ToolTipIcon.Warning);
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _hotkeys.Keys) _messages.UnregisterHotkey(id);
        _hotkeys.Clear();
    }

    private void OnHotkey(int id)
    {
        if (_hotkeys.TryGetValue(id, out var profile)) ApplyProfile(profile);
    }

    private void CheckUsb()
    {
        var usb = _config.Usb;
        if (!usb.Enabled || string.IsNullOrWhiteSpace(usb.InstanceId)) return;

        var present = UsbDevices.IsPresent(usb.InstanceId);
        if (present == _usbPresent) return;
        _usbPresent = present;

        var profile = _config.FindProfile(present ? usb.ConnectProfileId : usb.DisconnectProfileId);
        if (profile != null) ApplyProfile(profile);
    }

    public void ApplyProfile(Profile profile)
    {
        var targets = profile.Targets.ToList();
        var name = profile.Name;
        var ui = SynchronizationContext.Current;
        Task.Run(() => MonitorService.Apply(targets)).ContinueWith(t =>
        {
            var errors = t.Exception != null ? new List<string> { t.Exception.GetBaseException().Message } : t.Result;
            if (errors.Count == 0) return;
            void Report() => Notify($"'{name}' had problems", string.Join("\n", errors), ToolTipIcon.Error);
            if (ui != null) ui.Post(_ => Report(), null); else Report();
        });
    }

    private static void Notify(string title, string text, ToolTipIcon icon) => OsdPopup.Show(title, text, icon);

    private void OpenSettings()
    {
        if (_settings != null)
        {
            _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
            return;
        }

        // Release global hotkeys so they can be captured in the editor.
        UnregisterHotkeys();
        _settings = new SettingsForm(_config.Clone(), ApplyProfile) { Icon = _icon };
        _settings.FormClosed += (_, _) =>
        {
            var form = _settings!;
            _settings = null;
            if (form.DialogResult == DialogResult.OK)
            {
                var oldUsb = _config.Usb;
                _config = form.Config;
                try { _config.Save(); }
                catch (Exception ex) { Notify("Could not save settings", ex.Message, ToolTipIcon.Error); }

                if (oldUsb.InstanceId != _config.Usb.InstanceId || oldUsb.Enabled != _config.Usb.Enabled)
                    _usbPresent = UsbDevices.IsPresent(_config.Usb.InstanceId);
                BuildMenu();
            }
            RegisterHotkeys(showErrors: true);
            form.Dispose();
        };
        _settings.Show();
        _settings.Activate();
    }

    protected override void ExitThreadCore()
    {
        _pollTimer.Stop();
        _debounceTimer.Stop();
        _settings?.Close();
        UnregisterHotkeys();
        _messages.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}

internal static class AppIcon
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var frame = new SolidBrush(Color.FromArgb(40, 40, 48));
            using var screen = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillRectangle(frame, 2, 4, 28, 19);
            g.FillRectangle(screen, 4, 6, 24, 15);
            g.FillRectangle(frame, 13, 23, 6, 4);
            g.FillRectangle(frame, 8, 27, 16, 3);
            using var arrow = new Pen(Color.White, 2.5f)
            {
                EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor,
            };
            g.DrawLine(arrow, 8, 11, 24, 11);
            g.DrawLine(arrow, 24, 17, 8, 17);
        }
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        Native.DestroyIcon(hIcon);
        return icon;
    }
}
