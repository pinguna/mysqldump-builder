using MysqlDumpBuilder.Core;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>Import 단계: mysql 명령어 생성과 규칙 I1~.</summary>
public class ImportTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();

    private static DumpRequest NewRequest(JobKind job = JobKind.ExportAndImport)
    {
        var req = new DumpRequest
        {
            Host = "10.0.1.20",
            User = "backup",
            Databases = { "shop" },
            Job = job,
            MultiLine = false,
            Now = new DateTime(2026, 10, 6, 14, 30, 0)
        };
        req.ApplySchemaDefaults(Schema);
        req.Import.SameConnectionAsSource = false;
        req.Import.Host = "10.0.2.5";
        req.Import.User = "loader";
        return req;
    }

    private static BuildResult Build(DumpRequest req) => new ImportCommandBuilder().Build(req);

    [Fact]
    public void bash는_리다이렉션으로_파일을_넣는다()
    {
        var r = Build(NewRequest());

        Assert.False(r.HasErrors);
        Assert.Equal("mysql -h 10.0.2.5 -u loader -p --default-character-set=utf8mb4 shop < shop_20261006_1430.sql", r.Command);
    }

    [Fact]
    public void powershell은_source_명령으로_넣는다()
    {
        var req = NewRequest();
        req.Shell = ShellKind.PowerShell;

        var r = Build(req);

        Assert.Contains("--execute='source shop_20261006_1430.sql'", r.Command);
        Assert.DoesNotContain("<", r.Command);
        Assert.Contains(r.Messages, m => m.Code == "I2_PS_SOURCE");
    }

    [Fact]
    public void gzip_파일은_gunzip_파이프로_넣는다()
    {
        var req = NewRequest();
        req.OutputMode = OutputMode.GzipPipe;

        var r = Build(req);

        Assert.Equal("gunzip -c shop_20261006_1430.sql.gz | mysql -h 10.0.2.5 -u loader -p --default-character-set=utf8mb4 shop", r.Command);
    }

    [Fact]
    public void 원본과_같은_서버_같은_DB면_경고만_하고_명령어는_만든다()
    {
        // 같은 DB 에 백업을 다시 넣는(복원) 경우가 있으므로 막지 않는다.
        var req = NewRequest();
        req.Import.SameConnectionAsSource = true;

        var r = Build(req);

        Assert.False(r.HasErrors);
        Assert.Contains(r.Messages, m => m.Code == "I1_SAME_AS_SOURCE" && m.Severity == Severity.Warning);
        Assert.EndsWith(" shop < shop_20261006_1430.sql", r.Command);

        req.Import.Database = "shop_copy";   // DB 이름이 다르면 경고도 없다
        Assert.DoesNotContain(Build(req).Messages, m => m.Code == "I1_SAME_AS_SOURCE");
    }

    [Fact]
    public void 원본과_같음이면_원본_접속_정보를_그대로_쓴다()
    {
        var req = NewRequest();
        req.Import.SameConnectionAsSource = true;
        req.Import.Database = "shop_copy";
        req.PasswordMode = PasswordMode.LoginPath;
        req.LoginPath = "prod";

        var r = Build(req);

        Assert.Equal("mysql --login-path=prod -h 10.0.1.20 -P 3306 --default-character-set=utf8mb4 shop_copy < shop_20261006_1430.sql", r.Command);
    }

    [Fact]
    public void DB_생성_옵션은_명령을_하나_더_붙이고_FK_force는_옵션으로_들어간다()
    {
        var req = NewRequest();
        req.Import.CreateDatabase = true;
        req.Import.Force = true;
        req.Import.DisableForeignKeyChecks = true;

        var r = Build(req);

        // mysql 은 한 번만 실행된다: CREATE DATABASE + USE 를 파일 앞에 붙여 파이프로 넣고 DB 인자는 주지 않는다.
        Assert.Equal("{ echo 'CREATE DATABASE IF NOT EXISTS `shop`; USE `shop`;'; cat shop_20261006_1430.sql ; } | mysql -h 10.0.2.5 -u loader -p --default-character-set=utf8mb4 --force --init-command='SET FOREIGN_KEY_CHECKS=0'", r.Command);
        Assert.Contains(r.Messages, m => m.Code == "I6_FORCE");

        req.Shell = ShellKind.Cmd;
        Assert.Equal("(echo CREATE DATABASE IF NOT EXISTS `shop`; USE `shop`; & type shop_20261006_1430.sql ) | mysql -h 10.0.2.5 -u loader -p --default-character-set=utf8mb4 --force --init-command=\"SET FOREIGN_KEY_CHECKS=0\"", Build(req).Command);

        req.Shell = ShellKind.PowerShell;
        Assert.Contains("--execute=\"CREATE DATABASE IF NOT EXISTS ``shop``; USE ``shop``;`nsource shop_20261006_1430.sql\"", Build(req).Command);
        Assert.DoesNotContain(" shop --execute", Build(req).Command);
    }

    [Fact]
    public void 스크립트의_DB_생성은_OUT_FILE_변수를_파이프_앞에_쓴다()
    {
        var req = NewRequest();
        req.Import.CreateDatabase = true;

        var r = new ScriptExporter(Schema).Export(req, ShellKind.Bash);

        Assert.Contains("{ echo 'CREATE DATABASE IF NOT EXISTS `shop`; USE `shop`;'; cat \"$OUT_FILE\" ; } | \\\n  mysql", r.Content);
    }

    private static DumpRequest MultiRequest(string targets, JobKind job = JobKind.ExportAndImport)
    {
        var req = NewRequest(job);
        req.TargetMode = TargetMode.MultipleDatabases;
        req.Databases = new List<string> { "test", "test2", "test3" };
        req.Import.Database = targets;
        return req;
    }

    [Fact]
    public void 여러_DB_Import는_DB마다_mysqldump와_mysql을_만든다()
    {
        var job = new JobBuilder(Schema).Build(MultiRequest(""));

        Assert.False(job.HasErrors, string.Join("\n", job.ImportMessages));
        Assert.Equal(3, job.Parts.Count);
        Assert.Null(job.Export);
        Assert.Contains(job.ImportMessages, m => m.Code == "I8_SPLIT_PER_DB");

        // 단일 DB 형식(--databases 없음) 이라 파일에 USE 가 없고, 파일명은 DB 마다 다르다
        var export1 = job.Parts[0].Export!.Command;
        Assert.DoesNotContain("--databases", export1);
        Assert.EndsWith(" test > test_20261006_1430.sql", export1);
        Assert.EndsWith(" test2 > test2_20261006_1430.sql", job.Parts[1].Export!.Command);

        // 대상 DB 를 비우면 같은 이름
        Assert.Equal("mysql -h 10.0.2.5 -u loader -p --default-character-set=utf8mb4 test2 < test2_20261006_1430.sql", job.Parts[1].Import!.Command);

        // 미리보기 텍스트는 DB 순서대로 Export, Import 가 짝지어 나온다
        var text = job.CommandText;
        Assert.True(text.IndexOf("test_20261006_1430.sql", StringComparison.Ordinal) < text.IndexOf("test2_20261006_1430.sql", StringComparison.Ordinal));
        Assert.Equal(6, text.Split("\n\n").Length);
    }

    [Fact]
    public void 여러_DB_대상은_쉼표_목록을_순서대로_짝맞춘다()
    {
        var job = new JobBuilder(Schema).Build(MultiRequest("test123, test2 ,test3"));

        Assert.False(job.HasErrors);
        Assert.EndsWith(" test123 < test_20261006_1430.sql", job.Parts[0].Import!.Command);
        Assert.EndsWith(" test2 < test2_20261006_1430.sql", job.Parts[1].Import!.Command);
        Assert.EndsWith(" test3 < test3_20261006_1430.sql", job.Parts[2].Import!.Command);
        Assert.Equal("test123,test2,test3@10.0.2.5", ImportCommandBuilder.Describe(job.Request));
    }

    [Fact]
    public void 여러_DB_대상_개수가_다르면_오류()
    {
        var job = new JobBuilder(Schema).Build(MultiRequest("a, b"));

        Assert.True(job.HasErrors);
        Assert.Empty(job.Parts);
        var m = Assert.Single(job.ImportMessages, m => m.Code == "I9_TARGET_COUNT_MISMATCH");
        Assert.Contains("2개인데 원본 DB 는 3개", m.Text.Ko);
    }

    [Fact]
    public void 여러_DB_같은_서버면_제자리_DB에만_경고가_붙는다()
    {
        var req = MultiRequest("test123, test2, test3");
        req.Import.SameConnectionAsSource = true;

        var job = new JobBuilder(Schema).Build(req);

        Assert.False(job.HasErrors);                      // test2, test3 는 제자리 Import(복원) — 경고만
        Assert.DoesNotContain(job.Parts[0].ImportMessages, m => m.Code == "I1_SAME_AS_SOURCE");
        Assert.Contains(job.Parts[1].ImportMessages, m => m.Code == "I1_SAME_AS_SOURCE");
        Assert.Contains(job.ImportMessages, m => m.Code == "I1_SAME_AS_SOURCE" && m.Severity == Severity.Warning);
    }

    [Fact]
    public void 여러_DB_쪼개기는_databases_전용_옵션을_끈다()
    {
        var req = MultiRequest("");
        req.Set("add_drop_database", true);
        req.Set("no_create_db", true);

        var job = new JobBuilder(Schema).Build(req);

        Assert.False(job.HasErrors, string.Join("\n", job.ExportMessages.Concat(job.ImportMessages)));
        Assert.DoesNotContain("--add-drop-database", job.CommandText);
        Assert.DoesNotContain("--no-create-db", job.CommandText);
    }

    [Fact]
    public void 미리보기는_세트가_여럿이면_세트마다_머리줄을_단다()
    {
        var job = new JobBuilder(Schema).Build(MultiRequest("test123,test2,test3"));

        var text = ScriptExporter.CommandPreview(new[] { job }, ShellKind.Bash);
        Assert.StartsWith("# ---- 1/3: test@10.0.1.20:3306 -> test123@10.0.2.5 ----\nmysqldump", text);
        Assert.Contains(".sql\n\n\n# ---- 2/3: test2@10.0.1.20:3306 -> test2@10.0.2.5 ----\nmysqldump", text);
        Assert.Contains("REM ---- 3/3: ", ScriptExporter.CommandPreview(new[] { job }, ShellKind.Cmd));

        // 세트가 하나면 머리줄 없이 명령어만.
        var single = new JobBuilder(Schema).Build(NewRequest());
        Assert.Equal(single.CommandText, ScriptExporter.CommandPreview(new[] { single }, ShellKind.Bash));
    }

    [Fact]
    public void 여러_DB_스크립트는_DB마다_단계가_있고_머리말은_목록으로_적는다()
    {
        var r = new ScriptExporter(Schema).Export(MultiRequest("test123,test2,test3"), ShellKind.Bash);

        Assert.False(r.HasErrors);
        Assert.Single(r.Jobs);
        Assert.Contains("# Source DB: test,test2,test3@10.0.1.20:3306\n", r.Content);
        Assert.Contains("# Target DB: test123,test2,test3@10.0.2.5\n", r.Content);
        Assert.Contains("# ---- 1/3: test@10.0.1.20:3306 -> test123@10.0.2.5 ----", r.Content);
        Assert.Contains("# ---- 3/3: test3@10.0.1.20:3306 -> test3@10.0.2.5 ----", r.Content);
        Assert.Contains("OUT_FILE=\"test2_$(date +%Y%m%d)_$(date +%H%M).sql\"", r.Content);
        Assert.Contains("test123 < \"$OUT_FILE\"", r.Content);
        Assert.Equal(3, r.Content.Split("Export started").Length - 1);
        Assert.Equal(3, r.Content.Split("Import started").Length - 1);
    }

    [Fact]
    public void 작업_종류에_따라_Export_Import_빌드가_나뉜다()
    {
        var jobs = new JobBuilder(Schema);

        var exportOnly = jobs.Build(NewRequest(JobKind.ExportOnly));
        Assert.NotNull(exportOnly.Export);
        Assert.Null(exportOnly.Import);

        var both = jobs.Build(NewRequest(JobKind.ExportAndImport));
        Assert.NotNull(both.Export);
        Assert.NotNull(both.Import);
        Assert.Contains("mysqldump", both.CommandText);
        Assert.Contains("\n\nmysql -h 10.0.2.5", both.CommandText);

        var importOnly = jobs.Build(ImportOnlyRequest("backup/shop_20261001.sql"));
        Assert.Null(importOnly.Export);
        Assert.False(importOnly.HasErrors);
    }

    private static DumpRequest ImportOnlyRequest(string inputFile)
    {
        var req = NewRequest(JobKind.ImportOnly);
        req.Import.InputFile = inputFile;
        return req;
    }

    [Fact]
    public void Import_접속의_User가_비면_경고한다()
    {
        var req = NewRequest();
        req.Import.User = "";

        var r = Build(req);

        Assert.False(r.HasErrors);
        Assert.Contains(r.Messages, m => m.Code == "R24_NO_USER" && m.Severity == Severity.Warning);
        Assert.Equal("mysql -h 10.0.2.5 -p --default-character-set=utf8mb4 shop < shop_20261006_1430.sql", r.Command);
    }

    [Fact]
    public void Import만_할_때는_읽을_파일이_없으면_오류다()
    {
        var r = Build(ImportOnlyRequest(""));

        Assert.True(r.HasErrors);
        Assert.Contains(r.Messages, m => m.Code == "V_IMPORT_NO_FILE" && m.Severity == Severity.Error);
    }

    [Fact]
    public void Import만_할_때는_파일명_템플릿이_아니라_읽을_파일을_넣는다()
    {
        var r = Build(ImportOnlyRequest("backup/shop_20261001.sql"));

        Assert.False(r.HasErrors);
        Assert.Equal("mysql -h 10.0.2.5 -u loader -p --default-character-set=utf8mb4 shop < backup/shop_20261001.sql", r.Command);
        Assert.DoesNotContain("20261006", r.Command);
    }

    [Fact]
    public void Import만_할_때_gz_파일은_gunzip_파이프로_넣는다()
    {
        var r = Build(ImportOnlyRequest("backup/shop.sql.gz"));

        Assert.False(r.HasErrors);
        Assert.StartsWith("gunzip -c backup/shop.sql.gz | mysql ", r.Command);
        Assert.Contains(r.Messages, m => m.Code == "I3_GZIP_IMPORT");
    }

    [Fact]
    public void Import만_여러_DB면_읽을_파일의_db_토큰이_DB마다_바뀐다()
    {
        var req = ImportOnlyRequest("backup/{db}.sql");
        req.TargetMode = TargetMode.MultipleDatabases;
        req.Databases = new List<string> { "shop", "crm" };

        var r = new ScriptExporter(Schema).Export(req, ShellKind.Bash);

        Assert.False(r.HasErrors);
        Assert.Contains("# Input file: backup/{db}.sql\n", r.Content);
        Assert.Contains("OUT_FILE=\"backup/shop.sql\"\n", r.Content);
        Assert.Contains("OUT_FILE=\"backup/crm.sql\"\n", r.Content);
    }

    [Fact]
    public void 스크립트는_Export_뒤에_Import_단계를_넣고_머리말에_Target_DB를_적는다()
    {
        var r = new ScriptExporter(Schema).Export(NewRequest(), ShellKind.Bash);

        Assert.False(r.HasErrors);
        Assert.Contains("# Source DB: shop@10.0.1.20:3306\n", r.Content);
        Assert.Contains("# Target DB: shop@10.0.2.5\n", r.Content);
        Assert.Contains("Export started -> $OUT_FILE", r.Content);
        Assert.Contains("Import started: $OUT_FILE -> shop@10.0.2.5", r.Content);
        Assert.Contains("shop < \"$OUT_FILE\"", r.Content);
        Assert.True(r.Content.IndexOf("Export started", StringComparison.Ordinal) < r.Content.IndexOf("Import started", StringComparison.Ordinal));
    }

    [Fact]
    public void Import만_하는_스크립트에는_mysqldump가_없다()
    {
        var r = new ScriptExporter(Schema).Export(ImportOnlyRequest(@"D:\backup\shop_20261001.sql"), ShellKind.Cmd);

        Assert.False(r.HasErrors);
        Assert.DoesNotContain("mysqldump", r.Content);
        Assert.DoesNotContain("Source DB", r.Content);
        Assert.Contains("REM Input file: D:\\backup\\shop_20261001.sql\r\n", r.Content);
        Assert.Contains("REM Target DB: shop@10.0.2.5", r.Content);
        Assert.Contains("set \"OUT_FILE=D:\\backup\\shop_20261001.sql\"\r\n", r.Content);
        Assert.DoesNotContain("STAMP1", r.Content);
        Assert.Contains("shop < \"%OUT_FILE%\"", r.Content);
    }

    [Fact]
    public void powershell_source_경로의_역슬래시는_슬래시로_바꾼다()
    {
        // mysql 은 source 인자의 \t, \n 등을 클라이언트 명령으로 읽는다.
        var req = ImportOnlyRequest(@"C:\temp\new\shop.sql");
        req.Shell = ShellKind.PowerShell;

        Assert.Contains("--execute='source C:/temp/new/shop.sql'", Build(req).Command);

        var script = new ScriptExporter(Schema).Export(req, ShellKind.PowerShell).Content;
        Assert.Contains("--execute=\"source $($OutFile.Replace('\\','/'))\"", script);
    }

    [Fact]
    public void Export_Import에서_읽을_파일을_비우면_Export_파일을_읽는다()
    {
        var r = Build(NewRequest());

        Assert.EndsWith("shop < shop_20261006_1430.sql", r.Command);
        Assert.DoesNotContain(r.Messages, m => m.Code == "I11_OTHER_INPUT_FILE");
    }

    [Fact]
    public void Export_Import에서_읽을_파일을_적으면_그_파일을_읽고_경고한다()
    {
        // 14:30 에 Export 는 됐고 Import 만 실패해, 15:10 에 옵션을 바꿔 다시 만드는 상황.
        var req = NewRequest();
        req.Now = new DateTime(2026, 10, 6, 15, 10, 0);
        req.Import.InputFile = "shop_20261006_1430.sql";
        var job = new JobBuilder(Schema).Build(req);

        Assert.Contains("> shop_20261006_1510.sql", job.Export!.Command);      // Export 는 지금 시각 파일
        Assert.EndsWith("shop < shop_20261006_1430.sql", job.Import!.Command); // Import 는 적은 파일
        Assert.Contains(job.Import.Messages, m => m.Code == "I11_OTHER_INPUT_FILE" && m.Severity == Severity.Warning);
        Assert.Equal("shop_20261006_1510.sql", JobBuilder.FilePreview(req));   // 파일명 미리보기는 Export 파일 그대로

        // 스크립트도 Import 쪽은 OUT_FILE 대신 적은 경로를 쓴다.
        var script = new ScriptExporter(Schema).Export(req, ShellKind.Bash).Content;
        Assert.Contains("shop < shop_20261006_1430.sql", script);
        Assert.Contains("Import started: shop_20261006_1430.sql -> ", script);
        Assert.Contains("# Input file: shop_20261006_1430.sql", script);
        Assert.Contains("--result-file=\"$OutFile\"", new ScriptExporter(Schema).Export(req, ShellKind.PowerShell).Content);
        Assert.Contains("--execute='source shop_20261006_1430.sql'", new ScriptExporter(Schema).Export(req, ShellKind.PowerShell).Content);
    }

    [Fact]
    public void 여러_DB에서_읽을_파일의_db_토큰은_DB마다_바뀐다()
    {
        var req = MultiRequest("");
        req.Import.InputFile = "old/{db}.sql";
        var job = new JobBuilder(Schema).Build(req);

        Assert.EndsWith("test2 < old/test2.sql", job.Parts[1].Import!.Command);
        Assert.Equal("{db}_20261006_1430.sql", ImportCommandBuilder.ExportFileName(req, keepDbToken: true));
    }

    [Fact]
    public void I10_UTF16_파일이면_경고()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MysqlDumpBuilderTests");
        Directory.CreateDirectory(dir);
        var utf16 = Path.Combine(dir, "utf16.sql");
        var utf8 = Path.Combine(dir, "utf8.sql");
        File.WriteAllText(utf16, "-- dump\r\n", new System.Text.UnicodeEncoding(false, true));   // PowerShell 5.1 의 > 와 같은 FF FE
        File.WriteAllText(utf8, "-- dump\n", new System.Text.UTF8Encoding(false));

        Assert.Contains(Build(ImportOnlyRequest(utf16)).Messages, m => m.Code == "I10_UTF16_FILE");
        Assert.DoesNotContain(Build(ImportOnlyRequest(utf8)).Messages, m => m.Code == "I10_UTF16_FILE");
        Assert.DoesNotContain(Build(ImportOnlyRequest("backup/shop.sql")).Messages, m => m.Code == "I10_UTF16_FILE");
    }

    [Fact]
    public void 프리셋은_작업_종류와_Import_대상을_저장하되_평문_패스워드는_버린다()
    {
        var req = NewRequest();
        req.Import.PasswordMode = PasswordMode.Plain;
        req.Import.PlainPassword = "secret";
        req.Import.Database = "shop_copy";

        var preset = Preset.FromRequest("p", req);
        var restored = new DumpRequest();
        restored.ApplySchemaDefaults(Schema);
        preset.ApplyTo(restored, Schema);

        Assert.Equal(JobKind.ExportAndImport, restored.Job);
        Assert.Equal("shop_copy", restored.Import.Database);
        Assert.Equal("10.0.2.5", restored.Import.Host);
        Assert.Equal(PasswordMode.Prompt, restored.Import.PasswordMode);
        Assert.Equal("", restored.Import.PlainPassword);
    }

    [Fact]
    public void 테이블_지정_모드에서는_제외_테이블을_내보내지_않는다()
    {
        var req = NewRequest(JobKind.ExportOnly);
        req.TargetMode = TargetMode.Tables;
        req.Tables = new List<string> { "t1" };
        req.IgnoreTables = new List<string> { "shop.log" };

        var r = new CommandBuilder(Schema).Build(req);

        Assert.False(r.HasErrors);
        Assert.DoesNotContain("--ignore-table", r.Command);
    }

    [Fact]
    public void 설정_키는_이름을_빼고_비교한다()
    {
        var a = Preset.FromRequest("a", NewRequest());
        var b = Preset.FromRequest("b", NewRequest());
        var c = Preset.FromRequest("c", NewRequest(JobKind.ExportOnly));

        Assert.Equal(a.SettingsKey(), b.SettingsKey());
        Assert.NotEqual(a.SettingsKey(), c.SettingsKey());
        Assert.Equal("a", a.Name);
    }

    [Fact]
    public void 설정_키는_대상_OS를_빼고_비교한다()
    {
        // 대상 OS 는 스크립트 목록 공통이라 OS 만 바꿔도 목록의 같은 항목으로 본다 (스크립트 미리보기가 유지되게).
        var bash = NewRequest();
        var ps = NewRequest();
        ps.Shell = ShellKind.PowerShell;
        var a = Preset.FromRequest("", bash);
        var b = Preset.FromRequest("", ps);

        Assert.Equal(a.SettingsKey(), b.SettingsKey());
        Assert.Equal(ShellKind.PowerShell, b.Shell);   // 키만 빼고 값은 그대로
    }
}
