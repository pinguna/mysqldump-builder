namespace MysqlDumpBuilder.Core;

/// <summary>사용할 mysqldump 클라이언트 종류. 지원 옵션 목록이 달라진다.</summary>
public enum ClientKind
{
    Mysql57,
    Mysql80
}

/// <summary>비밀번호를 명령어에 어떻게 넣을지.</summary>
public enum PasswordMode
{
    /// <summary>-p 만 넣고 실행 시 프롬프트에서 입력 (기본값).</summary>
    Prompt,
    /// <summary>--login-path=NAME (mysql_config_editor에 저장된 정보).</summary>
    LoginPath,
    /// <summary>--defaults-extra-file=PATH (첫 번째 인자여야 함).</summary>
    DefaultsExtraFile,
    /// <summary>--password=평문. 경고가 붙는다.</summary>
    Plain
}

/// <summary>무엇을 덤프할지의 대상 범위.</summary>
public enum TargetMode
{
    /// <summary>DB 하나 전체: mysqldump ... dbname</summary>
    SingleDatabase,
    /// <summary>DB 하나에서 지정한 테이블만: mysqldump ... dbname t1 t2</summary>
    Tables,
    /// <summary>여러 DB: mysqldump ... --databases a b (Import 가 붙으면 DB 별 단일 DB 명령으로 쪼갠다 — JobBuilder 참고)</summary>
    MultipleDatabases
}

/// <summary>구조/데이터 선택 (라디오). --no-data 와 --no-create-info 를 동시에 고르는 것을 원천 차단한다.</summary>
public enum ContentMode
{
    StructureAndData,
    StructureOnly,
    DataOnly,
    /// <summary>DDL 파일(--no-data)과 데이터 파일(--no-create-info)을 따로 만든다. JobBuilder 가 두 단계로 쪼갠다.</summary>
    StructureAndDataSplit
}

/// <summary>INSERT 구문 형태 (라디오). --insert-ignore 와 --replace 는 배타.</summary>
public enum InsertMode
{
    Insert,
    InsertIgnore,
    Replace
}

/// <summary>덤프 결과를 어디로 보낼지.</summary>
public enum OutputMode
{
    /// <summary>--result-file=파일 (PowerShell 인코딩 문제 없음)</summary>
    ResultFile,
    /// <summary>> 파일 (표준출력 리다이렉션, 기본값)</summary>
    StdoutRedirect,
    /// <summary>| gzip > 파일.gz (Linux 전용)</summary>
    GzipPipe
}

/// <summary>명령어를 붙여넣을 셸. 인용부호와 줄 연결 문자가 달라진다.</summary>
public enum ShellKind
{
    Bash,
    Cmd,
    PowerShell
}

/// <summary>
/// 화면의 모든 입력값. UI는 이 객체를 채워 CommandBuilder에 넘기기만 한다.
/// 체크박스 옵션은 Flags(id → 체크 여부)와 Values(id → 입력값)에 담긴다.
/// </summary>
/// <summary>작업 종류: Export(mysqldump)만, Export 뒤 Import(mysql &lt; 파일), 기존 파일 Import만.</summary>
public enum JobKind
{
    ExportOnly,
    ExportAndImport,
    ImportOnly
}

/// <summary>
/// Import 대상(Target DB) 정보. 접속 정보가 원본과 같으면 SameConnectionAsSource 만 켜고 나머지 접속 칸은 비워 둔다.
/// 평문 패스워드는 프리셋·목록에 저장되지 않는다.
/// </summary>
public sealed class ImportTarget
{
    public bool SameConnectionAsSource { get; set; } = true;
    public string Host { get; set; } = "";
    public int? Port { get; set; }
    /// <summary>유닉스 소켓 경로 (-S). Host 가 localhost 이거나 비어 있을 때만 쓴다. 비우면 my.cnf [client] socket=, 없으면 클라이언트 기본 소켓.</summary>
    public string Socket { get; set; } = "";
    public string User { get; set; } = "";
    public PasswordMode PasswordMode { get; set; } = PasswordMode.Prompt;
    public string LoginPath { get; set; } = "";
    public string DefaultsExtraFile { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PlainPassword { get; set; } = "";

    /// <summary>
    /// 대상 DB 이름. 비우면 원본과 같은 이름을 쓴다.
    /// 여러 DB Export 면 쉼표로 나열한 목록이고 원본 DB 와 순서대로 짝이 된다 (예: 원본 a,b,c → 대상 a2,b,c). 개수가 다르면 오류.
    /// </summary>
    public string Database { get; set; } = "";
    public string BinaryPath { get; set; } = "mysql";

    /// <summary>
    /// Import 만 할 때 읽을 파일 경로 (.sql 또는 .sql.gz). 파일명 템플릿은 Export 가 만드는 파일이므로 여기서는 쓰지 않는다.
    /// 여러 DB 면 {db} 가 각 DB 이름으로 바뀐다 (예: backup/{db}.sql). .gz 로 끝나면 gunzip 파이프로 넣는다.
    /// </summary>
    public string InputFile { get; set; } = "";

    /// <summary>없으면 CREATE DATABASE IF NOT EXISTS 를 먼저 실행한다 (-p 면 패스워드를 한 번 더 묻는다).</summary>
    public bool CreateDatabase { get; set; }
    /// <summary>mysql --force: SQL 오류가 나도 계속 진행.</summary>
    public bool Force { get; set; }
    /// <summary>mysql --init-command="SET FOREIGN_KEY_CHECKS=0": 테이블 순서와 무관하게 넣는다 (--compact Export 처럼 파일에 SET 문이 없을 때 유용).</summary>
    public bool DisableForeignKeyChecks { get; set; }
    public string Charset { get; set; } = "utf8mb4";

    public ImportTarget Clone() => (ImportTarget)MemberwiseClone();
}

public sealed class DumpRequest
{
    // ---- 접속 ----
    public string BinaryPath { get; set; } = "mysqldump";
    public string Host { get; set; } = "localhost";
    public int? Port { get; set; } = 3306;
    /// <summary>
    /// 유닉스 소켓 경로 (-S). 리눅스 클라이언트는 localhost(또는 Host 없음)면 TCP 가 아니라 소켓으로 접속하고 포트를 무시한다.
    /// 한 서버에 인스턴스가 여러 개면 여기에 그 인스턴스의 소켓을 적는다. 비우면 my.cnf [client] socket=, 없으면 클라이언트 기본 소켓.
    /// </summary>
    public string Socket { get; set; } = "";
    public string User { get; set; } = "";
    public PasswordMode PasswordMode { get; set; } = PasswordMode.Prompt;
    public string LoginPath { get; set; } = "";
    public string DefaultsExtraFile { get; set; } = "";
    public string PlainPassword { get; set; } = "";
    public ClientKind Client { get; set; } = ClientKind.Mysql80;

    // ---- 대상 ----
    public TargetMode TargetMode { get; set; } = TargetMode.SingleDatabase;
    public List<string> Databases { get; set; } = new();
    public List<string> Tables { get; set; } = new();
    public List<string> IgnoreTables { get; set; } = new();
    public string Where { get; set; } = "";

    // ---- 라디오 ----
    public ContentMode ContentMode { get; set; } = ContentMode.StructureAndData;
    public InsertMode InsertMode { get; set; } = InsertMode.Insert;

    /// <summary>
    /// "DDL + DML 파일 분리" 를 JobBuilder 가 쪼갠 하위 요청이면 true. 화면에서 고르는 값이 아니다.
    /// 분리의 한쪽이라 생기는 안내(R23 DDL 생략, I5 테이블이 있어야 함)를 끄고, DDL 쪽에서 R25 안내를 한 번 낸다.
    /// </summary>
    public bool IsContentSplitPart { get; set; }

    // ---- 체크박스 옵션 (스키마 id 기준) ----
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    // ---- 출력 ----
    public OutputMode OutputMode { get; set; } = OutputMode.StdoutRedirect;
    public string OutputFileTemplate { get; set; } = "{db}_{date:yyyyMMdd}_{time:HHmm}.sql";
    /// <summary>스크립트 저장 파일명 템플릿. 확장자(.sh/.bat/.ps1)는 대상 OS에 맞춰 자동으로 붙는다.</summary>
    public string ScriptFileTemplate { get; set; } = "mysqldump_{db}";
    public ShellKind Shell { get; set; } = ShellKind.Bash;

    // ---- 작업 종류와 Import 대상 ----
    public JobKind Job { get; set; } = JobKind.ExportOnly;
    public ImportTarget Import { get; set; } = new();
    public bool MultiLine { get; set; } = true;

    /// <summary>기본값과 같은 옵션도 명령어에 명시한다 (가독성 우선, 확정).</summary>
    public bool ExplicitDefaults { get; set; } = true;

    /// <summary>파일명 토큰 치환에 쓸 시각. 테스트에서 고정하기 위해 바꿀 수 있다.</summary>
    public DateTime Now { get; set; } = DateTime.Now;

    /// <summary>
    /// 스크립트 내보내기 전용. 값이 있으면 출력 파일명 자리에 이 셸 식(예: "$OUT_FILE")을 인용 없이 그대로 넣는다.
    /// 화면의 명령어 생성에서는 항상 null 이다.
    /// </summary>
    public string? OutputFileShellExpression { get; set; }

    public bool IsChecked(string optionId) => Flags.TryGetValue(optionId, out var v) && v;

    public string? ValueOf(string optionId) => Values.TryGetValue(optionId, out var v) ? v : null;

    public void Set(string optionId, bool on) => Flags[optionId] = on;

    public void SetValue(string optionId, string value)
    {
        Flags[optionId] = true;
        Values[optionId] = value;
    }

    /// <summary>스키마의 기본값으로 Flags/Values를 채운다. 새 화면을 열 때 호출한다.</summary>
    public void ApplySchemaDefaults(OptionSchema schema)
    {
        foreach (var o in schema.Options)
        {
            Flags[o.Id] = o.DefaultChecked;
            if (o.IsValueType) Values[o.Id] = o.DefaultValue ?? "";
        }
    }

    public DumpRequest Clone()
    {
        var c = (DumpRequest)MemberwiseClone();
        c.Databases = new List<string>(Databases);
        c.Tables = new List<string>(Tables);
        c.IgnoreTables = new List<string>(IgnoreTables);
        c.Flags = new Dictionary<string, bool>(Flags, StringComparer.Ordinal);
        c.Values = new Dictionary<string, string>(Values, StringComparer.Ordinal);
        c.Import = Import.Clone();
        return c;
    }
}
