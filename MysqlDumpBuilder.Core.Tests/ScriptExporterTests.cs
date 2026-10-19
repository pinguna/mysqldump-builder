using System.Text;
using MysqlDumpBuilder.Core;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>2단계 FR-206/207: .sh / .bat / .ps1 스크립트 내보내기.</summary>
public class ScriptExporterTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();

    private static DumpRequest NewRequest()
    {
        var req = new DumpRequest
        {
            Host = "10.0.1.20",
            User = "backup",
            OutputMode = OutputMode.ResultFile,
            Databases = { "shop" },
            Now = new DateTime(2026, 10, 6, 14, 30, 0)
        };
        req.ApplySchemaDefaults(Schema);
        req.Set("single_transaction", true);
        return req;
    }

    private static ScriptExportResult Export(DumpRequest req, ShellKind shell)
        => new ScriptExporter(Schema).Export(req, shell);

    [Fact]
    public void bash_스크립트는_실행_시점_날짜와_실패_처리를_포함한다()
    {
        var r = Export(NewRequest(), ShellKind.Bash);

        Assert.False(r.HasErrors);
        Assert.Equal(".sh", r.Extension);
        Assert.Equal("mysqldump_shop.sh", r.SuggestedFileName);
        Assert.StartsWith("#!/usr/bin/env bash\n", r.Content);
        Assert.Contains("set -euo pipefail", r.Content);
        Assert.Contains("OUT_FILE=\"shop_$(date +%Y%m%d)_$(date +%H%M).sql\"", r.Content);
        Assert.Contains("--result-file=\"$OUT_FILE\"", r.Content);
        Assert.Contains("trap 'rc=$?; ", r.Content);
        Assert.Contains("chmod +x", r.Content);
        Assert.DoesNotContain("\r", r.Content);                   // LF 전용
        Assert.False(r.Encoding.GetPreamble().Length > 0);        // BOM 없음
    }

    [Fact]
    public void bash_gzip_출력은_변수에_gz가_붙고_파이프로_쓴다()
    {
        var req = NewRequest();
        req.OutputMode = OutputMode.GzipPipe;

        var r = Export(req, ShellKind.Bash);

        Assert.Contains("OUT_FILE=\"shop_$(date +%Y%m%d)_$(date +%H%M).sql.gz\"", r.Content);
        Assert.Contains("| gzip > \"$OUT_FILE\"", r.Content);
    }

    [Fact]
    public void bat_스크립트는_영문만_쓰고_powershell로_날짜를_만든다()
    {
        var r = Export(NewRequest(), ShellKind.Cmd);

        Assert.Equal(".bat", r.Extension);
        Assert.StartsWith("@echo off\r\n", r.Content);
        Assert.Contains("for /f %%i in ('powershell -NoProfile -Command \"Get-Date -Format yyyyMMdd\"') do set \"STAMP1=%%i\"", r.Content);
        Assert.Contains("for /f %%i in ('powershell -NoProfile -Command \"Get-Date -Format HHmm\"') do set \"STAMP2=%%i\"", r.Content);
        Assert.Contains("set \"OUT_FILE=shop_%STAMP1%_%STAMP2%.sql\"", r.Content);
        Assert.Contains("--result-file=\"%OUT_FILE%\"", r.Content);
        Assert.Contains("if errorlevel 1 (", r.Content);
        Assert.Contains("exit /b %errorlevel%", r.Content);
        Assert.All(r.Content, ch => Assert.True(ch < 128, $"non-ASCII char in .bat: {ch}"));

        // 배치 파일에서는 리터럴 % 가 %% 로 바뀌되, OUT_FILE 변수는 그대로다.
        var req = NewRequest();
        req.TargetMode = TargetMode.Tables;
        req.Tables.Add("t");
        req.Where = "name LIKE 'A%'";
        var r2 = Export(req, ShellKind.Cmd);
        Assert.Contains("--where=\"name LIKE 'A%%'\"", r2.Content);
        Assert.Contains("--result-file=\"%OUT_FILE%\"", r2.Content);
        Assert.DoesNotContain("\n", r.Content.Replace("\r\n", ""));  // CRLF 전용
    }

    [Fact]
    public void ps1_스크립트는_Get_Date와_LASTEXITCODE_검사를_포함한다()
    {
        var r = Export(NewRequest(), ShellKind.PowerShell);

        Assert.Equal(".ps1", r.Extension);
        Assert.Contains("$ErrorActionPreference = 'Stop'", r.Content);
        Assert.Contains("$OutFile = \"shop_$(Get-Date -Format 'yyyyMMdd')_$(Get-Date -Format 'HHmm').sql\"", r.Content);
        Assert.Contains("--result-file=\"$OutFile\"", r.Content);
        Assert.Contains("if ($LASTEXITCODE -ne 0)", r.Content);
        Assert.True(r.Encoding.GetPreamble().Length > 0);         // BOM 있음 (Windows PowerShell 5.1 호환)
        Assert.Contains("\r\n", r.Content);
    }

    [Fact]
    public void ps1_실행파일_경로에_공백이_있으면_호출_연산자를_붙인다()
    {
        var req = NewRequest();
        req.BinaryPath = @"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe";

        var r = Export(req, ShellKind.PowerShell);

        Assert.Contains("& 'C:\\Program Files\\MySQL\\MySQL Server 8.0\\bin\\mysqldump.exe' `\r\n  -h 10.0.1.20", r.Content);
    }

    [Fact]
    public void 비밀번호_프롬프트_모드면_자동_실행_주의_문구가_들어간다()
    {
        var r = Export(NewRequest(), ShellKind.Bash);
        Assert.Contains("--login-path", r.Content);

        var req = NewRequest();
        req.PasswordMode = PasswordMode.LoginPath;
        req.LoginPath = "prod";
        var r2 = Export(req, ShellKind.Bash);
        Assert.DoesNotContain("cron", r2.Content);
    }

    [Fact]
    public void 오류가_있으면_내용_없이_오류만_돌려준다()
    {
        var req = NewRequest();
        req.Databases.Clear();

        var r = Export(req, ShellKind.Bash);

        Assert.True(r.HasErrors);
        Assert.Equal("", r.Content);
        Assert.Contains(r.Build.Messages, m => m.Code == "V_NO_DATABASE");
    }

    [Fact]
    public void 사용자_정의_날짜_형식도_셸_식으로_바뀐다()
    {
        var req = NewRequest();
        req.OutputFileTemplate = "{host}_{db}_{date:yyyy-MM-dd}_{time:HHmmss}.sql";

        Assert.Contains("OUT_FILE=\"10.0.1.20_shop_$(date +%Y-%m-%d)_$(date +%H%M%S).sql\"", Export(req, ShellKind.Bash).Content);
        Assert.Contains("$OutFile = \"10.0.1.20_shop_$(Get-Date -Format 'yyyy-MM-dd')_$(Get-Date -Format 'HHmmss').sql\"", Export(req, ShellKind.PowerShell).Content);
        Assert.Equal("%Y-%m-%d_%H%M%S", FileNameTemplate.ToStrftime("yyyy-MM-dd_HHmmss"));
    }

    /// <summary>사람이 열어 보거나 bash -n / PowerShell 파서로 검사할 수 있도록 샘플 3종을 bin 폴더에 남긴다.</summary>
    [Fact]
    public void 스크립트_샘플을_파일로_남긴다()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "script-samples");
        Directory.CreateDirectory(dir);

        var req = NewRequest();
        req.BinaryPath = @"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe";
        req.TargetMode = TargetMode.Tables;
        req.Tables.AddRange(new[] { "orders", "order_items" });
        req.Where = "created_at >= '2026-01-01' AND status = \"paid\"";
        req.IgnoreTables.Add("shop.access_log");

        foreach (var shell in Enum.GetValues<ShellKind>())
        {
            if (shell == ShellKind.Bash) req.BinaryPath = "mysqldump";
            var r = Export(req, shell);
            Assert.False(r.HasErrors, string.Join("\n", r.Build.Messages));
            r.SaveTo(Path.Combine(dir, r.SuggestedFileName));
        }
        Assert.True(File.Exists(Path.Combine(dir, "mysqldump_shop.sh")));
    }

    [Fact]
    public void 화면용_명령어는_스크립트_변수의_영향을_받지_않는다()
    {
        var req = NewRequest();
        _ = Export(req, ShellKind.Bash);

        var plain = new CommandBuilder(Schema).Build(req);
        Assert.Contains("--result-file=shop_20261006_1430.sql", plain.Command);
        Assert.Null(req.OutputFileShellExpression);
    }

    [Fact]
    public void 스크립트명은_템플릿을_치환하고_OS_확장자를_붙인다()
    {
        var req = NewRequest();
        Assert.Equal("mysqldump_shop.sh", ScriptExporter.ScriptFileName(req, ShellKind.Bash));
        Assert.Equal("mysqldump_shop.bat", ScriptExporter.ScriptFileName(req, ShellKind.Cmd));

        req.ScriptFileTemplate = "dump_{db}_{date:yyyyMMdd}";
        Assert.Equal("dump_shop_20261006.ps1", ScriptExporter.ScriptFileName(req, ShellKind.PowerShell));

        req.ScriptFileTemplate = "backup.sh";   // 확장자를 이미 썼으면 두 번 붙이지 않는다
        Assert.Equal("backup.sh", ScriptExporter.ScriptFileName(req, ShellKind.Bash));
    }

    [Fact]
    public void 머리말은_영문이고_생성시각_대상_chmod_안내를_담는다()
    {
        var r = Export(NewRequest(), ShellKind.Bash);

        Assert.Contains("# Generated by MysqlDumpBuilder on 2026-10-06 14:30\n", r.Content);
        Assert.Contains("# Source DB: shop@10.0.1.20:3306\n", r.Content);
        Assert.Contains("# Required: chmod +x mysqldump_shop.sh\n", r.Content);
        Assert.Contains("# Note: -p asks for the password at run time.", r.Content);
    }

    [Fact]
    public void 덤프_목록은_한_스크립트에_순서대로_들어간다()
    {
        var a = NewRequest();
        var b = NewRequest();
        b.Databases = new List<string> { "crm" };
        b.ContentMode = ContentMode.StructureOnly;

        var r = new ScriptExporter(Schema).ExportMany(new[] { a, b }, ShellKind.Bash);

        Assert.False(r.HasErrors);
        Assert.Equal(2, r.Jobs.Count);
        Assert.Equal("mysqldump_batch.sh", r.SuggestedFileName);
        Assert.Contains("# Source DB 1/2: shop@10.0.1.20:3306\n", r.Content);
        Assert.Contains("# Source DB 2/2: crm@10.0.1.20:3306\n", r.Content);
        Assert.Equal(2, r.Content.Split("OUT_FILE=\"").Length - 1);
        Assert.True(r.Content.IndexOf(" shop ", StringComparison.Ordinal) < r.Content.IndexOf(" crm ", StringComparison.Ordinal));
        Assert.Contains("--no-data", r.Content);
        Assert.Equal(1, r.Content.Split("set -euo pipefail").Length - 1);   // 공통 머리말은 한 번만
    }

    [Fact]
    public void 덤프_목록에_오류_항목이_있으면_내용은_비고_항목별_결과는_남는다()
    {
        var ok = NewRequest();
        var bad = NewRequest();
        bad.Databases.Clear();   // DB 이름 없음 → 오류

        var r = new ScriptExporter(Schema).ExportMany(new[] { ok, bad }, ShellKind.Bash);

        Assert.True(r.HasErrors);
        Assert.Equal("", r.Content);
        Assert.False(r.Jobs[0].HasErrors);
        Assert.True(r.Jobs[1].HasErrors);
    }
}
