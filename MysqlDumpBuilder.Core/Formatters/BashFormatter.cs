using System.Text;

namespace MysqlDumpBuilder.Core.Formatters;

/// <summary>Linux bash 용. 작은따옴표를 우선 쓰고, 값 안에 작은따옴표가 있으면 큰따옴표 + 백슬래시 이스케이프.</summary>
public sealed class BashFormatter : ShellFormatterBase
{
    public override ShellKind Kind => ShellKind.Bash;
    public override string LineContinuation => "\\";

    private const string Special = " \t\"'$`\\|&;<>()*?[]!#~{}";

    protected override bool NeedsQuoting(string value) => value.IndexOfAny(Special.ToCharArray()) >= 0;

    public override string Quote(string value)
    {
        if (!value.Contains('\''))
            return "'" + value + "'";

        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            if (ch is '"' or '\\' or '$' or '`') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
