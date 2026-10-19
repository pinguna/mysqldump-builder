namespace MysqlDumpBuilder.App;

/// <summary>
/// 입력란(ThemedTextBox)과 같은 테두리를 가진 콤보박스. Windows 가 그린 뒤 테두리와 화살표 칸을 팔레트 색으로 덧그린다.
/// (FlatStyle.Flat 콤보는 테두리와 버튼을 제 색으로 그려서 입력란과 어긋나 보인다.) 펼쳐지는 목록 자체는 Windows 기본이다.
/// </summary>
public sealed class ThemedComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;

    public ThemedComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        DropDownStyle = ComboBoxStyle.DropDownList;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT && IsHandleCreated) PaintFrame();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    private void PaintFrame()
    {
        var t = AppTheme.Current;
        using var g = Graphics.FromHwnd(Handle);

        // 화살표 칸: 바탕을 입력란 색으로 덮고 작은 꺾쇠만 그린다.
        var buttonWidth = SystemInformation.VerticalScrollBarWidth;
        var button = new Rectangle(Width - buttonWidth - 1, 1, buttonWidth, Height - 2);
        using (var back = new SolidBrush(Enabled ? t.Surface : t.Background)) g.FillRectangle(back, button);
        var cx = button.Left + button.Width / 2;
        var cy = button.Top + button.Height / 2;
        var w = LogicalToDeviceUnits(3);
        using (var chevron = new Pen(Enabled ? t.SubText : Blend(t.SubText, t.Background, 0.5), 1.5f))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawLines(chevron, new[] { new Point(cx - w, cy - 1), new Point(cx, cy + w - 1), new Point(cx + w, cy - 1) });
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        // 테두리: 평소 Border, 포커스 Accent, 비활성은 더 연하게 (ThemedTextBox 와 같은 규칙)
        var color = !Enabled ? Blend(t.Border, t.Background, 0.5)
                  : Focused ? t.Accent
                  : t.Border;
        using var pen = new Pen(color);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private static Color Blend(Color a, Color b, double amount)
        => Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}
