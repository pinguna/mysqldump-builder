namespace MysqlDumpBuilder.Core;

/// <summary>UI 언어. 스키마와 메시지는 한글/영문을 모두 가진다.</summary>
public enum Language
{
    Ko,
    En
}

/// <summary>한글/영문 문자열 한 쌍.</summary>
public sealed class LocalizedText
{
    public string Ko { get; set; } = "";
    public string En { get; set; } = "";

    public LocalizedText() { }

    public LocalizedText(string ko, string en)
    {
        Ko = ko;
        En = en;
    }

    public string Get(Language lang) => lang == Language.Ko ? Ko : En;

    public override string ToString() => Ko;
}
