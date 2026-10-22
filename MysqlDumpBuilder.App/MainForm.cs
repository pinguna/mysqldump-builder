using MysqlDumpBuilder.Core;

namespace MysqlDumpBuilder.App;

/// <summary>
/// 메인 화면. 왼쪽은 입력(접속·대상·옵션·출력), 오른쪽은 결과(명령어·경고).
/// 체크박스 옵션은 options.schema.json 에서 자동 생성되므로 옵션을 추가할 때 이 파일을 고칠 필요가 없다.
/// 모든 입력 변경 → ReadRequest() → CommandBuilder.Build() → 결과 표시, 이 한 흐름뿐이다.
/// </summary>
public sealed class MainForm : Form
{
    private readonly OptionSchema _schema;
    private readonly JobBuilder _builder;
    private readonly AppSettings _settings;
    private readonly PresetStore _presetStore;
    private List<Preset> _userPresets;

    private bool _suspendRebuild;
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 200 };

    // 일괄 변경(언어 전환, 상태 로드, 체크박스 표시/숨김) 동안 레이아웃을 멈추기 위해 컨테이너를 모아 둔다.
    // 멈추지 않으면 컨트롤 하나 바뀔 때마다 전체 트리를 다시 계산해 시작에 10초 이상 걸린다.
    private readonly List<Control> _containers = new();

    // 언어 전환을 위해 문구를 가진 컨트롤을 등록해 둔다.
    private readonly List<(Control Control, Func<string> Text)> _localized = new();
    private readonly List<(Control Control, LocalizedText Help)> _helpTips = new();

    // ---- 상단 바 ----
    private ComboBox _presetCombo = null!;
    private Button _presetSave = null!, _presetDelete = null!, _resetButton = null!;
    private ComboBox _languageCombo = null!;

    // ---- [A] 접속 ----
    private TextBox _socket = null!, _importSocket = null!;
    private TextBox _host = null!, _port = null!, _user = null!, _passwordValue = null!, _binaryPath = null!;
    private CheckBox _darkSwitch = null!;
    private ComboBox _passwordMode = null!, _client = null!;
    private Label _passwordValueLabel = null!;

    // ---- [B] 대상 ----
    private RadioButton[] _targetRadios = null!;
    private TextBox _databases = null!, _tables = null!, _ignoreTables = null!, _where = null!;

    // ---- [C] 옵션 ----
    private RadioButton[] _contentRadios = null!, _insertRadios = null!;
    private CheckBox _showAdvanced = null!;
    private readonly Dictionary<string, CheckBox> _optionChecks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _optionValues = new(StringComparer.Ordinal);

    // ---- [D] 출력 ----
    private RadioButton[] _outputRadios = null!;
    private TextBox _fileTemplate = null!, _scriptTemplate = null!;
    private Label _fileNamePreview = null!, _scriptNamePreview = null!;
    private Label _previewTitle = null!;

    // ---- [D2] 작업 종류와 Import 대상 ----
    private RadioButton[] _jobRadios = null!;
    private readonly List<Control> _importSection = new();   // 작업이 Export 뿐이면 통째로 숨긴다
    private bool? _importShown, _importPwShown, _importInputShown;   // 마지막으로 적용한 표시 상태 (Visible 은 폼이 뜨기 전 신뢰 불가)
    private TextBox _importInputFile = null!;
    private Button _importBrowse = null!;
    private readonly List<Control> _importInputRow = new();   // "읽을 파일" 행: Import 만 할 때만 보인다
    private CheckBox _importSame = null!, _importCreateDb = null!, _importForce = null!, _importFkOff = null!;
    private TextBox _importHost = null!, _importPort = null!, _importUser = null!, _importPasswordValue = null!, _importDatabase = null!, _importBinaryPath = null!, _importCharset = null!;
    private ComboBox _importPasswordMode = null!;
    private Label _importPasswordValueLabel = null!;

    // ---- [G] 덤프 목록: 여러 mysqldump 를 한 스크립트에 담기 위한 항목들 (프리셋과 같은 모양, batch.json 에 저장) ----
    private readonly PresetStore _batchStore;
    private readonly List<Preset> _batch;
    private ListView _batchList = null!;
    private Button _batchAdd = null!, _batchUpdate = null!;
    private string _currentKey = "";          // 현재 화면 설정의 비교 키 (목록 항목과 같은지 판단)
    private bool _currentHasErrors;

    // ---- [E] 결과 ----
    private ComboBox _shell = null!;
    private CheckBox _multiLine = null!, _explicitDefaults = null!, _scriptPreview = null!;
    private Button _copy = null!, _exportScript = null!;
    private TextBox _command = null!;
    private Font _commandFont = null!;
    private Font _hintFont = null!;
    private Label _status = null!;
    private ListView _messages = null!;
    private SplitContainer _split = null!;

    private static readonly string[] PasswordModeKeys = { "PwPrompt", "PwLoginPath", "PwExtraFile", "PwPlain" };
    private static readonly string[] ClientKeys = { "ClientMysql57", "ClientMysql80" };
    private static readonly string[] ShellKeys = { "ShellBash", "ShellCmd", "ShellPs" };

    public MainForm(OptionSchema schema, AppSettings settings, PresetStore presetStore)
    {
        _schema = schema;
        _builder = new JobBuilder(schema);
        _settings = settings;
        _presetStore = presetStore;
        _userPresets = SafeLoadPresets();
        _batchStore = new PresetStore(Path.Combine(Path.GetDirectoryName(presetStore.Path) ?? AppContext.BaseDirectory, "batch.json"));
        try { _batch = _batchStore.Load(); } catch { _batch = new List<Preset>(); }
        UiStrings.Current = settings.Language;

        // 자체 테마: 글꼴(Noto Sans KR)과 라이트/다크 팔레트. 컨트롤을 다 만든 뒤 ApplyTheme() 로 색을 입힌다.
        AppTheme.Current = settings.DarkMode ? AppTheme.Dark : AppTheme.Light;
        Font = new Font(AppTheme.FontFamilyName(), 9.5f);
        if (AppTheme.LoadAppIcon() is { } icon) Icon = icon;   // 창·작업 표시줄 아이콘 (exe 아이콘과 같은 파일)

        Size = new Size(settings.WindowWidth ?? 1320, settings.WindowHeight ?? 880);
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        // 컨트롤이 다 만들어지기 전에는 이벤트가 Rebuild 를 부르지 못하게 막는다.
        _suspendRebuild = true;
        SuspendLayout();
        BuildLayout();
        WithLayoutSuspended(ApplyLanguage);
        ApplyTheme();
        ResumeLayout(true);
        _suspendRebuild = false;
        LoadInitialState();

        KeyDown += (_, e) =>
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.C) { CopyCommand(); e.Handled = true; }
        };

        var wheelFilter = new WheelToHoverFilter();
        Application.AddMessageFilter(wheelFilter);
        FormClosing += (_, _) => SaveSettings();
        FormClosed += (_, _) => Application.RemoveMessageFilter(wheelFilter);
    }

    // ==================================================================
    // 레이아웃
    // ==================================================================

    private void BuildLayout()
    {
        // 행: 프리셋·언어 바 / 작업 라디오 바 / 좌우 분할 영역
        var root = TrackLayout(new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) });
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildTopBar(), 0, 0);
        root.Controls.Add(BuildJobBar(), 0, 1);

        _split = TrackLayout(new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 });
        TrackLayout(_split.Panel1);
        TrackLayout(_split.Panel2);
        _split.Panel1.Controls.Add(BuildLeftPanel());
        _split.Panel2.Controls.Add(BuildRightPanel());
        root.Controls.Add(_split, 0, 2);

        Controls.Add(root);

        Shown += (_, _) =>
        {
            // 시작할 때는 항상 좌우 1:1 로 나눈다.
            _split.SplitterDistance = Math.Clamp(_split.Width / 2, 420, Math.Max(421, _split.Width - 380));
        };
    }

    private Control BuildTopBar()
    {
        var bar = TrackLayout(new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 2) });

        bar.Controls.Add(Localized(new Label { AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, "Preset"));
        _presetCombo = new ThemedComboBox { Width = 300, Margin = new Padding(0, 4, 6, 0) };
        _presetCombo.SelectedIndexChanged += (_, _) => OnPresetSelected();
        bar.Controls.Add(_presetCombo);

        _presetSave = Localized(new Button { AutoSize = true, Margin = new Padding(0, 3, 4, 0) }, "PresetSave");
        _presetSave.Click += (_, _) => SavePreset();
        bar.Controls.Add(_presetSave);

        _presetDelete = Localized(new Button { AutoSize = true, Margin = new Padding(0, 3, 24, 0) }, "PresetDelete");
        _presetDelete.Click += (_, _) => DeletePreset();
        bar.Controls.Add(_presetDelete);

        _resetButton = Localized(new Button { AutoSize = true, Margin = new Padding(0, 3, 24, 0) }, "Reset");
        _resetButton.Click += (_, _) => ResetOptions();
        bar.Controls.Add(_resetButton);

        bar.Controls.Add(Localized(new Label { AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, "Language"));
        _languageCombo = new ThemedComboBox { Width = 110, Margin = new Padding(0, 4, 24, 0) };
        _languageCombo.Items.AddRange(new object[] { "한국어", "English" });
        _languageCombo.SelectedIndex = UiStrings.Current == Language.Ko ? 0 : 1;
        _languageCombo.SelectedIndexChanged += (_, _) =>
        {
            UiStrings.Current = _languageCombo.SelectedIndex == 0 ? Language.Ko : Language.En;
            WithLayoutSuspended(ApplyLanguage);
            Rebuild();
        };
        bar.Controls.Add(_languageCombo);

        // 라이트/다크 전환 (설정에 저장)
        _darkSwitch = Localized(new CheckBox { AutoSize = true, Margin = new Padding(0, 7, 0, 0), Checked = _settings.DarkMode }, "DarkMode");
        _darkSwitch.CheckedChanged += (_, _) =>
        {
            AppTheme.Current = _darkSwitch.Checked ? AppTheme.Dark : AppTheme.Light;
            _settings.DarkMode = _darkSwitch.Checked;
            ApplyTheme();
            Rebuild();   // 경고 목록 글자색을 새 팔레트로 다시 칠한다
        };
        bar.Controls.Add(_darkSwitch);

        return bar;
    }

    /// <summary>현재 팔레트를 모든 컨트롤에 입힌다. 위험도 색(빨강·주황)은 테마가 덮어쓰므로 다시 칠한다.</summary>
    private void ApplyTheme()
    {
        WithLayoutSuspended(() =>
        {
            AppTheme.Apply(this);
            foreach (var o in _schema.Options)
                if (_optionChecks.TryGetValue(o.Id, out var cb)) cb.ForeColor = RiskColor(o.Risk);
        });
        Invalidate(true);
    }

    /// <summary>둘째 줄: 작업 종류 라디오. 화면 구성(Import 섹션 표시)을 결정한다.</summary>
    private Control BuildJobBar()
    {
        var bar = TrackLayout(new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 6) });
        bar.Controls.Add(Localized(new Label { AutoSize = true, Margin = new Padding(0, 5, 10, 0), Font = new Font(Font, FontStyle.Bold) }, "JobKind"));
        _jobRadios = RadioRow(bar, new[] { "JobExport", "JobExportImport", "JobImport" }, 0);
        foreach (var rb in _jobRadios)
        {
            rb.Margin = new Padding(0, 4, 18, 0);
            _helpTips.Add((rb, UiStrings.Text("JobHint")));
        }
        return bar;
    }

    // 왼쪽 입력 영역은 테이블 하나에 모두 넣는다 (섹션 제목은 굵은 라벨 + 구분선).
    // GroupBox 안에 테이블, 그 안에 또 GroupBox 를 중첩하면 AutoSize 계산이 단계마다 반복돼
    // 시작에 수 초가 걸리므로, 중첩 깊이를 1단계로 유지하는 것이 핵심이다.
    private TableLayoutPanel _left = null!;
    private int _leftRow;

    private Control BuildLeftPanel()
    {
        var scroll = TrackLayout(new Panel { Dock = DockStyle.Fill, AutoScroll = true });
        _left = TrackLayout(new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, Location = new Point(0, 0), Padding = new Padding(0, 0, 4, 8) });
        _left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        BuildConnectionSection();
        BuildImportSection();   // Export + Import / Import 일 때만 보인다
        BuildTargetSection();
        BuildOptionsSection();
        BuildOutputSection();

        // 맨 아래에 빈 행 하나. 테이블이 높이를 잴 때 라디오 묶음의 줄바꿈을 실제보다 한 줄 많게 계산하는 경우가 있어
        // 남는 공간이 생기는데, 그 공간을 마지막 행(파일명 템플릿)이 아니라 이 빈 행이 받게 해서 행 간격이 벌어지지 않게 한다.
        _left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _left.RowCount = _leftRow + 1;

        scroll.Controls.Add(_left);
        void FitWidth()
        {
            // 패널 전체 폭에서 스크롤바 폭을 항상 뺀다 (ClientSize 는 스크롤바 유무에 따라 흔들리므로 쓰지 않는다).
            var w = Math.Max(300, scroll.Width - SystemInformation.VerticalScrollBarWidth - 4);
            _left.MinimumSize = new Size(w, 0);
            _left.MaximumSize = new Size(w, 0);
        }
        scroll.Resize += (_, _) => FitWidth();
        FitWidth();
        return scroll;
    }

    // ---- 행 추가 헬퍼 ----

    private int NewRow()
    {
        _left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return _leftRow++;
    }

    /// <summary>섹션 제목: 굵은 라벨 한 줄 + 구분선. 언어 전환을 위해 라벨 문구는 함수로 받는다.</summary>
    private void SectionHeader(Func<string> text, bool first = false)
    {
        var row = NewRow();
        var label = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold), Margin = new Padding(0, first ? 2 : 14, 0, 0), Tag = AppTheme.TagHeader };
        _localized.Add((label, text));
        _left.Controls.Add(label, 0, row);
        _left.SetColumnSpan(label, 4);

        var line = new Panel { Height = 1, Dock = DockStyle.Top, Margin = new Padding(0, 2, 6, 4), Tag = AppTheme.TagLine };
        var lineRow = NewRow();
        _left.Controls.Add(line, 0, lineRow);
        _left.SetColumnSpan(line, 4);
    }

    private void SectionHeader(string key, bool first = false) => SectionHeader(() => UiStrings.Get(key), first);

    /// <summary>라벨 + 입력 한 쌍(또는 두 쌍)을 한 행에 놓는다.</summary>
    private void FieldRow(string key1, Control c1, string? key2 = null, Control? c2 = null)
    {
        var row = NewRow();
        _left.Controls.Add(FieldLabel(key1, c1), 0, row);
        _left.Controls.Add(c1, 1, row);
        if (key2 is null || c2 is null)
        {
            _left.SetColumnSpan(c1, 3);
            return;
        }
        _left.Controls.Add(FieldLabel(key2, c2), 2, row);
        _left.Controls.Add(c2, 3, row);
    }

    /// <summary>라벨 + 라디오 버튼 묶음 한 행.</summary>
    private RadioButton[] RadioFieldRow(string labelKey, string[] keys, int selected)
    {
        var row = NewRow();
        _left.Controls.Add(FieldLabel(labelKey), 0, row);
        var flow = NewFlow();
        var radios = RadioRow(flow, keys, selected);
        _left.Controls.Add(flow, 1, row);
        _left.SetColumnSpan(flow, 3);
        return radios;
    }

    /// <summary>컨트롤 하나가 네 칸을 모두 차지하는 행.</summary>
    private void FullRow(Control c)
    {
        var row = NewRow();
        _left.Controls.Add(c, 0, row);
        _left.SetColumnSpan(c, 4);
    }

    /// <summary>입력 칸 제목 라벨. field 를 주면 그 칸이 꺼질 때 제목도 꺼진 색이 된다 (AppTheme.BindLabel).</summary>
    private Label FieldLabel(string key, Control? field = null)
    {
        var label = Localized(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 3) }, key);
        if (field is not null) AppTheme.BindLabel(label, field);
        return label;
    }

    private TextBox Field(string? tooltipKey = null)
    {
        var tb = new ThemedTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 6, 3) };
        tb.TextChanged += (_, _) => Rebuild();
        if (tooltipKey is not null) _helpTips.Add((tb, UiStrings.Text(tooltipKey)));
        return tb;
    }

    private ComboBox KeyedCombo(string[] keys, int selected)
    {
        var cb = new ThemedComboBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 6, 3), Tag = keys };
        foreach (var k in keys) cb.Items.Add(UiStrings.Get(k));
        cb.SelectedIndex = selected;
        return cb;
    }

    private RadioButton[] RadioRow(FlowLayoutPanel host, string[] keys, int selected)
    {
        var radios = new RadioButton[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var rb = Localized(new ThemedRadioButton { AutoSize = true, Margin = new Padding(0, 2, 14, 2), Checked = i == selected }, keys[i]);
            rb.CheckedChanged += (_, _) => { if (rb.Checked) Rebuild(); };
            host.Controls.Add(rb);
            radios[i] = rb;
        }
        return radios;
    }

    // 셀 폭에 맞춰 줄바꿈한다. 왼쪽 테이블의 폭은 스크롤바와 무관하게 고정하므로(FitWidth) 줄바꿈이 되먹임을 일으키지 않는다.
    private FlowLayoutPanel NewFlow() => TrackLayout(new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2) });

    // ---- 섹션 ----

    private void BuildConnectionSection()
    {
        SectionHeader("GroupConnection", first: true);

        _host = Field();
        _host.Text = "localhost";
        _port = Field();
        _port.Text = "3306";
        _port.Width = 80;
        _port.Anchor = AnchorStyles.Left;
        FieldRow("Host", _host, "Port", _port);

        // 소켓: localhost 일 때만 의미가 있어 그때만 켠다 (ApplyDependencyUi). mysqldump 경로처럼 한 줄 전체.
        _socket = Field("SocketHint");
        FieldRow("Socket", _socket);

        _user = Field();
        _client = KeyedCombo(ClientKeys, 1);
        _client.SelectedIndexChanged += (_, _) => Rebuild();
        FieldRow("User", _user, "Client", _client);

        _passwordMode = KeyedCombo(PasswordModeKeys, 0);
        _passwordMode.SelectedIndexChanged += (_, _) => { UpdatePasswordValueField(); Rebuild(); };
        _passwordValue = Field();
        var row = NewRow();
        _left.Controls.Add(FieldLabel("PasswordMode", _passwordMode), 0, row);
        _left.Controls.Add(_passwordMode, 1, row);
        _passwordValueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 3) };
        AppTheme.BindLabel(_passwordValueLabel, _passwordValue);
        _left.Controls.Add(_passwordValueLabel, 2, row);
        _left.Controls.Add(_passwordValue, 3, row);

        _binaryPath = Field();
        _binaryPath.Text = "mysqldump";
        FieldRow("BinaryPath", _binaryPath);
    }

    private void BuildTargetSection()
    {
        SectionHeader("GroupTarget");

        var flow = NewFlow();
        _targetRadios = RadioRow(flow, new[] { "TargetSingle", "TargetTables", "TargetMulti" }, 0);
        FullRow(flow);

        _databases = Field("DatabasesHint");
        FieldRow("Databases", _databases);

        _tables = Field("TablesHint");
        FieldRow("Tables", _tables);

        _where = Field();
        FieldRow("Where", _where);

        _ignoreTables = Field("IgnoreTablesHint");
        _ignoreTables.Multiline = true;
        _ignoreTables.ScrollBars = ScrollBars.Vertical;
        _ignoreTables.Height = 64;
        _ignoreTables.AcceptsReturn = true;
        FieldRow("IgnoreTables", _ignoreTables);
    }

    private void BuildOptionsSection()
    {
        SectionHeader("GroupOptions");

        _contentRadios = RadioFieldRow("ContentMode", new[] { "ContentBoth", "ContentStructure", "ContentData", "ContentSplit" }, 0);
        _insertRadios = RadioFieldRow("InsertMode", new[] { "InsertNormal", "InsertIgnore", "InsertReplace" }, 0);

        // 고급 옵션 토글
        var toggleFlow = NewFlow();
        _showAdvanced = Localized(new CheckBox { AutoSize = true, Margin = new Padding(0, 4, 20, 4) }, "ShowAdvanced");
        _showAdvanced.CheckedChanged += (_, _) => Rebuild();
        toggleFlow.Controls.Add(_showAdvanced);
        FullRow(toggleFlow);

        // 스키마 그룹별 체크박스: 그룹 제목(작은 굵은 라벨) 아래 옵션 한 줄씩
        foreach (var group in _schema.Groups.OrderBy(g => g.Order))
        {
            var options = _schema.OptionsInGroup(group.Id).ToList();
            if (options.Count == 0) continue;

            var title = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2), Tag = AppTheme.TagSub };
            _localized.Add((title, () => group.Label.Get(UiStrings.Current)));
            FullRow(title);

            foreach (var o in options)
            {
                var row = NewRow();
                var cb = new CheckBox { AutoSize = true, Margin = new Padding(12, 2, 8, 2), Tag = o.Id, ForeColor = RiskColor(o.Risk) };
                _localized.Add((cb, () => OptionLabel(o)));
                _helpTips.Add((cb, o.Help));
                cb.CheckedChanged += (_, _) => Rebuild();
                _optionChecks[o.Id] = cb;

                if (o.IsValueType)
                {
                    // 값 입력란은 체크박스 바로 오른쪽에 일정한 간격으로 붙인다 (오른쪽 끝에 떨어져 있으면 어느 옵션의 값인지 보기 어렵다).
                    var tb = new ThemedTextBox { Width = 110, Margin = new Padding(0, 0, 6, 0), Tag = o.Id, PlaceholderText = o.ValuePlaceholder ?? "", Text = o.DefaultValue ?? "" };
                    tb.TextChanged += (_, _) => Rebuild();
                    _optionValues[o.Id] = tb;
                    var pair = TrackLayout(new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty });
                    pair.Controls.Add(cb);
                    pair.Controls.Add(tb);
                    _left.Controls.Add(pair, 0, row);
                    _left.SetColumnSpan(pair, 4);
                }
                else
                {
                    _left.Controls.Add(cb, 0, row);
                    _left.SetColumnSpan(cb, 3);
                }
            }
        }
    }

    /// <summary>체크박스 문구: "설명  (--flag)" — 값을 받는 옵션은 "(--flag=…)".</summary>
    private static string OptionLabel(OptionDefinition o)
        => o.Label.Get(UiStrings.Current) + "  (" + o.Flag + (o.IsValueType ? "=…" : "") + ")";


    private static Color RiskColor(string risk) => risk.ToLowerInvariant() switch
    {
        "critical" => AppTheme.Current.Error,
        "high" => AppTheme.Current.Error,
        "medium" => AppTheme.Current.Caution,
        _ => AppTheme.Current.Text
    };

    /// <summary>출력 방식 라디오 버튼의 화면 순서. 기본값인 "> 파일"을 맨 왼쪽에 둔다 (enum 순서와 다르다).</summary>
    private static readonly OutputMode[] OutputOrder = { OutputMode.StdoutRedirect, OutputMode.ResultFile, OutputMode.GzipPipe };
    private static readonly string[] OutputKeys = { "OutRedirect", "OutResultFile", "OutGzip" };

    private RadioButton OutputRadio(OutputMode mode) => _outputRadios[Array.IndexOf(OutputOrder, mode)];

    private void BuildOutputSection()
    {
        SectionHeader("GroupOutput");

        var flow = NewFlow();
        _outputRadios = RadioRow(flow, OutputKeys, Array.IndexOf(OutputOrder, OutputMode.StdoutRedirect));
        FullRow(flow);

        _fileTemplate = Field("FileTemplateHint");
        _fileTemplate.Text = "{db}_{date:yyyyMMdd}_{time:HHmm}.sql";
        FieldRow("FileTemplate", _fileTemplate);
        _fileNamePreview = PreviewRow();

        _scriptTemplate = Field("ScriptTemplateHint");
        _scriptTemplate.Text = ScriptExporter.DefaultScriptFileTemplate;
        FieldRow("ScriptTemplate", _scriptTemplate);
        _scriptNamePreview = PreviewRow();
    }

    /// <summary>Import 대상 섹션: Export 접속 정보와 같은 구조 + 대상 DB/문자셋 한 줄 + 체크박스. 작업이 Export 뿐이면 ApplyDependencyUi 가 통째로 숨긴다.</summary>
    private void BuildImportSection()
    {
        var before = _left.Controls.Count;
        SectionHeader("GroupImport");

        _importSame = Localized(new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(0, 4, 0, 4) }, "ImportSameConnection");
        _importSame.CheckedChanged += (_, _) => Rebuild();
        FullRow(_importSame);

        _importHost = Field();
        _importPort = Field();
        _importPort.Width = 80;
        FieldRow("Host", _importHost, "Port", _importPort);

        _importSocket = Field("SocketHint");
        FieldRow("Socket", _importSocket);

        _importUser = Field();
        FieldRow("User", _importUser);

        _importPasswordMode = KeyedCombo(PasswordModeKeys, 0);
        _importPasswordMode.SelectedIndexChanged += (_, _) => { UpdateImportPasswordValueField(); Rebuild(); };
        _importPasswordValue = Field();
        var pwRow = NewRow();
        _left.Controls.Add(FieldLabel("PasswordMode", _importPasswordMode), 0, pwRow);
        _left.Controls.Add(_importPasswordMode, 1, pwRow);
        _importPasswordValueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 3) };
        AppTheme.BindLabel(_importPasswordValueLabel, _importPasswordValue);
        _left.Controls.Add(_importPasswordValueLabel, 2, pwRow);
        _left.Controls.Add(_importPasswordValue, 3, pwRow);

        _importBinaryPath = Field();
        _importBinaryPath.Text = "mysql";
        FieldRow("ImportBinaryPath", _importBinaryPath);

        _importDatabase = Field("ImportDatabaseHint");
        _importCharset = Field();
        _importCharset.Text = "utf8mb4";
        FieldRow("ImportDatabase", _importDatabase, "ImportCharset", _importCharset);

        // 읽을 파일: 라벨 + 입력(두 칸) + 찾아보기. Import 만이면 꼭 적고, Export + Import 면 비워 두면 Export 파일을 읽는다 (ApplyDependencyUi).
        _importInputFile = Field("ImportInputFileHint");
        _importBrowse = Localized(new Button { AutoSize = true, Margin = new Padding(0, 2, 6, 2) }, "Browse");
        _importBrowse.Click += (_, _) => BrowseInputFile();
        var fileRow = NewRow();
        var fileLabel = FieldLabel("ImportInputFile", _importInputFile);
        _left.Controls.Add(fileLabel, 0, fileRow);
        _left.Controls.Add(_importInputFile, 1, fileRow);
        _left.SetColumnSpan(_importInputFile, 2);
        _left.Controls.Add(_importBrowse, 3, fileRow);
        _importInputRow.AddRange(new Control[] { fileLabel, _importInputFile, _importBrowse });

        var flags = NewFlow();
        CheckBox Flag(string key, string? hintKey)
        {
            var cb = Localized(new CheckBox { AutoSize = true, Margin = new Padding(0, 4, 20, 4) }, key);
            if (hintKey is not null) _helpTips.Add((cb, UiStrings.Text(hintKey)));
            cb.CheckedChanged += (_, _) => Rebuild();
            flags.Controls.Add(cb);
            return cb;
        }
        _importCreateDb = Flag("ImportCreateDb", "ImportCreateDbHint");
        _importForce = Flag("ImportForce", null);
        _importFkOff = Flag("ImportFkOff", "ImportFkOffHint");
        FullRow(flags);

        for (var i = before; i < _left.Controls.Count; i++) _importSection.Add(_left.Controls[i]);
    }

    /// <summary>읽을 파일 고르기. 스크립트는 서버에서 돌므로 이 PC 에 없는 경로를 적을 수도 있어 파일 존재는 확인하지 않는다.</summary>
    private void BrowseInputFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = UiStrings.Get("InputFileFilter"),
            CheckFileExists = false,
            Title = UiStrings.Get("ImportInputFile")
        };
        var current = _importInputFile.Text.Trim();
        if (current.Length > 0)
        {
            try
            {
                var dir = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
                dialog.FileName = Path.GetFileName(current);
            }
            catch (ArgumentException) { }
        }
        if (dialog.ShowDialog(this) == DialogResult.OK) _importInputFile.Text = dialog.FileName;
    }

    private void UpdateImportPasswordValueField()
    {
        var mode = (PasswordMode)Math.Max(0, _importPasswordMode.SelectedIndex);
        _importPasswordValueLabel.Text = mode switch
        {
            PasswordMode.LoginPath => UiStrings.Get("PwValueLoginPath"),
            PasswordMode.DefaultsExtraFile => UiStrings.Get("PwValueExtraFile"),
            PasswordMode.Plain => UiStrings.Get("PwValuePlain"),
            _ => ""
        };
    }

    /// <summary>템플릿 행 바로 아래의 "미리보기: 실제 파일명" 행. 템플릿만 봐서는 결과를 짐작하기 어려워서 둔다.</summary>
    private Label PreviewRow()
    {
        var row = NewRow();
        var caption = Localized(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 6), Tag = AppTheme.TagSub }, "FileNamePreview");
        var value = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 6), Tag = AppTheme.TagSub };
        _left.Controls.Add(caption, 0, row);
        _left.Controls.Add(value, 1, row);
        _left.SetColumnSpan(value, 3);
        return value;
    }

    private Control BuildRightPanel()
    {
        // 행: 툴바 / 제목 / 명령어 / 덤프 목록 제목+버튼 / 덤프 목록 / 경고 제목 / 경고 목록
        var t = TrackLayout(new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(8, 0, 0, 0) });
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 29));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 21));

        // 툴바
        // 오른쪽 툴바는 줄바꿈해도 안전하다 (스크롤바와 무관한 고정 폭 영역).
        var bar = TrackLayout(new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 4) });
        bar.Controls.Add(Localized(new Label { AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, "Shell"));
        _shell = KeyedCombo(ShellKeys, 0);
        _shell.Width = 180;
        _shell.Anchor = AnchorStyles.Left;
        _shell.Margin = new Padding(0, 4, 12, 0);
        _shell.SelectedIndexChanged += (_, _) => Rebuild();
        bar.Controls.Add(_shell);

        _multiLine = Localized(new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(0, 7, 12, 0) }, "MultiLine");
        _multiLine.CheckedChanged += (_, _) => Rebuild();
        bar.Controls.Add(_multiLine);

        _explicitDefaults = Localized(new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(0, 7, 12, 0) }, "ExplicitDefaults");
        _explicitDefaults.CheckedChanged += (_, _) => Rebuild();
        bar.Controls.Add(_explicitDefaults);

        _copy = Localized(new Button { AutoSize = true, Margin = new Padding(0, 2, 8, 0), Tag = AppTheme.TagPrimary }, "Copy");
        _copy.Click += (_, _) => CopyCommand();
        bar.Controls.Add(_copy);

        _scriptPreview = Localized(new CheckBox { AutoSize = true, Margin = new Padding(0, 7, 12, 0) }, "ScriptPreview");
        _helpTips.Add((_scriptPreview, UiStrings.Text("ScriptPreviewHint")));
        _scriptPreview.CheckedChanged += (_, _) => Rebuild();
        bar.Controls.Add(_scriptPreview);

        _exportScript = Localized(new Button { AutoSize = true, Margin = new Padding(0, 2, 8, 0) }, "ExportScript");
        _helpTips.Add((_exportScript, UiStrings.Text("ExportScriptHint")));
        _exportScript.Click += (_, _) => ExportScript();
        bar.Controls.Add(_exportScript);

        _status = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        bar.Controls.Add(_status);
        t.Controls.Add(bar, 0, 0);

        // 결과 창 제목: "옵션 미리보기" 또는 "스크립트 미리보기: 파일명" (Rebuild 에서 갱신)
        _previewTitle = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 2), Font = new Font(Font, FontStyle.Bold) };
        _localized.Add((_previewTitle, () => PreviewTitle(ReadRequest())));
        t.Controls.Add(_previewTitle, 0, 1);

        // 명령어 (읽기 전용 — 확정)
        _commandFont = new Font("Consolas", 10.5f);
        _hintFont = new Font(Font.FontFamily, 10.5f);
        _command = new ThemedTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = _commandFont,
            Margin = new Padding(0, 0, 0, 6)
        };
        t.Controls.Add(_command, 0, 2);

        t.Controls.Add(Localized(new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 2), Font = new Font(Font, FontStyle.Bold) }, "GroupMessages"), 0, 5);

        // 경고·설명
        // 번호는 메시지가 처음 나타난 순서(세션 안에서 계속 커짐)이고 큰 번호가 위로 온다: 방금 바뀐 설정 때문에 새로 생긴 줄이 맨 위에 보인다.
        _messages = new ListView { Name = "messages", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false };
        _messages.Columns.Add(UiStrings.Get("ColNo"), 44);
        _messages.Columns.Add(UiStrings.Get("ColSeverity"), 70);
        _messages.Columns.Add(UiStrings.Get("ColMessage"), 600);
        _messages.ShowItemToolTips = true;   // 긴 메시지는 마우스를 올리면 전체가 보인다
        _messages.MouseClick += (_, e) => { if (_messages.GetItemAt(e.X, e.Y) is { } hit) ShowMessageDetail(hit); };   // 한 번 클릭으로 팝업 (긴 글을 한눈에)
        _messages.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && _messages.SelectedItems.Count > 0) { e.Handled = true; ShowMessageDetail(_messages.SelectedItems[0]); } };
        t.Controls.Add(_messages, 0, 6);

        // 덤프 목록: 제목 + 버튼 한 줄, 그 아래 목록
        var batchBar = TrackLayout(new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 0), Padding = new Padding(0, 0, 0, 2) });
        batchBar.Controls.Add(Localized(new Label { AutoSize = true, Margin = new Padding(0, 6, 12, 0), Font = new Font(Font, FontStyle.Bold) }, "BatchTitle"));
        Button BatchButton(string key, Action onClick)
        {
            var b = Localized(new Button { AutoSize = true, Margin = new Padding(0, 0, 6, 0) }, key);
            b.Click += (_, _) => onClick();
            batchBar.Controls.Add(b);
            return b;
        }
        _batchAdd = BatchButton("BatchAdd", AddBatchItem);
        _batchAdd.Tag = AppTheme.TagPrimary;   // 왼쪽 설정이 목록으로 들어가는 입구라 강조색
        _helpTips.Add((_batchAdd, UiStrings.Text("BatchAddHint")));
        _batchUpdate = BatchButton("BatchUpdate", UpdateBatchItem);
        _helpTips.Add((_batchUpdate, UiStrings.Text("BatchUpdateHint")));
        BatchButton("BatchUp", () => MoveBatchItem(-1));
        BatchButton("BatchDown", () => MoveBatchItem(+1));
        BatchButton("BatchRemove", RemoveBatchItem);
        BatchButton("BatchClear", ClearBatch);
        t.Controls.Add(batchBar, 0, 3);

        _batchList = new ListView { Name = "batch", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false, ShowItemToolTips = true };
        _batchList.Columns.Add(UiStrings.Get("BatchColNo"), 36);
        _batchList.Columns.Add(UiStrings.Get("BatchColTarget"), 200);
        _batchList.Columns.Add(UiStrings.Get("BatchColScope"), 160);
        _batchList.Columns.Add(UiStrings.Get("BatchColFile"), 240);
        _batchList.DoubleClick += (_, _) => ShowBatchDetail();   // 상세 팝업 (팝업의 "왼쪽에 불러오기"로 편집)
        _batchList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && SelectedBatchIndex is not null) { e.Handled = true; ShowBatchDetail(); } };
        _batchList.SelectedIndexChanged += (_, _) => UpdateBatchButtons();
        AppTheme.StyleListView(_batchList);
        AppTheme.StyleListView(_messages);
        t.Controls.Add(_batchList, 0, 4);

        return t;
    }

    private T Localized<T>(T control, string key) where T : Control
    {
        _localized.Add((control, () => UiStrings.Get(key)));
        return control;
    }

    private T TrackLayout<T>(T control) where T : Control
    {
        _containers.Add(control);
        return control;
    }

    /// <summary>모든 컨테이너의 레이아웃을 멈춘 채 action 을 실행하고, 끝나면 안쪽부터 한 번씩만 다시 계산한다.</summary>
    private void WithLayoutSuspended(Action action)
    {
        SuspendLayout();
        foreach (var c in _containers) c.SuspendLayout();
        try
        {
            action();
        }
        finally
        {
            for (var i = _containers.Count - 1; i >= 0; i--) _containers[i].ResumeLayout(true);
            ResumeLayout(true);
        }
    }

    // ==================================================================
    // 언어
    // ==================================================================

    private void ApplyLanguage()
    {
        Text = UiStrings.Get("Title") + "  v" + (GetType().Assembly.GetName().Version?.ToString(3) ?? "");
        // WinForms 는 '&' 를 단축키 표시로 먹어 버리므로 '&&' 로 바꿔 글자 그대로 보이게 한다.
        foreach (var (control, text) in _localized) control.Text = text().Replace("&", "&&");
        foreach (var (control, help) in _helpTips) _tooltip.SetToolTip(control, help.Get(UiStrings.Current));

        RefillCombo(_passwordMode);
        RefillCombo(_client);
        RefillCombo(_shell);
        RefillCombo(_importPasswordMode);
        UpdatePasswordValueField();
        UpdateImportPasswordValueField();

        if (_messages.Columns.Count == 3)
        {
            _messages.Columns[0].Text = UiStrings.Get("ColNo");
            _messages.Columns[1].Text = UiStrings.Get("ColSeverity");
            _messages.Columns[2].Text = UiStrings.Get("ColMessage");
        }
        if (_batchList.Columns.Count == 4)
        {
            _batchList.Columns[0].Text = UiStrings.Get("BatchColNo");
            _batchList.Columns[1].Text = UiStrings.Get("BatchColTarget");
            _batchList.Columns[2].Text = UiStrings.Get("BatchColScope");
            _batchList.Columns[3].Text = UiStrings.Get("BatchColFile");
        }
        RefillPresetCombo();
        RefillBatchList(SelectedBatchIndex);
    }

    private static void RefillCombo(ComboBox cb)
    {
        if (cb.Tag is not string[] keys) return;
        var selected = cb.SelectedIndex;
        cb.Items.Clear();
        foreach (var k in keys) cb.Items.Add(UiStrings.Get(k));
        cb.SelectedIndex = Math.Max(0, selected);
    }

    private void UpdatePasswordValueField()
    {
        var mode = (PasswordMode)_passwordMode.SelectedIndex;
        var visible = mode != PasswordMode.Prompt;
        _passwordValueLabel.Visible = visible;
        _passwordValue.Visible = visible;
        _passwordValueLabel.Text = mode switch
        {
            PasswordMode.LoginPath => UiStrings.Get("PwValueLoginPath"),
            PasswordMode.DefaultsExtraFile => UiStrings.Get("PwValueExtraFile"),
            PasswordMode.Plain => UiStrings.Get("PwValuePlain"),
            _ => ""
        };
    }

    // ==================================================================
    // 상태 ↔ 컨트롤
    // ==================================================================

    private DumpRequest ReadRequest()
    {
        var req = new DumpRequest
        {
            BinaryPath = _binaryPath.Text,
            Host = _host.Text,
            Port = int.TryParse(_port.Text.Trim(), out var port) ? port : null,
            Socket = _socket.Text,
            User = _user.Text,
            PasswordMode = (PasswordMode)Math.Max(0, _passwordMode.SelectedIndex),
            Client = (ClientKind)Math.Max(0, _client.SelectedIndex),
            TargetMode = (TargetMode)SelectedIndex(_targetRadios),
            Databases = SplitList(_databases.Text),
            Tables = SplitList(_tables.Text),
            IgnoreTables = SplitList(_ignoreTables.Text),
            Where = _where.Text,
            ContentMode = (ContentMode)SelectedIndex(_contentRadios),
            InsertMode = (InsertMode)SelectedIndex(_insertRadios),
            OutputMode = OutputOrder[SelectedIndex(_outputRadios)],
            OutputFileTemplate = _fileTemplate.Text,
            ScriptFileTemplate = _scriptTemplate.Text,
            Shell = (ShellKind)Math.Max(0, _shell.SelectedIndex),
            MultiLine = _multiLine.Checked,
            ExplicitDefaults = _explicitDefaults.Checked,
            Now = DateTime.Now,
            Job = (JobKind)SelectedIndex(_jobRadios),
            Import = new ImportTarget
            {
                SameConnectionAsSource = _importSame.Checked,
                Host = _importHost.Text,
                Port = int.TryParse(_importPort.Text.Trim(), out var importPort) ? importPort : null,
                Socket = _importSocket.Text,
                User = _importUser.Text,
                PasswordMode = (PasswordMode)Math.Max(0, _importPasswordMode.SelectedIndex),
                Database = _importDatabase.Text,
                InputFile = _importInputFile.Text,
                BinaryPath = _importBinaryPath.Text,
                Charset = _importCharset.Text,
                CreateDatabase = _importCreateDb.Checked,
                Force = _importForce.Checked,
                DisableForeignKeyChecks = _importFkOff.Checked
            }
        };

        switch (req.Import.PasswordMode)
        {
            case PasswordMode.LoginPath: req.Import.LoginPath = _importPasswordValue.Text; break;
            case PasswordMode.DefaultsExtraFile: req.Import.DefaultsExtraFile = _importPasswordValue.Text; break;
            case PasswordMode.Plain: req.Import.PlainPassword = _importPasswordValue.Text; break;
        }

        switch (req.PasswordMode)
        {
            case PasswordMode.LoginPath: req.LoginPath = _passwordValue.Text; break;
            case PasswordMode.DefaultsExtraFile: req.DefaultsExtraFile = _passwordValue.Text; break;
            case PasswordMode.Plain: req.PlainPassword = _passwordValue.Text; break;
        }

        foreach (var (id, cb) in _optionChecks) req.Flags[id] = cb.Checked;
        foreach (var (id, tb) in _optionValues) req.Values[id] = tb.Text;
        return req;
    }

    private void LoadRequest(DumpRequest req)
    {
        _suspendRebuild = true;
        try
        {
            WithLayoutSuspended(() => LoadRequestCore(req));
        }
        finally
        {
            _suspendRebuild = false;
        }
        Rebuild();
    }

    private void LoadRequestCore(DumpRequest req)
    {
        {
            _binaryPath.Text = req.BinaryPath;
            _host.Text = req.Host;
            _port.Text = req.Port?.ToString() ?? "";
            _socket.Text = req.Socket;
            _user.Text = req.User;
            _passwordMode.SelectedIndex = (int)req.PasswordMode;
            _passwordValue.Text = req.PasswordMode switch
            {
                PasswordMode.LoginPath => req.LoginPath,
                PasswordMode.DefaultsExtraFile => req.DefaultsExtraFile,
                _ => ""
            };
            _client.SelectedIndex = (int)req.Client;

            _targetRadios[(int)req.TargetMode].Checked = true;
            _databases.Text = string.Join(" ", req.Databases);
            _tables.Text = string.Join(" ", req.Tables);
            _ignoreTables.Text = string.Join(Environment.NewLine, req.IgnoreTables);
            _where.Text = req.Where;

            _contentRadios[(int)req.ContentMode].Checked = true;
            _insertRadios[(int)req.InsertMode].Checked = true;

            foreach (var (id, cb) in _optionChecks) cb.Checked = req.IsChecked(id);
            foreach (var (id, tb) in _optionValues) tb.Text = req.ValueOf(id) ?? _schema.Find(id)?.DefaultValue ?? "";

            OutputRadio(req.OutputMode).Checked = true;
            _fileTemplate.Text = req.OutputFileTemplate;
            _scriptTemplate.Text = req.ScriptFileTemplate;
            _jobRadios[(int)req.Job].Checked = true;

            var import = req.Import;
            _importSame.Checked = import.SameConnectionAsSource;
            _importHost.Text = import.Host;
            _importPort.Text = import.Port?.ToString() ?? "";
            _importSocket.Text = import.Socket;
            _importUser.Text = import.User;
            _importPasswordMode.SelectedIndex = (int)import.PasswordMode;
            _importPasswordValue.Text = import.PasswordMode switch
            {
                PasswordMode.LoginPath => import.LoginPath,
                PasswordMode.DefaultsExtraFile => import.DefaultsExtraFile,
                _ => ""
            };
            _importDatabase.Text = import.Database;
            _lastMirroredDbText = string.Join(", ", ImportCommandBuilder.SplitNames(_databases.Text));   // 불러온 값은 덮어쓰지 않는다
            _importInputFile.Text = import.InputFile;
            _lastJob = req.Job;   // 불러온 작업 종류에서는 읽을 파일을 자동으로 채우지 않는다
            _importBinaryPath.Text = import.BinaryPath;
            _importCharset.Text = import.Charset;
            _importCreateDb.Checked = import.CreateDatabase;
            _importForce.Checked = import.Force;
            _importFkOff.Checked = import.DisableForeignKeyChecks;
            UpdateImportPasswordValueField();

            _shell.SelectedIndex = (int)req.Shell;
            _multiLine.Checked = req.MultiLine;
            _explicitDefaults.Checked = req.ExplicitDefaults;
            UpdatePasswordValueField();
        }
    }

    private static int SelectedIndex(RadioButton[] radios)
    {
        for (var i = 0; i < radios.Length; i++) if (radios[i].Checked) return i;
        return 0;
    }

    private static List<string> SplitList(string text)
        => text.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ==================================================================
    // 빌드 (핵심 흐름)
    // ==================================================================

    private void Rebuild()
    {
        if (_suspendRebuild) return;

        var req = ReadRequest();
        ApplyDependencyUi(req);
        req = ReadRequest(); // 의존 규칙으로 바뀐 체크 상태를 다시 읽는다

        var output = BuildOutput(req);

        _command.Text = output.Text;
        _command.ForeColor = output.HasErrors ? AppTheme.Current.SubText : AppTheme.Current.Text;
        // 오류 안내는 한글이라 Consolas 로 그리면 대체 글꼴이 작게 나온다 → 화면 글꼴을 같은 크기로 쓴다.
        _command.Font = output.HasErrors ? _hintFont : _commandFont;
        _previewTitle.Text = PreviewTitle(req);
        _currentKey = Preset.FromRequest("", req).SettingsKey();
        _currentHasErrors = output.Form.HasErrors;
        UpdateBatchButtons();
        _fileNamePreview.Text = req.Job == JobKind.ImportOnly ? CommandBuilder.ExpandOutputFileName(req) : JobBuilder.FilePreview(req);   // 파일 분리면 _ddl, _data 두 이름
        _scriptNamePreview.Text = ScriptExporter.ScriptFileName(req, req.Shell);
        _status.Text = "";

        var importPrefix = UiStrings.Get("ImportMsgPrefix");
        var current = new List<(BuildMessage Message, string Prefix)>();
        foreach (var m in output.Form.ExportMessages) current.Add((m, ""));
        foreach (var m in output.Form.ImportMessages) current.Add((m, importPrefix));
        // Export 목록 항목의 오류·경고는 "[목록 N]" 을 앞에 붙여 보여 준다 (정보는 생략).
        for (var i = 0; i < output.Batch.Count; i++)
        {
            var prefix = string.Format(UiStrings.Get("BatchItemPrefix"), i + 1);
            foreach (var m in output.Batch[i].ExportMessages.Where(m => m.Severity != Severity.Info)) current.Add((m, prefix));
            foreach (var m in output.Batch[i].ImportMessages.Where(m => m.Severity != Severity.Info)) current.Add((m, prefix + importPrefix));
        }
        ShowMessages(current);
    }

    // 메시지 번호: 처음 나타날 때 번호를 받고, 사라졌다가 다시 나오면 새 번호를 받는다 (규칙 코드·옵션·접두사로 구분, 언어와 무관).
    private readonly Dictionary<string, int> _messageSeq = new();
    private int _messageCounter;

    private void ShowMessages(List<(BuildMessage Message, string Prefix)> current)
    {
        var keys = new HashSet<string>();
        var rows = new List<(int Seq, BuildMessage Message, string Prefix)>();
        foreach (var (m, prefix) in current)
        {
            var key = prefix + "|" + m.Code + "|" + m.OptionId;
            if (!keys.Add(key)) continue;
            if (!_messageSeq.TryGetValue(key, out var seq)) _messageSeq[key] = seq = ++_messageCounter;
            rows.Add((seq, m, prefix));
        }
        foreach (var gone in _messageSeq.Keys.Where(k => !keys.Contains(k)).ToList()) _messageSeq.Remove(gone);

        _messages.BeginUpdate();
        _messages.Items.Clear();
        foreach (var (seq, m, prefix) in rows.OrderByDescending(r => r.Seq))
        {
            var text = prefix + m.Text.Get(UiStrings.Current);
            var item = new ListViewItem(seq.ToString()) { Tag = prefix.Length == 0 ? m.OptionId : null, ForeColor = SeverityColor(m.Severity), ToolTipText = text };
            item.SubItems.Add(SeverityLabel(m.Severity));
            item.SubItems.Add(text);
            _messages.Items.Add(item);
        }
        _messages.EndUpdate();
    }

    /// <summary>경고·설명 한 줄을 팝업으로 크게 보여 준다. 옵션과 연결된 줄이면 팝업에서 그 옵션으로 갈 수 있다.</summary>
    private void ShowMessageDetail(ListViewItem item)
    {
        var canGo = item.Tag is string id && _optionChecks.TryGetValue(id, out var cb) && cb.Visible;
        var head = "#" + item.Text + "  " + item.SubItems[1].Text;
        using var dialog = new MessageDialog(Font, UiStrings.Get("MessageDetailTitle"), head, item.ForeColor, item.SubItems[2].Text, canGo ? UiStrings.Get("GoToOption") : null);
        dialog.ShowDialog(this);
        if (dialog.ActionRequested) FocusMessageOption();
    }

    /// <summary>스크립트 목록 한 항목의 명령어 전체를 팝업으로 보여 준다. "왼쪽에 불러오기"로 그 항목을 편집할 수 있다.</summary>
    private void ShowBatchDetail()
    {
        if (SelectedBatchIndex is not int i) return;
        var row = _batchList.Items[i];
        var req = BatchRequests(ReadRequest())[i];
        var job = _builder.Build(req);
        var head = "#" + (i + 1) + "  " + row.SubItems[1].Text + "  ·  " + row.SubItems[2].Text + "  ·  " + row.SubItems[3].Text;
        var body = job.HasErrors
            ? string.Join(Environment.NewLine, job.ExportMessages.Concat(job.ImportMessages).Where(m => m.Severity == Severity.Error).Select(m => m.Text.Get(UiStrings.Current)))
            : ScriptExporter.CommandPreview(new[] { job }, req.Shell).Replace("\n", Environment.NewLine);
        using var dialog = new MessageDialog(Font, UiStrings.Get("BatchDetailTitle"), head, job.HasErrors ? AppTheme.Current.Error : AppTheme.Current.Header, body, UiStrings.Get("BatchLoad"), monospace: true);
        dialog.ShowDialog(this);
        if (dialog.ActionRequested) LoadBatchItem();
    }

    /// <summary>결과 창에 보여 줄 것. Export 목록이 비어 있으면 현재 화면 하나, 아니면 목록 전체.</summary>
    private sealed record OutputState(string Text, bool HasErrors, JobResult Form, List<DumpRequest> BatchRequests, List<JobResult> Batch);

    private OutputState BuildOutput(DumpRequest req)
    {
        var form = _builder.Build(req);
        var batchReqs = BatchRequests(req);
        var batch = batchReqs.Select(_builder.Build).ToList();

        // 목록이 비었거나, 왼쪽 설정이 목록의 어느 항목과도 다르면(= 아직 추가하지 않은 새 설정) 왼쪽 설정을 보여 준다.
        // 목록의 한 항목과 같을 때만 목록 전체 스크립트를 보여 준다.
        if (batch.Count == 0 || IsNewSettings(req))
        {
            var text = form.HasErrors ? NoCommandText(form) : CurrentOutputText(req, new[] { req }, new[] { form });
            return new OutputState(text, form.HasErrors, form, batchReqs, batch);
        }

        var firstError = batch.FindIndex(b => b.HasErrors);
        string batchText;
        if (firstError >= 0)
        {
            // 어느 항목의 무슨 오류인지 바로 보이게 첫 오류 문구를 같이 적는다.
            var job = batch[firstError];
            var message = job.ExportMessages.Concat(job.ImportMessages).First(m => m.Severity == Severity.Error).Text.Get(UiStrings.Current);
            batchText = string.Format(UiStrings.Get("BatchErrors"), firstError + 1, message).Replace("\n", Environment.NewLine);
        }
        else
        {
            batchText = CurrentOutputText(req, batchReqs, batch);
        }
        return new OutputState(batchText, firstError >= 0, form, batchReqs, batch);
    }

    /// <summary>오류가 있을 때 결과 창 문구: 안내 한 줄 뒤에 어떤 옵션·입력의 무슨 오류인지 한 줄씩 적는다 (Import 쪽은 접두사를 붙인다).</summary>
    private string NoCommandText(JobResult job)
    {
        var importPrefix = UiStrings.Get("ImportMsgPrefix");
        var lines = job.ExportMessages.Where(m => m.Severity == Severity.Error).Select(m => m.Text.Get(UiStrings.Current))
            .Concat(job.ImportMessages.Where(m => m.Severity == Severity.Error).Select(m => importPrefix + m.Text.Get(UiStrings.Current)))
            .Distinct()
            .Select(t => "- " + t);
        return UiStrings.Get("NoCommand") + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 스키마의 implies / conflicts / requiresMode / clients 를 화면에 반영한다:
    /// 지원하지 않는 클라이언트 → 숨김, 모드가 안 맞음 → 비활성, implies → 강제 체크 후 비활성, conflicts → 상대 비활성.
    /// </summary>
    private void ApplyDependencyUi(DumpRequest req)
    {
        _suspendRebuild = true;
        try
        {
            var modeName = req.TargetMode.ToString();
            var ddlOnly = req.ContentMode == ContentMode.StructureOnly;

            // 표시/숨김은 레이아웃을 다시 계산하게 하므로, 실제로 바뀌는 것만 레이아웃을 멈춘 채 바꾼다.
            var visibilityChanges = new List<(Control Control, bool Visible)>();
            foreach (var o in _schema.Options)
            {
                var cb = _optionChecks[o.Id];
                var supported = o.SupportsClient(req.Client);
                var visible = supported && (!o.IsAdvanced || _showAdvanced.Checked);
                if (cb.Visible != visible) visibilityChanges.Add((cb, visible));
                if (_optionValues.TryGetValue(o.Id, out var tb) && tb.Visible != visible) visibilityChanges.Add((tb, visible));
                if (!supported) cb.Checked = false;

                var modeOk = o.RequiresMode.Count == 0 || o.RequiresMode.Contains(modeName, StringComparer.OrdinalIgnoreCase);
                if (!modeOk) cb.Checked = false;
                // DDL 만 내보낼 때는 데이터(INSERT) 전용 옵션을 잠근다 (체크 상태는 두되 명령어에는 들어가지 않는다).
                cb.Enabled = modeOk && !(ddlOnly && o.DataOnly);
                if (_optionValues.TryGetValue(o.Id, out var valueBox)) valueBox.Enabled = cb.Enabled;
            }
            foreach (var r in _insertRadios) r.Enabled = !ddlOnly;

            if (visibilityChanges.Count > 0)
                WithLayoutSuspended(() => { foreach (var (control, visible) in visibilityChanges) control.Visible = visible; });

            // R8: 데이터만 덤프하면 DROP TABLE 옵션은 무의미
            if (req.ContentMode == ContentMode.DataOnly && _optionChecks.TryGetValue("skip_add_drop_table", out var dropCb))
                dropCb.Enabled = false;

            foreach (var o in _schema.Options)
            {
                var cb = _optionChecks[o.Id];
                if (!cb.Checked) continue;

                foreach (var (targetId, value) in o.Implies)
                {
                    if (!_optionChecks.TryGetValue(targetId, out var target)) continue;
                    target.Checked = value;
                    target.Enabled = false;
                }
                foreach (var otherId in o.Conflicts)
                {
                    if (!_optionChecks.TryGetValue(otherId, out var other)) continue;
                    if (!other.Checked) other.Enabled = false;
                }
            }

            // 대상 모드에 따른 입력란 활성화
            var tablesMode = req.TargetMode == TargetMode.Tables;
            _tables.Enabled = tablesMode;
            _where.Enabled = tablesMode && !ddlOnly;   // 행 조건은 데이터가 있을 때만
            _ignoreTables.Enabled = !tablesMode;   // 테이블을 직접 고르는 모드에서는 제외 목록이 의미 없다

            // gzip 은 bash 전용
            OutputRadio(OutputMode.GzipPipe).Enabled = req.Shell == ShellKind.Bash;

            // Import 대상 섹션: 작업이 Export 뿐이면 숨기고, "원본과 같음"이면 접속 칸을 잠근다.
            // Visible 속성은 폼이 뜨기 전에는 부모 때문에 항상 false 라 비교에 쓸 수 없다. 마지막으로 적용한 값을 따로 기억한다.
            var showImport = req.Job != JobKind.ExportOnly;
            if (_importShown != showImport)
            {
                WithLayoutSuspended(() => { foreach (var c in _importSection) c.Visible = showImport; });
                _importShown = showImport;
                _importPwShown = null;   // 섹션을 통째로 켜고 끈 뒤에는 패스워드 값 칸과 읽을 파일 행 표시를 다시 계산한다
                _importInputShown = null;
            }
            // 읽을 파일 행은 Import 가 있으면 늘 보인다. Import 만이면 파일명 템플릿 칸을 잠근다 (Export 가 없으니 쓰지 않는다).
            // Export + Import 에서는 비워 두면 Export 가 만드는 파일을 읽고, 그 이름을 흐린 글씨로 보여 준다.
            // 따로 적으면 그 파일을 읽는다 (Export 는 됐고 Import 만 다시 할 때, 시간이 지나 템플릿 이름이 바뀌는 문제를 피하려고).
            var importOnly = req.Job == JobKind.ImportOnly;
            var showInput = showImport;
            if (_importInputShown != showInput)
            {
                WithLayoutSuspended(() => { foreach (var c in _importInputRow) c.Visible = showInput; });
                _importInputShown = showInput;
            }
            _fileTemplate.Enabled = !importOnly;
            var exportFile = ExportFileText(req);
            // DDL + DML 파일 분리는 파일이 둘이라 읽을 파일 하나로 대신할 수 없다.
            var inputEditable = importOnly || !JobBuilder.SplitsContent(req);
            _importInputFile.Enabled = inputEditable;
            _importBrowse.Enabled = inputEditable;
            _importInputFile.PlaceholderText = importOnly ? "" : string.Format(UiStrings.Get("ImportInputFollowsExport"), exportFile);
            // Export + Import → Import 로 바꿀 때 칸이 비어 있으면 Export 파일 이름을 넣어 둔다 (시각만 고쳐 쓰면 되게).
            if (importOnly && _lastJob == JobKind.ExportAndImport && _importInputFile.Text.Trim().Length == 0)
                _importInputFile.Text = exportFile;
            _lastJob = req.Job;
            var ownConnection = !_importSame.Checked;
            foreach (var c in new Control[] { _importHost, _importPort, _importUser, _importPasswordMode, _importPasswordValue })
                c.Enabled = ownConnection;
            // 소켓 칸은 소켓으로 접속하는 호스트(localhost 또는 빈 값)일 때만 켠다. 값은 지우지 않는다 (호스트를 되돌리면 다시 쓰인다).
            _socket.Enabled = CommandBuilder.IsSocketHost(_host.Text);
            _importSocket.Enabled = ownConnection && CommandBuilder.IsSocketHost(_importHost.Text);
            if (!ownConnection)
            {
                // "원본과 같음": 접속 칸을 원본 값으로 되돌려 보여 준다 (값을 고쳤더라도 체크하면 원본으로 롤백, 원본이 바뀌면 따라간다).
                _importHost.Text = _host.Text;
                _importPort.Text = _port.Text;
                _importSocket.Text = _socket.Text;
                _importUser.Text = _user.Text;
                _importPasswordMode.SelectedIndex = _passwordMode.SelectedIndex;
                _importPasswordValue.Text = _passwordValue.Text;
                UpdateImportPasswordValueField();
            }
            var pwVisible = showImport && (PasswordMode)Math.Max(0, _importPasswordMode.SelectedIndex) != PasswordMode.Prompt;
            if (_importPwShown != pwVisible)
            {
                WithLayoutSuspended(() => { _importPasswordValue.Visible = pwVisible; _importPasswordValueLabel.Visible = pwVisible; });
                _importPwShown = pwVisible;
            }
            // 대상 DB 는 원본 DB 를 따라간다 (여러 DB 면 쉼표 목록). 사용자가 다르게 고친 뒤에는 그대로 두고, 원본이 바뀌면 다시 따라간다.
            MirrorTargetDatabase();
        }
        finally
        {
            _suspendRebuild = false;
        }
    }

    private static string SeverityLabel(Severity s) => s switch
    {
        Severity.Error => UiStrings.Get("SevError"),
        Severity.Warning => UiStrings.Get("SevWarning"),
        _ => UiStrings.Get("SevInfo")
    };

    private static Color SeverityColor(Severity s) => s switch
    {
        Severity.Error => AppTheme.Current.Error,
        Severity.Warning => AppTheme.Current.Caution,
        _ => AppTheme.Current.Text
    };

    private void FocusMessageOption()
    {
        if (_messages.SelectedItems.Count == 0) return;
        if (_messages.SelectedItems[0].Tag is string id && _optionChecks.TryGetValue(id, out var cb) && cb.Visible)
        {
            cb.Focus();
            if (_split.Panel1.Controls[0] is Panel scroll) scroll.ScrollControlIntoView(cb);
        }
    }

    /// <summary>결과 창에 보여 줄 텍스트: 보통은 명령어(세트가 여럿이면 "# ---- 1/N: 원본 -> 대상 ----" 머리줄로 구분), "스크립트 미리보기"가 켜져 있으면 내보낼 스크립트 전체.</summary>
    private string CurrentOutputText(DumpRequest current, IReadOnlyList<DumpRequest> reqs, IReadOnlyList<JobResult> results)
    {
        if (_scriptPreview.Checked)
        {
            var script = new ScriptExporter(_schema).ExportMany(reqs, current.Shell);
            if (!script.HasErrors) return script.Content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }
        return ScriptExporter.CommandPreview(results, current.Shell).Replace("\n", Environment.NewLine);
    }

    /// <summary>결과 창 제목: "옵션 미리보기" / "덤프 목록 미리보기 (N건)" / 스크립트 미리보기가 켜져 있으면 "스크립트 미리보기: 저장될 스크립트 파일명".</summary>
    /// <summary>왼쪽 설정이 목록의 어느 항목과도 같지 않은가 (목록이 있을 때만 의미 있음).</summary>
    private bool IsNewSettings(DumpRequest req)
    {
        if (_batch.Count == 0) return false;
        var key = Preset.FromRequest("", req).SettingsKey();
        return _batch.All(p => p.SettingsKey() != key);
    }

    private string PreviewTitle(DumpRequest req)
    {
        var isNew = IsNewSettings(req);
        var n = isNew ? 0 : _batch.Count;
        if (!_scriptPreview.Checked)
            return n == 0 ? UiStrings.Get("PreviewTitle") : string.Format(UiStrings.Get("BatchPreviewTitle"), n);

        var nameReq = n == 0 ? req : BatchRequests(req)[0];
        var name = ScriptExporter.ScriptFileName(nameReq, req.Shell, n > 1 ? "batch" : null);
        return UiStrings.Get("ScriptPreviewTitle") + " " + name + (n == 0 ? "" : string.Format(UiStrings.Get("BatchCount"), n));
    }

    private void CopyCommand()
    {
        var output = BuildOutput(ReadRequest());
        if (output.HasErrors || string.IsNullOrWhiteSpace(output.Text))
        {
            _status.ForeColor = AppTheme.Current.Error;
            _status.Text = UiStrings.Get("NothingToCopy");
            return;
        }
        Clipboard.SetText(output.Text);
        _status.ForeColor = AppTheme.Current.Success;
        _status.Text = UiStrings.Get("Copied");
    }

    /// <summary>현재 대상 OS에 맞는 스크립트 파일(.sh / .bat / .ps1)로 저장한다. 덤프 목록이 있으면 목록 전체를 한 파일에 담는다.</summary>
    private void ExportScript()
    {
        var req = ReadRequest();
        var reqs = BatchRequests(req);
        if (reqs.Count == 0) reqs.Add(req);
        var script = new ScriptExporter(_schema).ExportMany(reqs, req.Shell);
        if (script.HasErrors)
        {
            _status.ForeColor = AppTheme.Current.Error;
            _status.Text = UiStrings.Get("NothingToCopy");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            FileName = script.SuggestedFileName,
            DefaultExt = script.Extension.TrimStart('.'),
            Filter = UiStrings.Get(req.Shell switch { ShellKind.Bash => "ScriptFilterSh", ShellKind.Cmd => "ScriptFilterBat", _ => "ScriptFilterPs1" }),
            AddExtension = true,
            OverwritePrompt = true,
            Title = UiStrings.Get("ExportScript").TrimEnd('…')
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            script.SaveTo(dialog.FileName);
            _status.ForeColor = AppTheme.Current.Success;
            _status.Text = string.Format(UiStrings.Get(req.Shell == ShellKind.Bash ? "ScriptSavedChmod" : "ScriptSaved"), dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==================================================================
    // 덤프 목록
    // ==================================================================

    /// <summary>목록 항목들을 요청으로 바꾼다. 접속 경로·대상 OS·표시 방식은 현재 화면 것을 따른다 (한 스크립트는 한 OS용이므로).</summary>
    private List<DumpRequest> BatchRequests(DumpRequest current)
    {
        var list = new List<DumpRequest>();
        foreach (var item in _batch)
        {
            var req = new DumpRequest();
            req.ApplySchemaDefaults(_schema);
            item.ApplyTo(req, _schema);
            req.BinaryPath = current.BinaryPath;
            req.Shell = current.Shell;
            req.MultiLine = current.MultiLine;
            req.ExplicitDefaults = current.ExplicitDefaults;
            req.Now = current.Now;
            list.Add(req);
        }
        return list;
    }

    private int? SelectedBatchIndex => _batchList.SelectedIndices.Count > 0 ? _batchList.SelectedIndices[0] : null;

    private static string BatchTarget(DumpRequest r)
    {
        // 공백 없이: "shop", "a,b", "shop[t1,t2]" (테이블이 비어 있으면 대괄호도 생략)
        var dbs = string.Join(",", r.Databases);
        var tables = string.Join(",", r.Tables);
        return r.TargetMode == TargetMode.Tables && tables.Length > 0 ? dbs + "[" + tables + "]" : dbs;
    }

    /// <summary>마지막으로 화면에 적용한 작업 종류 (Export + Import → Import 전환을 알아채려고).</summary>
    private JobKind? _lastJob;

    /// <summary>Export 가 만드는 파일 이름. 여러 DB 면 {db} 를 남겨 한 줄로 (읽을 파일 칸에 그대로 쓸 수 있는 모양).</summary>
    private static string ExportFileText(DumpRequest req)
        => ImportCommandBuilder.ExportFileName(req, keepDbToken: req.TargetMode == TargetMode.MultipleDatabases);

    // 대상 DB 자동 채움: 마지막으로 채워 넣은 원본 DB 문구. 입력란이 이 값(또는 빈 값)이면 원본을 따라가고, 다르면 사용자가 고친 것이다.
    private string _lastMirroredDbText = "";

    private void MirrorTargetDatabase()
    {
        var sourceText = string.Join(", ", ImportCommandBuilder.SplitNames(_databases.Text));
        if (sourceText == _lastMirroredDbText) return;
        var current = _importDatabase.Text.Trim();
        if (current.Length == 0 || current == _lastMirroredDbText) _importDatabase.Text = sourceText;
        _lastMirroredDbText = sourceText;
    }

    /// <summary>목록 한 줄 요약: "shop@src→shop@dst" (작업 종류에 따라 한쪽만, 공백 없음).</summary>
    private static string BatchDescription(DumpRequest r)
    {
        var parts = new List<string>();
        if (r.Job != JobKind.ImportOnly) parts.Add(BatchTarget(r) + "@" + r.Host + (r.Port is int p ? ":" + p : ""));
        if (r.Job != JobKind.ExportOnly) parts.Add(ImportCommandBuilder.Describe(r));
        return string.Join("→", parts);
    }

    /// <summary>목록 "조건" 열: 옵션 플래그 없이 DDL / DML / DDL + DML 만.</summary>
    private static string ScopeShort(ContentMode mode) => mode switch
    {
        ContentMode.StructureOnly => "DDL",
        ContentMode.DataOnly => "DML",
        ContentMode.StructureAndDataSplit => "DDL / DML",
        _ => "DDL + DML"
    };

    /// <summary>"추가"는 화면 설정이 오류 없고 목록에 같은 항목이 없을 때만, "덮어쓰기"는 고른 항목과 다를 때만 켠다.</summary>
    private void UpdateBatchButtons()
    {
        if (_batchAdd is null || _batchUpdate is null) return;
        var duplicate = _batch.Any(p => p.SettingsKey() == _currentKey);
        _batchAdd.Enabled = !_currentHasErrors && !duplicate;
        var selected = SelectedBatchIndex;
        _batchUpdate.Enabled = !_currentHasErrors && selected is int i && _batch[i].SettingsKey() != _currentKey;
    }

    private void RefillBatchList(int? select = null)
    {
        _batchList.BeginUpdate();
        _batchList.Items.Clear();
        var reqs = BatchRequests(ReadRequest());
        for (var i = 0; i < reqs.Count; i++)
        {
            var r = reqs[i];
            var item = new ListViewItem((i + 1).ToString());
            var target = BatchDescription(r);
            var scope = r.Job switch
            {
                JobKind.ImportOnly => UiStrings.Get("JobImport"),
                JobKind.ExportAndImport => ScopeShort(r.ContentMode) + UiStrings.Get("BatchImportSuffix"),
                _ => ScopeShort(r.ContentMode)
            };
            var file = JobBuilder.FilePreview(r);   // Import 만이면 읽을 파일, 아니면 Export 가 만드는 파일 (파일 분리면 두 개)
            item.SubItems.Add(target);
            item.SubItems.Add(scope);
            item.SubItems.Add(file);
            item.ToolTipText = target + "  —  " + scope + "  —  " + file;
            _batchList.Items.Add(item);
        }
        _batchList.EndUpdate();
        if (select is { } idx && idx >= 0 && idx < _batchList.Items.Count)
        {
            _batchList.Items[idx].Selected = true;
            _batchList.Items[idx].EnsureVisible();
        }
    }

    private void BatchChanged(int? select)
    {
        try { _batchStore.Save(_batch); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RefillBatchList(select);
        Rebuild();
    }

    /// <summary>오류가 있는 화면 설정은 목록에 넣지 않는다 (넣으면 목록 전체 스크립트가 막히므로).</summary>
    private bool FormHasErrors(DumpRequest req)
    {
        if (!_builder.Build(req).HasErrors) return false;
        _status.ForeColor = AppTheme.Current.Error;
        _status.Text = UiStrings.Get("BatchAddBlocked");
        return true;
    }

    private void AddBatchItem()
    {
        var req = ReadRequest();
        if (FormHasErrors(req)) return;
        _batch.Add(Preset.FromRequest(BatchDescription(req), req));
        BatchChanged(_batch.Count - 1);
    }

    private void UpdateBatchItem()
    {
        if (SelectedBatchIndex is not int i) return;
        var req = ReadRequest();
        if (FormHasErrors(req)) return;
        _batch[i] = Preset.FromRequest(BatchDescription(req), req);
        BatchChanged(i);
    }

    private void RemoveBatchItem()
    {
        if (SelectedBatchIndex is not int i) return;
        _batch.RemoveAt(i);
        BatchChanged(Math.Min(i, _batch.Count - 1));
    }

    private void MoveBatchItem(int delta)
    {
        if (SelectedBatchIndex is not int i) return;
        var j = i + delta;
        if (j < 0 || j >= _batch.Count) return;
        (_batch[i], _batch[j]) = (_batch[j], _batch[i]);
        BatchChanged(j);
    }

    private void ClearBatch()
    {
        if (_batch.Count == 0) return;
        if (MessageBox.Show(this, UiStrings.Get("BatchClearConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _batch.Clear();
        BatchChanged(null);
    }

    /// <summary>더블클릭한 항목을 화면으로 불러온다. 고친 뒤 "항목 갱신"으로 되돌려 넣는다.</summary>
    private void LoadBatchItem()
    {
        if (SelectedBatchIndex is not int i) return;
        var req = ReadRequest();
        _batch[i].ApplyTo(req, _schema);
        LoadRequest(req);
        RefillBatchList(i);
        _status.ForeColor = AppTheme.Current.Success;
        _status.Text = string.Format(UiStrings.Get("BatchLoaded"), i + 1);
    }

    // ==================================================================
    // 프리셋
    // ==================================================================

    private List<Preset> SafeLoadPresets()
    {
        try { return _presetStore.Load(); }
        catch { return new List<Preset>(); }
    }

    private IEnumerable<Preset> AllPresets => BuiltInPresets.All.Concat(_userPresets);

    private void RefillPresetCombo(string? selectName = null)
    {
        var previous = selectName ?? (_presetCombo.SelectedItem as PresetItem)?.Preset?.Name;
        var wasSuspended = _suspendRebuild;
        _suspendRebuild = true;
        try
        {
            _presetCombo.Items.Clear();
            _presetCombo.Items.Add(new PresetItem(null));
            foreach (var p in AllPresets) _presetCombo.Items.Add(new PresetItem(p));
            var idx = 0;
            for (var i = 1; i < _presetCombo.Items.Count; i++)
                if (_presetCombo.Items[i] is PresetItem { Preset: { } p } && p.Name == previous) { idx = i; break; }
            _presetCombo.SelectedIndex = idx;
        }
        finally
        {
            _suspendRebuild = wasSuspended;
        }
    }

    private void OnPresetSelected()
    {
        if (_suspendRebuild) return;
        if (_presetCombo.SelectedItem is not PresetItem { Preset: { } preset }) return;

        var req = ReadRequest();
        preset.ApplyTo(req, _schema);
        LoadRequest(req);
    }

    private void SavePreset()
    {
        var current = (_presetCombo.SelectedItem as PresetItem)?.Preset;
        var initial = current is { IsBuiltIn: false } ? current.Name : "";
        var name = InputDialog.Ask(this, UiStrings.Get("PresetNameTitle"), UiStrings.Get("PresetNamePrompt"), initial);
        if (name is null) return;

        if (BuiltInPresets.All.Any(p => p.Name == name))
        {
            MessageBox.Show(this, UiStrings.Get("PresetBuiltInReadOnly"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var existing = _userPresets.FindIndex(p => p.Name == name);
        if (existing >= 0 &&
            MessageBox.Show(this, UiStrings.Get("PresetOverwriteConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var preset = Preset.FromRequest(name, ReadRequest());
        if (existing >= 0) _userPresets[existing] = preset; else _userPresets.Add(preset);
        TrySavePresets();
        RefillPresetCombo(name);
    }

    private void DeletePreset()
    {
        if (_presetCombo.SelectedItem is not PresetItem { Preset: { } preset }) return;
        if (preset.IsBuiltIn)
        {
            MessageBox.Show(this, UiStrings.Get("PresetBuiltInReadOnly"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, string.Format(UiStrings.Get("PresetDeleteConfirm"), preset.Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _userPresets.RemoveAll(p => p.Name == preset.Name);
        TrySavePresets();
        RefillPresetCombo("");
    }

    private void TrySavePresets()
    {
        try { _presetStore.Save(_userPresets); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ResetOptions()
    {
        if (MessageBox.Show(this, UiStrings.Get("ResetConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var current = ReadRequest();
        var fresh = new DumpRequest
        {
            BinaryPath = current.BinaryPath,
            Host = current.Host,
            Port = current.Port,
            Socket = current.Socket,
            User = current.User,
            PasswordMode = current.PasswordMode,
            LoginPath = current.LoginPath,
            DefaultsExtraFile = current.DefaultsExtraFile,
            Client = current.Client,
            Shell = current.Shell,
            MultiLine = current.MultiLine,
            ExplicitDefaults = current.ExplicitDefaults
        };
        fresh.ApplySchemaDefaults(_schema);
        RefillPresetCombo("");
        LoadRequest(fresh);
    }

    /// <summary>콤보박스 항목. 기본 프리셋은 이름 앞에 [기본] 표시.</summary>
    private sealed class PresetItem
    {
        public Preset? Preset { get; }
        public PresetItem(Preset? preset) => Preset = preset;
        public override string ToString() => Preset is null
            ? UiStrings.Get("PresetNone")
            : (Preset.IsBuiltIn ? UiStrings.Get("PresetBuiltInPrefix") : "") + Preset.Name;
    }

    // ==================================================================
    // 시작/종료
    // ==================================================================

    private void LoadInitialState()
    {
        var req = new DumpRequest();
        req.ApplySchemaDefaults(_schema);
        if (_settings.LastState is { } last)
        {
            try { last.ApplyTo(req, _schema); } catch { /* 깨진 설정은 무시 */ }
        }
        req.Import.CreateDatabase = false;   // "DB 생성"은 항상 꺼진 채로 시작 (예전 settings.json 에 켜져 있어도)
        req.MultiLine = _settings.MultiLine;
        req.ExplicitDefaults = _settings.ExplicitDefaults;
        _showAdvanced.Checked = _settings.ShowAdvanced;
        LoadRequest(req);
    }

    private void SaveSettings()
    {
        var req = ReadRequest();
        _settings.Language = UiStrings.Current;
        _settings.ShowAdvanced = _showAdvanced.Checked;
        _settings.MultiLine = req.MultiLine;
        _settings.ExplicitDefaults = req.ExplicitDefaults;
        _settings.DarkMode = _darkSwitch.Checked;
        _settings.WindowWidth = WindowState == FormWindowState.Normal ? Width : RestoreBounds.Width;
        _settings.WindowHeight = WindowState == FormWindowState.Normal ? Height : RestoreBounds.Height;
        _settings.LastState = Preset.FromRequest("__last", req);
        if (_settings.LastState.Import is { } lastImport) lastImport.CreateDatabase = false;   // "DB 생성"은 항상 꺼진 채로 시작한다 (프리셋·목록에는 그대로 저장됨)
        _settings.Save();
    }
}
