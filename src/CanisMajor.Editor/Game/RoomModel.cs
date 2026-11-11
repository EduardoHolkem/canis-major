using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanisMajor;

namespace CanisEditor.Game;

// Uma instância na sala. Extra guarda as outras propriedades, como "speed": 2, que o jogo aplica no objeto.
public sealed class RoomInstance
{
    public string Object { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public JsonObject Extra { get; set; } = new();

    // A camada fica em "layer", junto das outras propriedades.
    public string? Layer
    {
        get => Extra["layer"] is JsonValue v && v.TryGetValue<string>(out var name) ? name : null;
        set
        {
            if (value is null) Extra.Remove("layer");
            else Extra["layer"] = value;
        }
    }

    public RoomInstance Clone() => new() { Object = Object, X = X, Y = Y, Extra = (JsonObject)Extra.DeepClone() };

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["object"] = Object, ["x"] = Number(X), ["y"] = Number(Y) };
        foreach (var (key, value) in Extra)
            json[key] = value?.DeepClone();
        return json;
    }

    private static JsonNode Number(double value) =>
        value == Math.Floor(value) ? JsonValue.Create((long)value) : JsonValue.Create(Math.Round(value, 4));

    public static RoomInstance FromJson(JsonObject json)
    {
        var instance = new RoomInstance();
        foreach (var (key, value) in json)
        {
            switch (key)
            {
                case "object":
                    instance.Object = value is JsonValue v && v.TryGetValue<string>(out var name) ? name : "";
                    break;
                case "x":
                    instance.X = AsNumber(value);
                    break;
                case "y":
                    instance.Y = AsNumber(value);
                    break;
                default:
                    instance.Extra[key] = value?.DeepClone();
                    break;
            }
        }
        return instance;
    }

    internal static double AsNumber(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? d
        : node is JsonValue s && s.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : 0;
}

// A sala aberta no editor. As propriedades que o editor não conhece continuam no arquivo.
public sealed class RoomModel
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public JsonObject Properties { get; private set; } = new();
    public List<RoomInstance> Instances { get; } = new();

    public int Width { get => Int("width", 480); set => Properties["width"] = Math.Max(1, value); }
    public int Height { get => Int("height", 270); set => Properties["height"] = Math.Max(1, value); }
    public int Grid { get => Int("grid", 16); set => Properties["grid"] = Math.Max(1, value); }

    public string Background
    {
        get => Properties["background"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : "#16181D";
        set => Properties["background"] = value;
    }

    public List<string> PostShaders
    {
        get => Properties["post_shaders"] is JsonArray array
            ? array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList()
            : [];
        set => Properties["post_shaders"] = new JsonArray(value.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
    }

    private int Int(string key, int fallback)
    {
        var value = RoomInstance.AsNumber(Properties[key]);
        return Properties[key] is null || value < 1 ? fallback : (int)value;
    }

    // Lança JsonException quando o texto não é uma sala válida.
    public static RoomModel Parse(string text)
    {
        var node = JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
            ?? throw new JsonException("A sala precisa ser um objeto JSON, entre chaves.");
        var model = new RoomModel();
        foreach (var (key, value) in node)
        {
            if (key == "layers" && value is JsonArray layers)
            {
                foreach (var item in layers)
                {
                    if (item is JsonObject obj && obj.Deserialize<LayerData>(GameProject.Json) is { } layer)
                        model.Layers.Add(layer);
                }
            }
            else if (key == "instances")
            {
                if (value is not JsonArray list)
                    throw new JsonException("\"instances\" precisa ser uma lista.");
                foreach (var item in list)
                {
                    if (item is JsonObject obj)
                        model.Instances.Add(RoomInstance.FromJson(obj));
                }
            }
            else
            {
                model.Properties[key] = value?.DeepClone();
            }
        }
        return model;
    }

    public string Serialize()
    {
        var root = (JsonObject)Properties.DeepClone();
        if (Layers.Count > 0)
            root["layers"] = new JsonArray(Layers.Select(l => (JsonNode?)LayerToJson(l)).ToArray());
        root["instances"] = new JsonArray(Instances.Select(i => (JsonNode?)i.ToJson()).ToArray());
        return RoomJson.Format(root);
    }

    public RoomModel Clone() => Parse(Serialize());

    // Camadas. Vazia, a sala tem só a camada de instâncias padrão, e o arquivo nem ganha "layers".
    public List<LayerData> Layers { get; } = new();

    // As camadas como o jogo vai ver.
    public List<LayerData> EffectiveLayers => Layers.Count > 0 ? Layers : [new LayerData { Name = RoomLayers.DefaultName }];

    // Antes de mexer nas camadas pela interface, a padrão vira de verdade no arquivo.
    public void EnsureLayers()
    {
        if (Layers.Count == 0)
            Layers.Add(new LayerData { Name = RoomLayers.DefaultName });
    }

    public LayerData? FindLayer(string? name) =>
        name is null ? null : EffectiveLayers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    // A camada de uma instância: a do "layer", ou a primeira de instâncias.
    public LayerData? LayerOf(RoomInstance instance) =>
        FindLayer(instance.Layer) ?? RoomLayers.DefaultInstanceLayer(EffectiveLayers);

    // Só o que foge do padrão vai para o arquivo, para ele ficar curto.
    private static JsonObject LayerToJson(LayerData layer)
    {
        var json = new JsonObject { ["name"] = layer.Name, ["kind"] = layer.Kind, ["depth"] = layer.Depth };
        if (!layer.Visible) json["visible"] = false;
        if (layer.YSort) json["y_sort"] = true;
        if (layer.Color is not null) json["color"] = layer.Color;
        if (layer.Sprite is not null) json["sprite"] = layer.Sprite;
        if (layer.TileX) json["tile_x"] = true;
        if (layer.TileY) json["tile_y"] = true;
        if (layer.Parallax != 0) json["parallax"] = layer.Parallax;
        return json;
    }
}
