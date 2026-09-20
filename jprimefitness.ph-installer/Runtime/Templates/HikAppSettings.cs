using System.Text.Json;
using System.Text.Json.Nodes;

namespace JPrime.Panel.Runtime.Templates;

/// <summary>The helper accepts exactly these five keys under <c>HikVision</c>; any other key is a fatal startup error.</summary>
public sealed record HikAppSettings(string Username, string Password, string ForwardUrl, string ForwardToken, bool Debug)
{
    public string ToJson()
    {
        var obj = new JsonObject
        {
            ["HikVision"] = new JsonObject
            {
                ["Username"] = Username,
                ["Password"] = Password,
                ["ForwardUrl"] = ForwardUrl,
                ["ForwardToken"] = ForwardToken,
                ["Debug"] = Debug,
            },
        };
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public void Save(string path) => File.WriteAllText(path, ToJson() + Environment.NewLine);

    public static HikAppSettings? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var node = JsonNode.Parse(File.ReadAllText(path))?["HikVision"];
            if (node is null) return null;
            return new HikAppSettings(
                node["Username"]?.GetValue<string>() ?? "admin",
                node["Password"]?.GetValue<string>() ?? "",
                node["ForwardUrl"]?.GetValue<string>() ?? "",
                node["ForwardToken"]?.GetValue<string>() ?? "",
                node["Debug"]?.GetValue<bool>() ?? false);
        }
        catch
        {
            return null;
        }
    }
}
