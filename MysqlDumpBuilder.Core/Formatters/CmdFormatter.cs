using System.Text;

namespace MysqlDumpBuilder.Core.Formatters;

/// <summary>Windows cmd.exe 용. 큰따옴표만 쓸 수 있고, 안의 큰따옴표는 \" 로 (mysqldump 는 MSVC 런타임 규칙으로 인자를 나눈다).</summary>
public sealed class CmdFormatter : ShellFormatterBase
{
    public override ShellKind Kind => ShellKind.Cmd;
    public override string LineContinuation => "^";

    private const string Special = " \t\"&|<>^()%!'=";

    protected override bool NeedsQuoting(string value) => value.IndexOfAny(Special.ToCharArray()) >= 0;

    public override string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            if (ch == '"') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
