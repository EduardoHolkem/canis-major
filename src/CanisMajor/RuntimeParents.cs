using System.Reflection;
using System.Text.Json.Nodes;

namespace CanisMajor;

// Objetos pais (Parent Objects do GameMaker) e o código das salas.
internal static partial class Runtime
{
    // Todas as classes de objeto, inclusive as abstratas, que só servem de pai.
    public static readonly Dictionary<string, Type> ObjectClasses = new(StringComparer.OrdinalIgnoreCase);

    // ---------- pais ----------

    /// <summary>
    /// O tipo e os pais dele, do mais perto ao mais longe. Segue o "parent" da ficha e, quando não há,
    /// a classe base do script. <paramref name="type"/> é a classe real, para quando ela não foi registrada.
    /// </summary>
    internal static string[] LineageOf(string kind, Type? type = null)
    {
        var names = new List<string>();
        string? current = kind;
        var currentType = type ?? ObjectClasses.GetValueOrDefault(kind);
        while (current != null)
        {
            if (names.Contains(current, StringComparer.OrdinalIgnoreCase))
                throw new GameException($"Os pais formam um círculo ({string.Join(" → ", names)} → {current}). Tire o \"parent\" de um deles.");
            names.Add(current);
            if (ObjectDefOf(current)?.Parent is { Length: > 0 } parent)
            {
                if (!ObjectClasses.ContainsKey(parent) && ObjectDefOf(parent) is null)
                    throw new GameException($"objects/{current}.json diz que o pai é '{parent}', mas esse objeto não existe.");
                current = parent;
                currentType = ObjectClasses.GetValueOrDefault(parent);
                continue;
            }
            var baseType = currentType?.BaseType;
            if (baseType is null || baseType == typeof(GameObject) || baseType == typeof(DefinedObject) || !baseType.IsSubclassOf(typeof(GameObject)))
                break;
            current = baseType.Name;
            currentType = baseType;
        }
        return [.. names];
    }

    // A classe que dá os eventos: a do próprio objeto ou a do pai mais perto que tenha script.
    private static Type ClassFor(string[] lineage)
    {
        foreach (var name in lineage)
            if (Types.TryGetValue(name, out var type)) return type;
        return typeof(DefinedObject);
    }

    // As fichas valem do pai mais longe para o próprio objeto: o filho só muda o que quiser.
    // Um componente do mesmo tipo no filho troca o do pai; os outros se somam.
    internal static void ApplyDef(GameObject obj)
    {
        var lineage = obj.Lineage.Length > 0 ? obj.Lineage : [obj.Kind];
        var components = new List<(JsonObject Json, string Owner)>();
        for (var i = lineage.Length - 1; i >= 0; i--)
        {
            if (ObjectDefOf(lineage[i]) is not { } def) continue;
            if (def.Sprite is not null) obj.Sprite = def.Sprite;
            if (def.Shader is not null) obj.Shader = def.Shader;
            if (def.Layer is { } layer) obj.Layer = layer;
            if (def.Visible is { } visible) obj.Visible = visible;
            if (def.YSort is { } ysort) obj.YSort = ysort;
            if (def.SortOffset is { } offset) obj.SortOffset = offset;
            if (def.Color is { } color) obj.Color = Color.Hex(color);
            if (def.Alpha is { } alpha) obj.Alpha = alpha;
            if (def.ScaleX is { } sx) obj.ScaleX = sx;
            if (def.ScaleY is { } sy) obj.ScaleY = sy;
            if (def.Unlit is { } unlit) obj.Unlit = unlit;
            if (def.Persistent is { } persistent) obj.Persistent = persistent;
            foreach (var json in def.Components)
            {
                var type = TypeOf(json);
                var at = type is null ? -1 : components.FindIndex(c => string.Equals(TypeOf(c.Json), type, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) components[at] = (json, lineage[i]);
                else components.Add((json, lineage[i]));
            }
            // As variáveis do pai valem primeiro; a do filho com o mesmo nome troca o valor.
            foreach (var variable in def.Variables)
                if (variable.Name.Length > 0)
                    SetVariable(obj, variable.Name, VariableDef.Convert(variable.Value, variable.Type));
        }
        foreach (var (json, owner) in components)
            obj.Add(ComponentTypes.Create(json, $"objects/{owner}.json"));
    }

    // As variáveis definidas para o objeto, do pai mais longe para ele (o filho troca a do pai).
    internal static Dictionary<string, VariableDef> VariablesOf(GameObject obj)
    {
        var all = new Dictionary<string, VariableDef>(StringComparer.OrdinalIgnoreCase);
        var lineage = obj.Lineage.Length > 0 ? obj.Lineage : [obj.Kind];
        for (var i = lineage.Length - 1; i >= 0; i--)
            if (ObjectDefOf(lineage[i]) is { } def)
                foreach (var variable in def.Variables)
                    all[variable.Name] = variable;
        return all;
    }

    // Põe o valor no campo ou propriedade de mesmo nome do script; sem um, guarda em Vars.
    internal static void SetVariable(GameObject obj, string name, object? value)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var type = obj.GetType();
        var member = ComponentTypes.Pascal(name);
        try
        {
            if (type.GetProperty(member, flags) is { CanWrite: true, SetMethod.IsPublic: true } prop)
            {
                prop.SetValue(obj, ConvertTo(value, prop.PropertyType));
                return;
            }
            if (type.GetField(member, flags) is { IsInitOnly: false } field)
            {
                field.SetValue(obj, ConvertTo(value, field.FieldType));
                return;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidCastException or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"Aviso: a variável '{name}' de {obj.Kind} não serve para o campo do script ({e.Message}). Ficou em Vars.");
        }
        obj.Vars[name] = value;
    }

    private static object? ConvertTo(object? value, Type target)
    {
        if (value is null || target.IsInstanceOfType(value)) return value;
        var inner = Nullable.GetUnderlyingType(target) ?? target;
        if (inner == typeof(Color) && value is string hex) return Color.Hex(hex);
        return System.Convert.ChangeType(value, inner, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? TypeOf(JsonObject json) => json["type"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // ---------- código da sala ----------

    public static readonly Dictionary<string, Type> RoomScripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>O código da sala aberta, se ela tiver um.</summary>
    internal static RoomScript? RoomCode;

    // "fase-1", "Fase_1" e "FASE1" dão no mesmo.
    internal static string RoomKey(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static void RegisterRoomScript(Type type)
    {
        if (type is not { IsAbstract: false, IsClass: true } || !type.IsSubclassOf(typeof(RoomScript)) || type.GetConstructor(Type.EmptyTypes) is null) return;
        var name = type.GetCustomAttribute<RoomAttribute>()?.Name ?? type.Name;
        RoomScripts[RoomKey(name)] = type;
    }

    private static void StartRoomCode(string room)
    {
        RoomCode = null;
        if (!RoomScripts.TryGetValue(RoomKey(room), out var type)) return;
        RoomCode = (RoomScript)Activator.CreateInstance(type)!;
        RoomCode.Room = room;
        RoomCode.OnCreate();
    }

    private static void EndRoomCode()
    {
        var code = RoomCode;
        RoomCode = null;
        code?.OnEnd();
    }
}
