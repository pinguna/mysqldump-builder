using System.Text;

namespace MysqlDumpBuilder.Core.Formatters;

/// <summary>토큰 묶음을 특정 셸에 맞는 문자열로 바꾼다. 인용부호와 줄 연결 문자만 셸마다 다르다.</summary>
public interface IShellFormatter
{
    ShellKind Kind { get; }

    /// <summary>줄 끝에 붙는 연결 문자. bash "\", cmd "^", PowerShell "`".</summary>
    string LineContinuation { get; }

    /// <summary>값 하나를 이 셸에서 안전하게 쓰도록 인용한다. 특수문자가 없으면 그대로 돌려준다.</summary>
    string Quote(string value);

    string Format(IReadOnlyList<CommandLineGroup> groups, bool multiLine);
}

public static class ShellFormatters
{
    public static IShellFormatter For(ShellKind kind) => kind switch
    {
        ShellKind.Bash => new BashFormatter(),
        ShellKind.Cmd => new CmdFormatter(),
        ShellKind.PowerShell => new PowerShellFormatter(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

/// <summary>세 포맷터가 공유하는 조립 로직. 상속 클래스는 인용 규칙만 정의한다.</summary>
public abstract class ShellFormatterBase : IShellFormatter
{
    public abstract ShellKind Kind { get; }
    public abstract string LineContinuation { get; }
    public abstract string Quote(string value);

    /// <summary>인용이 필요한지 판단하는 문자 집합.</summary>
    protected abstract bool NeedsQuoting(string value);

    protected string QuoteIfNeeded(string value)
        => value.Length == 0 || NeedsQuoting(value) ? Quote(value) : value;

    public string RenderToken(CommandToken token)
    {
        if (token.Raw) return token.Text;
        if (token.Value is not null) return token.Text + "=" + (token.RawValue ? token.Value : QuoteIfNeeded(token.Value));
        return token.Positional ? QuoteIfNeeded(token.Text) : token.Text;
    }

    /// <summary>첫 토큰(실행 파일)을 그린 뒤 셸이 요구하는 접두어가 있으면 붙인다 (PowerShell 의 &amp;).</summary>
    protected virtual string DecorateCommandLine(string firstLine) => firstLine;

    public string Format(IReadOnlyList<CommandLineGroup> groups, bool multiLine)
    {
        var lines = groups
            .Where(g => g.Tokens.Count > 0)
            .Select(g => string.Join(" ", g.Tokens.Select(RenderToken)))
            .ToList();

        if (lines.Count == 0) return "";
        lines[0] = DecorateCommandLine(lines[0]);
        if (!multiLine) return string.Join(" ", lines);

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(lines[i]);
            if (i < lines.Count - 1)
            {
                sb.Append(' ').Append(LineContinuation);
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }
}
