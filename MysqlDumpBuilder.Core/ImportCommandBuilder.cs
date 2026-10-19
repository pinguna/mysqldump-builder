using MysqlDumpBuilder.Core.Formatters;

namespace MysqlDumpBuilder.Core;

/// <summary>
/// Import 단계: Export 파일을 대상 DB 에 넣는 mysql 명령어 + 경고.
///   bash/cmd  : mysql [접속] [옵션] db &lt; "파일"      (gzip 파일은 gunzip -c "파일" | mysql ...)
///   PowerShell: mysql [접속] [옵션] db --execute="source 파일"   (PowerShell 은 &lt; 리다이렉션이 없다)
/// "없으면 DB 생성"이 켜져 있으면 CREATE DATABASE IF NOT EXISTS 를 먼저 실행하는 줄이 하나 더 붙는다.
/// 규칙 코드는 I1~ 이다.
/// </summary>
public sealed class ImportCommandBuilder
{
    public BuildResult Build(DumpRequest req)
    {
        var messages = new List<BuildMessage>();
        var import = req.Import;
        var conn = EffectiveConnection(req);
        var targetDb = TargetDatabase(req);
        var dbArgNeeded = req.TargetMode is TargetMode.SingleDatabase or TargetMode.Tables;

        // ---- 규칙 ----
        if (dbArgNeeded && targetDb.Length == 0)
            messages.Add(RuleMessages.Make("V_IMPORT_NO_DATABASE"));

        if (IsSameAsSource(req, conn, targetDb))
            messages.Add(RuleMessages.Make("I1_SAME_AS_SOURCE"));

        if (req.Job != JobKind.ImportOnly && req.ContentMode == ContentMode.DataOnly && !req.IsContentSplitPart)
            messages.Add(RuleMessages.Make("I5_DATA_ONLY_NEEDS_TABLES"));

        if (import.Force)
            messages.Add(RuleMessages.Make("I6_FORCE"));

        // Import 만이면 "읽을 파일" 이 곧 파일이고 .gz 여부도 그 이름으로 정한다. Export 가 있으면 Export 가 만든 파일(템플릿)을 읽는다.
        var importOnly = req.Job == JobKind.ImportOnly;
        var gzip = UsesGzip(req);
        if (gzip && req.Shell != ShellKind.Bash)
            messages.Add(RuleMessages.Make("V_GZIP_NOT_BASH"));
        else if (gzip)
            messages.Add(RuleMessages.Make("I3_GZIP_IMPORT"));

        if (req.Shell == ShellKind.PowerShell)
            messages.Add(RuleMessages.Make("I2_PS_SOURCE"));

        var fileName = InputFileName(req);
        if (string.IsNullOrWhiteSpace(fileName))
            messages.Add(RuleMessages.Make(importOnly ? "V_IMPORT_NO_FILE" : "V_NO_OUTPUT_FILE"));

        // ---- 토큰 ----
        var groups = new List<CommandLineGroup>();
        var raw = req.OutputFileShellExpression;   // 스크립트: "$OUT_FILE" 같은 셸 식, 화면: null
        CommandToken FileToken() => raw is null ? CommandToken.Arg(fileName) : CommandToken.Shell(raw);

        // "DB 생성": mysql 을 두 번 띄우지 않고, CREATE DATABASE + USE 문을 파일 앞에 붙여 한 번에 흘려 넣는다.
        //   bash: { echo '...'; cat 파일; } | mysql ...     cmd: (echo ... & type 파일) | mysql ...
        //   PowerShell: --execute="...;`nsource 파일"  (-e 안의 줄바꿈은 mysql 이 줄 단위로 읽으므로 source 가 먹는다)
        // 이때는 DB 인자를 주지 않는다 (없는 DB 를 기본 DB 로 지정하면 접속 자체가 실패하므로 USE 로 대신한다).
        var createDb = import.CreateDatabase && dbArgNeeded && targetDb.Length > 0;
        var prologue = "CREATE DATABASE IF NOT EXISTS `" + targetDb + "`; USE `" + targetDb + "`;";

        if (createDb && req.Shell == ShellKind.Bash)
        {
            var pre = new CommandLineGroup("prologue");
            pre.Tokens.Add(CommandToken.Shell("{ echo '" + prologue + "';"));
            pre.Tokens.Add(CommandToken.Shell(gzip ? "gunzip -c" : "cat"));
            pre.Tokens.Add(FileToken());
            pre.Tokens.Add(CommandToken.Shell("; } |"));
            groups.Add(pre);
        }
        else if (createDb && req.Shell == ShellKind.Cmd)
        {
            var pre = new CommandLineGroup("prologue");
            pre.Tokens.Add(CommandToken.Shell("(echo " + prologue + " & type"));
            pre.Tokens.Add(FileToken());
            pre.Tokens.Add(CommandToken.Shell(") |"));
            groups.Add(pre);
        }
        else if (gzip && req.Shell == ShellKind.Bash)
        {
            var pipe = new CommandLineGroup("gunzip");
            pipe.Tokens.Add(CommandToken.Shell("gunzip"));
            pipe.Tokens.Add(CommandToken.Shell("-c"));
            pipe.Tokens.Add(FileToken());
            pipe.Tokens.Add(CommandToken.Shell("|"));
            groups.Add(pipe);
        }

        groups.Add(BinaryGroup(import));
        groups.Add(ConnectionGroup(conn, ForcesTcp(req), req.Shell, messages));
        groups.Add(OptionGroup(import));

        // 대상 DB 와 입력 파일은 한 줄에 둔다 (여러 줄 모드에서 "shop < 파일" 이 갈라지지 않게).
        var input = new CommandLineGroup("target-input");
        if (req.Shell == ShellKind.PowerShell)
        {
            if (dbArgNeeded && targetDb.Length > 0 && !createDb) input.Tokens.Add(CommandToken.Arg(targetDb));
            var file = raw is null ? fileName : raw.Trim('"');
            if (createDb)
            {
                // PowerShell 큰따옴표 안에서 백틱은 이스케이프 문자라 두 번 쓴다. `n 은 줄바꿈.
                var ps = prologue.Replace("`", "``") + "`nsource " + file;
                input.Tokens.Add(CommandToken.FlagWithRawValue("--execute", "\"" + ps + "\""));
            }
            else
            {
                input.Tokens.Add(raw is null
                    ? CommandToken.FlagWithValue("--execute", "source " + fileName)
                    : CommandToken.FlagWithRawValue("--execute", "\"source " + file + "\""));
            }
        }
        else if (createDb)
        {
            // 파일은 앞의 파이프로 들어오고 DB 는 USE 로 고른다.
        }
        else
        {
            if (dbArgNeeded && targetDb.Length > 0) input.Tokens.Add(CommandToken.Arg(targetDb));
            if (!gzip)
            {
                input.Tokens.Add(CommandToken.Shell("<"));
                input.Tokens.Add(FileToken());
            }
        }
        groups.Add(input);

        var hasErrors = messages.Any(m => m.Severity == Severity.Error);
        var command = hasErrors ? "" : ShellFormatters.For(req.Shell).Format(groups, req.MultiLine);

        return new BuildResult
        {
            Command = command,
            Groups = groups,
            Messages = messages
                .OrderBy(m => m.Severity == Severity.Error ? 0 : m.Severity == Severity.Warning ? 1 : 2)
                .ToList()
        };
    }

    /// <summary>대상 DB 이름: 입력값이 있으면 그것, 없으면 원본의 첫 DB.</summary>
    public static string TargetDatabase(DumpRequest req)
    {
        var db = req.Import.Database.Trim();
        if (db.Length > 0) return db;
        return req.Databases.Select(d => d.Trim()).FirstOrDefault(d => d.Length > 0) ?? "";
    }

    /// <summary>쉼표로 나열한 이름을 목록으로 (빈 항목 제거).</summary>
    public static List<string> SplitNames(string text)
        => text.Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();

    /// <summary>여러 DB Export 의 대상 DB 목록: 입력값이 있으면 그 목록, 없으면 원본 DB 목록 그대로. 개수 검사는 JobBuilder 가 한다.</summary>
    public static List<string> TargetDatabases(DumpRequest req)
    {
        var entered = SplitNames(req.Import.Database);
        return entered.Count > 0 ? entered : SplitNames(string.Join(",", req.Databases));
    }

    /// <summary>실제로 쓸 접속 정보. "원본과 같음"이면 원본 접속을 그대로 쓴다.</summary>
    public static ImportTarget EffectiveConnection(DumpRequest req)
    {
        var t = req.Import;
        if (!t.SameConnectionAsSource) return t;
        var c = t.Clone();
        c.Host = req.Host;
        c.Port = req.Port;
        c.Socket = req.Socket;
        c.User = req.User;
        c.PasswordMode = req.PasswordMode;
        c.LoginPath = req.LoginPath;
        c.DefaultsExtraFile = req.DefaultsExtraFile;
        c.PlainPassword = req.PlainPassword;
        return c;
    }

    /// <summary>
    /// mysql 이 읽을 파일. Import 만이면 "읽을 파일"({db} 토큰만 의미 있음), 아니면 Export 가 만드는 파일(템플릿 + .gz).
    /// 화면과 스크립트 목록이 같은 이름을 보여 주도록 한 곳에서 정한다.
    /// </summary>
    public static string InputFileName(DumpRequest req)
    {
        if (req.Job == JobKind.ImportOnly)
        {
            var db = req.Databases.Select(d => d.Trim()).FirstOrDefault(d => d.Length > 0) ?? "";
            return FileNameTemplate.Expand(req.Import.InputFile?.Trim() ?? "", db, req.Host ?? "", req.Now).Trim();
        }
        var name = CommandBuilder.ExpandOutputFileName(req);
        return req.OutputMode == OutputMode.GzipPipe ? CommandBuilder.GzipName(name) : name;
    }

    /// <summary>gunzip 파이프로 넣어야 하는지. Import 만이면 파일 이름이 .gz 로 끝나는지, 아니면 Export 출력 방식이 gzip 인지.</summary>
    public static bool UsesGzip(DumpRequest req)
        => req.Job == JobKind.ImportOnly
            ? InputFileName(req).EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            : req.OutputMode == OutputMode.GzipPipe;

    /// <summary>"대상@host:port" 표시용 (공백 없음).</summary>
    public static string Describe(DumpRequest req)
    {
        var conn = EffectiveConnection(req);
        var host = string.IsNullOrWhiteSpace(conn.Host) ? "localhost" : conn.Host.Trim();
        var db = req.TargetMode == TargetMode.MultipleDatabases ? string.Join(",", TargetDatabases(req)) : TargetDatabase(req);
        return db + "@" + host + (conn.Port is int p ? ":" + p : "");
    }

    /// <summary>같은 서버의 같은 DB 로 넣으려는지 (Export 한 것을 제자리에 다시 넣는 실수 방지).</summary>
    private static bool IsSameAsSource(DumpRequest req, ImportTarget conn, string targetDb)
    {
        var sameServer = conn.SameConnectionAsSource ||
                         (string.Equals(conn.Host.Trim(), req.Host.Trim(), StringComparison.OrdinalIgnoreCase) && (conn.Port ?? 3306) == (req.Port ?? 3306));
        if (!sameServer) return false;
        var sourceDb = req.Databases.Select(d => d.Trim()).FirstOrDefault(d => d.Length > 0) ?? "";
        return targetDb.Length > 0 && string.Equals(targetDb, sourceDb, StringComparison.OrdinalIgnoreCase) && req.Job != JobKind.ImportOnly;
    }

    private static CommandLineGroup BinaryGroup(ImportTarget import)
    {
        var g = new CommandLineGroup("binary");
        g.Tokens.Add(CommandToken.Arg(string.IsNullOrWhiteSpace(import.BinaryPath) ? "mysql" : import.BinaryPath.Trim()));
        return g;
    }

    /// <summary>"원본과 같음"이면 원본의 TCP 강제(--protocol=TCP)도 따라간다. 안 그러면 Export 는 TCP 로, Import 는 소켓으로 다른 인스턴스에 붙을 수 있다.</summary>
    private static bool ForcesTcp(DumpRequest req) => req.Import.SameConnectionAsSource && req.IsChecked("protocol_tcp");

    private static CommandLineGroup ConnectionGroup(ImportTarget conn, bool forceTcp, ShellKind shell, List<BuildMessage> messages)
    {
        var g = new CommandLineGroup("connection");
        if (conn.PasswordMode == PasswordMode.DefaultsExtraFile)
        {
            if (string.IsNullOrWhiteSpace(conn.DefaultsExtraFile)) messages.Add(RuleMessages.Make("V_NO_EXTRA_FILE"));
            else g.Tokens.Add(CommandToken.FlagWithValue("--defaults-extra-file", conn.DefaultsExtraFile.Trim()));
        }
        if (conn.PasswordMode == PasswordMode.LoginPath)
        {
            if (string.IsNullOrWhiteSpace(conn.LoginPath)) messages.Add(RuleMessages.Make("V_NO_LOGIN_PATH"));
            else g.Tokens.Add(CommandToken.FlagWithValue("--login-path", conn.LoginPath.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(conn.Host)) { g.Tokens.Add(CommandToken.Flag("-h")); g.Tokens.Add(CommandToken.Arg(conn.Host.Trim())); }
        if (conn.Port is int port) { g.Tokens.Add(CommandToken.Flag("-P")); g.Tokens.Add(CommandToken.Arg(port.ToString())); }
        if (forceTcp) g.Tokens.Add(CommandToken.Flag("--protocol=TCP"));
        CommandBuilder.AddSocket(g, conn.Host, conn.Port, conn.Socket, forceTcp, shell, messages);

        if (conn.PasswordMode is PasswordMode.Prompt or PasswordMode.Plain)
        {
            if (!string.IsNullOrWhiteSpace(conn.User)) { g.Tokens.Add(CommandToken.Flag("-u")); g.Tokens.Add(CommandToken.Arg(conn.User.Trim())); }
            else messages.Add(RuleMessages.Make("R24_NO_USER"));
            if (conn.PasswordMode == PasswordMode.Prompt) g.Tokens.Add(CommandToken.Flag("-p"));
            else
            {
                g.Tokens.Add(CommandToken.FlagWithValue("--password", conn.PlainPassword));
                messages.Add(RuleMessages.Make("R15_PLAIN_PASSWORD"));
            }
        }
        return g;
    }

    private static CommandLineGroup OptionGroup(ImportTarget import)
    {
        var g = new CommandLineGroup("options");
        if (!string.IsNullOrWhiteSpace(import.Charset)) g.Tokens.Add(CommandToken.FlagWithValue("--default-character-set", import.Charset.Trim()));
        if (import.Force) g.Tokens.Add(CommandToken.Flag("--force"));
        if (import.DisableForeignKeyChecks) g.Tokens.Add(CommandToken.FlagWithValue("--init-command", "SET FOREIGN_KEY_CHECKS=0"));
        return g;
    }
}

/// <summary>
/// 한 작업(Export, Import 또는 둘 다)의 빌드 결과. 화면 미리보기와 스크립트 내보내기가 같은 것을 쓴다.
/// 여러 DB + Import 는 DB 마다 하위 작업(Parts)으로 쪼개진다. 그때 Export/Import 는 null 이고 메시지·명령어는 하위 작업을 모은 것이다.
/// </summary>
public sealed class JobResult
{
    /// <summary>이 결과를 만든 요청 (하위 작업이면 DB 하나짜리 요청).</summary>
    public DumpRequest Request { get; init; } = new();
    public BuildResult? Export { get; init; }
    public BuildResult? Import { get; init; }

    /// <summary>DB 별 하위 작업. 비어 있으면 Export/Import 가 이 작업의 전부다.</summary>
    public IReadOnlyList<JobResult> Parts { get; init; } = Array.Empty<JobResult>();

    /// <summary>쪼개기 자체에 대한 메시지 (대상 DB 개수 불일치 등). Import 메시지로 보여 준다.</summary>
    public IReadOnlyList<BuildMessage> OwnMessages { get; init; } = Array.Empty<BuildMessage>();

    /// <summary>실행할 단계 순서: 하위 작업이 있으면 그것들(하위의 하위까지 펼쳐서), 없으면 자기 자신.
    /// 여러 DB + Import 를 DB 별로 쪼갠 뒤 각 DB 가 다시 DDL/데이터로 쪼개질 수 있어 두 단계까지 중첩된다.</summary>
    public IReadOnlyList<JobResult> Steps => Parts.Count > 0 ? Parts.SelectMany(p => p.Steps).ToList() : new[] { this };

    public bool HasErrors => OwnMessages.Any(m => m.Severity == Severity.Error)
                             || (Export?.HasErrors ?? false) || (Import?.HasErrors ?? false)
                             || Parts.Any(p => p.HasErrors);

    /// <summary>Export 메시지 뒤에 Import 메시지. Import 쪽은 UI 가 접두사를 붙여 구분한다. 하위 작업의 같은 메시지는 한 번만.</summary>
    public IReadOnlyList<BuildMessage> ExportMessages
        => Export is not null ? Export.Messages : Distinct(Parts.SelectMany(p => p.ExportMessages));

    public IReadOnlyList<BuildMessage> ImportMessages
        => Import is not null ? Import.Messages : Distinct(OwnMessages.Concat(Parts.SelectMany(p => p.ImportMessages)));

    /// <summary>화면·클립보드용 텍스트: 단계마다 Export 명령, 빈 줄, Import 명령.</summary>
    public string CommandText
    {
        get
        {
            var parts = new List<string>();
            foreach (var step in Steps)
            {
                if (step.Export is { Command.Length: > 0 }) parts.Add(step.Export.Command);
                if (step.Import is { Command.Length: > 0 }) parts.Add(step.Import.Command);
            }
            return string.Join("\n\n", parts);
        }
    }

    private static List<BuildMessage> Distinct(IEnumerable<BuildMessage> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return messages.Where(m => seen.Add(m.Code + "|" + m.OptionId + "|" + m.Text.Ko))
            .OrderBy(m => m.Severity == Severity.Error ? 0 : m.Severity == Severity.Warning ? 1 : 2)
            .ToList();
    }
}

/// <summary>
/// DumpRequest 의 Job 에 따라 Export/Import 빌더를 호출한다.
/// 여러 DB + Import 는 DB 마다 단일 DB 요청으로 쪼개어 각각 Export/Import 를 만든다:
///   --databases 한 파일에는 CREATE DATABASE/USE 문이 들어 있어 대상 DB 이름을 바꿀 수 없기 때문이다.
///   대상 DB 는 쉼표 목록(Import.Database)을 원본 DB 와 순서대로 짝 맞추고, 비어 있으면 같은 이름을 쓴다.
/// </summary>
public sealed class JobBuilder
{
    private readonly CommandBuilder _export;
    private readonly ImportCommandBuilder _import = new();

    public JobBuilder(OptionSchema schema)
    {
        _export = new CommandBuilder(schema);
    }

    public static bool SplitsPerDatabase(DumpRequest req)
        => req.Job != JobKind.ExportOnly && req.TargetMode == TargetMode.MultipleDatabases;

    /// <summary>"DDL + DML 파일 분리" 인지. Import 만 하는 작업은 Export 가 없으니 쪼갤 것이 없다.</summary>
    public static bool SplitsContent(DumpRequest req)
        => req.Job != JobKind.ImportOnly && req.ContentMode == ContentMode.StructureAndDataSplit;

    /// <summary>파일명 템플릿에 접미사를 붙인다: 끝이 .sql 이면 그 앞에, 아니면 맨 뒤에. 예: {db}_{date}.sql → {db}_{date}_ddl.sql</summary>
    public static string WithSuffix(string template, string suffix)
    {
        var t = template ?? "";
        return t.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? t[..^4] + suffix + t[^4..] : t + suffix;
    }

    /// <summary>화면 "미리보기" 와 스크립트 목록 파일명 열에 쓸 파일 이름. 파일 분리면 "a_ddl.sql, a_data.sql".</summary>
    public static string FilePreview(DumpRequest req)
    {
        if (!SplitsContent(req)) return ImportCommandBuilder.InputFileName(req);
        string Part(string suffix)
        {
            var r = req.Clone();
            r.OutputFileTemplate = WithSuffix(req.OutputFileTemplate, suffix);
            return ImportCommandBuilder.InputFileName(r);
        }
        return Part("_ddl") + ", " + Part("_data");
    }

    public JobResult Build(DumpRequest req)
    {
        if (SplitsPerDatabase(req)) return BuildSplit(req);
        if (SplitsContent(req)) return BuildContentSplit(req);
        return new()
        {
            Request = req,
            Export = req.Job == JobKind.ImportOnly ? null : _export.Build(req),
            Import = req.Job == JobKind.ExportOnly ? null : _import.Build(req)
        };
    }

    private JobResult BuildSplit(DumpRequest req)
    {
        var sources = ImportCommandBuilder.SplitNames(string.Join(",", req.Databases));
        var targets = ImportCommandBuilder.TargetDatabases(req);
        var own = new List<BuildMessage>();

        if (sources.Count == 0) own.Add(RuleMessages.Make("V_NO_DATABASE"));
        else if (targets.Count != sources.Count) own.Add(RuleMessages.Make("I9_TARGET_COUNT_MISMATCH", null, targets.Count, sources.Count));
        own.Add(RuleMessages.Make("I8_SPLIT_PER_DB"));

        var parts = new List<JobResult>();
        if (!own.Any(m => m.Severity == Severity.Error))
        {
            for (var i = 0; i < sources.Count; i++)
            {
                var sub = req.Clone();
                sub.TargetMode = TargetMode.SingleDatabase;
                sub.Databases = new List<string> { sources[i] };
                sub.Import.Database = targets[i];
                // --databases 전용 옵션은 단일 DB 명령에 들어갈 수 없다 (I8 안내문이 설명한다).
                sub.Set("add_drop_database", false);
                sub.Set("no_create_db", false);
                parts.Add(Build(sub));
            }
        }
        return new JobResult { Request = req, Parts = parts, OwnMessages = own };
    }

    /// <summary>
    /// DDL + DML 파일 분리: 같은 요청을 DDL 단계(--no-data, 파일 _ddl)와 데이터 단계(--no-create-info, 파일 _data)로 나눈다.
    /// 루틴·이벤트·트리거는 DDL 파일에만 들어가도록 데이터 쪽에서 끈다 (데이터 파일을 넣을 때 트리거가 겹쳐 실패하지 않게).
    /// 데이터 쪽은 DB 를 지우거나 만들지 않는다 (--databases 모드에서 DDL 다음에 넣어도 안전하게).
    /// </summary>
    private JobResult BuildContentSplit(DumpRequest req)
    {
        var ddl = req.Clone();
        ddl.ContentMode = ContentMode.StructureOnly;
        ddl.IsContentSplitPart = true;
        ddl.OutputFileTemplate = WithSuffix(req.OutputFileTemplate, "_ddl");

        var data = req.Clone();
        data.ContentMode = ContentMode.DataOnly;
        data.IsContentSplitPart = true;
        data.OutputFileTemplate = WithSuffix(req.OutputFileTemplate, "_data");
        data.Set("routines", false);
        data.Set("events", false);
        data.Set("skip_triggers", true);
        data.Set("add_drop_database", false);
        if (req.TargetMode == TargetMode.MultipleDatabases) data.Set("no_create_db", true);

        return new JobResult { Request = req, Parts = new[] { Build(ddl), Build(data) } };
    }
}
