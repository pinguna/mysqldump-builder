using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysqlDumpBuilder.Core;

/// <summary>
/// 자주 쓰는 설정 조합. 접속 정보(host/port/user/클라이언트)는 포함하고 비밀번호는 절대 포함하지 않는다.
/// 내장 프리셋은 수정할 수 없고, 사용자 프리셋은 presets.json 에 저장된다.
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = "";
    public LocalizedText Description { get; set; } = new();

    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    // 접속 (프리셋에 포함, 비밀번호 제외 — 확정)
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Socket { get; set; }
    public string? User { get; set; }
    public ClientKind? Client { get; set; }
    public PasswordMode? PasswordMode { get; set; }
    public string? LoginPath { get; set; }
    public string? DefaultsExtraFile { get; set; }

    // 대상
    public TargetMode? TargetMode { get; set; }
    public List<string>? Databases { get; set; }
    public List<string>? Tables { get; set; }
    public List<string>? IgnoreTables { get; set; }
    public string? Where { get; set; }

    // 옵션
    public ContentMode? ContentMode { get; set; }
    public InsertMode? InsertMode { get; set; }
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    // 출력
    public OutputMode? OutputMode { get; set; }
    public string? OutputFileTemplate { get; set; }
    public string? ScriptFileTemplate { get; set; }

    // 작업 종류·Import 대상 (평문 패스워드는 JsonIgnore 라 저장되지 않는다)
    public JobKind? Job { get; set; }
    public ImportTarget? Import { get; set; }
    public ShellKind? Shell { get; set; }

    /// <summary>프리셋의 값을 요청에 덮어쓴다. null 인 항목은 건드리지 않는다. 체크박스는 스키마 기본값으로 초기화한 뒤 적용한다.</summary>
    public void ApplyTo(DumpRequest req, OptionSchema schema)
    {
        if (Host is not null) req.Host = Host;
        if (Port is not null) req.Port = Port;
        if (Socket is not null) req.Socket = Socket;
        if (User is not null) req.User = User;
        if (Client is not null) req.Client = Client.Value;
        if (PasswordMode is not null) req.PasswordMode = PasswordMode.Value;
        if (LoginPath is not null) req.LoginPath = LoginPath;
        if (DefaultsExtraFile is not null) req.DefaultsExtraFile = DefaultsExtraFile;

        if (TargetMode is not null) req.TargetMode = TargetMode.Value;
        if (Databases is not null) req.Databases = new List<string>(Databases);
        if (Tables is not null) req.Tables = new List<string>(Tables);
        if (IgnoreTables is not null) req.IgnoreTables = new List<string>(IgnoreTables);
        if (Where is not null) req.Where = Where;

        if (ContentMode is not null) req.ContentMode = ContentMode.Value;
        if (InsertMode is not null) req.InsertMode = InsertMode.Value;

        req.ApplySchemaDefaults(schema);
        foreach (var (id, on) in Flags) req.Flags[id] = on;
        foreach (var (id, value) in Values) req.Values[id] = value;

        if (OutputMode is not null) req.OutputMode = OutputMode.Value;
        if (OutputFileTemplate is not null) req.OutputFileTemplate = OutputFileTemplate;
        if (ScriptFileTemplate is not null) req.ScriptFileTemplate = ScriptFileTemplate;
        if (Job is not null) req.Job = Job.Value;
        if (Import is not null) req.Import = Import.Clone();
        if (Shell is not null) req.Shell = Shell.Value;
    }

    /// <summary>
    /// 이름과 대상 OS 를 뺀 설정 내용만의 키. 두 프리셋(목록 항목)이 같은 설정인지 비교할 때 쓴다.
    /// 대상 OS 는 스크립트 목록 공통(현재 화면 값)이라, OS 만 바꿨다고 "목록에 없는 새 설정" 이 되면 안 된다.
    /// </summary>
    public string SettingsKey()
    {
        var (name, shell) = (Name, Shell);
        Name = "";
        Shell = null;
        try { return JsonSerializer.Serialize(this, PresetStore.JsonOptions); }
        finally { (Name, Shell) = (name, shell); }
    }

    /// <summary>현재 요청 전체를 프리셋으로 스냅샷한다 (비밀번호 제외).</summary>
    public static Preset FromRequest(string name, DumpRequest req)
    {
        return new Preset
        {
            Name = name,
            Host = req.Host,
            Port = req.Port,
            Socket = req.Socket,
            User = req.User,
            Client = req.Client,
            PasswordMode = req.PasswordMode == Core.PasswordMode.Plain ? Core.PasswordMode.Prompt : req.PasswordMode,
            LoginPath = req.LoginPath,
            DefaultsExtraFile = req.DefaultsExtraFile,
            TargetMode = req.TargetMode,
            Databases = new List<string>(req.Databases),
            Tables = new List<string>(req.Tables),
            IgnoreTables = new List<string>(req.IgnoreTables),
            Where = req.Where,
            ContentMode = req.ContentMode,
            InsertMode = req.InsertMode,
            Flags = new Dictionary<string, bool>(req.Flags, StringComparer.Ordinal),
            Values = new Dictionary<string, string>(req.Values, StringComparer.Ordinal),
            OutputMode = req.OutputMode,
            OutputFileTemplate = req.OutputFileTemplate,
            ScriptFileTemplate = req.ScriptFileTemplate,
            Job = req.Job,
            Import = SnapshotImport(req.Import),
            Shell = req.Shell
        };
    }

    /// <summary>Import 대상 스냅샷. 평문 패스워드는 버리고 모드도 프롬프트로 바꾼다 (원본 접속과 같은 규칙).</summary>
    private static ImportTarget SnapshotImport(ImportTarget import)
    {
        var copy = import.Clone();
        copy.PlainPassword = "";
        if (copy.PasswordMode == Core.PasswordMode.Plain) copy.PasswordMode = Core.PasswordMode.Prompt;
        return copy;
    }
}

/// <summary>
/// 기획서 UC-01, 02, 03, 05 에 해당하는 내장 프리셋.
/// 이름은 "무엇을 내보내는지"(DDL / 데이터) 로 짓는다 — 화면에는 이름만 보이고 설명은 보이지 않기 때문이다.
/// 모든 프리셋은 작업 = Export, 출력 = > 파일 로 맞춘다. 프리셋을 고르면 작업·출력 라디오도 그에 맞게 바뀐다.
/// 일부 테이블 제외는 프리셋으로 두지 않는다 (전체 백업 + 제외 테이블 목록으로 사용자가 고른다).
/// Import 대상 설정은 건드리지 않으므로, 프리셋을 고른 뒤 작업을 Export + Import 로 바꾸면 그대로 이어서 쓸 수 있다.
/// </summary>
public static class BuiltInPresets
{
    public static IReadOnlyList<Preset> All { get; } = new List<Preset>
    {
        new()
        {
            Name = "DDL + 데이터 (전체 백업)",
            IsBuiltIn = true,
            Description = new LocalizedText(
                "서비스 중단 없이 DB 하나를 통째로. 프로시저·이벤트·트리거 포함, 바이너리 안전.",
                "Full backup of one database with no service interruption. Routines, events, triggers, binary-safe."),
            Job = JobKind.ExportOnly,
            OutputMode = OutputMode.StdoutRedirect,
            TargetMode = TargetMode.SingleDatabase,
            ContentMode = ContentMode.StructureAndData,
            InsertMode = InsertMode.Insert,
            Flags = new(StringComparer.Ordinal)
            {
                ["single_transaction"] = true,
                ["quick"] = true,
                ["routines"] = true,
                ["events"] = true,
                ["hex_blob"] = true,
                ["set_gtid_purged_off"] = true
            }
        },
        new()
        {
            Name = "DDL + 데이터 (파일 분리)",
            IsBuiltIn = true,
            Description = new LocalizedText(
                "전체 백업과 같은 내용을 DDL 파일(_ddl)과 데이터 파일(_data) 두 개로. 구조만 먼저 넣거나 데이터만 다시 넣을 때 편함.",
                "Same content as the full backup, written to a DDL file (_ddl) and a data file (_data). Handy for loading structure first or reloading only data."),
            Job = JobKind.ExportOnly,
            OutputMode = OutputMode.StdoutRedirect,
            TargetMode = TargetMode.SingleDatabase,
            ContentMode = ContentMode.StructureAndDataSplit,
            InsertMode = InsertMode.Insert,
            Flags = new(StringComparer.Ordinal)
            {
                ["single_transaction"] = true,
                ["quick"] = true,
                ["routines"] = true,
                ["events"] = true,
                ["hex_blob"] = true,
                ["set_gtid_purged_off"] = true
            }
        },
        new()
        {
            Name = "DDL 오브젝트 (데이터 제외)",
            IsBuiltIn = true,
            Description = new LocalizedText(
                "테이블·뷰·프로시저·이벤트·트리거 구조만. 데이터 제외. 개발 DB 에 구조를 맞출 때.",
                "Tables, views, routines, events and triggers only. No data. For syncing structure to a dev database."),
            Job = JobKind.ExportOnly,
            OutputMode = OutputMode.StdoutRedirect,
            TargetMode = TargetMode.SingleDatabase,
            ContentMode = ContentMode.StructureOnly,
            InsertMode = InsertMode.Insert,
            Flags = new(StringComparer.Ordinal)
            {
                ["routines"] = true,
                ["events"] = true,
                ["skip_add_drop_table"] = false,
                ["single_transaction"] = true,
                ["quick"] = true
            }
        },
        new()
        {
            Name = "데이터 (DDL 제외, INSERT IGNORE)",
            IsBuiltIn = true,
            Description = new LocalizedText(
                "대상에 테이블이 이미 있을 때 데이터만 채움. 중복 키는 건너뛰고 컬럼명을 포함.",
                "Data only, into tables that already exist. Skips duplicate keys, includes column names."),
            Job = JobKind.ExportOnly,
            OutputMode = OutputMode.StdoutRedirect,
            TargetMode = TargetMode.SingleDatabase,
            ContentMode = ContentMode.DataOnly,
            InsertMode = InsertMode.InsertIgnore,
            Flags = new(StringComparer.Ordinal)
            {
                ["skip_triggers"] = true,
                ["complete_insert"] = true,
                ["single_transaction"] = true,
                ["quick"] = true,
                ["hex_blob"] = true
            }
        }
    };
}

/// <summary>presets.json 읽기/쓰기. 포터블 구성이므로 exe 와 같은 폴더를 쓴다.</summary>
public sealed class PresetStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Path { get; }

    public PresetStore(string path)
    {
        Path = path;
    }

    public List<Preset> Load()
    {
        if (!File.Exists(Path)) return new List<Preset>();
        var json = File.ReadAllText(Path);
        if (string.IsNullOrWhiteSpace(json)) return new List<Preset>();
        return JsonSerializer.Deserialize<List<Preset>>(json, JsonOptions) ?? new List<Preset>();
    }

    public void Save(IEnumerable<Preset> presets)
    {
        var json = JsonSerializer.Serialize(presets.Where(p => !p.IsBuiltIn).ToList(), JsonOptions);
        File.WriteAllText(Path, json);
    }
}
