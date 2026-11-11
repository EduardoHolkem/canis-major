namespace CanisMajor;

/// <summary>
/// Base de todo objeto do jogo. Cada arquivo em objects/ define uma classe filha:
/// <code>
/// public class Jogador : GameObject
/// {
///     public Jogador() { Sprite = "jogador"; }
///     public override void OnStep(float dt) { X += Keys.Axis("left", "right") * 120 * dt; }
/// }
/// </code>
/// </summary>
public abstract class GameObject
{
    public string? Sprite { get; set; }
    /// <summary>Maior fica por cima.</summary>
    public int Layer { get; set; }
    /// <summary>Nome da camada da sala em que a instância nasceu (null quando criada por código sem camada).</summary>
    public string? LayerName { get; set; }
    /// <summary>
    /// Ordena pela posição Y dentro da mesma profundidade: quem está mais embaixo na tela fica na frente.
    /// É assim que o personagem passa atrás de um poste. Vem da camada, e o objeto pode mudar.
    /// </summary>
    public bool YSort { get; set; }
    /// <summary>Soma ao Y só para ordenar, como o pé de um poste alto que fica acima da origem.</summary>
    public float SortOffset { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    /// <summary>Graus, sentido horário.</summary>
    public float Rotation { get; set; }
    public Color Color { get; set; } = Color.White;
    public float Alpha { get; set; } = 1;
    public float Frame { get; set; }
    /// <summary>Quadros por segundo; null usa o do sprite.</summary>
    public float? AnimationSpeed { get; set; }
    public bool Visible { get; set; } = true;
    /// <summary>Nome de um shader em shaders/ para desenhar este objeto.</summary>
    public string? Shader { get; set; }
    public bool Alive { get; internal set; } = true;
    /// <summary>Nome do tipo (o nome da classe), usado pelas salas e por <see cref="Collide(string, float, float)"/>.</summary>
    public string Kind { get; internal set; } = "";

    // ---------- eventos ----------

    /// <summary>Uma vez, quando o objeto entra na sala.</summary>
    public virtual void OnCreate() { }

    /// <summary>A cada quadro. dt é o tempo desde o quadro anterior, em segundos.</summary>
    public virtual void OnStep(float dt) { }

    /// <summary>A cada quadro, para desenhar. O padrão desenha o próprio sprite.</summary>
    public virtual void OnDraw() => DrawSelf();

    /// <summary>Quando o objeto é destruído.</summary>
    public virtual void OnDestroy() { }

    // ---------- componentes ----------

    private readonly List<Component> _components = [];

    /// <summary>Os componentes do objeto, na ordem em que rodam.</summary>
    public IReadOnlyList<Component> Components => _components;

    /// <summary>Pendura um componente no objeto. Devolve o próprio componente, para ajustar na mesma linha.</summary>
    public T Add<T>(T component) where T : Component
    {
        component.Owner = this;
        _components.Add(component);
        return component;
    }

    public T Add<T>() where T : Component, new() => Add(new T());

    /// <summary>O primeiro componente do tipo, ou null.</summary>
    public T? Get<T>() where T : Component
    {
        foreach (var c in _components)
            if (c is T found) return found;
        return null;
    }

    public void Remove(Component component) => _components.Remove(component);

    internal void StepComponents(float dt)
    {
        for (var i = 0; i < _components.Count && Alive; i++)
            if (_components[i].Enabled) _components[i].OnStep(dt);
    }

    internal void DrawComponents()
    {
        foreach (var c in _components)
            if (c.Enabled) c.OnDraw();
    }

    internal void CreateComponents()
    {
        foreach (var c in _components.ToArray())
            c.OnCreate();
    }

    internal void DestroyComponents()
    {
        foreach (var c in _components.ToArray())
            c.OnDestroy();
    }

    // ---------- ajudas ----------

    public void DrawSelf() => Runtime.DrawObject(this);

    public void Destroy() => Runtime.Destroy(this);

    public float Width => Runtime.SpriteInfo(Sprite) is { } s ? s.Width * MathF.Abs(ScaleX) : 0;
    public float Height => Runtime.SpriteInfo(Sprite) is { } s ? s.Height * MathF.Abs(ScaleY) : 0;

    /// <summary>Retângulo de colisão (esquerda, topo, direita, base), sem considerar a rotação.</summary>
    public (float Left, float Top, float Right, float Bottom) BBox(float dx = 0, float dy = 0)
    {
        // Um Collider troca o retângulo do sprite por uma máscara própria.
        if (Get<Collider>() is { Enabled: true } c)
            return (X + dx + c.OffsetX, Y + dy + c.OffsetY, X + dx + c.OffsetX + c.Width, Y + dy + c.OffsetY + c.Height);
        if (Runtime.SpriteInfo(Sprite) is not { } s) return (X + dx, Y + dy, X + dx, Y + dy);
        var left = X + dx - s.OriginX * ScaleX;
        var top = Y + dy - s.OriginY * ScaleY;
        var right = left + s.Width * ScaleX;
        var bottom = top + s.Height * ScaleY;
        return (MathF.Min(left, right), MathF.Min(top, bottom), MathF.Max(left, right), MathF.Max(top, bottom));
    }

    public bool Collides(GameObject other, float dx = 0, float dy = 0)
    {
        if (ReferenceEquals(other, this) || !other.Alive) return false;
        var a = BBox(dx, dy);
        var b = other.BBox();
        return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    }

    /// <summary>Primeiro objeto do tipo que encostaria em mim se eu andasse dx, dy.</summary>
    public GameObject? Collide(string kind, float dx = 0, float dy = 0)
    {
        foreach (var other in Runtime.InstancesOf(kind))
            if (Collides(other, dx, dy)) return other;
        return null;
    }

    public T? Collide<T>(float dx = 0, float dy = 0) where T : GameObject
    {
        foreach (var other in Runtime.InstancesOf<T>())
            if (Collides(other, dx, dy)) return other;
        return null;
    }

    /// <summary>Anda dx, dy parando ao encostar em objetos do tipo <paramref name="solid"/>.</summary>
    public (bool HitX, bool HitY) Move(float dx, float dy, string? solid = null)
    {
        if (solid is null)
        {
            X += dx;
            Y += dy;
            return (false, false);
        }
        return (MoveAxis(dx, 0, solid), MoveAxis(0, dy, solid));
    }

    private bool MoveAxis(float dx, float dy, string solid)
    {
        var amount = dx != 0 ? dx : dy;
        if (amount == 0) return false;
        if (Collide(solid, dx, dy) is null)
        {
            X += dx;
            Y += dy;
            return false;
        }
        var sign = MathF.Sign(amount);
        var step = 0f;
        while (MathF.Abs(step + sign) <= MathF.Abs(amount))
        {
            var (nx, ny) = dx != 0 ? (step + sign, 0f) : (0f, step + sign);
            if (Collide(solid, nx, ny) is not null) break;
            step += sign;
        }
        if (dx != 0) X += step; else Y += step;
        return true;
    }

    public float DistanceTo(GameObject other) => MathF.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));

    /// <summary>Ângulo em graus até o outro objeto (0 = direita, 90 = baixo).</summary>
    public float DirectionTo(GameObject other) => MathF.Atan2(other.Y - Y, other.X - X) * 180f / MathF.PI;

    public override string ToString() => $"{Kind} em ({X:0}, {Y:0})";
}
