using MysqlDumpBuilder.Core;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>기획서 4장 규칙 R1~R16 (+ 추가 규칙) 과 입력 검증.</summary>
public class RuleTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();

    private static DumpRequest NewRequest()
    {
        var req = new DumpRequest { User = "u", MultiLine = false, OutputMode = OutputMode.ResultFile, Databases = { "shop" } };
        req.ApplySchemaDefaults(Schema);
        return req;
    }

    private static BuildResult Build(DumpRequest req) => new CommandBuilder(Schema).Build(req);

    [Fact]
    public void R24_User가_비면_경고만_하고_u_없이_명령어를_만든다()
    {
        var req = NewRequest();
        req.User = "  ";

        var result = Build(req);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "R24_NO_USER" && m.Severity == Severity.Warning);
        Assert.DoesNotContain(" -u ", result.Command);
        Assert.Contains(" -p ", result.Command);
    }

    [Fact]
    public void R24_login_path나_extra_file이면_User가_비어도_경고하지_않는다()
    {
        var byLoginPath = NewRequest();
        byLoginPath.User = "";
        byLoginPath.PasswordMode = PasswordMode.LoginPath;
        byLoginPath.LoginPath = "prod";
        Assert.DoesNotContain(Build(byLoginPath).Messages, m => m.Code == "R24_NO_USER");

        var byExtraFile = NewRequest();
        byExtraFile.User = "";
        byExtraFile.PasswordMode = PasswordMode.DefaultsExtraFile;
        byExtraFile.DefaultsExtraFile = "/etc/mysql/backup.cnf";
        Assert.DoesNotContain(Build(byExtraFile).Messages, m => m.Code == "R24_NO_USER");
    }

    [Fact]
    public void R3_single_transaction과_lock_all_tables는_오류()
    {
        var req = NewRequest();
        req.Set("single_transaction", true);
        req.Set("lock_all_tables", true);

        var result = Build(req);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "R3_SINGLE_TX_VS_LOCK_ALL" && m.Severity == Severity.Error);
        Assert.Equal("", result.Command);
    }

    [Fact]
    public void R4_single_transaction은_skip_lock_tables를_강제한다()
    {
        var req = NewRequest();
        req.Set("single_transaction", true);
        req.Set("skip_lock_tables", false);

        var result = Build(req);

        Assert.Contains("--skip-lock-tables", result.PlainTokens);
        Assert.DoesNotContain("--lock-tables", result.PlainTokens.Replace("--skip-lock-tables", ""));
        Assert.Contains(result.Messages, m => m.Code == "R4_LOCK_TABLES_AUTO_OFF" && m.Severity == Severity.Info);
    }

    [Theory]
    [InlineData("add_drop_database")]
    [InlineData("no_create_db")]
    public void R5_databases_전용_옵션은_단일_DB_모드에서_오류(string optionId)
    {
        var req = NewRequest();
        req.TargetMode = TargetMode.SingleDatabase;
        req.Set(optionId, true);

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R5_NEEDS_DATABASES_MODE" && m.OptionId == optionId);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void R5_databases_모드에서는_허용()
    {
        var req = NewRequest();
        req.TargetMode = TargetMode.MultipleDatabases;
        req.Set("add_drop_database", true);

        var result = Build(req);

        Assert.DoesNotContain(result.Messages, m => m.Code == "R5_NEEDS_DATABASES_MODE");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void R6_where는_테이블_지정_모드가_아니면_경고()
    {
        var req = NewRequest();
        req.Where = "id > 10";

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R6_WHERE_WITHOUT_TABLES" && m.Severity == Severity.Warning);
        Assert.False(result.HasErrors);
        Assert.Contains("--where='id > 10'", result.Command);
    }

    [Fact]
    public void R7_compact는_하위_skip_옵션을_따로_내보내지_않는다()
    {
        var req = NewRequest();
        req.Set("compact", true);
        req.Set("skip_comments", true);
        req.Set("skip_add_locks", true);

        var result = Build(req);

        var tokens = result.PlainTokens;
        Assert.Contains("--compact", tokens);
        Assert.DoesNotContain("--skip-comments", tokens);
        Assert.DoesNotContain("--skip-add-locks", tokens);
        Assert.DoesNotContain("--add-drop-table", tokens);
        Assert.Contains(result.Messages, m => m.Code == "R7_COMPACT_IMPLIES");
    }

    [Fact]
    public void R8_데이터만_모드에서_skip_add_drop_table은_정보_메시지와_함께_생략()
    {
        var req = NewRequest();
        req.ContentMode = ContentMode.DataOnly;
        req.Set("skip_add_drop_table", true);
        req.Set("skip_triggers", true);

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R8_DROP_TABLE_MEANINGLESS" && m.Severity == Severity.Info);
        Assert.DoesNotContain("add-drop-table", result.PlainTokens);
    }

    [Fact]
    public void R9_데이터만_모드에서_트리거_포함이면_경고()
    {
        var req = NewRequest();
        req.ContentMode = ContentMode.DataOnly;

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R9_DATA_ONLY_WITH_TRIGGERS" && m.Severity == Severity.Warning);
    }

    [Fact]
    public void R10_column_statistics는_5_7_클라이언트에서_오류()
    {
        var req = NewRequest();
        req.Client = ClientKind.Mysql57;
        req.Set("column_statistics_off", true);

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R10_OPTION_NOT_IN_CLIENT" && m.OptionId == "column_statistics_off");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void R10_column_statistics는_8_0_클라이언트에서_정상()
    {
        var req = NewRequest();
        req.Client = ClientKind.Mysql80;
        req.Set("column_statistics_off", true);

        var result = Build(req);

        Assert.False(result.HasErrors);
        Assert.Contains("--column-statistics=0", result.Command);
    }

    [Fact]
    public void R15_평문_비밀번호는_경고와_함께_포함()
    {
        var req = NewRequest();
        req.PasswordMode = PasswordMode.Plain;
        req.PlainPassword = "s3cret";

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R15_PLAIN_PASSWORD" && m.Severity == Severity.Warning);
        Assert.Contains("--password=s3cret", result.Command);
        Assert.DoesNotContain(" -p ", result.Command);
    }

    [Fact]
    public void R16_defaults_extra_file은_첫_번째_인자()
    {
        var req = NewRequest();
        req.PasswordMode = PasswordMode.DefaultsExtraFile;
        req.DefaultsExtraFile = "/home/dba/.my.cnf";

        var result = Build(req);

        Assert.StartsWith("mysqldump --defaults-extra-file=/home/dba/.my.cnf -h localhost", result.Command);
        Assert.DoesNotContain(" -u ", result.Command);
        Assert.DoesNotContain(" -p", result.Command.Replace("-P 3306", ""));
    }

    [Fact]
    public void login_path는_사용자와_비밀번호를_대체한다()
    {
        var req = NewRequest();
        req.PasswordMode = PasswordMode.LoginPath;
        req.LoginPath = "prod";

        var result = Build(req);

        Assert.Contains("--login-path=prod -h localhost -P 3306", result.Command);
        Assert.DoesNotContain(" -u ", result.Command);
    }

    [Fact]
    public void R17_PowerShell_리다이렉션은_result_file로_바꾼다()
    {
        var req = NewRequest();
        req.Shell = ShellKind.PowerShell;
        req.OutputMode = OutputMode.StdoutRedirect;

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "R17_PS_REDIRECT_UTF16" && m.Severity == Severity.Info);
        Assert.Contains("--result-file=", result.Command);
        Assert.DoesNotContain(">", result.Command);

        req.Shell = ShellKind.Cmd;
        Assert.Contains(" > ", Build(req).Command);   // cmd 의 > 는 바이트 그대로라 그대로 둔다
    }

    [Fact]
    public void R22_PowerShell에서_값_안의_큰따옴표는_경고()
    {
        var req = NewRequest();
        req.Shell = ShellKind.PowerShell;
        req.TargetMode = TargetMode.Tables;
        req.Tables.Add("t");
        req.Where = "status = \"paid\"";

        Assert.Contains(Build(req).Messages, m => m.Code == "R22_PS_DOUBLE_QUOTE_IN_VALUE" && m.Severity == Severity.Warning);

        req.Where = "status = 'paid'";
        Assert.DoesNotContain(Build(req).Messages, m => m.Code == "R22_PS_DOUBLE_QUOTE_IN_VALUE");
    }

    [Fact]
    public void gzip은_bash가_아니면_오류()
    {
        var req = NewRequest();
        req.Shell = ShellKind.Cmd;
        req.OutputMode = OutputMode.GzipPipe;

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "V_GZIP_NOT_BASH");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void 제외_테이블_와일드카드는_오류()
    {
        var req = NewRequest();
        req.IgnoreTables.Add("shop.log_*");

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "V_IGNORE_TABLE_FORMAT");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void DB_이름_없으면_오류()
    {
        var req = NewRequest();
        req.Databases.Clear();

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "V_NO_DATABASE");
        Assert.Equal("", result.Command);
    }

    [Fact]
    public void 테이블_모드에서_테이블_없으면_오류()
    {
        var req = NewRequest();
        req.TargetMode = TargetMode.Tables;

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "V_NO_TABLES");
    }

    [Fact]
    public void 값_옵션에_값이_비어_있으면_오류()
    {
        var req = NewRequest();
        req.SetValue("max_allowed_packet", "   ");

        var result = Build(req);

        Assert.Contains(result.Messages, m => m.Code == "V_EMPTY_VALUE" && m.OptionId == "max_allowed_packet");
    }

    [Fact]
    public void 기본값_명시를_끄면_기본_플래그가_빠진다()
    {
        var req = NewRequest();
        req.ExplicitDefaults = false;
        req.Set("default_character_set", false);

        var result = Build(req);

        var tokens = result.PlainTokens;
        Assert.DoesNotContain("--quick", tokens);
        Assert.DoesNotContain("--triggers", tokens);
        Assert.DoesNotContain("--lock-tables", tokens);
        Assert.DoesNotContain("--add-drop-table", tokens);
        Assert.DoesNotContain("--extended-insert", tokens);
    }

    [Fact]
    public void R23_DDL만_내보낼_때는_데이터_전용_옵션을_뺀다()
    {
        var req = NewRequest();
        req.ContentMode = ContentMode.StructureOnly;
        req.InsertMode = InsertMode.Replace;
        req.Set("hex_blob", true);
        req.Set("skip_extended_insert", true);
        req.Set("routines", true);   // DDL 옵션은 그대로

        var result = Build(req);

        Assert.False(result.HasErrors, string.Join("\n", result.Messages));
        Assert.Contains("--no-data", result.Command);
        Assert.Contains("--routines", result.Command);
        Assert.DoesNotContain("--hex-blob", result.Command);
        Assert.DoesNotContain("--skip-extended-insert", result.Command);
        Assert.DoesNotContain("--extended-insert", result.Command);   // 기본값 명시도 생략
        Assert.DoesNotContain("--quick", result.Command);
        Assert.DoesNotContain("--replace", result.Command);
        var info = Assert.Single(result.Messages, m => m.Code == "R23_DDL_SKIPS_DML");
        Assert.Equal(Severity.Info, info.Severity);
        Assert.Contains("--hex-blob, --skip-extended-insert, --replace", info.Text.Ko);

        req.ContentMode = ContentMode.StructureAndData;
        Assert.Contains("--hex-blob", Build(req).Command);
    }

    [Fact]
    public void 메시지는_오류_경고_정보_순으로_정렬된다()
    {
        var req = NewRequest();
        req.Set("single_transaction", true);          // Info R4
        req.PasswordMode = PasswordMode.Plain;          // Warning R15
        req.PlainPassword = "x";
        req.Set("lock_all_tables", true);              // Error R3

        var result = Build(req);

        Assert.Equal(Severity.Error, result.Messages[0].Severity);
        Assert.Equal(Severity.Info, result.Messages[^1].Severity);
    }

    [Fact]
    public void 스키마_검증은_중복_id를_잡는다()
    {
        const string json = """
        { "groups": [ { "id": "g", "order": 1, "label": { "ko": "g", "en": "g" } } ],
          "options": [
            { "id": "a", "flag": "--a", "group": "g" },
            { "id": "a", "flag": "--b", "group": "g" } ] }
        """;
        Assert.Throws<InvalidOperationException>(() => OptionSchema.LoadFromJson(json));
    }
}
