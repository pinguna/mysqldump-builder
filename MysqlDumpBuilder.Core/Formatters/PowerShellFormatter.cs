using System.Text;

namespace MysqlDumpBuilder.Core.Formatters;

/// <summary>PowerShell 용. 작은따옴표는 완전한 리터럴이라 우선 쓰고, 안에 작은따옴표가 있으면 큰따옴표 + 백틱 이스케이프.</summary>
public sealed class PowerShellFormatter : ShellFormatterBase
{
    public override ShellKind Kind => ShellKind.PowerShell;
    public override string LineContinuation => "`";

    private const string Special = " \t\"'$`;&|<>(){}@#,";

    protected override bool NeedsQuoting(string value) => value.IndexOfAny(Special.ToCharArray()) >= 0;

    /// <summary>실행 파일 경로가 인용되면 PowerShell 은 문자열로 취급하므로 호출 연산자 &amp; 를 앞에 붙여야 실행된다.</summary>
    protected override string DecorateCommandLine(string firstLine)
        => firstLine.StartsWith('"') || firstLine.StartsWith('\'') ? "& " + firstLine : firstLine;

    public override string Quote(string value)
    {
        if (!value.Contains('\''))
            return "'" + value + "'";

        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            if (ch is '"' or '`' or '$') sb.Append('`');
            sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
