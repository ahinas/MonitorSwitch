namespace MonitorSwitch;

public sealed class SettingsForm : Form
{
    private sealed class ProfileListBox : ListBox
    {
        public bool Refreshing { get; private set; }

        // RefreshItem re-inserts the item and re-raises SelectedIndexChanged; flag it so the editor isn't rebuilt.
        public void RefreshAt(int index)
        {
            if (index < 0) return;
            Refreshing = true;
            try { RefreshItem(index); }
            finally { Refreshing = false; }
        }
    }

    private sealed record ProfileChoice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public AppConfig Config { get; }
    private readonly Action<Profile> _apply;
    private List<MonitorInfo> _monitors = new();
    private bool _loading;
    private bool _startupEdited;

    // Profiles tab
    private readonly ProfileListBox _profileList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _profileName = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _hotkeyBox = new() { Width = 260 };
    private readonly Label _hotkeyWarning = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly TableLayoutPanel _targetsTable = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
    private readonly TableLayoutPanel _editor = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8, 0, 0, 0) };

    // Displays tab
    private readonly ListView _displayList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, GridLines = true,
    };
    private readonly ComboBox _testSource = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _displayStatus = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    // USB tab
    private readonly CheckBox _usbEnabled = new() { Text = "Switch automatically when a USB device is connected / disconnected", AutoSize = true };
    private readonly TextBox _usbId = new() { Dock = DockStyle.Fill };
    private readonly Label _usbStatus = new() { AutoSize = true };
    private readonly ComboBox _usbDevices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520 };
    private readonly ComboBox _connectProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _disconnectProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };

    // General tab
    private readonly CheckBox _startup = new() { Text = "Start Monitor Switch when I sign in to Windows", AutoSize = true };

    public SettingsForm(AppConfig config, Action<Profile> apply)
    {
        Config = config;
        _apply = apply;

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        Text = "Monitor Switch – Settings";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(900, 620);
        MinimumSize = new Size(760, 500);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildProfilesTab());
        tabs.TabPages.Add(BuildDisplaysTab());
        tabs.TabPages.Add(BuildUsbTab());
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.SelectedIndexChanged += (_, _) => LoadUsbProfileChoices();

        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 0) };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        Controls.Add(tabs);
        Controls.Add(buttons);
        CancelButton = cancel;

        LoadProfiles();
        LoadUsb();
        _startup.Checked = StartupManager.IsEnabled;
        _startup.Click += (_, _) => _startupEdited = true;
        // The setting can be changed outside the app (e.g. Task Manager), so re-read it when the window regains focus.
        Activated += (_, _) => { if (!_startupEdited) _startup.Checked = StartupManager.IsEnabled; };

        Load += async (_, _) =>
        {
            await RefreshMonitorsAsync();
            await RefreshUsbDevicesAsync();
        };
    }

    // ---------------------------------------------------------------- Profiles

    private TabPage BuildProfilesTab()
    {
        var page = new TabPage("Profiles") { Padding = new Padding(8) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Left: profile list
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_profileList, 0, 0);
        var listButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        listButtons.Controls.Add(MakeButton("Add", AddProfile));
        listButtons.Controls.Add(MakeButton("Duplicate", DuplicateProfile));
        listButtons.Controls.Add(MakeButton("Remove", RemoveProfile));
        listButtons.Controls.Add(MakeButton("▲", () => MoveProfile(-1)));
        listButtons.Controls.Add(MakeButton("▼", () => MoveProfile(1)));
        left.Controls.Add(listButtons, 0, 1);
        _profileList.SelectedIndexChanged += (_, _) => { if (!_loading && !_profileList.Refreshing) ShowProfile(SelectedProfile); };

        // Right: editor
        _editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) _editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _editor.Controls.Add(MakeLabel("Name:"), 0, 0);
        _editor.Controls.Add(_profileName, 1, 0);

        _editor.Controls.Add(MakeLabel("Hotkey:"), 0, 1);
        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        hotkeyRow.Controls.Add(_hotkeyBox);
        hotkeyRow.Controls.Add(MakeButton("Clear", () => { _hotkeyBox.Value = null; OnHotkeyChanged(); }));
        _editor.Controls.Add(hotkeyRow, 1, 1);

        _editor.Controls.Add(_hotkeyWarning, 1, 2);

        var help = new Label
        {
            AutoSize = true,
            Text = "Tick the displays this profile should switch, and choose the input for each. " +
                   "You can also type a raw VCP code (e.g. 15 or 0x0F) if your monitor uses non-standard values.",
            MaximumSize = new Size(560, 0),
            Padding = new Padding(0, 10, 0, 4),
        };
        _editor.Controls.Add(help, 0, 3);
        _editor.SetColumnSpan(help, 2);

        var targetsHeader = new Label { Text = "Displays", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _editor.Controls.Add(targetsHeader, 0, 4);
        _editor.SetColumnSpan(targetsHeader, 2);

        _targetsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _targetsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var targetsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
        targetsScroll.Controls.Add(_targetsTable);
        _editor.Controls.Add(targetsScroll, 0, 5);
        _editor.SetColumnSpan(targetsScroll, 2);

        var editorButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        editorButtons.Controls.Add(MakeButton("Apply this profile now", () => { if (SelectedProfile is { } p) _apply(p); }));
        editorButtons.Controls.Add(MakeButton("Refresh displays", async () => await RefreshMonitorsAsync()));
        _editor.Controls.Add(editorButtons, 0, 6);
        _editor.SetColumnSpan(editorButtons, 2);

        _profileName.TextChanged += (_, _) =>
        {
            if (_loading || SelectedProfile is not { } p) return;
            p.Name = _profileName.Text;
            _profileList.RefreshAt(_profileList.SelectedIndex);
        };
        _hotkeyBox.ValueChanged += (_, _) => OnHotkeyChanged();

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(_editor, 1, 0);
        page.Controls.Add(root);
        return page;
    }

    private Profile? SelectedProfile => _profileList.SelectedItem as Profile;

    private void LoadProfiles(Profile? select = null)
    {
        _loading = true;
        _profileList.Items.Clear();
        foreach (var p in Config.Profiles) _profileList.Items.Add(p);
        _loading = false;
        if (select != null) _profileList.SelectedItem = select;
        else if (_profileList.Items.Count > 0) _profileList.SelectedIndex = 0;
        else ShowProfile(null);
    }

    private void ShowProfile(Profile? profile)
    {
        _loading = true;
        _editor.Enabled = profile != null;
        _profileName.Text = profile?.Name ?? "";
        _hotkeyBox.Value = profile?.Hotkey;
        _loading = false;
        UpdateHotkeyWarning();
        BuildTargetRows(profile);
    }

    private void OnHotkeyChanged()
    {
        if (_loading || SelectedProfile is not { } p) return;
        p.Hotkey = _hotkeyBox.Value;
        _profileList.RefreshAt(_profileList.SelectedIndex);
        UpdateHotkeyWarning();
    }

    private void UpdateHotkeyWarning()
    {
        var p = SelectedProfile;
        var clash = p?.Hotkey == null ? null : Config.Profiles.FirstOrDefault(o => o != p && p.Hotkey.SameAs(o.Hotkey));
        _hotkeyWarning.Text = clash == null ? "" : $"Already used by '{clash.Name}'";
    }

    private void BuildTargetRows(Profile? profile)
    {
        _targetsTable.SuspendLayout();
        foreach (Control c in _targetsTable.Controls.Cast<Control>().ToList()) c.Dispose();
        _targetsTable.Controls.Clear();
        _targetsTable.RowStyles.Clear();
        _targetsTable.RowCount = 0;

        if (profile != null)
        {
            var rows = _monitors.Select(m => (m.Key, Label: m.Label + CurrentInputSuffix(m), m.Name)).ToList();
            foreach (var t in profile.Targets)
            {
                if (!rows.Any(r => string.Equals(r.Key, t.MonitorKey, StringComparison.OrdinalIgnoreCase)))
                    rows.Add((t.MonitorKey, $"{t.MonitorName}  (not connected)", t.MonitorName));
            }
            if (rows.Count == 0)
            {
                var none = new Label { Text = "No displays detected.", AutoSize = true, Padding = new Padding(4) };
                _targetsTable.Controls.Add(none, 0, 0);
            }

            var rowIndex = 0;
            foreach (var row in rows)
            {
                var key = row.Key;
                ProfileTarget? Find() => profile.Targets.FirstOrDefault(t => string.Equals(t.MonitorKey, key, StringComparison.OrdinalIgnoreCase));
                var target = Find();

                var check = new CheckBox
                {
                    Text = row.Label, AutoSize = true, Checked = target != null, Anchor = AnchorStyles.Left,
                    Margin = new Padding(6, 6, 6, 6),
                };
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown, Width = 190, Enabled = target != null, Anchor = AnchorStyles.Right,
                    Margin = new Padding(6, 4, 10, 4),
                };
                combo.Items.AddRange(InputSources.Items);
                combo.Text = InputSources.Format(target?.Source ?? 15);

                check.CheckedChanged += (_, _) =>
                {
                    combo.Enabled = check.Checked;
                    var existing = Find();
                    if (check.Checked && existing == null)
                    {
                        var source = InputSources.TryParse(combo.Text, out var code) ? code : 15;
                        profile.Targets.Add(new ProfileTarget { MonitorKey = key, MonitorName = row.Name, Source = source });
                    }
                    else if (!check.Checked && existing != null)
                    {
                        profile.Targets.Remove(existing);
                    }
                };
                combo.TextChanged += (_, _) =>
                {
                    var ok = InputSources.TryParse(combo.Text, out var code);
                    combo.BackColor = ok ? SystemColors.Window : Color.MistyRose;
                    if (ok && Find() is { } t) t.Source = code;
                };

                _targetsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _targetsTable.Controls.Add(check, 0, rowIndex);
                _targetsTable.Controls.Add(combo, 1, rowIndex);
                rowIndex++;
            }
        }
        _targetsTable.ResumeLayout();
    }

    private static string CurrentInputSuffix(MonitorInfo m) =>
        m.CurrentInput is { } c ? $"  – now: {InputSources.Format(c)}" : "";

    private void AddProfile()
    {
        var p = new Profile { Name = $"Profile {Config.Profiles.Count + 1}" };
        Config.Profiles.Add(p);
        LoadProfiles(p);
        _profileName.Focus();
        _profileName.SelectAll();
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is not { } src) return;
        var copy = new Profile
        {
            Name = src.Name + " (copy)",
            Targets = src.Targets.Select(t => new ProfileTarget { MonitorKey = t.MonitorKey, MonitorName = t.MonitorName, Source = t.Source }).ToList(),
        };
        Config.Profiles.Insert(Config.Profiles.IndexOf(src) + 1, copy);
        LoadProfiles(copy);
    }

    private void RemoveProfile()
    {
        if (SelectedProfile is not { } p) return;
        if (MessageBox.Show(this, $"Remove profile '{p.Name}'?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var index = Config.Profiles.IndexOf(p);
        Config.Profiles.Remove(p);
        if (Config.Usb.ConnectProfileId == p.Id) Config.Usb.ConnectProfileId = null;
        if (Config.Usb.DisconnectProfileId == p.Id) Config.Usb.DisconnectProfileId = null;
        LoadProfiles(Config.Profiles.Count == 0 ? null : Config.Profiles[Math.Min(index, Config.Profiles.Count - 1)]);
    }

    private void MoveProfile(int delta)
    {
        if (SelectedProfile is not { } p) return;
        var i = Config.Profiles.IndexOf(p);
        var j = i + delta;
        if (j < 0 || j >= Config.Profiles.Count) return;
        Config.Profiles.RemoveAt(i);
        Config.Profiles.Insert(j, p);
        LoadProfiles(p);
    }

    // ---------------------------------------------------------------- Displays

    private TabPage BuildDisplaysTab()
    {
        var page = new TabPage("Displays") { Padding = new Padding(8) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _displayList.Columns.Add("Display", 200);
        _displayList.Columns.Add("Windows name", 100);
        _displayList.Columns.Add("Resolution", 90);
        _displayList.Columns.Add("Position", 90);
        _displayList.Columns.Add("Current input", 140);
        _displayList.Columns.Add("Device ID", 300);
        root.Controls.Add(_displayList, 0, 0);

        var testRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        testRow.Controls.Add(MakeButton("Refresh", async () => await RefreshMonitorsAsync()));
        testRow.Controls.Add(MakeLabel("   Switch selected displays (or all if none selected) to:"));
        _testSource.Items.AddRange(InputSources.Items);
        _testSource.Text = InputSources.Format(15);
        testRow.Controls.Add(_testSource);
        testRow.Controls.Add(MakeButton("Switch", TestSwitch));
        root.Controls.Add(testRow, 0, 1);
        root.Controls.Add(_displayStatus, 0, 2);

        page.Controls.Add(root);
        return page;
    }

    private async Task RefreshMonitorsAsync()
    {
        _displayStatus.Text = "Detecting displays…";
        UseWaitCursor = true;
        try
        {
            _monitors = await Task.Run(() => MonitorService.GetMonitors(readCurrentInput: true));
        }
        catch (Exception ex)
        {
            _displayStatus.Text = "Failed to detect displays: " + ex.Message;
            return;
        }
        finally
        {
            UseWaitCursor = false;
        }
        if (IsDisposed) return;

        _displayList.BeginUpdate();
        _displayList.Items.Clear();
        foreach (var m in _monitors)
        {
            var item = new ListViewItem(m.Name + (m.Primary ? " (primary)" : "")) { Tag = m };
            item.SubItems.Add(m.ShortGdiName);
            item.SubItems.Add($"{m.Bounds.Width}x{m.Bounds.Height}");
            item.SubItems.Add($"{m.Bounds.X}, {m.Bounds.Y}");
            item.SubItems.Add(m.CurrentInput is { } c ? InputSources.Format(c) : "? (no DDC/CI reply)");
            item.SubItems.Add(m.Key);
            _displayList.Items.Add(item);
        }
        _displayList.EndUpdate();
        _displayStatus.Text = $"{_monitors.Count} display(s) found. Displays that are currently showing another input are usually not visible to Windows.";
        BuildTargetRows(SelectedProfile);
    }

    private void TestSwitch()
    {
        if (!InputSources.TryParse(_testSource.Text, out var code))
        {
            MessageBox.Show(this, "Unknown input source.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var selected = _displayList.SelectedItems.Cast<ListViewItem>().Select(i => (MonitorInfo)i.Tag!).ToList();
        if (selected.Count == 0) selected = _monitors;
        _apply(new Profile
        {
            Name = $"Switch to {InputSources.Format(code)}",
            Targets = selected.Select(m => new ProfileTarget { MonitorKey = m.Key, MonitorName = m.Name, Source = code }).ToList(),
        });
    }

    // ---------------------------------------------------------------- USB

    private TabPage BuildUsbTab()
    {
        var page = new TabPage("USB trigger") { Padding = new Padding(8) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        t.Controls.Add(_usbEnabled, 0, 0);
        t.SetColumnSpan(_usbEnabled, 2);

        t.Controls.Add(MakeLabel("Device instance ID:"), 0, 1);
        t.Controls.Add(_usbId, 1, 1);
        t.Controls.Add(_usbStatus, 1, 2);

        t.Controls.Add(MakeLabel("Pick a connected device:"), 0, 3);
        var pickRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        pickRow.Controls.Add(_usbDevices);
        pickRow.Controls.Add(MakeButton("Refresh", async () => await RefreshUsbDevicesAsync()));
        t.Controls.Add(pickRow, 1, 3);

        t.Controls.Add(MakeLabel("When connected, apply:"), 0, 4);
        t.Controls.Add(_connectProfile, 1, 4);
        t.Controls.Add(MakeLabel("When disconnected, apply:"), 0, 5);
        t.Controls.Add(_disconnectProfile, 1, 5);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            Padding = new Padding(0, 16, 0, 0),
            Text = "Tip: to find your USB switch, click Refresh, toggle the switch to the other computer, click Refresh again, " +
                   "and look for the device that disappeared. A USB hub inside the switch usually works best.",
        };
        t.Controls.Add(help, 0, 6);
        t.SetColumnSpan(help, 2);
        for (var i = 0; i < 7; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _usbEnabled.CheckedChanged += (_, _) => { if (!_loading) Config.Usb.Enabled = _usbEnabled.Checked; };
        _usbId.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                Config.Usb.InstanceId = _usbId.Text.Trim();
                Config.Usb.DeviceName = (_usbDevices.SelectedItem as UsbDevice)?.InstanceId == Config.Usb.InstanceId
                    ? ((UsbDevice)_usbDevices.SelectedItem!).Name
                    : "";
            }
            UpdateUsbStatus();
        };
        _usbDevices.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _usbDevices.SelectedItem is not UsbDevice d) return;
            _usbId.Text = d.InstanceId;
        };
        _connectProfile.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading) Config.Usb.ConnectProfileId = (_connectProfile.SelectedItem as ProfileChoice)?.Id;
        };
        _disconnectProfile.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading) Config.Usb.DisconnectProfileId = (_disconnectProfile.SelectedItem as ProfileChoice)?.Id;
        };

        page.Controls.Add(t);
        return page;
    }

    private void LoadUsb()
    {
        _loading = true;
        _usbEnabled.Checked = Config.Usb.Enabled;
        _usbId.Text = Config.Usb.InstanceId;
        _loading = false;
        UpdateUsbStatus();
        LoadUsbProfileChoices();
    }

    private void LoadUsbProfileChoices()
    {
        _loading = true;
        foreach (var (combo, id) in new[] { (_connectProfile, Config.Usb.ConnectProfileId), (_disconnectProfile, Config.Usb.DisconnectProfileId) })
        {
            combo.Items.Clear();
            combo.Items.Add(new ProfileChoice(null, "(do nothing)"));
            foreach (var p in Config.Profiles) combo.Items.Add(new ProfileChoice(p.Id, p.Name));
            combo.SelectedItem = combo.Items.Cast<ProfileChoice>().FirstOrDefault(c => c.Id == id) ?? combo.Items[0];
        }
        _loading = false;
    }

    private void UpdateUsbStatus()
    {
        var id = _usbId.Text.Trim();
        if (id.Length == 0)
        {
            _usbStatus.Text = "No device selected.";
            _usbStatus.ForeColor = SystemColors.GrayText;
        }
        else if (UsbDevices.IsPresent(id))
        {
            _usbStatus.Text = "✔ Device is currently connected";
            _usbStatus.ForeColor = Color.ForestGreen;
        }
        else
        {
            _usbStatus.Text = "Device is currently not connected";
            _usbStatus.ForeColor = Color.DarkOrange;
        }
    }

    private async Task RefreshUsbDevicesAsync()
    {
        var devices = await Task.Run(UsbDevices.ListPresent);
        if (IsDisposed) return;
        _loading = true;
        _usbDevices.Items.Clear();
        foreach (var d in devices) _usbDevices.Items.Add(d);
        _usbDevices.SelectedItem = devices.FirstOrDefault(d => string.Equals(d.InstanceId, Config.Usb.InstanceId, StringComparison.OrdinalIgnoreCase));
        _loading = false;
        UpdateUsbStatus();
    }

    // ---------------------------------------------------------------- General

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General") { Padding = new Padding(8) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(_startup);
        flow.Controls.Add(new Label { AutoSize = true, Text = "Settings file: " + AppConfig.FilePath, Padding = new Padding(0, 16, 0, 4) });
        flow.Controls.Add(MakeButton("Open settings folder", () =>
        {
            Directory.CreateDirectory(AppConfig.Directory);
            System.Diagnostics.Process.Start("explorer.exe", AppConfig.Directory);
        }));
        page.Controls.Add(flow);
        return page;
    }

    // ---------------------------------------------------------------- Save

    private void Save()
    {
        for (var i = 0; i < Config.Profiles.Count; i++)
        {
            var p = Config.Profiles[i];
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = $"Profile {i + 1}";
            p.Name = p.Name.Trim();
            var clash = Config.Profiles.Take(i).FirstOrDefault(o => p.Hotkey != null && p.Hotkey.SameAs(o.Hotkey));
            if (clash != null)
            {
                MessageBox.Show(this, $"'{p.Name}' and '{clash.Name}' use the same hotkey ({p.Hotkey}).", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _profileList.SelectedItem = p;
                return;
            }
        }

        try
        {
            if (_startup.Checked != StartupManager.IsEnabled) StartupManager.Set(_startup.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not update the startup setting: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------------------------------------------------------- Helpers

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
}
