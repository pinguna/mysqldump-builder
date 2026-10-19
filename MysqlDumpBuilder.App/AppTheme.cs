using System.Runtime.InteropServices;

namespace MysqlDumpBuilder.App;

/// <summary>
/// 자체 테마: 라이트/다크 팔레트와 글꼴. 외부 라이브러리 없이 기본 WinForms 컨트롤의 색과 스타일만 바꾼다.
///  - 색은 여기 팔레트에서만 정한다. 화면 코드는 AppTheme.Current.Xxx 를 쓰거나, 라벨에 Tag 를 붙여 역할만 표시한다.
///  - 글꼴은 Noto Sans KR. PC 에 없으면 exe 에 내장한 파일을 임시 폴더로 풀어 이 프로세스에만 등록한다 (설치 불필요).
/// </summary>
public sealed class AppTheme
{
    // 라벨 역할 표시용 Tag (색을 직접 지정하지 않고 역할만 적는다)
    public const string TagSub = "theme:sub";         // 보조 글자 (미리보기 값, 그룹 제목)
    public const string TagHeader = "theme:header";   // 섹션 제목
    public const string TagLine = "theme:line";       // 섹션 구분선
    public const string TagPrimary = "theme:primary"; // 주 버튼 (강조색 채움)

    public bool IsDark { get; init; }
    public Color Background { get; init; }   // 창·패널 바탕
    public Color Surface { get; init; }      // 입력란·목록 바탕
    public Color Border { get; init; }       // 테두리·구분선
    public Color Text { get; init; }         // 본문 글자
    public Color SubText { get; init; }      // 보조 글자
    public Color DisabledText { get; init; } // 꺼진 칸의 제목·꺼진 라디오 (본문 > 보조 > 꺼짐 순으로 흐려진다)
    public Color Header { get; init; }       // 섹션 제목
    public Color Accent { get; init; }       // 강조 (주 버튼)
    public Color AccentText { get; init; }
    public Color Error { get; init; }        // 빨강: 오류, 위험 옵션
    public Color Caution { get; init; }      // 주황: 경고, 주의 옵션
    public Color Success { get; init; }      // 초록: 완료 상태

    public static readonly AppTheme Light = new()
    {
        IsDark = false,
        Background = Color.FromArgb(0xF6, 0xF7, 0xF9),
        Surface = Color.White,
        Border = Color.FromArgb(0xD5, 0xD9, 0xE0),
        Text = Color.FromArgb(0x1F, 0x23, 0x28),
        SubText = Color.FromArgb(0x6B, 0x72, 0x80),
        DisabledText = Color.FromArgb(0xA8, 0xAE, 0xB8),
        Header = Color.FromArgb(0x11, 0x18, 0x27),
        Accent = Color.FromArgb(0x25, 0x63, 0xEB),
        AccentText = Color.White,
        Error = Color.FromArgb(0xB9, 0x1C, 0x1C),
        Caution = Color.FromArgb(0xB4, 0x53, 0x09),
        Success = Color.FromArgb(0x15, 0x80, 0x3D)
    };

    public static readonly AppTheme Dark = new()
    {
        IsDark = true,
        Background = Color.FromArgb(0x1E, 0x1F, 0x24),
        Surface = Color.FromArgb(0x2A, 0x2C, 0x33),
        Border = Color.FromArgb(0x42, 0x46, 0x50),
        Text = Color.FromArgb(0xE6, 0xE8, 0xEC),
        SubText = Color.FromArgb(0x9A, 0xA0, 0xAA),
        DisabledText = Color.FromArgb(0x5E, 0x64, 0x6E),
        Header = Color.FromArgb(0xF3, 0xF4, 0xF6),
        Accent = Color.FromArgb(0x60, 0xA5, 0xFA),
        AccentText = Color.FromArgb(0x0B, 0x12, 0x20),
        Error = Color.FromArgb(0xF8, 0x71, 0x71),
        Caution = Color.FromArgb(0xFB, 0xBF, 0x24),
        Success = Color.FromArgb(0x4A, 0xDE, 0x80)
    };

    public static AppTheme Current { get; set; } = Light;

    // ------------------------------------------------------------------
    // 적용
    // ------------------------------------------------------------------

    /// <summary>폼과 모든 자식 컨트롤에 현재 팔레트를 입힌다. 제목 표시줄도 다크/라이트에 맞춘다.</summary>
    public static void Apply(Form form)
    {
        ApplyTo(form);
        SetTitleBar(form);
        form.HandleCreated -= OnHandleCreated;
        form.HandleCreated += OnHandleCreated;
    }

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Form f) SetTitleBar(f);
    }

    private static void ApplyTo(Control c)
    {
        var t = Current;
        switch (c)
        {
            case Form f:
                f.BackColor = t.Background;
                f.ForeColor = t.Text;
                break;

            case TextBox tb:
                tb.BackColor = t.Surface;
                tb.ForeColor = t.Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = t.Surface;
                cb.ForeColor = t.Text;
                break;

            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                if (_hookedButtons.Add(b)) b.EnabledChanged += (s, _) => PaintButton((Button)s!);
                PaintButton(b);
                break;

            case CheckBox cb:
                cb.FlatStyle = FlatStyle.Flat;   // 네모와 체크 표시를 ForeColor 로 그린다 (다크 모드에서도 맞는다)
                cb.BackColor = t.Background;
                cb.ForeColor = t.Text;           // 위험도 색은 MainForm 이 이 뒤에 다시 칠한다
                break;

            case RadioButton rb:
                rb.FlatStyle = FlatStyle.Flat;
                rb.BackColor = t.Background;
                rb.ForeColor = t.Text;
                break;

            case Label l:
                l.BackColor = t.Background;
                PaintLabel(l);
                break;

            case ListView lv:
                lv.BackColor = t.Surface;
                lv.ForeColor = t.Text;
                lv.BorderStyle = BorderStyle.FixedSingle;
                break;

            case Panel p when Equals(p.Tag, TagLine):
                p.BackColor = t.Border;
                break;

            default:
                c.BackColor = t.Background;   // Panel, TableLayoutPanel, FlowLayoutPanel, SplitContainer 패널
                break;
        }

        foreach (Control child in c.Controls) ApplyTo(child);
    }

    // 입력 칸 제목 라벨 → 그 입력 칸. 칸이 꺼지면 제목도 꺼진 색으로 칠한다.
    private static readonly Dictionary<Label, Control> _labelFor = new();

    /// <summary>
    /// 라벨을 입력 칸의 제목으로 묶는다. 칸이 꺼지면(Enabled=false) 라벨 글자가 DisabledText 로 바뀐다.
    /// 라벨 자체의 Enabled 는 쓰지 않는다 — Windows 가 그리는 꺼진 라벨은 다크 모드에서 음각으로 뭉개진다.
    /// </summary>
    public static void BindLabel(Label label, Control field)
    {
        _labelFor[label] = field;
        field.EnabledChanged += (_, _) => PaintLabel(label);
        PaintLabel(label);
    }

    private static void PaintLabel(Label l)
    {
        var t = Current;
        l.ForeColor = _labelFor.TryGetValue(l, out var field) && !field.Enabled ? t.DisabledText
                    : Equals(l.Tag, TagSub) ? t.SubText
                    : Equals(l.Tag, TagHeader) ? t.Header
                    : t.Text;
    }

    // 버튼은 켜짐/꺼짐에 따라 색이 달라야 해서 EnabledChanged 때 다시 칠한다 (한 버튼에 한 번만 연결).
    private static readonly HashSet<Button> _hookedButtons = new();

    /// <summary>버튼 색: 주 버튼은 강조색, 보통 버튼은 입력란색. 꺼진 버튼은 둘 다 회색 바탕에 흐린 글자라 한눈에 구분된다.</summary>
    private static void PaintButton(Button b)
    {
        var t = Current;
        if (!b.Enabled)
        {
            b.BackColor = Blend(t.Background, t.Border, 0.55);
            b.ForeColor = t.SubText;
            b.FlatAppearance.BorderColor = t.Border;
            return;
        }
        if (Equals(b.Tag, TagPrimary))
        {
            b.BackColor = t.Accent;
            b.ForeColor = t.AccentText;
            b.FlatAppearance.BorderColor = t.Accent;
            b.FlatAppearance.MouseOverBackColor = Blend(t.Accent, t.Text, 0.12);
        }
        else
        {
            b.BackColor = t.Surface;
            b.ForeColor = t.Text;
            b.FlatAppearance.BorderColor = t.Border;
            b.FlatAppearance.MouseOverBackColor = Blend(t.Surface, t.Text, 0.08);
        }
    }

    /// <summary>
    /// 목록의 열 머리글을 직접 그린다 (Windows 가 그리는 머리글은 다크 모드에서 밝게 남는다). 항목은 기본 그리기를 쓴다.
    /// 한 번만 호출하면 되고, 색은 그릴 때마다 Current 에서 읽는다.
    /// </summary>
    public static void StyleListView(ListView lv)
    {
        // 마지막 열이 남은 폭을 모두 쓰게 한다: 머리글 오른쪽에 Windows 가 그리는 빈 조각이 남지 않고 가로 스크롤바도 생기지 않는다.
        var fitting = false;
        void FitLastColumn()
        {
            if (fitting || !lv.IsHandleCreated || lv.Columns.Count == 0) return;
            fitting = true;
            try
            {
                var others = 0;
                for (var i = 0; i < lv.Columns.Count - 1; i++) others += lv.Columns[i].Width;
                lv.Columns[lv.Columns.Count - 1].Width = Math.Max(120, lv.ClientSize.Width - others - 2);   // 2px: 딱 맞추면 가로 스크롤바가 생긴다
                lv.BeginUpdate(); lv.EndUpdate();   // 열 폭만 바꾸면 가로 스크롤바가 남는 경우가 있어 다시 계산하게 한다
            }
            finally { fitting = false; }
        }
        lv.ClientSizeChanged += (_, _) => FitLastColumn();
        lv.ColumnWidthChanged += (_, _) => FitLastColumn();
        lv.HandleCreated += (_, _) => lv.BeginInvoke(FitLastColumn);

        lv.OwnerDraw = true;
        lv.DrawColumnHeader += (_, e) =>
        {
            var t = Current;
            using var back = new SolidBrush(t.Background);
            using var pen = new Pen(t.Border);
            e.Graphics.FillRectangle(back, e.Bounds);
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", lv.Font, Rectangle.Inflate(e.Bounds, -6, 0), t.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        lv.DrawItem += (_, e) => e.DrawDefault = true;
        lv.DrawSubItem += (_, e) => e.DrawDefault = true;
    }

    private static Color Blend(Color a, Color b, double amount)
        => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));

    // Windows 10 20H1 이상: 제목 표시줄을 어둡게 (실패해도 조용히 무시)
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void SetTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            var dark = Current.IsDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        }
        catch
        {
            // 구형 Windows: 제목 표시줄은 기본 색으로 둔다
        }
    }

    // ------------------------------------------------------------------
    // 아이콘
    // ------------------------------------------------------------------

    private static Icon? _appIcon;

    /// <summary>exe 에 내장한 앱 아이콘(Resources/app.ico, MySQL 돌고래). 없으면 null.</summary>
    public static Icon? LoadAppIcon()
    {
        if (_appIcon is not null) return _appIcon;
        try
        {
            var asm = typeof(AppTheme).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using var stream = asm.GetManifestResourceStream(name)!;
            return _appIcon = new Icon(stream);
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // 글꼴
    // ------------------------------------------------------------------

    private const string PreferredFont = "Noto Sans KR";

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
    private const uint FR_PRIVATE = 0x10;

    /// <summary>쓸 글꼴 이름. Noto Sans KR → (없으면 내장 파일 등록) → 맑은 고딕 → Segoe UI 순.</summary>
    public static string FontFamilyName()
    {
        if (IsAvailable(PreferredFont)) return PreferredFont;
        if (TryRegisterEmbeddedFont() && IsAvailable(PreferredFont)) return PreferredFont;
        return IsAvailable("Malgun Gothic") ? "Malgun Gothic" : "Segoe UI";
    }

    private static bool IsAvailable(string family)
    {
        try { using var f = new FontFamily(family); return true; }
        catch { return false; }
    }

    /// <summary>exe 에 내장한 Noto Sans KR 을 임시 폴더에 풀고 이 프로세스에만 등록한다 (관리자 권한·설치 불필요).</summary>
    private static bool TryRegisterEmbeddedFont()
    {
        try
        {
            var asm = typeof(AppTheme).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("NotoSansKR.ttf", StringComparison.OrdinalIgnoreCase));
            if (name is null) return false;

            var dir = Path.Combine(Path.GetTempPath(), "MysqlDumpBuilder");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "NotoSansKR.ttf");

            using var stream = asm.GetManifestResourceStream(name)!;
            if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
            {
                using var file = File.Create(path);
                stream.CopyTo(file);
            }
            return AddFontResourceEx(path, FR_PRIVATE, IntPtr.Zero) > 0;
        }
        catch
        {
            return false;
        }
    }
}
