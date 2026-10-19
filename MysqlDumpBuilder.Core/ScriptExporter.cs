using System.Text;

namespace MysqlDumpBuilder.Core;

/// <summary>스크립트 내보내기 결과. 어느 항목이든 오류가 있으면 Content 는 빈 문자열이다.</summary>
public sealed class ScriptExportResult
{
    public string Content { get; init; } = "";
    public string SuggestedFileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public Encoding Encoding { get; init; } = new UTF8Encoding(false);

    /// <summary>항목별 결과 (Export 목록이면 항목 수만큼, 아니면 하나).</summary>
    public IReadOnlyList<JobResult> Jobs { get; init; } = Array.Empty<JobResult>();

    /// <summary>첫 항목의 Export 빌드 결과 (항목이 하나일 때 쓰기 편하도록).</summary>
    public BuildResult Build => Jobs.Count > 0 ? Jobs[0].Export ?? new BuildResult() : new BuildResult();

    public bool HasErrors => Jobs.Any(j => j.HasErrors);

    /// <summary>파일로 저장한다. 줄바꿈과 인코딩은 셸에 맞게 이미 정해져 있다.</summary>
    public void SaveTo(string path) => File.WriteAllText(path, Content, Encoding);
}

/// <summary>
/// 요청을 바로 실행 가능한 스크립트 파일(.sh / .bat / .ps1)로 만든다. 요청이 여러 개(Export 목록)면 한 파일에 순서대로 넣는다.
///  - 항목마다 Export 단계(mysqldump → 파일)와 Import 단계(파일 → mysql)가 작업 종류에 따라 들어간다.
///  - 날짜·시간 토큰은 스크립트 실행 시점에 치환된다 (OUT_FILE 변수). Import 만 하는 단계는 OUT_FILE 이 "읽을 파일" 그대로다.
///  - 실패하면 0 이 아닌 코드로 종료하고(목록이면 그 자리에서 멈춤), 완료 시 파일명과 소요 시간을 찍는다.
///  - 주석과 실행 중 메시지는 모두 영문 ASCII 다. DB 서버는 로케일이 C/POSIX 이거나 터미널이 UTF-8 이 아닌 경우가 많아 한글·특수문자가 깨진다.
///  - .sh 는 LF + UTF-8(BOM 없음), .ps1 은 CRLF + UTF-8(BOM, Windows PowerShell 5.1 호환),
///    .bat 는 CRLF + ASCII (cmd 는 코드페이지 문제로 한글 주석을 깨뜨리므로 영문만 쓴다).
/// </summary>
public sealed class ScriptExporter
{
    public const string DefaultScriptFileTemplate = "mysqldump_{db}";

    private readonly JobBuilder _jobs;

    public ScriptExporter(OptionSchema schema)
    {
        _jobs = new JobBuilder(schema);
    }

    /// <summary>스크립트 한 단계: 요청(DB 하나), 빌드 결과, 셸용 출력 파일명 식, cmd 전용 준비 줄.</summary>
    private sealed record Item(DumpRequest Request, JobResult Job, string FileExpr, List<string> SetupLines)
    {
        public bool HasExport => Request.Job != JobKind.ImportOnly;
        public bool HasImport => Request.Job != JobKind.ExportOnly;
    }

    public static string ExtensionFor(ShellKind shell) => shell switch { ShellKind.Bash => ".sh", ShellKind.Cmd => ".bat", _ => ".ps1" };

    /// <summary>
    /// 스크립트 파일명: 스크립트명 템플릿을 지금 시각으로 치환하고 대상 OS 확장자를 붙인다.
    /// 템플릿의 {db} 는 출력 파일명과 같은 규칙(첫 DB 이름)이고, DB 가 비어 있으면 backup 이다.
    /// </summary>
    public static string ScriptFileName(DumpRequest req, ShellKind shell, string? dbOverride = null)
    {
        var db = dbOverride ?? DbNameForTemplate(req);
        if (string.IsNullOrEmpty(db)) db = "backup";
        var template = string.IsNullOrWhiteSpace(req.ScriptFileTemplate) ? DefaultScriptFileTemplate : req.ScriptFileTemplate.Trim();
        var name = FileNameTemplate.Expand(template, db, req.Host ?? "", req.Now).Trim();
        if (name.Length == 0) name = "mysqldump_" + db;
        var ext = ExtensionFor(shell);
        return name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
    }

    public ScriptExportResult Export(DumpRequest original, ShellKind shell)
        => ExportMany(new[] { original }, shell);

    /// <summary>여러 요청을 한 스크립트로. 둘 이상이면 파일명의 {db} 는 batch 가 된다.</summary>
    public ScriptExportResult ExportMany(IReadOnlyList<DumpRequest> originals, ShellKind shell)
    {
        if (originals.Count == 0) throw new ArgumentException("내보낼 요청이 없습니다.", nameof(originals));

        // 요청 하나가 여러 단계(여러 DB + Import 는 DB 마다 한 단계)가 될 수 있다. 머리말은 요청 단위, 본문은 단계 단위다.
        var jobs = new List<JobResult>();
        var items = new List<Item>();
        foreach (var original in originals)
        {
            var req = original.Clone();
            req.Shell = shell;
            req.MultiLine = true;
            req.OutputFileShellExpression = shell switch
            {
                ShellKind.Bash => "\"$OUT_FILE\"",
                ShellKind.Cmd => "\"%OUT_FILE%\"",
                _ => "\"$OutFile\""
            };

            var job = _jobs.Build(req);
            jobs.Add(job);
            foreach (var step in job.Steps)
            {
                var fileExpr = "";
                var setupLines = new List<string>();
                if (!job.HasErrors && step.Request.Job == JobKind.ImportOnly)
                {
                    // 읽을 파일은 이미 있는 파일이므로 날짜 토큰을 실행 시점으로 미루지 않고 그대로 쓴다.
                    fileExpr = ImportCommandBuilder.InputFileName(step.Request);
                }
                else if (!job.HasErrors)
                {
                    fileExpr = FileNameTemplate.ToShellExpression(step.Request.OutputFileTemplate ?? "", DbNameForTemplate(step.Request), req.Host ?? "", shell, out setupLines);
                    if (req.OutputMode == OutputMode.GzipPipe) fileExpr = CommandBuilder.GzipName(fileExpr);
                }
                items.Add(new Item(step.Request, step, fileExpr, setupLines));
            }
        }

        var ext = ExtensionFor(shell);
        var first = jobs[0].Request;
        var fileName = ScriptFileName(first, shell, jobs.Count > 1 ? "batch" : null);

        if (jobs.Any(j => j.HasErrors))
            return new ScriptExportResult { Jobs = jobs, Extension = ext, SuggestedFileName = fileName };

        var header = HeaderLines(jobs);
        var content = shell switch
        {
            ShellKind.Bash => BuildBash(items, header, fileName),
            ShellKind.Cmd => BuildCmd(items, header, fileName),
            _ => BuildPowerShell(items, header, fileName)
        };

        return new ScriptExportResult
        {
            Content = content,
            Jobs = jobs,
            Extension = ext,
            SuggestedFileName = fileName,
            Encoding = shell == ShellKind.PowerShell ? new UTF8Encoding(true) : shell == ShellKind.Cmd ? Encoding.ASCII : new UTF8Encoding(false)
        };
    }

    private static string DbNameForTemplate(DumpRequest req)
        => req.Databases.Select(d => d.Trim()).FirstOrDefault(d => d.Length > 0) ?? "";

    private static string Source(DumpRequest req)
    {
        // 공백 없이: "shop", "a,b", "shop[t1,t2]"
        var dbs = string.Join(",", req.Databases.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()));
        var tables = string.Join(",", req.Tables.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        var what = req.TargetMode == TargetMode.Tables && tables.Length > 0 ? dbs + "[" + tables + "]" : dbs;
        return what + "@" + (string.IsNullOrWhiteSpace(req.Host) ? "localhost" : req.Host.Trim()) + (req.Port is int p ? ":" + p : "");
    }

    /// <summary>항목 한 줄 요약: "shop@src -> shop@dst" 처럼.</summary>
    private static string Describe(Item item)
    {
        var parts = new List<string>();
        if (item.HasExport) parts.Add(Source(item.Request));
        if (item.HasImport) parts.Add(ImportCommandBuilder.Describe(item.Request));
        return string.Join(" -> ", parts);
    }

    /// <summary>머리말 주석 줄들 (주석 기호 없이, 요청 단위). 세 셸이 같은 문구를 쓴다. 셸별 추가 줄은 각 빌더가 붙인다.</summary>
    private static List<string> HeaderLines(List<JobResult> jobs)
    {
        var first = jobs[0].Request;
        var lines = new List<string> { "Generated by MysqlDumpBuilder on " + first.Now.ToString("yyyy-MM-dd HH:mm") };
        for (var i = 0; i < jobs.Count; i++)
        {
            var req = jobs[i].Request;
            var suffix = jobs.Count == 1 ? "" : $" {i + 1}/{jobs.Count}";
            if (req.Job != JobKind.ImportOnly) lines.Add("Source DB" + suffix + ": " + Source(req));
            if (req.Job == JobKind.ImportOnly) lines.Add("Input file" + suffix + ": " + req.Import.InputFile.Trim());
            if (req.Job != JobKind.ExportOnly) lines.Add("Target DB" + suffix + ": " + ImportCommandBuilder.Describe(req));
        }
        return lines;
    }

    private static bool NeedsPasswordNote(List<Item> items)
        => items.Any(i => (i.HasExport && i.Request.PasswordMode == PasswordMode.Prompt)
                          || (i.HasImport && ImportCommandBuilder.EffectiveConnection(i.Request).PasswordMode == PasswordMode.Prompt));

    private static IEnumerable<string> Header(List<string> header, List<Item> items, string? extraLine)
    {
        foreach (var line in header) yield return line;
        if (extraLine is not null) yield return extraLine;
        if (NeedsPasswordNote(items)) yield return "Note: -p asks for the password at run time. For cron or scheduled runs, use --login-path.";
    }

    /// <summary>Import 명령은 "DB 생성" 줄이 앞에 붙을 수 있어 빈 줄로 나뉜 명령 여러 개일 수 있다.</summary>
    private static IEnumerable<string> ImportCommands(Item item)
        => (item.Job.Import?.Command ?? "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

    // ------------------------------------------------------------------

    private static string BuildBash(List<Item> items, List<string> header, string fileName)
    {
        var sb = new StringBuilder();
        sb.Append("#!/usr/bin/env bash\n");
        foreach (var line in Header(header, items, "Required: chmod +x " + fileName)) sb.Append("# ").Append(line).Append('\n');
        sb.Append("set -euo pipefail\n");
        // $? 는 $(date) 뒤에 읽으면 0 이 되므로 trap 첫머리에서 rc 로 잡아 둔다.
        sb.Append("trap 'rc=$?; echo \"[$(date \"+%F %T\")] ERROR: failed (exit $rc)\" >&2' ERR\n");

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.Append('\n');
            if (items.Count > 1) sb.Append("# ---- ").Append(i + 1).Append('/').Append(items.Count).Append(": ").Append(Describe(item)).Append(" ----\n");
            sb.Append("OUT_FILE=\"").Append(item.FileExpr).Append("\"\n");
            if (item.HasExport)
            {
                sb.Append("START=$(date +%s)\n");
                sb.Append("echo \"[$(date \"+%F %T\")] Export started -> $OUT_FILE\"\n");
                sb.Append(item.Job.Export!.Command).Append('\n');
                sb.Append("echo \"[$(date \"+%F %T\")] Export done: $OUT_FILE ($(du -h \"$OUT_FILE\" | cut -f1), $(( $(date +%s) - START ))s)\"\n");
            }
            if (item.HasImport)
            {
                var target = ImportCommandBuilder.Describe(item.Request);
                sb.Append("START=$(date +%s)\n");
                sb.Append("echo \"[$(date \"+%F %T\")] Import started: $OUT_FILE -> ").Append(target).Append("\"\n");
                foreach (var cmd in ImportCommands(item)) sb.Append(cmd).Append('\n');
                sb.Append("echo \"[$(date \"+%F %T\")] Import done: ").Append(target).Append(" ($(( $(date +%s) - START ))s)\"\n");
            }
        }
        return sb.ToString();
    }

    private static string BuildCmd(List<Item> items, List<string> header, string fileName)
    {
        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        foreach (var line in Header(header, items, null)) sb.Append("REM ").Append(line).Append("\r\n");
        sb.Append("setlocal\r\n");

        void CheckError(string stage)
        {
            sb.Append("if errorlevel 1 (\r\n");
            sb.Append("  echo [%date% %time%] ERROR: ").Append(stage).Append(" failed with exit code %errorlevel% 1>&2\r\n");
            sb.Append("  exit /b %errorlevel%\r\n");
            sb.Append(")\r\n");
        }

        // 배치 파일 안에서 % 는 변수 기호라서 리터럴 % 는 %% 로 써야 한다 (WHERE 의 LIKE '%x%' 등). 우리 변수만 되돌린다.
        static string Escape(string command) => command.Replace("%", "%%").Replace("\"%%OUT_FILE%%\"", "\"%OUT_FILE%\"").Replace("\n", "\r\n");

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.Append("\r\n");
            if (items.Count > 1) sb.Append("REM ---- ").Append(i + 1).Append('/').Append(items.Count).Append(": ").Append(Describe(item)).Append(" ----\r\n");
            foreach (var line in item.SetupLines) sb.Append(line).Append("\r\n");
            sb.Append("set \"OUT_FILE=").Append(item.FileExpr).Append("\"\r\n");
            if (item.HasExport)
            {
                sb.Append("echo [%date% %time%] Export started -^> %OUT_FILE%\r\n");
                sb.Append(Escape(item.Job.Export!.Command)).Append("\r\n");
                CheckError("mysqldump");
                sb.Append("echo [%date% %time%] Export done: %OUT_FILE%\r\n");
            }
            if (item.HasImport)
            {
                var target = ImportCommandBuilder.Describe(item.Request);
                sb.Append("echo [%date% %time%] Import started: %OUT_FILE% -^> ").Append(target).Append("\r\n");
                foreach (var cmd in ImportCommands(item))
                {
                    sb.Append(Escape(cmd)).Append("\r\n");
                    CheckError("mysql");
                }
                sb.Append("echo [%date% %time%] Import done: ").Append(target).Append("\r\n");
            }
        }
        sb.Append("endlocal\r\n");
        return sb.ToString();
    }

    private static string BuildPowerShell(List<Item> items, List<string> header, string fileName)
    {
        var sb = new StringBuilder();
        foreach (var line in Header(header, items, "If execution is blocked: powershell -ExecutionPolicy Bypass -File " + fileName)) sb.Append("# ").Append(line).Append("\r\n");
        sb.Append("$ErrorActionPreference = 'Stop'\r\n");

        void CheckError(string stage)
        {
            // Write-Error 는 ErrorActionPreference=Stop 에서 즉시 종료(코드 1)되므로 쓰지 않는다. 종료 코드를 그대로 돌려준다.
            sb.Append("if ($LASTEXITCODE -ne 0) {\r\n");
            sb.Append("    [Console]::Error.WriteLine(\"[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] ERROR: ").Append(stage).Append(" failed (exit $LASTEXITCODE)\")\r\n");
            sb.Append("    exit $LASTEXITCODE\r\n");
            sb.Append("}\r\n");
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.Append("\r\n");
            if (items.Count > 1) sb.Append("# ---- ").Append(i + 1).Append('/').Append(items.Count).Append(": ").Append(Describe(item)).Append(" ----\r\n");
            sb.Append("$OutFile = \"").Append(item.FileExpr).Append("\"\r\n");
            if (item.HasExport)
            {
                sb.Append("$start = Get-Date\r\n");
                sb.Append("Write-Host \"[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Export started -> $OutFile\"\r\n");
                sb.Append(item.Job.Export!.Command.Replace("\n", "\r\n")).Append("\r\n");
                CheckError("mysqldump");
                sb.Append("$elapsed = [int]((Get-Date) - $start).TotalSeconds\r\n");
                sb.Append("Write-Host \"[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Export done: $OutFile (${elapsed}s)\"\r\n");
            }
            if (item.HasImport)
            {
                var target = ImportCommandBuilder.Describe(item.Request);
                sb.Append("$start = Get-Date\r\n");
                sb.Append("Write-Host \"[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Import started: $OutFile -> ").Append(target).Append("\"\r\n");
                foreach (var cmd in ImportCommands(item))
                {
                    sb.Append(cmd.Replace("\n", "\r\n")).Append("\r\n");
                    CheckError("mysql");
                }
                sb.Append("$elapsed = [int]((Get-Date) - $start).TotalSeconds\r\n");
                sb.Append("Write-Host \"[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Import done: ").Append(target).Append(" (${elapsed}s)\"\r\n");
            }
        }
        return sb.ToString();
    }
}
