using System.Text;
using System.Text.RegularExpressions;

namespace MysqlDumpBuilder.Core;

/// <summary>
/// 출력 파일명 템플릿의 토큰을 치환한다.
/// 지원 토큰: {db} {host} {date} {date:형식} {time} {time:형식}
/// 기본 템플릿: {db}_{date:yyyyMMdd}_{time:HHmm}.sql
///
/// 두 가지 치환 방식이 있다:
///  - Expand: 지금 시각으로 치환 (화면의 명령어 복사용)
///  - ToShellExpression: 날짜·시간을 셸의 날짜 명령으로 바꿔 스크립트 실행 시점에 치환되게 한다 (스크립트 내보내기용)
/// </summary>
public static class FileNameTemplate
{
    private static readonly Regex TokenPattern = new(@"\{(?<name>[a-zA-Z]+)(?::(?<fmt>[^}]+))?\}", RegexOptions.Compiled);

    public const string DefaultDateFormat = "yyyyMMdd";
    public const string DefaultTimeFormat = "HHmm";

    /// <summary>db 가 null 이면 {db} 토큰은 그대로 둔다.</summary>
    public static string Expand(string template, string? db, string host, DateTime now)
    {
        return TokenPattern.Replace(template, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            return name switch
            {
                "db" => db is null ? m.Value : SafeName(db),
                "host" => SafeName(host),
                "date" => now.ToString(fmt ?? DefaultDateFormat),
                "time" => now.ToString(fmt ?? DefaultTimeFormat),
                _ => m.Value
            };
        });
    }

    /// <summary>
    /// 스크립트용 파일명 식. 날짜·시간 토큰은 실행 시점에 평가되는 셸 식으로 바뀐다.
    /// cmd 는 식을 인라인할 수 없어 변수(STAMP1, STAMP2…)를 먼저 만들어야 하므로 setupLines 에 그 줄들이 담긴다.
    /// 반환값은 따옴표 없이 쓸 수 있는 조각이며, 호출자가 셸에 맞게 큰따옴표로 감싼다.
    /// </summary>
    public static string ToShellExpression(string template, string db, string host, ShellKind shell, out List<string> setupLines)
    {
        var setup = new List<string>();
        var stampIndex = 0;

        var result = TokenPattern.Replace(template, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            switch (name)
            {
                case "db": return SafeName(db);
                case "host": return SafeName(host);
                case "date":
                case "time":
                    var format = fmt ?? (name == "date" ? DefaultDateFormat : DefaultTimeFormat);
                    switch (shell)
                    {
                        case ShellKind.Bash:
                            return "$(date +" + ToStrftime(format) + ")";
                        case ShellKind.PowerShell:
                            return "$(Get-Date -Format '" + format + "')";
                        default:
                            stampIndex++;
                            var varName = "STAMP" + stampIndex;
                            setup.Add($"for /f %%i in ('powershell -NoProfile -Command \"Get-Date -Format {format}\"') do set \"{varName}=%%i\"");
                            return "%" + varName + "%";
                    }
                default:
                    return m.Value;
            }
        });

        setupLines = setup;
        return result;
    }

    /// <summary>.NET 날짜 형식(yyyyMMdd_HHmmss)을 date 명령의 strftime 형식(%Y%m%d_%H%M%S)으로 바꾼다.</summary>
    public static string ToStrftime(string dotNetFormat)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < dotNetFormat.Length)
        {
            if (Starts(dotNetFormat, i, "yyyy")) { sb.Append("%Y"); i += 4; }
            else if (Starts(dotNetFormat, i, "yy")) { sb.Append("%y"); i += 2; }
            else if (Starts(dotNetFormat, i, "MM")) { sb.Append("%m"); i += 2; }
            else if (Starts(dotNetFormat, i, "dd")) { sb.Append("%d"); i += 2; }
            else if (Starts(dotNetFormat, i, "HH")) { sb.Append("%H"); i += 2; }
            else if (Starts(dotNetFormat, i, "mm")) { sb.Append("%M"); i += 2; }
            else if (Starts(dotNetFormat, i, "ss")) { sb.Append("%S"); i += 2; }
            else
            {
                var ch = dotNetFormat[i];
                if (ch == '%') sb.Append("%%"); else sb.Append(ch);
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool Starts(string s, int at, string what)
        => string.CompareOrdinal(s, at, what, 0, what.Length) == 0;

    /// <summary>파일명에 쓸 수 없는 문자를 _ 로 바꾼다.</summary>
    private static string SafeName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
