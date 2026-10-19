using MysqlDumpBuilder.Core;
using Xunit;

namespace MysqlDumpBuilder.Core.Tests;

/// <summary>소켓 경로(-S)와 localhost 포트 함정(R26, R27).</summary>
public class SocketTests
{
    private static readonly OptionSchema Schema = OptionSchema.LoadEmbedded();

    private static DumpRequest NewRequest(string host = "localhost", int? port = 3306)
    {
        var req = new DumpRequest { Host = host, Port = port, User = "backup", Databases = { "shop" }, MultiLine = false };
        req.ApplySchemaDefaults(Schema);
        return req;
    }

    private static BuildResult Build(DumpRequest req) => new CommandBuilder(Schema).Build(req);

    [Fact]
    public void localhost_에_소켓을_적으면_포트_다음에_S_를_넣는다()
    {
        var req = NewRequest(port: 3307);
        req.Socket = "/data/mysql3307/mysql.sock";

        var result = Build(req);

        Assert.Contains("-h localhost -P 3307 -S /data/mysql3307/mysql.sock -u backup", result.Command);
        Assert.DoesNotContain(result.Messages, m => m.Code is "R26_LOCALHOST_PORT" or "R27_SOCKET_IGNORED");
    }

    [Theory]
    [InlineData("10.0.1.20")]
    [InlineData("127.0.0.1")]
    public void localhost_가_아니면_소켓은_조용히_뺀다(string host)
    {
        var req = NewRequest(host, 3307);
        req.Socket = "/tmp/mysql.sock";

        var result = Build(req);

        Assert.DoesNotContain(" -S ", result.Command);
        Assert.DoesNotContain(result.Messages, m => m.Code is "R26_LOCALHOST_PORT" or "R27_SOCKET_IGNORED");
    }

    [Fact]
    public void Host_가_비어도_소켓_접속이라_S_를_넣는다()
    {
        var req = NewRequest(host: "");
        req.Socket = "/tmp/mysql.sock";
        Assert.Contains("-S /tmp/mysql.sock", Build(req).Command);
    }

    [Fact]
    public void R26_localhost_에_3306_아닌_포트만_있으면_경고()
    {
        var result = Build(NewRequest(port: 3307));

        Assert.False(result.HasErrors);
        Assert.Contains(result.Messages, m => m.Code == "R26_LOCALHOST_PORT" && m.Severity == Severity.Warning && m.Text.Ko.Contains("3307"));
    }

    [Fact]
    public void R26_3306_이거나_TCP_강제거나_Windows_대상이면_경고하지_않는다()
    {
        Assert.DoesNotContain(Build(NewRequest(port: 3306)).Messages, m => m.Code == "R26_LOCALHOST_PORT");
        Assert.DoesNotContain(Build(NewRequest(port: null)).Messages, m => m.Code == "R26_LOCALHOST_PORT");
        Assert.DoesNotContain(Build(NewRequest("127.0.0.1", 3307)).Messages, m => m.Code == "R26_LOCALHOST_PORT");

        var tcp = NewRequest(port: 3307);
        tcp.Set("protocol_tcp", true);
        Assert.DoesNotContain(Build(tcp).Messages, m => m.Code == "R26_LOCALHOST_PORT");

        var windows = NewRequest(port: 3307);
        windows.Shell = ShellKind.Cmd;
        Assert.DoesNotContain(Build(windows).Messages, m => m.Code == "R26_LOCALHOST_PORT");
    }

    [Fact]
    public void R27_TCP_강제면_소켓을_넣지_않고_안내()
    {
        var req = NewRequest(port: 3307);
        req.Socket = "/tmp/mysql.sock";
        req.Set("protocol_tcp", true);

        var result = Build(req);

        Assert.DoesNotContain(" -S ", result.Command);
        Assert.Contains("--protocol=TCP", result.Command);
        Assert.Contains(result.Messages, m => m.Code == "R27_SOCKET_IGNORED" && m.Severity == Severity.Info);
    }

    [Fact]
    public void Import_원본과_같음이면_소켓과_TCP_강제를_따라간다()
    {
        var req = NewRequest(port: 3307);
        req.Job = JobKind.ExportAndImport;
        req.Import.Database = "shop_copy";
        req.Socket = "/data/mysql3307/mysql.sock";

        var import = new JobBuilder(Schema).Build(req).Import!;
        Assert.Contains("-h localhost -P 3307 -S /data/mysql3307/mysql.sock -u backup", import.Command);

        req.Set("protocol_tcp", true);
        import = new JobBuilder(Schema).Build(req).Import!;
        Assert.Contains("-P 3307 --protocol=TCP -u backup", import.Command);
        Assert.DoesNotContain(" -S ", import.Command);
    }

    [Fact]
    public void Import_대상_접속이_따로면_대상의_소켓과_R26_을_쓴다()
    {
        var req = NewRequest("10.0.1.20");
        req.Job = JobKind.ExportAndImport;
        req.Import.SameConnectionAsSource = false;
        req.Import.Host = "localhost";
        req.Import.Port = 3308;
        req.Import.User = "loader";

        var import = new JobBuilder(Schema).Build(req).Import!;
        Assert.Contains(import.Messages, m => m.Code == "R26_LOCALHOST_PORT");

        req.Import.Socket = "/data/mysql3308/mysql.sock";
        import = new JobBuilder(Schema).Build(req).Import!;
        Assert.Contains("-P 3308 -S /data/mysql3308/mysql.sock -u loader", import.Command);
        Assert.DoesNotContain(import.Messages, m => m.Code == "R26_LOCALHOST_PORT");
    }
}
