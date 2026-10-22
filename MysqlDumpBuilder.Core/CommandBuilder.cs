using System.Text.RegularExpressions;
using MysqlDumpBuilder.Core.Formatters;

namespace MysqlDumpBuilder.Core;

/// <summary>
/// DumpRequest(화면 상태) → mysqldump 명령어 + 경고 목록.
/// UI 를 전혀 모르는 순수 함수이며, 모든 규칙(R1~R16)은 여기서만 적용한다.
///
/// 옵션 배치 순서 (기획서 5장):
///   바이너리 → defaults-extra-file → 접속 → 문자셋/보안(3xx) → 호환성(4xx) → 일관성(5xx)
///   → 내용(6xx) → 형식(7xx) → 성능(8xx) → ignore-table → where → 대상 → 출력
/// 스키마의 Order 값이 그대로 이 순서를 결정한다. 같은 백 단위(예: 5xx)는 여러 줄 모드에서 한 줄에 놓인다.
/// </summary>
public sealed class CommandBuilder
{
    private readonly OptionSchema _schema;

    private const int OrderContentMode = 600;   // --no-data / --no-create-info
    private const int OrderInsertMode = 700;    // --insert-ignore / --replace

    private static readonly Regex IgnoreTablePattern = new(@"^[^\s.*%?]+\.[^\s.*%?]+$", RegexOptions.Compiled);

    public CommandBuilder(OptionSchema schema)
    {
        _schema = schema;
    }

    public BuildResult Build(DumpRequest req)
    {
        var messages = new List<BuildMessage>();
        var groups = new List<CommandLineGroup>();

        // 1) 효과적인 체크 상태 계산 (implies 적용) 및 규칙 검사
        var effective = ComputeEffectiveFlags(req, messages, out var suppressed);
        CheckConflicts(req, effective, messages);
        CheckModeRequirements(req, effective, messages);
        CheckClientSupport(req, effective, messages);
        ApplyContentModeRules(req, effective, suppressed, messages);

        // 2) 토큰 조립
        groups.Add(BuildBinaryGroup(req));
        groups.Add(BuildConnectionGroup(req, messages));
        groups.AddRange(BuildSchemaOptionGroups(req, effective, suppressed, messages));
        groups.Add(BuildIgnoreTableGroup(req, messages));
        groups.Add(BuildWhereGroup(req, messages));
        groups.Add(BuildTargetGroup(req, messages));
        groups.Add(BuildOutputGroup(req, messages));

        // 3) 포맷
        if (req.Shell == ShellKind.PowerShell &&
            groups.SelectMany(g => g.Tokens).Any(t => !t.Raw && !t.RawValue && (t.Value ?? t.Text).Contains('"')))
            messages.Add(RuleMessages.Make("R22_PS_DOUBLE_QUOTE_IN_VALUE"));

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

    // ------------------------------------------------------------------
    // 규칙
    // ------------------------------------------------------------------

    /// <summary>체크 상태에 implies 를 적용한다. compact 가 포함하는 옵션은 suppressed 에 넣어 따로 내보내지 않는다.</summary>
    private Dictionary<string, bool> ComputeEffectiveFlags(DumpRequest req, List<BuildMessage> messages, out HashSet<string> suppressed)
    {
        var effective = new Dictionary<string, bool>(StringComparer.Ordinal);
        suppressed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var o in _schema.Options)
            effective[o.Id] = req.IsChecked(o.Id);

        foreach (var o in _schema.Options.Where(o => o.Implies.Count > 0))
        {
            if (!effective[o.Id]) continue;

            foreach (var (targetId, value) in o.Implies)
            {
                var wasAlready = effective[targetId] == value;
                effective[targetId] = value;

                if (o.Id == "single_transaction" && targetId == "skip_lock_tables")
                {
                    messages.Add(RuleMessages.Make("R4_LOCK_TABLES_AUTO_OFF", o.Id));
                }
                else if (o.Id == "compact")
                {
                    suppressed.Add(targetId);
                }
                else if (!wasAlready)
                {
                    var target = _schema.Find(targetId);
                    messages.Add(RuleMessages.Make("R19_IMPLIED", o.Id, o.Flag, target?.Flag ?? targetId));
                }
            }

            if (o.Id == "compact")
                messages.Add(RuleMessages.Make("R7_COMPACT_IMPLIES", o.Id));
        }

        return effective;
    }

    /// <summary>R3 및 스키마의 conflicts: 함께 켜진 배타 옵션은 오류.</summary>
    private void CheckConflicts(DumpRequest req, Dictionary<string, bool> effective, List<BuildMessage> messages)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in _schema.Options)
        {
            if (!effective[o.Id]) continue;
            foreach (var otherId in o.Conflicts)
            {
                if (!effective.TryGetValue(otherId, out var on) || !on) continue;
                var pairKey = string.CompareOrdinal(o.Id, otherId) < 0 ? o.Id + "|" + otherId : otherId + "|" + o.Id;
                if (!reported.Add(pairKey)) continue;

                var other = _schema.Find(otherId);
                if (pairKey == "lock_all_tables|single_transaction")
                    messages.Add(RuleMessages.Make("R3_SINGLE_TX_VS_LOCK_ALL", o.Id));
                else
                    messages.Add(RuleMessages.Make("R18_CONFLICT", o.Id, o.Flag, other?.Flag ?? otherId));
            }
        }
    }

    /// <summary>R5: --databases 에서만 유효한 옵션.</summary>
    private void CheckModeRequirements(DumpRequest req, Dictionary<string, bool> effective, List<BuildMessage> messages)
    {
        var modeName = req.TargetMode.ToString();
        foreach (var o in _schema.Options.Where(o => o.RequiresMode.Count > 0))
        {
            if (!effective[o.Id]) continue;
            if (!o.RequiresMode.Contains(modeName, StringComparer.OrdinalIgnoreCase))
                messages.Add(RuleMessages.Make("R5_NEEDS_DATABASES_MODE", o.Id, o.Flag));
        }
    }

    /// <summary>R10: 선택한 클라이언트에 없는 옵션.</summary>
    private void CheckClientSupport(DumpRequest req, Dictionary<string, bool> effective, List<BuildMessage> messages)
    {
        foreach (var o in _schema.Options)
        {
            if (!effective[o.Id]) continue;
            if (!o.SupportsClient(req.Client))
                messages.Add(RuleMessages.Make("R10_OPTION_NOT_IN_CLIENT", o.Id, o.Flag, ClientLabel(req.Client)));
        }
    }

    /// <summary>R8, R9: 데이터만 덤프할 때의 특수 처리.</summary>
    private void ApplyContentModeRules(DumpRequest req, Dictionary<string, bool> effective, HashSet<string> suppressed, List<BuildMessage> messages)
    {
        if (req.ContentMode != ContentMode.DataOnly) return;

        if (effective.TryGetValue("skip_add_drop_table", out var skipDrop) && skipDrop && !suppressed.Contains("skip_add_drop_table"))
            messages.Add(RuleMessages.Make("R8_DROP_TABLE_MEANINGLESS", "skip_add_drop_table"));
        suppressed.Add("skip_add_drop_table");

        if (effective.TryGetValue("skip_triggers", out var skipTriggers) && !skipTriggers)
            messages.Add(RuleMessages.Make("R9_DATA_ONLY_WITH_TRIGGERS", "skip_triggers"));
    }

    // ------------------------------------------------------------------
    // 토큰 조립
    // ------------------------------------------------------------------

    private static CommandLineGroup BuildBinaryGroup(DumpRequest req)
    {
        var g = new CommandLineGroup("binary");
        var binary = string.IsNullOrWhiteSpace(req.BinaryPath) ? "mysqldump" : req.BinaryPath.Trim();
        g.Tokens.Add(CommandToken.Arg(binary));
        return g;
    }

    /// <summary>클라이언트가 소켓으로 접속하는 호스트인지 (localhost 이거나 비어 있음). 127.0.0.1 은 TCP 다.</summary>
    public static bool IsSocketHost(string? host)
        => string.IsNullOrWhiteSpace(host) || string.Equals(host.Trim(), "localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 소켓 경로(-S)와 localhost 포트 함정 경고. mysqldump 와 mysql(Import) 이 같이 쓴다.
    /// R26: localhost + 3306 아닌 포트 + 소켓 없음 + TCP 강제 아님 → 포트가 무시되고 기본 소켓 인스턴스로 붙는다 (bash 대상만. Windows 클라이언트는 localhost 도 TCP).
    /// R27: 소켓을 적었지만 TCP 강제라 쓰이지 않음. 호스트가 localhost 가 아니면 화면에서 소켓 칸이 잠기므로 조용히 뺀다.
    /// </summary>
    public static void AddSocket(CommandLineGroup g, string? host, int? port, string? socket, bool forceTcp, ShellKind shell, List<BuildMessage> messages)
    {
        if (!IsSocketHost(host)) return;
        var path = socket?.Trim() ?? "";
        if (path.Length > 0)
        {
            if (forceTcp) messages.Add(RuleMessages.Make("R27_SOCKET_IGNORED"));
            else
            {
                g.Tokens.Add(CommandToken.Flag("-S"));
                g.Tokens.Add(CommandToken.Arg(path));
            }
        }
        else if (!forceTcp && shell == ShellKind.Bash && port is int p && p != 3306)
        {
            messages.Add(RuleMessages.Make("R26_LOCALHOST_PORT", null, p));
        }
    }

    private static CommandLineGroup BuildConnectionGroup(DumpRequest req, List<BuildMessage> messages)
    {
        var g = new CommandLineGroup("connection");

        // R16: --defaults-extra-file 은 반드시 첫 번째 인자
        if (req.PasswordMode == PasswordMode.DefaultsExtraFile)
        {
            if (string.IsNullOrWhiteSpace(req.DefaultsExtraFile))
                messages.Add(RuleMessages.Make("V_NO_EXTRA_FILE"));
            else
            {
                g.Tokens.Add(CommandToken.FlagWithValue("--defaults-extra-file", req.DefaultsExtraFile.Trim()));
                messages.Add(RuleMessages.Make("R16_EXTRA_FILE_FIRST"));
            }
        }

        if (req.PasswordMode == PasswordMode.LoginPath)
        {
            if (string.IsNullOrWhiteSpace(req.LoginPath))
                messages.Add(RuleMessages.Make("V_NO_LOGIN_PATH"));
            else
                g.Tokens.Add(CommandToken.FlagWithValue("--login-path", req.LoginPath.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(req.Host))
        {
            g.Tokens.Add(CommandToken.Flag("-h"));
            g.Tokens.Add(CommandToken.Arg(req.Host.Trim()));
        }
        if (req.Port is int port)
        {
            g.Tokens.Add(CommandToken.Flag("-P"));
            g.Tokens.Add(CommandToken.Arg(port.ToString()));
        }
        AddSocket(g, req.Host, req.Port, req.Socket, req.IsChecked("protocol_tcp"), req.Shell, messages);

        // login-path / defaults-extra-file 모드에서는 사용자와 비밀번호를 그쪽에서 가져온다.
        var userAndPasswordInline = req.PasswordMode is PasswordMode.Prompt or PasswordMode.Plain;
        if (userAndPasswordInline)
        {
            if (!string.IsNullOrWhiteSpace(req.User))
            {
                g.Tokens.Add(CommandToken.Flag("-u"));
                g.Tokens.Add(CommandToken.Arg(req.User.Trim()));
            }
            else
            {
                messages.Add(RuleMessages.Make("R24_NO_USER"));
            }

            if (req.PasswordMode == PasswordMode.Prompt)
            {
                g.Tokens.Add(CommandToken.Flag("-p"));
            }
            else
            {
                g.Tokens.Add(CommandToken.FlagWithValue("--password", req.PlainPassword));
                messages.Add(RuleMessages.Make("R15_PLAIN_PASSWORD"));
            }
        }

        return g;
    }

    /// <summary>스키마 옵션 + 라디오(구조/데이터, INSERT 형태)를 Order 순으로 정렬해 백 단위로 줄을 나눈다.</summary>
    private IEnumerable<CommandLineGroup> BuildSchemaOptionGroups(DumpRequest req, Dictionary<string, bool> effective, HashSet<string> suppressed, List<BuildMessage> messages)
    {
        var ordered = new List<(int Order, CommandToken Token)>();
        // DDL 만 내보낼 때(--no-data) 데이터 전용 옵션(INSERT 형식, 행 단위 처리 등)은 뜻이 없으므로 내보내지 않는다. 화면도 잠근다.
        var ddlOnly = req.ContentMode == ContentMode.StructureOnly;
        var skippedForDdl = new List<string>();

        foreach (var o in _schema.Options)
        {
            if (suppressed.Contains(o.Id)) continue;
            if (ddlOnly && o.DataOnly)
            {
                if (effective[o.Id] && !o.DefaultChecked) skippedForDdl.Add(o.Flag);
                continue;
            }

            if (effective[o.Id])
            {
                if (o.IsValueType)
                {
                    var value = (req.ValueOf(o.Id) ?? o.DefaultValue ?? "").Trim();
                    if (value.Length == 0)
                    {
                        messages.Add(RuleMessages.Make("V_EMPTY_VALUE", o.Id, o.Flag));
                        continue;
                    }
                    // 기본값 생략 모드: 기본으로 켜진 값 옵션이 기본값 그대로면 내보내지 않는다.
                    if (!req.ExplicitDefaults && o.DefaultChecked && value == (o.DefaultValue ?? "")) continue;
                    ordered.Add((o.Order, CommandToken.FlagWithValue(o.Flag, value)));
                }
                else
                {
                    // 기본값 생략 모드: mysqldump 기본값과 같은 플래그(--quick 등)는 내보내지 않는다.
                    if (!req.ExplicitDefaults && o.DefaultChecked) continue;
                    ordered.Add((o.Order, CommandToken.Flag(o.Flag)));
                }
            }
            else if (req.ExplicitDefaults && !string.IsNullOrEmpty(o.UncheckedFlag))
            {
                ordered.Add((o.Order, CommandToken.Flag(o.UncheckedFlag)));
            }
        }

        switch (req.ContentMode)
        {
            case ContentMode.StructureOnly:
                ordered.Add((OrderContentMode, CommandToken.Flag("--no-data")));
                break;
            case ContentMode.DataOnly:
                ordered.Add((OrderContentMode, CommandToken.Flag("--no-create-info")));
                break;
        }

        switch (req.InsertMode)
        {
            case InsertMode.InsertIgnore when ddlOnly:
                skippedForDdl.Add("--insert-ignore");
                break;
            case InsertMode.Replace when ddlOnly:
                skippedForDdl.Add("--replace");
                break;
            case InsertMode.InsertIgnore:
                ordered.Add((OrderInsertMode, CommandToken.Flag("--insert-ignore")));
                break;
            case InsertMode.Replace:
                ordered.Add((OrderInsertMode, CommandToken.Flag("--replace")));
                break;
        }

        if (ddlOnly && !string.IsNullOrWhiteSpace(req.Where)) skippedForDdl.Add("--where");
        // 파일 분리의 DDL 쪽이면 생략된 데이터 옵션은 데이터 파일에 들어가므로 R23 대신 분리 안내를 낸다.
        if (req.IsContentSplitPart && ddlOnly)
            messages.Add(RuleMessages.Make("R25_CONTENT_SPLIT"));
        else if (skippedForDdl.Count > 0)
            messages.Add(RuleMessages.Make("R23_DDL_SKIPS_DML", null, string.Join(", ", skippedForDdl)));

        return ordered
            .OrderBy(t => t.Order)
            .GroupBy(t => t.Order / 100)
            .Select(bucket =>
            {
                var g = new CommandLineGroup(BucketName(bucket.Key));
                g.Tokens.AddRange(bucket.Select(t => t.Token));
                return g;
            });
    }

    private static string BucketName(int bucket) => bucket switch
    {
        3 => "charset-security",
        4 => "compatibility",
        5 => "consistency",
        6 => "content",
        7 => "format",
        8 => "performance",
        _ => "options-" + bucket
    };

    private static CommandLineGroup BuildIgnoreTableGroup(DumpRequest req, List<BuildMessage> messages)
    {
        var g = new CommandLineGroup("ignore-tables");
        // 테이블을 직접 지정하는 모드에서는 제외 목록이 의미가 없다 (화면도 입력란을 잠근다).
        if (req.TargetMode == TargetMode.Tables) return g;
        var defaultDb = req.TargetMode is TargetMode.SingleDatabase or TargetMode.Tables
            ? req.Databases.FirstOrDefault()?.Trim()
            : null;

        foreach (var raw in req.IgnoreTables)
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;

            if (!entry.Contains('.') && !string.IsNullOrEmpty(defaultDb))
            {
                var qualified = defaultDb + "." + entry;
                messages.Add(RuleMessages.Make("R20_IGNORE_TABLE_PREFIXED", null, entry, qualified));
                entry = qualified;
            }

            if (!IgnoreTablePattern.IsMatch(entry))
            {
                messages.Add(RuleMessages.Make("V_IGNORE_TABLE_FORMAT", null, entry));
                continue;
            }
            g.Tokens.Add(CommandToken.FlagWithValue("--ignore-table", entry));
        }
        return g;
    }

    private static CommandLineGroup BuildWhereGroup(DumpRequest req, List<BuildMessage> messages)
    {
        var g = new CommandLineGroup("where");
        if (string.IsNullOrWhiteSpace(req.Where)) return g;
        if (req.ContentMode == ContentMode.StructureOnly) return g;   // DDL 만: 행 조건은 뜻이 없다 (R23 안내)

        if (req.TargetMode != TargetMode.Tables)
            messages.Add(RuleMessages.Make("R6_WHERE_WITHOUT_TABLES"));

        g.Tokens.Add(CommandToken.FlagWithValue("--where", req.Where.Trim()));
        return g;
    }

    private static CommandLineGroup BuildTargetGroup(DumpRequest req, List<BuildMessage> messages)
    {
        var g = new CommandLineGroup("target");
        var dbs = req.Databases.Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
        var tables = req.Tables.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

        switch (req.TargetMode)
        {
            case TargetMode.SingleDatabase:
                if (dbs.Count == 0) { messages.Add(RuleMessages.Make("V_NO_DATABASE")); break; }
                g.Tokens.Add(CommandToken.Arg(dbs[0]));
                break;

            case TargetMode.Tables:
                if (dbs.Count == 0) { messages.Add(RuleMessages.Make("V_NO_DATABASE")); break; }
                if (tables.Count == 0) { messages.Add(RuleMessages.Make("V_NO_TABLES")); break; }
                g.Tokens.Add(CommandToken.Arg(dbs[0]));
                foreach (var t in tables) g.Tokens.Add(CommandToken.Arg(t));
                break;

            case TargetMode.MultipleDatabases:
                if (dbs.Count == 0) { messages.Add(RuleMessages.Make("V_NO_DATABASE")); break; }
                g.Tokens.Add(CommandToken.Flag("--databases"));
                foreach (var d in dbs) g.Tokens.Add(CommandToken.Arg(d));
                break;
        }
        return g;
    }

    private static CommandLineGroup BuildOutputGroup(DumpRequest req, List<BuildMessage> messages)
    {
        var g = new CommandLineGroup("output");
        var fileName = ExpandOutputFileName(req);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            messages.Add(RuleMessages.Make("V_NO_OUTPUT_FILE"));
            return g;
        }

        // 스크립트 내보내기: 파일명 자리에 셸 변수를 그대로 넣는다 (.gz 붙이기는 변수를 정의하는 쪽이 책임진다).
        var raw = req.OutputFileShellExpression;
        CommandToken FileToken(string name) => raw is null ? CommandToken.Arg(name) : CommandToken.Shell(raw);

        switch (req.OutputMode)
        {
            case OutputMode.ResultFile:
                g.Tokens.Add(raw is null
                    ? CommandToken.FlagWithValue("--result-file", fileName)
                    : CommandToken.FlagWithRawValue("--result-file", raw));
                break;

            case OutputMode.StdoutRedirect:
                // PowerShell 의 > 는 mysqldump 출력을 문자열로 받아 다시 쓴다 (5.1 은 UTF-16, 한글은 콘솔 코드페이지로 깨짐).
                // 그 파일은 mysql 이 읽지 못하므로('\0' 오류) PowerShell 에서는 바이트 그대로 쓰는 --result-file 로 바꾼다.
                if (req.Shell == ShellKind.PowerShell)
                {
                    messages.Add(RuleMessages.Make("R17_PS_REDIRECT_UTF16"));
                    g.Tokens.Add(raw is null
                        ? CommandToken.FlagWithValue("--result-file", fileName)
                        : CommandToken.FlagWithRawValue("--result-file", raw));
                    break;
                }
                g.Tokens.Add(CommandToken.Shell(">"));
                g.Tokens.Add(FileToken(fileName));
                break;

            case OutputMode.GzipPipe:
                if (req.Shell != ShellKind.Bash)
                {
                    messages.Add(RuleMessages.Make("V_GZIP_NOT_BASH"));
                    break;
                }
                g.Tokens.Add(CommandToken.Shell("|"));
                g.Tokens.Add(CommandToken.Shell("gzip"));
                g.Tokens.Add(CommandToken.Shell(">"));
                g.Tokens.Add(FileToken(GzipName(fileName)));
                break;
        }
        return g;
    }

    /// <summary>gzip 파이프 출력의 파일명: .gz 가 없으면 붙인다.</summary>
    public static string GzipName(string fileName)
        => fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".gz";

    /// <summary>파일명 템플릿의 {db} 는 첫 DB 이름이다.</summary>
    public static string ExpandOutputFileName(DumpRequest req)
    {
        var db = req.Databases.Select(d => d.Trim()).FirstOrDefault(d => d.Length > 0) ?? "";
        return FileNameTemplate.Expand(req.OutputFileTemplate ?? "", db, req.Host ?? "", req.Now).Trim();
    }

    private static string ClientLabel(ClientKind client) => client switch
    {
        ClientKind.Mysql57 => "MySQL 5.7",
        ClientKind.Mysql80 => "MySQL 8.0+",
        _ => client.ToString()
    };
}
