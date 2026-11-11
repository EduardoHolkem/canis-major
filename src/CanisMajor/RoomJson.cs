using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanisMajor;

/// <summary>
/// Escreve uma sala com cada instância numa linha só. Fica curta de ler e, no Git, mover uma
/// parede muda uma linha em vez de quatro.
/// </summary>
public static class RoomJson
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>Formata o objeto da sala. As instâncias vão por último, uma por linha.</summary>
    public static string Format(JsonObject room)
    {
        var sb = new StringBuilder("{\n");
        var props = room.Where(p => p.Key != "instances").ToList();
        var instances = room["instances"] as JsonArray ?? [];
        foreach (var (key, value) in props)
            sb.Append("  ").Append(Key(key)).Append(": ").Append(Value(value)).Append(",\n");
        sb.Append("  \"instances\": [");
        if (instances.Count == 0)
        {
            sb.Append("]\n}\n");
            return sb.ToString();
        }
        sb.Append('\n');
        for (var i = 0; i < instances.Count; i++)
        {
            sb.Append("    ").Append(Instance(instances[i]));
            sb.Append(i < instances.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    /// <summary>Formata uma <see cref="RoomData"/>, como as salas criadas pelos modelos.</summary>
    public static string Format(RoomData room) =>
        Format(JsonSerializer.SerializeToNode(room, GameProject.Json)!.AsObject());

    private static string Instance(JsonNode? node)
    {
        if (node is not JsonObject obj) return Value(node);
        if (obj.Count == 0) return "{}";
        return "{ " + string.Join(", ", obj.Select(p => $"{Key(p.Key)}: {Value(p.Value)}")) + " }";
    }

    private static string Key(string key) => JsonSerializer.Serialize(key, Compact);

    private static string Value(JsonNode? node)
    {
        if (node is null) return "null";
        if (node is JsonValue v && v.TryGetValue<double>(out var d) && v.GetValueKind() == JsonValueKind.Number)
            return Number(d);
        if (node is JsonArray array && array.All(item => item is null or JsonValue))
            return "[" + string.Join(", ", array.Select(Value)) + "]";
        return node.ToJsonString(Compact);
    }

    /// <summary>Número sem ".0" à toa e sempre com ponto.</summary>
    public static string Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
