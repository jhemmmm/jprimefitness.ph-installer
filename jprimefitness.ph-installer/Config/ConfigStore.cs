using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JPrime.Panel.App;

namespace JPrime.Panel.Config;

/// <summary>Loads/saves <see cref="PanelConfig"/> atomically with schema migrations.</summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();

    public ConfigStore(string file)
    {
        File = file;
    }

    public string File { get; }

    public bool Exists => System.IO.File.Exists(File);

    public PanelConfig Load()
    {
        lock (_gate)
        {
            var text = System.IO.File.ReadAllText(File);
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                       ?? throw new InvalidDataException("panel.json is empty");
            var version = node["schemaVersion"]?.GetValue<int>() ?? 0;
            var migrated = false;
            while (version < PanelConfig.CurrentSchemaVersion)
            {
                ConfigMigrations.Apply(version, node);
                version++;
                node["schemaVersion"] = version;
                migrated = true;
            }
            var config = node.Deserialize<PanelConfig>(JsonOptions) ?? throw new InvalidDataException("panel.json could not be parsed");
            if (migrated)
            {
                Log.Info($"panel.json migrated to schema {version}");
                Save(config);
            }
            return config;
        }
    }

    public void Save(PanelConfig config)
    {
        lock (_gate)
        {
            config.SchemaVersion = PanelConfig.CurrentSchemaVersion;
            var dir = Path.GetDirectoryName(File)!;
            Directory.CreateDirectory(dir);
            var tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
            if (System.IO.File.Exists(File))
            {
                var bak = File + ".bak";
                System.IO.File.Copy(File, bak, overwrite: true);
                System.IO.File.Replace(tmp, File, null);
            }
            else
            {
                System.IO.File.Move(tmp, File);
            }
        }
    }
}

/// <summary>One entry per schema version bump: mutate the raw JSON from version N to N+1.</summary>
public static class ConfigMigrations
{
    private static readonly Dictionary<int, Action<JsonNode>> Steps = new()
    {
        // 0 -> 1: initial schema, nothing to migrate.
        [0] = _ => { },
    };

    public static void Apply(int fromVersion, JsonNode node)
    {
        if (Steps.TryGetValue(fromVersion, out var step)) step(node);
    }
}
