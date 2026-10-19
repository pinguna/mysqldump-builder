namespace MysqlDumpBuilder.App;

/// <summary>
/// 목록 한 줄을 크게 보여 주는 대화상자. 경고·설명 한 줄(줄바꿈 본문)과 스크립트 목록 항목(고정폭 명령어)에 같이 쓴다.
/// 본문은 드래그해서 복사할 수 있고, 왼쪽 버튼(actionText)은 호출한 쪽이 뜻을 정한다 ("옵션으로 이동", "왼쪽에 불러오기").
/// </summary>
public sealed class MessageDialog : Form
{
    private const int Gutter = 12;
    private const int ButtonWidth = 120;
    private const int ButtonHeight = 30;
    private const int MinTextHeight = 72;
    private const int MaxTextHeight = 420;

    private readonly Label _head;
    private readonly TextBox _body;
    private readonly Button _action, _copy, _close;
    private readonly string _text;
    private readonly int _contentWidth;
    private readonly bool _monospace;

    /// <summary>왼쪽 버튼(actionText)을 눌러 닫혔으면 true.</summary>
    public bool ActionRequested { get; private set; }

    /// <param name="actionText">왼쪽 버튼 글자. null 이면 버튼이 없다.</param>
    /// <param name="monospace">true 면 본문을 고정폭 글꼴로 줄바꿈 없이 보여 준다 (명령어용).</param>
    public MessageDialog(Font font, string title, string head, Color headColor, string text, string? actionText, bool monospace = false)
    {
        _text = text;
        _monospace = monospace;
        _contentWidth = monospace ? 760 : 600;
        Text = title;
        Font = font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        _head = new Label { Text = head, AutoSize = false, Font = new Font(font, FontStyle.Bold) };
        _body = new ThemedTextBox
        {
            Text = text,
            ReadOnly = true,
            Multiline = true,
            WordWrap = !monospace,
            ScrollBars = monospace ? ScrollBars.Both : ScrollBars.Vertical,
            Font = monospace ? new Font("Consolas", 10.5f) : font
        };
        _action = new Button { Text = actionText ?? "", Visible = actionText is not null };
        _copy = new Button { Text = UiStrings.Get("CopyText") };
        _close = new Button { Text = UiStrings.Get("Close"), DialogResult = DialogResult.Cancel };

        _action.Click += (_, _) => { ActionRequested = true; Close(); };
        _copy.Click += (_, _) => { try { Clipboard.SetText(_text); } catch { /* 클립보드가 잠겨 있으면 그냥 둔다 */ } };

        Controls.AddRange(new Control[] { _head, _body, _action, _copy, _close });
        CancelButton = _close;
        AcceptButton = _close;

        AppTheme.Apply(this);
        _head.ForeColor = headColor;
        Shown += (_, _) => { _body.Select(0, 0); _close.Focus(); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var gutter = LogicalToDeviceUnits(Gutter);
        var width = LogicalToDeviceUnits(_contentWidth);
        var buttonWidth = LogicalToDeviceUnits(ButtonWidth);
        var buttonHeight = LogicalToDeviceUnits(ButtonHeight);

        _head.SetBounds(gutter, gutter, width, _head.GetPreferredSize(new Size(width, 0)).Height + LogicalToDeviceUnits(2));

        // 본문 높이는 글 길이에 맞추되 너무 길면 스크롤로 넘긴다.
        var inner = width - LogicalToDeviceUnits(24);   // 테두리·스크롤바 몫
        var flags = TextFormatFlags.NoPrefix | (_monospace ? TextFormatFlags.Default : TextFormatFlags.WordBreak);
        var measured = TextRenderer.MeasureText(_text, _body.Font, new Size(inner, 0), flags).Height + LogicalToDeviceUnits(_monospace ? 30 : 12);
        var bodyHeight = Math.Clamp(measured, LogicalToDeviceUnits(MinTextHeight), LogicalToDeviceUnits(MaxTextHeight));
        _body.SetBounds(gutter, _head.Bottom + LogicalToDeviceUnits(6), width, bodyHeight);

        var buttonTop = _body.Bottom + LogicalToDeviceUnits(12);
        _action.SetBounds(gutter, buttonTop, buttonWidth, buttonHeight);
        _close.SetBounds(gutter + width - buttonWidth, buttonTop, buttonWidth, buttonHeight);
        _copy.SetBounds(_close.Left - buttonWidth - LogicalToDeviceUnits(6), buttonTop, buttonWidth, buttonHeight);

        ClientSize = new Size(width + gutter * 2, _close.Bottom + gutter);
        CenterToParent();
    }
}
