using MysqlDumpBuilder.Core;
using MysqlDumpBuilder.Core.Formatters;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>bash / cmd / PowerShell 3종의 인용·이스케이프·줄 연결.</summary>
public class FormatterTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();

    private static DumpRequest NewRequest(ShellKind shell)
    {
        var req = new DumpRequest
        {
            User = "u",
            Shell = shell,
            OutputMode = OutputMode.ResultFile,
            MultiLine = false,
            Databases = { "shop" },
            Now = new DateTime(2026, 10, 6, 14, 30, 0)
        };
        req.ApplySchemaDefaults(Schema);
        return req;
    }

    private static string Build(DumpRequest req) => new CommandBuilder(Schema).Build(req).Command;

    [Fact]
    public void bash_where는_작은따옴표가_있으면_큰따옴표로_감싼다()
    {
        var req = NewRequest(ShellKind.Bash);
        req.TargetMode = TargetMode.Tables;
        req.Tables.Add("t");
        req.Where = "created_at >= '2026-01-01'";

        Assert.Contains("--where=\"created_at >= '2026-01-01'\"", Build(req));
    }

    [Fact]
    public void bash_where는_작은따옴표가_없으면_작은따옴표로_감싼다()
    {
        var req = NewRequest(ShellKind.Bash);
        req.TargetMode = TargetMode.Tables;
        req.Tables.Add("t");
        req.Where = "id > 10 AND status = 1";

        Assert.Contains("--where='id > 10 AND status = 1'", Build(req));
    }

    [Fact]
    public void bash_큰따옴표_안에서는_달러와_백틱을_이스케이프한다()
    {
        var f = new BashFormatter();
        Assert.Equal("\"it's \\$x \\`y\\` \\\"z\\\"\"", f.Quote("it's $x `y` \"z\""));
    }

    [Fact]
    public void cmd_where는_큰따옴표로_감싸고_안의_큰따옴표는_백슬래시()
    {
        var req = NewRequest(ShellKind.Cmd);
        req.TargetMode = TargetMode.Tables;
        req.Tables.Add("t");
        req.Where = "name = \"A B\"";

        Assert.Contains("--where=\"name = \\\"A B\\\"\"", Build(req));
    }

    [Fact]
    public void cmd_출력은_result_file이_기본이고_리다이렉션도_가능()
    {
        var req = NewRequest(ShellKind.Cmd);
        Assert.EndsWith("--result-file=shop_20261006_1430.sql", Build(req));

        req.OutputMode = OutputMode.StdoutRedirect;
        Assert.EndsWith("> shop_20261006_1430.sql", Build(req));
    }

    [Fact]
    public void powershell_값은_작은따옴표를_우선_쓴다()
    {
        var req = NewRequest(ShellKind.PowerShell);
        req.PasswordMode = PasswordMode.Plain;
        req.PlainPassword = "pa$$ word";

        Assert.Contains("--password='pa$$ word'", Build(req));
    }

    [Fact]
    public void powershell_작은따옴표가_있으면_큰따옴표와_백틱_이스케이프()
    {
        var f = new PowerShellFormatter();
        Assert.Equal("\"it's `$x\"", f.Quote("it's $x"));
    }

    [Theory]
    [InlineData(ShellKind.Bash, " \\")]
    [InlineData(ShellKind.Cmd, " ^")]
    [InlineData(ShellKind.PowerShell, " `")]
    public void 여러_줄_연결_문자는_셸마다_다르다(ShellKind shell, string suffix)
    {
        var req = NewRequest(shell);
        req.MultiLine = true;
        req.Set("single_transaction", true);

        var lines = Build(req).Split('\n');
        Assert.True(lines.Length > 2);
        Assert.EndsWith(suffix, lines[0]);
        Assert.False(lines[^1].EndsWith(suffix.Trim()));
    }

    [Fact]
    public void 공백이_있는_바이너리_경로는_인용된다()
    {
        var req = NewRequest(ShellKind.Cmd);
        req.BinaryPath = @"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe";

        Assert.StartsWith("\"C:\\Program Files\\MySQL\\MySQL Server 8.0\\bin\\mysqldump.exe\" -h localhost", Build(req));
    }

    [Fact]
    public void powershell_인용된_실행파일_경로에는_호출_연산자가_붙는다()
    {
        var req = NewRequest(ShellKind.PowerShell);
        req.BinaryPath = @"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqldump.exe";

        Assert.StartsWith("& 'C:\\Program Files\\MySQL\\MySQL Server 8.0\\bin\\mysqldump.exe' -h localhost", Build(req));
        Assert.StartsWith("mysqldump -h", Build(NewRequest(ShellKind.PowerShell)));
    }

    [Fact]
    public void 특수문자_없는_값은_인용하지_않는다()
    {
        foreach (var shell in Enum.GetValues<ShellKind>())
        {
            var f = ShellFormatters.For(shell);
            var group = new CommandLineGroup("g");
            group.Tokens.Add(CommandToken.FlagWithValue("--max-allowed-packet", "1G"));
            group.Tokens.Add(CommandToken.Arg("shop"));
            Assert.Equal("--max-allowed-packet=1G shop", f.Format(new[] { group }, multiLine: false));
        }
    }

    [Fact]
    public void 파일명_템플릿_토큰_치환()
    {
        var now = new DateTime(2026, 10, 6, 14, 30, 0);
        Assert.Equal("shop_20261006_1430.sql", FileNameTemplate.Expand("{db}_{date:yyyyMMdd}_{time:HHmm}.sql", "shop", "h", now));
        Assert.Equal("h_shop_2026-10-06.sql", FileNameTemplate.Expand("{host}_{db}_{date:yyyy-MM-dd}.sql", "shop", "h", now));
        Assert.Equal("20261006_1430", FileNameTemplate.Expand("{date}_{time}", "", "", now));
        Assert.Equal("10.0.1.20_shop.sql", FileNameTemplate.Expand("{host}_{db}.sql", "shop", "10.0.1.20", now));
        Assert.Equal("a_b.sql", FileNameTemplate.Expand("{db}.sql", "a/b", "", now).Replace("a_b", "a_b"));
    }
}
