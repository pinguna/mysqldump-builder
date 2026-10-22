using MysqlDumpBuilder.Core;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>조건 "DDL + DML 파일 분리": DDL 파일(_ddl)과 데이터 파일(_data)을 따로 만든다.</summary>
public class ContentSplitTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();

    private static DumpRequest NewRequest(JobKind job = JobKind.ExportOnly)
    {
        var req = new DumpRequest
        {
            Host = "10.0.1.20",
            User = "backup",
            Databases = { "shop" },
            Job = job,
            MultiLine = false,
            ContentMode = ContentMode.StructureAndDataSplit,
            Now = new DateTime(2026, 10, 6, 14, 30, 0)
        };
        req.ApplySchemaDefaults(Schema);
        req.Set("routines", true);
        req.Set("events", true);
        req.Set("hex_blob", true);
        req.Import.SameConnectionAsSource = false;
        req.Import.Host = "10.0.2.5";
        req.Import.User = "loader";
        return req;
    }

    private static JobResult Build(DumpRequest req) => new JobBuilder(Schema).Build(req);

    [Fact]
    public void DDL_파일과_데이터_파일_두_명령을_만든다()
    {
        var job = Build(NewRequest());

        Assert.False(job.HasErrors, string.Join("\n", job.ExportMessages));
        Assert.Equal(2, job.Steps.Count);
        var ddl = job.Steps[0].Export!.Command;
        var data = job.Steps[1].Export!.Command;

        Assert.Contains("--no-data", ddl);
        Assert.Contains("--routines", ddl);
        Assert.Contains("--events", ddl);
        Assert.EndsWith("> shop_20261006_1430_ddl.sql", ddl);

        Assert.Contains("--no-create-info", data);
        Assert.Contains("--skip-triggers", data);
        Assert.Contains("--hex-blob", data);
        Assert.DoesNotContain("--routines", data);
        Assert.DoesNotContain("--events", data);
        Assert.EndsWith("> shop_20261006_1430_data.sql", data);
    }

    [Fact]
    public void 분리_안내만_한_번_나오고_DDL_생략이나_트리거_경고는_나오지_않는다()
    {
        var job = Build(NewRequest());

        Assert.Single(job.ExportMessages, m => m.Code == "R25_CONTENT_SPLIT");
        Assert.DoesNotContain(job.ExportMessages, m => m.Code is "R23_DDL_SKIPS_DML" or "R9_DATA_ONLY_WITH_TRIGGERS");
    }

    [Fact]
    public void Import_가_붙으면_DDL을_먼저_넣고_데이터를_넣는다()
    {
        var job = Build(NewRequest(JobKind.ExportAndImport));

        Assert.False(job.HasErrors);
        Assert.DoesNotContain(job.ImportMessages, m => m.Code == "I5_DATA_ONLY_NEEDS_TABLES");
        var text = job.CommandText;
        var order = new[] { "> shop_20261006_1430_ddl.sql", "shop < shop_20261006_1430_ddl.sql", "> shop_20261006_1430_data.sql", "shop < shop_20261006_1430_data.sql" }
            .Select(s => text.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void 여러_DB_Import와_함께면_DB마다_두_단계씩_생긴다()
    {
        var req = NewRequest(JobKind.ExportAndImport);
        req.TargetMode = TargetMode.MultipleDatabases;
        req.Databases = new List<string> { "shop", "crm" };

        var job = Build(req);

        Assert.False(job.HasErrors, string.Join("\n", job.ExportMessages.Concat(job.ImportMessages)));
        Assert.Equal(4, job.Steps.Count);
        Assert.EndsWith("> shop_20261006_1430_ddl.sql", job.Steps[0].Export!.Command);
        Assert.EndsWith("> crm_20261006_1430_data.sql", job.Steps[3].Export!.Command);
    }

    [Fact]
    public void 여러_DB_Export만이면_데이터_파일은_DB를_지우거나_만들지_않는다()
    {
        var req = NewRequest();
        req.TargetMode = TargetMode.MultipleDatabases;
        req.Databases = new List<string> { "shop", "crm" };
        req.Set("add_drop_database", true);

        var job = Build(req);

        Assert.False(job.HasErrors, string.Join("\n", job.ExportMessages));
        Assert.Contains("--add-drop-database", job.Steps[0].Export!.Command);
        Assert.DoesNotContain("--add-drop-database", job.Steps[1].Export!.Command);
        Assert.Contains("--no-create-db", job.Steps[1].Export!.Command);
    }

    [Fact]
    public void 스크립트는_단계마다_ddl_data_파일명을_쓴다()
    {
        var r = new ScriptExporter(Schema).Export(NewRequest(), ShellKind.Bash);

        Assert.False(r.HasErrors);
        Assert.Contains("OUT_FILE=\"shop_$(date +%Y%m%d)_$(date +%H%M)_ddl.sql\"", r.Content);
        Assert.Contains("OUT_FILE=\"shop_$(date +%Y%m%d)_$(date +%H%M)_data.sql\"", r.Content);
    }

    [Theory]
    [InlineData("{db}_{date}.sql", "{db}_{date}_ddl.sql")]
    [InlineData("{db}_{date}.SQL", "{db}_{date}_ddl.SQL")]
    [InlineData("{db}_{date}", "{db}_{date}_ddl")]
    public void 파일명_접미사는_sql_확장자_앞에_붙는다(string template, string expected)
        => Assert.Equal(expected, JobBuilder.WithSuffix(template, "_ddl"));

    [Fact]
    public void 미리보기_파일명은_두_개를_보여준다()
        => Assert.Equal("shop_20261006_1430_ddl.sql, shop_20261006_1430_data.sql", JobBuilder.FilePreview(NewRequest()));

    [Fact]
    public void 파일_분리에서는_읽을_파일을_무시하고_각자_만든_파일을_읽는다()
    {
        var req = NewRequest(JobKind.ExportAndImport);
        req.Import.InputFile = "old.sql";
        var job = Build(req);

        Assert.EndsWith("< shop_20261006_1430_ddl.sql", job.Parts[0].Import!.Command);
        Assert.EndsWith("< shop_20261006_1430_data.sql", job.Parts[1].Import!.Command);
        Assert.DoesNotContain(job.ImportMessages, m => m.Code == "I11_OTHER_INPUT_FILE");
    }
}
