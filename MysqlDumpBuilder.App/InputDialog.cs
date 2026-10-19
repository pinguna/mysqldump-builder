namespace MysqlDumpBuilder.App;

/// <summary>프리셋 이름처럼 문자열 하나를 묻는 작은 대화상자.
/// 안내 문구가 길면 자동으로 줄바꿈되고 창 높이도 그에 맞춰 늘어난다.</summary>
public sealed class InputDialog : Form
{
    private const int Gutter = 12;        // 창 가장자리 여백
    private const int ContentWidth = 540; // 문구·입력란 폭
    private const int ButtonWidth = 85;
    private const int ButtonHeight = 30;  // 기본 23px 은 9.5pt 글꼴에서 글자 아래가 잘린다

    private readonly Label _label;
    private readonly TextBox _textBox;
    private readonly Button _ok, _cancel;

    public string Value => _textBox.Text.Trim();

    public InputDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.5f);

        _label = new Label { Text = prompt, AutoSize = false };
        _textBox = new ThemedTextBox { Text = initialValue };
        _cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK };

        Controls.AddRange(new Control[] { _label, _textBox, _ok, _cancel });
        AcceptButton = _ok;
        CancelButton = _cancel;

        Shown += (_, _) => { _textBox.SelectAll(); _textBox.Focus(); };
    }

    /// <summary>DPI 배율이 적용된 뒤(OnLoad) 실제 컨트롤 크기로 자리를 잡는다. 그래야 고배율 화면에서도 버튼이 잘리지 않는다.</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var gutter = LogicalToDeviceUnits(Gutter);
        var width = LogicalToDeviceUnits(ContentWidth);
        var buttonWidth = LogicalToDeviceUnits(ButtonWidth);
        var buttonHeight = LogicalToDeviceUnits(ButtonHeight);

        // 안내 문구: 폭을 고정하고 높이는 글 길이에 맞춰 계산한다 (긴 문구는 줄바꿈).
        _label.SetBounds(gutter, gutter, width, _label.GetPreferredSize(new Size(width, 0)).Height + LogicalToDeviceUnits(4));

        _textBox.SetBounds(gutter, _label.Bottom + LogicalToDeviceUnits(8), width, _textBox.Height);

        var buttonTop = _textBox.Bottom + LogicalToDeviceUnits(14);
        _cancel.SetBounds(gutter + width - buttonWidth, buttonTop, buttonWidth, buttonHeight);
        _ok.SetBounds(_cancel.Left - buttonWidth - LogicalToDeviceUnits(6), buttonTop, buttonWidth, buttonHeight);

        ClientSize = new Size(width + gutter * 2, _ok.Bottom + gutter);
        CenterToParent();
    }

    public static string? Ask(IWin32Window owner, string title, string prompt, string initialValue = "")
    {
        using var dialog = new InputDialog(title, prompt, initialValue);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Value.Length > 0 ? dialog.Value : null;
    }
}
