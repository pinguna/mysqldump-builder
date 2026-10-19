using System.Drawing.Drawing2D;

namespace MysqlDumpBuilder.App;

/// <summary>
/// 직접 그리는 라디오 버튼. FlatStyle.Flat 라디오는 원을 계단지게 그려 테두리가 깨져 보이므로 안티앨리어싱으로 원과 점을 그린다.
/// 색은 팔레트: 원 테두리 Border(선택 시 Accent), 점 Accent, 비활성은 연하게.
/// </summary>
public sealed class ThemedRadioButton : RadioButton
{
    public ThemedRadioButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = AppTheme.Current;
        var g = e.Graphics;
        g.Clear(BackColor);

        var d = LogicalToDeviceUnits(13);
        var circle = new Rectangle(0, (Height - d) / 2, d, d);
        var disabledBorder = Blend(t.Border, t.Background, 0.5);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(Enabled ? t.Surface : t.Background)) g.FillEllipse(fill, circle);
        using (var pen = new Pen(!Enabled ? disabledBorder : Checked ? t.Accent : t.Border, 1.2f)) g.DrawEllipse(pen, circle);
        if (Checked)
        {
            var dot = Rectangle.Inflate(circle, -LogicalToDeviceUnits(4), -LogicalToDeviceUnits(4));
            using var dotBrush = new SolidBrush(Enabled ? t.Accent : t.DisabledText);
            g.FillEllipse(dotBrush, dot);
        }
        g.SmoothingMode = SmoothingMode.None;

        var textRect = new Rectangle(d + LogicalToDeviceUnits(5), 0, Math.Max(0, Width - d - LogicalToDeviceUnits(5)), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : t.DisabledText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            var size = TextRenderer.MeasureText(g, Text, Font, textRect.Size, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            ControlPaint.DrawFocusRectangle(g, new Rectangle(textRect.Left - 1, (Height - size.Height) / 2 - 1, size.Width + 2, size.Height + 2));
        }
    }

    private static Color Blend(Color a, Color b, double amount)
        => Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}
