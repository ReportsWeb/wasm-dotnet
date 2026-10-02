using System.Text.Json.Nodes;
namespace ReportsWeb.Sample;

public sealed class AssetResolver(string resources, string assetBase, IEnumerable<string> trusted)
{
    readonly string[] bases = [assetBase, .. trusted];
    public byte[] Read(string name)
    {
        if (name is not ("kakuin.png" or "estimate-header.jpg"))
            throw new ArgumentException("Unregistered image.");
        return File.ReadAllBytes(Path.Combine(resources, "images", name));
    }
    public void Externalize(JsonObject definition, string sample)
    {
        foreach (var item in definition["Objects"]!.AsArray())
        {
            string? name = null;
            if ((sample == "invoice" || sample == "estimate") && item!["Name"]?.ToString() == "Image1")
                name = "kakuin.png";
            if (sample == "estimate" && item!["Name"]?.ToString() == "Image2")
                name = "estimate-header.jpg";
            if (name != null)
            {
                item!["ImagePath"] = assetBase + name;
                item["ImageDataBase64"] = "";
            }
        }
    }
    public void Inline(JsonNode node)
    {
        if (node is JsonObject o)
        {
            foreach (var (key, value) in o.ToArray())
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var text))
                {
                    var prefix = bases.FirstOrDefault(b => text.StartsWith(b, StringComparison.Ordinal));
                    if (prefix != null)
                    {
                        var name = text[prefix.Length..];
                        o[key] = "data:image/" + (name.EndsWith(".jpg") ? "jpeg" : "png") + ";base64," + Convert.ToBase64String(Read(name));
                    }
                    else if (key == "ImagePath" && !string.IsNullOrWhiteSpace(text) && !text.StartsWith("data:image/", StringComparison.Ordinal))
                        throw new ArgumentException("External image not registered.");
                }
                else if (value != null)
                    Inline(value);
            }
        }
        else if (node is JsonArray a)
            foreach (var child in a)
                if (child != null)
                    Inline(child);
    }
}
