using MysqlDumpBuilder.Core;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>기획서 2장 UC-01~06: 각 시나리오가 기대 명령어로 재현되는지.</summary>
public class ScenarioTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();
    private static readonly DateTime FixedNow = new(2026, 10, 6, 14, 30, 0);

    /// <summary>테스트용 기본 요청: 접속 정보 채우고 스키마 기본값 적용.</summary>
    private static DumpRequest NewRequest()
    {
        var req = new DumpRequest
        {
            Host = "10.0.1.20",
            Port = 3306,
            User = "backup",
            Client = ClientKind.Mysql80,
            OutputMode = OutputMode.ResultFile,
            Shell = ShellKind.Bash,
            MultiLine = false,
            Now = FixedNow,
            Databases = { "shop" }
        };
        req.ApplySchemaDefaults(Schema);
        return req;
    }

    private static BuildResult Build(DumpRequest req) => new CommandBuilder(Schema).Build(req);

    [Fact]
    public void UC01_운영_전체_백업()
    {
        var req = NewRequest();
        req.Set("single_transaction", true);
        req.Set("routines", true);
        req.Set("events", true);
        req.Set("hex_blob", true);
        req.Set("set_gtid_purged_off", true);

        var result = Build(req);

        Assert.False(result.HasErrors, string.Join("\n", result.Messages));
        Assert.Equal(
            "mysqldump -h 10.0.1.20 -P 3306 -u backup -p " +
            "--default-character-set=utf8mb4 " +
            "--single-transaction --skip-lock-tables --set-gtid-purged=OFF " +
            "--routines --events --triggers --add-drop-table " +
            "--hex-blob --extended-insert " +
            "--quick " +
            "shop --result-file=shop_20261006_1430.sql",
            result.Command);
        Assert.Contains(result.Messages, m => m.Code == "R4_LOCK_TABLES_AUTO_OFF");
    }

    [Fact]
    public void UC02_개발_스키마_동기화()
    {
        var req = NewRequest();
        req.ContentMode = ContentMode.StructureOnly;
        req.Set("routines", true);
        req.Set("events", true);

        var result = Build(req);

        Assert.False(result.HasErrors);
        var tokens = result.PlainTokens;
        Assert.Contains("--no-data", tokens);
        Assert.Contains("--routines --events --triggers --add-drop-table", tokens);
        Assert.DoesNotContain("--no-create-info", tokens);
    }

    [Fact]
    public void UC03_데이터만_이관()
    {
        var req = NewRequest();
        req.ContentMode = ContentMode.DataOnly;
        req.InsertMode = InsertMode.InsertIgnore;
        req.Set("skip_triggers", true);
        req.Set("complete_insert", true);

        var result = Build(req);

        Assert.False(result.HasErrors);
        var tokens = result.PlainTokens;
        Assert.Contains("--no-create-info", tokens);
        Assert.Contains("--skip-triggers", tokens);
        Assert.Contains("--insert-ignore", tokens);
        Assert.Contains("--complete-insert", tokens);
        // 데이터만 덤프할 때는 DROP TABLE 관련 플래그가 아예 나오지 않는다 (R8)
        Assert.DoesNotContain("--add-drop-table", tokens);
        Assert.DoesNotContain("--skip-add-drop-table", tokens);
    }

    [Fact]
    public void UC04_특정_테이블_부분_복사_with_where()
    {
        var req = NewRequest();
        req.TargetMode = TargetMode.Tables;
        req.Tables.AddRange(new[] { "orders", "order_items" });
        req.Where = "created_at >= '2026-01-01'";

        var result = Build(req);

        Assert.False(result.HasErrors);
        Assert.Contains("--where=\"created_at >= '2026-01-01'\" shop orders order_items", result.Command);
        Assert.DoesNotContain(result.Messages, m => m.Code == "R6_WHERE_WITHOUT_TABLES");
    }

    [Fact]
    public void UC05_로그_테이블_제외_gzip()
    {
        var req = NewRequest();
        req.Set("single_transaction", true);
        req.IgnoreTables.AddRange(new[] { "shop.access_log", "sessions" });
        req.OutputMode = OutputMode.GzipPipe;

        var result = Build(req);

        Assert.False(result.HasErrors);
        Assert.Contains("--ignore-table=shop.access_log --ignore-table=shop.sessions shop | gzip > shop_20261006_1430.sql.gz", result.Command);
        Assert.Contains(result.Messages, m => m.Code == "R20_IGNORE_TABLE_PREFIXED");
    }

    [Fact]
    public void UC06_여러_DB_전체_인스턴스_이관()
    {
        var req = NewRequest();
        req.TargetMode = TargetMode.MultipleDatabases;
        req.Databases.Clear();
        req.Databases.AddRange(new[] { "a", "b", "c" });
        req.Set("add_drop_database", true);
        req.Set("routines", true);
        req.Set("events", true);

        var result = Build(req);

        Assert.False(result.HasErrors, string.Join("\n", result.Messages));
        Assert.Contains("--add-drop-database", result.Command);
        Assert.Contains("--databases a b c --result-file=a_20261006_1430.sql", result.Command);
    }

    [Fact]
    public void 내장_프리셋은_모두_오류_없이_빌드된다()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            var req = NewRequest();
            preset.ApplyTo(req, Schema);
            if (preset.OutputMode == OutputMode.GzipPipe) req.IgnoreTables.Add("shop.access_log");

            var result = Build(req);

            Assert.False(result.HasErrors, preset.Name + ": " + string.Join("\n", result.Messages));
            Assert.StartsWith("mysqldump ", result.Command);
        }
    }

    [Fact]
    public void 내장_프리셋은_작업과_출력을_명시해서_고르면_라디오가_따라온다()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            Assert.Equal(JobKind.ExportOnly, preset.Job);
            Assert.NotNull(preset.OutputMode);

            // Export + Import 상태에서 프리셋을 골라도 작업은 Export 로 돌아오고, Import 대상 설정은 그대로 남는다.
            var req = NewRequest();
            req.Job = JobKind.ExportAndImport;
            req.OutputMode = OutputMode.ResultFile;
            req.Import.Database = "shop_copy";
            preset.ApplyTo(req, Schema);

            Assert.Equal(JobKind.ExportOnly, req.Job);
            Assert.Equal(preset.OutputMode, req.OutputMode);
            Assert.Equal("shop_copy", req.Import.Database);
        }
    }

    [Fact]
    public void 여러_줄_모드는_bash_연결문자로_줄을_나눈다()
    {
        var req = NewRequest();
        req.MultiLine = true;
        req.Set("single_transaction", true);

        var result = Build(req);

        var lines = result.Command.Split('\n');
        Assert.True(lines.Length >= 4);
        Assert.StartsWith("mysqldump", lines[0]);
        for (var i = 0; i < lines.Length - 1; i++)
            Assert.EndsWith(" \\", lines[i]);
        Assert.False(lines[^1].EndsWith("\\"));
        Assert.All(lines.Skip(1), l => Assert.StartsWith("  ", l));
    }
}
