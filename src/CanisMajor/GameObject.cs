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
    /// <summary>Não recebe a iluminação: aparece sempre com a cor normal, como uma placa luminosa ou um efeito.</summary>
    public bool Unlit { get; set; }
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

    // ---------- mais eventos, na ordem do quadro ----------
    // Begin Step, alarmes, teclado e mouse, Step, colisões, End Step, Draw e Draw GUI.

    /// <summary>No começo do quadro, antes de todos os OnStep.</summary>
    public virtual void OnBeginStep(float dt) { }

    /// <summary>No fim do quadro, depois dos OnStep e das colisões. Bom para acertar a posição final.</summary>
    public virtual void OnEndStep(float dt) { }

    /// <summary>Quando um alarme chega a zero. Ligue com <see cref="SetAlarm"/>.</summary>
    public virtual void OnAlarm(int index) { }

    /// <summary>
    /// A cada quadro em que este objeto encosta em outro (pelos retângulos de colisão). Filtre o tipo
    /// no código: <c>if (other is Estrela e) e.Destroy();</c>
    /// </summary>
    public virtual void OnCollision(GameObject other) { }

    /// <summary>Quando uma tecla é apertada, com o nome dela ("left", "space", "a").</summary>
    public virtual void OnKeyPressed(string key) { }

    /// <summary>Quando uma tecla é solta.</summary>
    public virtual void OnKeyReleased(string key) { }

    /// <summary>Quando um botão do mouse é apertado em cima deste objeto (1 esquerdo, 2 meio, 3 direito).</summary>
    public virtual void OnClick(int button) { }

    /// <summary>Quando o mouse entra no retângulo deste objeto.</summary>
    public virtual void OnMouseEnter() { }

    /// <summary>Quando o mouse sai do retângulo deste objeto.</summary>
    public virtual void OnMouseLeave() { }

    /// <summary>
    /// Desenho da interface, depois de tudo e preso à tela: vida, placar, menus. Aqui o <c>Draw</c> já
    /// usa a tela (0, 0 é o canto da janela do jogo), e nada recebe a iluminação.
    /// </summary>
    public virtual void OnDrawGui() { }

    // ---------- alarmes ----------

    private readonly float[] _alarms = Enumerable.Repeat(-1f, AlarmCount).ToArray();

    /// <summary>Quantos alarmes cada objeto tem (0 a 11), como no GameMaker.</summary>
    public const int AlarmCount = 12;

    /// <summary>Liga o alarme: daqui a tantos segundos, <see cref="OnAlarm"/> roda com o índice. Negativo desliga.</summary>
    public void SetAlarm(int index, float seconds)
    {
        if (index < 0 || index >= AlarmCount) throw new ArgumentOutOfRangeException(nameof(index), $"Os alarmes vão de 0 a {AlarmCount - 1}.");
        _alarms[index] = seconds;
    }

    /// <summary>Quanto falta para o alarme tocar, em segundos, ou -1 se está desligado.</summary>
    public float GetAlarm(int index) => index is >= 0 and < AlarmCount ? _alarms[index] : -1;

    internal void TickAlarms(float dt)
    {
        for (var i = 0; i < AlarmCount && Alive; i++)
        {
            if (_alarms[i] < 0) continue;
            _alarms[i] -= dt;
            if (_alarms[i] > 0) continue;
            _alarms[i] = -1;
            OnAlarm(i);
        }
    }

    // O mouse estava em cima no quadro anterior (para OnMouseEnter e OnMouseLeave).
    internal bool MouseInside;

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
        // A máscara do sprite (do editor de sprites) recorta o quadro; sem ela, vale o quadro inteiro.
        var (ml, mt, mr, mb) = s.Mask ?? (0, 0, s.Width, s.Height);
        var left = X + dx + (ml - s.OriginX) * ScaleX;
        var top = Y + dy + (mt - s.OriginY) * ScaleY;
        var right = X + dx + (mr - s.OriginX) * ScaleX;
        var bottom = Y + dy + (mb - s.OriginY) * ScaleY;
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
        if (!Blocked(solid, dx, dy))
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
            if (Blocked(solid, nx, ny)) break;
            step += sign;
        }
        if (dx != 0) X += step; else Y += step;
        return true;
    }

    // Bloqueia o movimento: um objeto do tipo, ou um tile sólido da camada de tiles com esse nome.
    // Confere o caminho inteiro, do primeiro passo até a chegada: uma queda rápida não atravessa o chão.
    private bool Blocked(string solid, float dx, float dy)
    {
        var first = BBox(MathF.Sign(dx) * MathF.Min(1, MathF.Abs(dx)), MathF.Sign(dy) * MathF.Min(1, MathF.Abs(dy)));
        var last = BBox(dx, dy);
        var box = (MathF.Min(first.Left, last.Left), MathF.Min(first.Top, last.Top), MathF.Max(first.Right, last.Right), MathF.Max(first.Bottom, last.Bottom));
        foreach (var other in Runtime.InstancesOf(solid))
        {
            if (ReferenceEquals(other, this)) continue;
            var b = other.BBox();
            if (box.Item1 < b.Right && box.Item3 > b.Left && box.Item2 < b.Bottom && box.Item4 > b.Top) return true;
        }
        return Runtime.TilesBlock(solid, box);
    }

    /// <summary>Se eu encostaria num tile sólido da camada de tiles se andasse dx, dy.</summary>
    public bool TouchingTiles(string layer, float dx = 0, float dy = 0) => Runtime.TilesBlock(layer, BBox(dx, dy));

    public float DistanceTo(GameObject other) => MathF.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));

    /// <summary>Ângulo em graus até o outro objeto (0 = direita, 90 = baixo).</summary>
    public float DirectionTo(GameObject other) => MathF.Atan2(other.Y - Y, other.X - X) * 180f / MathF.PI;

    public override string ToString() => $"{Kind} em ({X:0}, {Y:0})";
}
