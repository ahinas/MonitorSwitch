namespace MonitorSwitch;

/// <summary>Small borderless popup that does not depend on Windows notification settings.</summary>
public sealed class OsdPopup : Form
{
    private static OsdPopup? _current;

    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly TableLayoutPanel _layout;
    private int _remainingMs;
    private const int FadeMs = 300;

    private OsdPopup(string title, string text, ToolTipIcon kind)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(32, 32, 36);
        ForeColor = Color.White;
        Padding = new Padding(1);
        Font = SystemFonts.MessageBoxFont;

        var accent = kind switch
        {
            ToolTipIcon.Error => Color.FromArgb(232, 72, 72),
            ToolTipIcon.Warning => Color.FromArgb(240, 170, 40),
            _ => Color.FromArgb(0, 120, 215),
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill,
            Padding = new Padding(0, 10, 16, 12), BackColor = BackColor,
        };
        var bar = new Panel { BackColor = accent, Size = new Size(5, 1), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        var titleLabel = new Label
        {
            Text = title, AutoSize = true, Font = new Font(Font.FontFamily, Font.Size + 2, FontStyle.Bold),
            MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 2),
        };
        layout.Controls.Add(bar, 0, 0);
        layout.Controls.Add(titleLabel, 1, 0);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var body = new Label { Text = text, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.Gainsboro };
            layout.Controls.Add(body, 1, 1);
            layout.SetRowSpan(bar, 2);
        }
        Controls.Add(layout);
        _layout = layout;

        _remainingMs = kind == ToolTipIcon.Info ? 2500 : 6000;
        _timer.Interval = 30;
        _timer.Tick += (_, _) =>
        {
            _remainingMs -= _timer.Interval;
            if (_remainingMs <= 0) Close();
            else if (_remainingMs < FadeMs) Opacity = _remainingMs / (double)FadeMs;
        };
        Click += (_, _) => Close();
        foreach (Control c in layout.Controls) c.Click += (_, _) => Close();
        layout.Click += (_, _) => Close();
    }

    public static void Show(string title, string text, ToolTipIcon kind)
    {
        _current?.Close();
        var popup = new OsdPopup(title, text, kind);
        _current = popup;
        popup.FormClosed += (_, _) => { if (_current == popup) _current = null; popup._timer.Dispose(); popup.Dispose(); };
        popup.Show();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var preferred = _layout.GetPreferredSize(Size.Empty);
        ClientSize = new Size(Math.Max(preferred.Width, LogicalToDeviceUnits(240)) + Padding.Horizontal,
            preferred.Height + Padding.Vertical);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(70, 70, 78));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
