using System.Runtime.InteropServices;

namespace MysqlDumpBuilder.App;

/// <summary>
/// 테마 색 테두리와 좌우 안쪽 여백이 있는 입력란.
///  - 기본 FixedSingle 테두리는 검정에 가까워 눈에 띄므로, Windows 가 테두리를 그린 뒤 팔레트의 Border 색으로 덧그린다 (포커스 땐 Accent).
///  - TextBox 는 Padding 이 없어서 EM_SETMARGINS 로 글자 좌우 여백을 준다.
/// </summary>
public sealed class ThemedTextBox : TextBox
{
    private const int WM_PAINT = 0x000F;
    private const int WM_NCPAINT = 0x0085;
    private const int WM_SETFOCUS = 0x0007;
    private const int WM_KILLFOCUS = 0x0008;
    private const int EM_SETMARGINS = 0x00D3;
    private const int EC_LEFTMARGIN = 0x0001;
    private const int EC_RIGHTMARGIN = 0x0002;
    private const uint RDW_FRAME = 0x0400, RDW_INVALIDATE = 0x0001, RDW_UPDATENOW = 0x0100;

    /// <summary>글자 좌우 여백 (논리 픽셀).</summary>
    public int SideMargin { get; set; } = 6;

    public ThemedTextBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMargins();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (IsHandleCreated) ApplyMargins();
    }

    private void ApplyMargins()
    {
        var m = LogicalToDeviceUnits(SideMargin);
        SendMessage(Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), (IntPtr)((m << 16) | m));
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        RedrawFrame();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        switch (m.Msg)
        {
            case WM_NCPAINT:
            case WM_PAINT:
                PaintBorder();
                break;
            case WM_SETFOCUS:
            case WM_KILLFOCUS:
                RedrawFrame();
                break;
        }
    }

    private void RedrawFrame()
    {
        if (IsHandleCreated) RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_FRAME | RDW_INVALIDATE | RDW_UPDATENOW);
    }

    private void PaintBorder()
    {
        if (!IsHandleCreated || BorderStyle != BorderStyle.FixedSingle) return;
        var hdc = GetWindowDC(Handle);
        if (hdc == IntPtr.Zero) return;
        try
        {
            using var g = Graphics.FromHdc(hdc);
            var t = AppTheme.Current;
            var color = !Enabled ? Blend(t.Border, t.Background, 0.5)
                      : Focused ? t.Accent
                      : t.Border;
            using var pen = new Pen(color);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        finally
        {
            ReleaseDC(Handle, hdc);
        }
    }

    private static Color Blend(Color a, Color b, double amount)
        => Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
}
