using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanisMajor;

/// <summary>
/// Um pedaço de comportamento que se pendura num objeto. O objeto roda os componentes depois do
/// próprio OnStep e do próprio OnDraw. Crie os seus herdando desta classe:
/// <code>
/// public class Piscar : Component
/// {
///     public float Velocidade { get; set; } = 4;
///     public override void OnStep(float dt) => Owner.Alpha = 0.5f + 0.5f * MathF.Sin(Game.Time * Velocidade);
/// }
/// </code>
/// No editor, ele aparece em "+ Componente", com as propriedades públicas editáveis.
/// </summary>
public abstract class Component
{
    /// <summary>O objeto que tem este componente.</summary>
    public GameObject Owner { get; internal set; } = null!;
    public bool Enabled { get; set; } = true;

    public virtual void OnCreate() { }
    public virtual void OnStep(float dt) { }
    public virtual void OnDraw() { }
    public virtual void OnDestroy() { }
}

/// <summary>
/// Movimento pronto, como o do GameMaker: velocidade, direção, gravidade e atrito, sem escrever código.
/// Com Solid preenchido, para ao encostar nesses objetos.
/// </summary>
public sealed class Motion : Component
{
    /// <summary>Pixels por segundo na horizontal.</summary>
    public float SpeedX { get; set; }
    /// <summary>Pixels por segundo na vertical.</summary>
    public float SpeedY { get; set; }
    /// <summary>Aceleração para baixo, em pixels por segundo ao quadrado.</summary>
    public float Gravity { get; set; }
    /// <summary>Quanto da velocidade se perde por segundo (0 a 1).</summary>
    public float Friction { get; set; }
    /// <summary>Velocidade máxima (0 = sem limite).</summary>
    public float MaxSpeed { get; set; }
    /// <summary>Tipo de objeto que bloqueia o movimento, como "Parede".</summary>
    public string? Solid { get; set; }

    public override void OnStep(float dt)
    {
        SpeedY += Gravity * dt;
        if (Friction > 0)
        {
            var keep = MathF.Max(0, 1 - Friction * dt);
            SpeedX *= keep;
            SpeedY *= keep;
        }
        if (MaxSpeed > 0)
        {
            var speed = MathF.Sqrt(SpeedX * SpeedX + SpeedY * SpeedY);
            if (speed > MaxSpeed)
            {
                SpeedX *= MaxSpeed / speed;
                SpeedY *= MaxSpeed / speed;
            }
        }
        var (hitX, hitY) = Owner.Move(SpeedX * dt, SpeedY * dt, Solid);
        if (hitX) SpeedX = 0;
        if (hitY) SpeedY = 0;
    }
}

/// <summary>
/// Máscara de colisão própria, em vez do retângulo do sprite. Útil para o pé de um personagem
/// colidir sem a cabeça, ou para objetos sem sprite.
/// </summary>
public sealed class Collider : Component
{
    /// <summary>Canto da máscara, a partir da posição do objeto.</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Width { get; set; } = 16;
    public float Height { get; set; } = 16;
    /// <summary>Desenha a máscara em vermelho, para conferir no jogo.</summary>
    public bool Debug { get; set; }

    public override void OnDraw()
    {
        if (!Debug) return;
        var (l, t, r, b) = Owner.BBox();
        Draw.Rect(l, t, r - l, b - t, Color.Hex("#FF5050"), outline: 1);
    }
}

/// <summary>Manda valores para o shader do objeto a cada quadro (param(0) a param(3) no WGSL).</summary>
public sealed class ShaderParams : Component
{
    public float P0X { get; set; }
    public float P0Y { get; set; }
    public float P0Z { get; set; }
    public float P0W { get; set; }
    /// <summary>Soma o tempo do jogo em P0W, para animar o shader sem código.</summary>
    public bool TimeInW { get; set; }

    public override void OnStep(float dt)
    {
        if (Owner.Shader is { } shader)
            Game.SetShaderParam(shader, 0, P0X, P0Y, P0Z, TimeInW ? Game.Time : P0W);
    }
}

/// <summary>Destrói o objeto depois de um tempo, como uma bala ou uma explosão.</summary>
public sealed class Lifetime : Component
{
    public float Seconds { get; set; } = 1;
    private float _elapsed;

    public override void OnStep(float dt)
    {
        _elapsed += dt;
        if (_elapsed >= Seconds) Owner.Destroy();
    }
}

/// <summary>Os tipos de componente conhecidos: os que vêm na engine e os do jogo.</summary>
public static class ComponentTypes
{
    public static readonly Dictionary<string, Type> All = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Motion)] = typeof(Motion),
        [nameof(Collider)] = typeof(Collider),
        [nameof(ShaderParams)] = typeof(ShaderParams),
        [nameof(Lifetime)] = typeof(Lifetime),
        [nameof(Light)] = typeof(Light),
        [nameof(ShadowCaster)] = typeof(ShadowCaster),
    };

    public static void Register(Type type)
    {
        if (type is { IsAbstract: false, IsClass: true } && type.IsSubclassOf(typeof(Component)) && type.GetConstructor(Type.EmptyTypes) != null)
            All[type.Name] = type;
    }

    /// <summary>Cria o componente a partir do JSON { "type": "Motion", "gravity": 400 }.</summary>
    public static Component Create(JsonObject json, string where)
    {
        var name = json["type"]?.GetValue<string>() ?? throw new GameException($"{where}: um componente não diz o \"type\".");
        if (!All.TryGetValue(name, out var type))
            throw new GameException($"{where}: o componente '{name}' não existe. Os prontos são {string.Join(", ", All.Keys)}.");
        var component = (Component)Activator.CreateInstance(type)!;
        foreach (var (key, value) in json)
        {
            if (key is "type" || value is null) continue;
            var prop = type.GetProperty(Pascal(key), BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is not { CanWrite: true })
            {
                Console.Error.WriteLine($"Aviso: {where}: o componente {name} não tem a propriedade '{key}'.");
                continue;
            }
            try
            {
                prop.SetValue(component, value.Deserialize(prop.PropertyType, GameProject.Json));
            }
            catch (JsonException)
            {
                throw new GameException($"{where}: o valor de '{key}' no componente {name} não serve para {prop.PropertyType.Name}.");
            }
        }
        return component;
    }

    internal static string Pascal(string key) =>
        string.Concat(key.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
}

/// <summary>
/// A ficha de um objeto (objects/Nome.json), editada pelo Inspetor: sprite, shader, profundidade e
/// componentes. A classe C# com o mesmo nome é o script. Ela é opcional: um objeto só com a ficha
/// já funciona, com os componentes fazendo o trabalho.
/// </summary>
public sealed class ObjectDef
{
    public string? Sprite { get; set; }
    public string? Shader { get; set; }
    public int? Layer { get; set; }
    public bool? Visible { get; set; }
    public bool? YSort { get; set; }
    public float? SortOffset { get; set; }
    public string? Color { get; set; }
    public float? Alpha { get; set; }
    public float? ScaleX { get; set; }
    public float? ScaleY { get; set; }
    public bool? Unlit { get; set; }
    public List<JsonObject> Components { get; set; } = [];
}

/// <summary>Objeto que só tem a ficha, sem classe C#.</summary>
public sealed class DefinedObject : GameObject;
