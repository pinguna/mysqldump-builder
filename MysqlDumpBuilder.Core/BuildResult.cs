namespace MysqlDumpBuilder.Core;

public enum Severity
{
    /// <summary>참고 사항. 명령어는 정상 생성.</summary>
    Info,
    /// <summary>생성은 되지만 주의가 필요.</summary>
    Warning,
    /// <summary>명령어 생성 차단.</summary>
    Error
}

/// <summary>빌더가 돌려주는 경고/오류 한 건.</summary>
public sealed class BuildMessage
{
    public string Code { get; }
    public Severity Severity { get; }
    public LocalizedText Text { get; }

    /// <summary>이 메시지와 관련된 옵션 id (UI에서 강조용). 없을 수 있다.</summary>
    public string? OptionId { get; }

    public BuildMessage(string code, Severity severity, LocalizedText text, string? optionId = null)
    {
        Code = code;
        Severity = severity;
        Text = text;
        OptionId = optionId;
    }

    public override string ToString() => $"[{Severity}] {Code}: {Text.Ko}";
}

/// <summary>명령어 한 줄을 이루는 토큰. 포맷터가 셸에 맞게 인용부호를 붙인다.</summary>
public sealed class CommandToken
{
    /// <summary>플래그 또는 위치 인자 그대로. 예: "--routines", "shop", "-h"</summary>
    public string Text { get; }

    /// <summary>"=값" 형태로 붙는 값. 예: --where 의 조건식. 특수문자가 있으면 포맷터가 인용한다.</summary>
    public string? Value { get; }

    /// <summary>true 이면 셸 문법(>, |, gzip)이므로 절대 인용하지 않는다.</summary>
    public bool Raw { get; }

    /// <summary>true 이면 값이 아니라 위치 인자(DB명, 테이블명, 파일명)라서 필요 시 토큰 전체를 인용한다.</summary>
    public bool Positional { get; }

    /// <summary>true 이면 Value 가 이미 셸 식(예: "$OUT_FILE")이라 인용하지 않고 그대로 붙인다.</summary>
    public bool RawValue { get; }

    private CommandToken(string text, string? value, bool raw, bool positional, bool rawValue = false)
    {
        Text = text;
        Value = value;
        Raw = raw;
        Positional = positional;
        RawValue = rawValue;
    }

    public static CommandToken Flag(string text) => new(text, null, raw: false, positional: false);
    public static CommandToken FlagWithValue(string flag, string value) => new(flag, value, raw: false, positional: false);
    public static CommandToken FlagWithRawValue(string flag, string shellExpression) => new(flag, shellExpression, raw: false, positional: false, rawValue: true);
    public static CommandToken Arg(string text) => new(text, null, raw: false, positional: true);
    public static CommandToken Shell(string text) => new(text, null, raw: true, positional: false);

    public override string ToString() => Value is null ? Text : $"{Text}={Value}";
}

/// <summary>여러 줄 모드에서 한 줄에 놓이는 토큰 묶음.</summary>
public sealed class CommandLineGroup
{
    public string Name { get; }
    public List<CommandToken> Tokens { get; } = new();

    public CommandLineGroup(string name) => Name = name;
}

/// <summary>CommandBuilder.Build 의 결과.</summary>
public sealed class BuildResult
{
    /// <summary>셸에 맞게 포맷된 최종 명령어. 오류가 있으면 빈 문자열.</summary>
    public string Command { get; init; } = "";

    /// <summary>포맷 전 토큰 묶음. 테스트와 디버깅용.</summary>
    public IReadOnlyList<CommandLineGroup> Groups { get; init; } = Array.Empty<CommandLineGroup>();

    public IReadOnlyList<BuildMessage> Messages { get; init; } = Array.Empty<BuildMessage>();

    public bool HasErrors => Messages.Any(m => m.Severity == Severity.Error);

    public IEnumerable<CommandToken> AllTokens => Groups.SelectMany(g => g.Tokens);

    /// <summary>테스트용: 토큰을 공백으로 이은 단순 문자열 (인용 없음).</summary>
    public string PlainTokens => string.Join(" ", AllTokens.Select(t => t.ToString()));
}
