using System.Runtime.InteropServices;

namespace MysqlDumpBuilder.App;

/// <summary>
/// 마우스 휠을 "포커스된 컨트롤"이 아니라 "커서 아래 컨트롤"로 보낸다.
/// WinForms 기본 동작은 포커스 기준이라, 왼쪽 패널 위에서 휠을 굴려도 스크롤되지 않는 문제를 고친다.
/// 커서가 콤보박스 위에 있을 때는 값이 바뀌지 않도록 부모(스크롤 패널)로 보낸다.
/// </summary>
public sealed class WheelToHoverFilter : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point p);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;

        var hover = WindowFromPoint(Cursor.Position);
        if (hover == IntPtr.Zero || hover == m.HWnd) return false;

        var control = Control.FromChildHandle(hover);
        if (control is null) return false;                 // 다른 프로그램의 창

        if (control is ComboBox && control.Parent is not null)
            hover = control.Parent.Handle;                 // 콤보박스 값이 실수로 바뀌는 것을 막는다

        SendMessage(hover, m.Msg, m.WParam, m.LParam);
        return true;
    }
}
