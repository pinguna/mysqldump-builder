using System.Text.Json;
using System.Text.Json.Serialization;
using MysqlDumpBuilder.Core;

namespace MysqlDumpBuilder.App;

/// <summary>settings.json — 언어, 마지막 화면 상태(비밀번호 제외), 창 크기. exe 와 같은 폴더에 저장 (포터블).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public Language Language { get; set; } = Language.Ko;
    public bool ShowAdvanced { get; set; }
    public bool MultiLine { get; set; } = true;
    public bool ExplicitDefaults { get; set; } = true;
    public bool DarkMode { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }

    /// <summary>마지막 입력 상태. Preset 과 같은 모양이라 비밀번호는 절대 들어가지 않는다.</summary>
    public Preset? LastState { get; set; }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // 설정 파일이 깨졌으면 기본값으로 시작한다. 프로그램이 안 뜨는 것보다 낫다.
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 읽기 전용 폴더 등에서는 조용히 넘어간다.
        }
    }
}
