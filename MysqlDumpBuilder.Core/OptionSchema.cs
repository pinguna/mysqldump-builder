using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysqlDumpBuilder.Core;

/// <summary>체크박스 하나에 해당하는 mysqldump 옵션 정의. options.schema.json의 한 항목.</summary>
public sealed class OptionDefinition
{
    /// <summary>내부 식별자. 프리셋과 규칙에서 이 값으로 참조한다.</summary>
    public string Id { get; set; } = "";

    /// <summary>체크했을 때 내보내는 플래그. value 타입이면 "=값"이 뒤에 붙는다.</summary>
    public string Flag { get; set; } = "";

    /// <summary>체크하지 않았을 때 내보내는 플래그(기본값 명시용). 없으면 아무것도 내보내지 않는다.</summary>
    public string? UncheckedFlag { get; set; }

    public string Group { get; set; } = "";

    /// <summary>명령어 안에서의 배치 순서. 작을수록 앞.</summary>
    public int Order { get; set; }

    public LocalizedText Label { get; set; } = new();
    public LocalizedText Help { get; set; } = new();

    /// <summary>"flag" 또는 "value".</summary>
    public string Type { get; set; } = "flag";

    public bool DefaultChecked { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValuePlaceholder { get; set; }

    /// <summary>low / medium / high / critical</summary>
    public string Risk { get; set; } = "low";

    /// <summary>basic(기본 노출) / advanced(고급 접기)</summary>
    public string Tier { get; set; } = "basic";

    /// <summary>함께 켤 수 없는 옵션 id 목록.</summary>
    public List<string> Conflicts { get; set; } = new();

    /// <summary>이 옵션을 켜면 강제로 정해지는 다른 옵션 (id → 값).</summary>
    public Dictionary<string, bool> Implies { get; set; } = new();

    /// <summary>이 옵션이 유효한 덤프 대상 모드. 비어 있으면 모든 모드.</summary>
    public List<string> RequiresMode { get; set; } = new();

    /// <summary>true 면 데이터(INSERT)에만 관계있는 옵션. DDL 만 내보낼 때(--no-data) 화면에서 잠기고 명령어에서 빠진다.</summary>
    public bool DataOnly { get; set; }

    /// <summary>이 옵션이 존재하는 클라이언트 종류. 비어 있으면 모든 클라이언트.</summary>
    public List<string> Clients { get; set; } = new();

    public bool IsValueType => string.Equals(Type, "value", StringComparison.OrdinalIgnoreCase);
    public bool IsAdvanced => string.Equals(Tier, "advanced", StringComparison.OrdinalIgnoreCase);

    public bool SupportsClient(ClientKind client)
        => Clients.Count == 0 || Clients.Contains(ClientKindName(client), StringComparer.OrdinalIgnoreCase);

    public static string ClientKindName(ClientKind client) => client switch
    {
        ClientKind.Mysql57 => "mysql57",
        ClientKind.Mysql80 => "mysql80",
        _ => client.ToString().ToLowerInvariant()
    };
}

public sealed class OptionGroup
{
    public string Id { get; set; } = "";
    public int Order { get; set; }
    public LocalizedText Label { get; set; } = new();
}

/// <summary>options.schema.json 전체.</summary>
public sealed class OptionSchema
{
    public int SchemaVersion { get; set; } = 1;
    public List<OptionGroup> Groups { get; set; } = new();
    public List<OptionDefinition> Options { get; set; } = new();

    [JsonIgnore]
    public IReadOnlyDictionary<string, OptionDefinition> ById => _byId ??= Options.ToDictionary(o => o.Id, StringComparer.Ordinal);
    private Dictionary<string, OptionDefinition>? _byId;

    public OptionDefinition? Find(string id) => ById.TryGetValue(id, out var def) ? def : null;

    public IEnumerable<OptionDefinition> OptionsInGroup(string groupId)
        => Options.Where(o => o.Group == groupId).OrderBy(o => o.Order);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>DLL에 내장된 기본 스키마를 읽는다.</summary>
    public static OptionSchema LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("options.schema.json")
            ?? throw new InvalidOperationException("내장 스키마(options.schema.json)를 찾을 수 없습니다.");
        return Load(stream);
    }

    /// <summary>파일이 있으면 파일에서, 없으면 내장 스키마를 읽는다.</summary>
    public static OptionSchema LoadFromFileOrEmbedded(string path)
    {
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            return Load(stream);
        }
        return LoadEmbedded();
    }

    public static OptionSchema Load(Stream stream)
    {
        var schema = JsonSerializer.Deserialize<OptionSchema>(stream, JsonOptions)
            ?? throw new InvalidOperationException("스키마 JSON이 비어 있습니다.");
        schema.Validate();
        return schema;
    }

    public static OptionSchema LoadFromJson(string json)
    {
        var schema = JsonSerializer.Deserialize<OptionSchema>(json, JsonOptions)
            ?? throw new InvalidOperationException("스키마 JSON이 비어 있습니다.");
        schema.Validate();
        return schema;
    }

    /// <summary>id 중복, 존재하지 않는 참조 등 스키마 자체의 오류를 잡는다.</summary>
    public void Validate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(Groups.Select(g => g.Id), StringComparer.Ordinal);
        foreach (var o in Options)
        {
            if (string.IsNullOrWhiteSpace(o.Id)) throw new InvalidOperationException("id가 없는 옵션이 있습니다.");
            if (!ids.Add(o.Id)) throw new InvalidOperationException($"옵션 id가 중복됩니다: {o.Id}");
            if (string.IsNullOrWhiteSpace(o.Flag)) throw new InvalidOperationException($"옵션 {o.Id}에 flag가 없습니다.");
            if (!groupIds.Contains(o.Group)) throw new InvalidOperationException($"옵션 {o.Id}의 group '{o.Group}'이 groups에 없습니다.");
        }
        foreach (var o in Options)
        {
            foreach (var c in o.Conflicts)
                if (!ids.Contains(c)) throw new InvalidOperationException($"옵션 {o.Id}의 conflicts에 없는 id '{c}'가 있습니다.");
            foreach (var kv in o.Implies)
                if (!ids.Contains(kv.Key)) throw new InvalidOperationException($"옵션 {o.Id}의 implies에 없는 id '{kv.Key}'가 있습니다.");
        }
    }
}
