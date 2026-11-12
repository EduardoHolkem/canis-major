namespace CanisMajor;

/// <summary>
/// Inteligência pronta para inimigos, guardas, animais e ajudantes. Ponha na ficha do objeto e
/// ajuste pelos números, ou mande pelo código (<see cref="Chase"/>, <see cref="Flee"/>,
/// <see cref="GoTo"/>, <see cref="Stop"/>).
///
/// Comportamento base (quando não vê o alvo): "idle" (parado), "wander" (anda à toa perto de onde
/// nasceu), "patrol" (segue os pontos de <see cref="PatrolPoints"/>) ou "follow" (acompanha o alvo
/// de longe, como um ajudante). Ao ver o alvo dentro de <see cref="SightRange"/>, troca para
/// <see cref="OnSight"/>: "chase" (persegue), "flee" (foge) ou "none" (ignora). Ao perder o alvo de
/// vista por <see cref="ForgetSeconds"/>, volta ao comportamento base.
/// </summary>
public sealed class AI : Component
{
    // ---------- o que dá para ajustar na ficha ----------

    /// <summary>"idle", "wander", "patrol" ou "follow".</summary>
    public string Behavior { get; set; } = "wander";
    /// <summary>O tipo de objeto que ela observa, como "Jogador".</summary>
    public string Target { get; set; } = "Jogador";
    /// <summary>"chase", "flee" ou "none".</summary>
    public string OnSight { get; set; } = "chase";
    /// <summary>Distância em que enxerga o alvo (0 nunca vê).</summary>
    public float SightRange { get; set; } = 120;
    /// <summary>Só vê se nada sólido estiver no meio.</summary>
    public bool LineOfSight { get; set; } = true;
    /// <summary>Segundos sem ver o alvo até desistir.</summary>
    public float ForgetSeconds { get; set; } = 2;
    /// <summary>Velocidade andando, em pixels por segundo.</summary>
    public float Speed { get; set; } = 50;
    /// <summary>Velocidade perseguindo ou fugindo (0 usa o dobro de Speed).</summary>
    public float RunSpeed { get; set; }
    /// <summary>O que bloqueia o caminho: um tipo de objeto ou uma camada de tiles, como "Parede".</summary>
    public string? Solid { get; set; } = "Parede";
    /// <summary>Desvia das paredes pelo caminho mais curto (A*). Desligado, anda em linha reta.</summary>
    public bool Pathfinding { get; set; } = true;
    /// <summary>Tamanho da célula do caminho, em pixels.</summary>
    public int Cell { get; set; } = 16;
    /// <summary>Patrulha: pontos "x,y; x,y; x,y", em pixels na sala.</summary>
    public string PatrolPoints { get; set; } = "";
    /// <summary>Patrulha: true volta pelo mesmo caminho (ida e volta); false recomeça do primeiro.</summary>
    public bool PingPong { get; set; }
    /// <summary>Segundos parado em cada ponto da patrulha ou do passeio.</summary>
    public float Wait { get; set; } = 1;
    /// <summary>Passeio: até que distância de onde nasceu ela anda à toa.</summary>
    public float WanderRadius { get; set; } = 64;
    /// <summary>Perseguir e acompanhar: para a esta distância do alvo.</summary>
    public float StopDistance { get; set; } = 12;
    /// <summary>Fugir: corre até ficar a esta distância.</summary>
    public float SafeDistance { get; set; } = 160;
    /// <summary>Vira o sprite para o lado em que anda.</summary>
    public bool FlipSprite { get; set; } = true;
    /// <summary>Desenha o caminho e o alcance da visão, para conferir no jogo.</summary>
    public bool Debug { get; set; }

    // ---------- estado, para ler no código ----------

    /// <summary>O que está fazendo agora: "idle", "wander", "patrol", "follow", "chase", "flee", "goto".</summary>
    public string State { get; private set; } = "idle";
    /// <summary>O alvo visto por último (ou o mandado pelo código).</summary>
    public GameObject? Seen { get; private set; }
    /// <summary>Chamado quando o estado muda: (antigo, novo).</summary>
    public event Action<string, string>? StateChanged;

    private float _homeX, _homeY;
    private List<(float X, float Y)> _patrol = [];
    private int _patrolIndex;
    private int _patrolStep = 1;
    private float _waitLeft;
    private (float X, float Y)? _destination;
    private List<(float X, float Y)>? _path;
    private int _pathIndex;
    private float _repathIn;
    private float _lostFor;
    private GameObject? _forced;
    private string? _forcedState;
    private readonly Random _random = new();

    public override void OnCreate()
    {
        _homeX = Owner.X;
        _homeY = Owner.Y;
        _patrol = ParsePoints(PatrolPoints);
        SetState(Behavior);
    }

    // ---------- comandos para o código ----------

    /// <summary>Persegue esse objeto até mandar outra coisa.</summary>
    public void Chase(GameObject target) => Force(target, "chase");

    /// <summary>Foge desse objeto até mandar outra coisa.</summary>
    public void Flee(GameObject target) => Force(target, "flee");

    /// <summary>Anda até o ponto e para.</summary>
    public void GoTo(float x, float y)
    {
        _forced = null;
        _forcedState = "goto";
        // Primeiro o estado (que limpa o caminho antigo), depois o destino novo.
        SetState("goto");
        SetDestination(x, y);
    }

    /// <summary>Para tudo e fica parado até mandar outra coisa.</summary>
    public void Stop()
    {
        _forced = null;
        _forcedState = "idle";
        ClearPath();
        SetState("idle");
    }

    /// <summary>Esquece as ordens e volta a decidir sozinha.</summary>
    public void Resume()
    {
        _forced = null;
        _forcedState = null;
        SetState(Behavior);
    }

    /// <summary>Se ela enxerga o objeto agora (distância e, se ligado, linha de visão).</summary>
    public bool CanSee(GameObject other)
    {
        if (!other.Alive || SightRange <= 0 || Owner.DistanceTo(other) > SightRange) return false;
        return !LineOfSight || Solid is null || !CanisMajor.Pathfinding.LineBlocked(Owner.X, Owner.Y, other.X, other.Y, Solid, Cell);
    }

    private void Force(GameObject target, string state)
    {
        _forced = target;
        _forcedState = state;
        Seen = target;
        SetState(state);
    }

    // ---------- a cada quadro ----------

    public override void OnStep(float dt)
    {
        Decide(dt);
        switch (State)
        {
            case "wander": Wander(dt); break;
            case "patrol": Patrol(dt); break;
            case "follow": Follow(dt); break;
            case "chase": ChaseStep(dt); break;
            case "flee": FleeStep(dt); break;
            case "goto":
                if (MoveAlong(Speed, dt)) { _forcedState = "idle"; SetState("idle"); }
                break;
        }
    }

    private void Decide(float dt)
    {
        if (_forcedState is not null)
        {
            if (_forced is { Alive: false }) Resume();
            return;
        }
        var target = FindTarget();
        if (target is not null && OnSight is "chase" or "flee" && CanSee(target))
        {
            Seen = target;
            _lostFor = 0;
            SetState(OnSight);
            return;
        }
        if (State is "chase" or "flee")
        {
            _lostFor += dt;
            if (_lostFor >= ForgetSeconds || Seen is not { Alive: true })
                SetState(Behavior);
        }
    }

    private GameObject? FindTarget()
    {
        GameObject? best = null;
        var bestDistance = float.MaxValue;
        foreach (var o in Runtime.InstancesOf(Target))
        {
            if (ReferenceEquals(o, Owner)) continue;
            var d = Owner.DistanceTo(o);
            if (d < bestDistance) { best = o; bestDistance = d; }
        }
        return best;
    }

    private void Wander(float dt)
    {
        if (_destination is null)
        {
            if (Waiting(dt)) return;
            // Um ponto livre perto de casa.
            for (var tries = 0; tries < 8; tries++)
            {
                var angle = _random.NextSingle() * MathF.Tau;
                var dist = _random.NextSingle() * WanderRadius;
                var x = _homeX + MathF.Cos(angle) * dist;
                var y = _homeY + MathF.Sin(angle) * dist;
                if (Solid is null || !CanisMajor.Pathfinding.IsBlocked(x, y, Solid, Cell)) { SetDestination(x, y); break; }
            }
            return;
        }
        if (MoveAlong(Speed, dt)) { ClearPath(); _waitLeft = Wait; }
    }

    private void Patrol(float dt)
    {
        if (_patrol.Count == 0) { Wander(dt); return; }
        if (_destination is null)
        {
            if (Waiting(dt)) return;
            var (x, y) = _patrol[_patrolIndex];
            SetDestination(x, y);
            return;
        }
        if (!MoveAlong(Speed, dt)) return;
        ClearPath();
        _waitLeft = Wait;
        if (_patrol.Count == 1) return;
        if (PingPong)
        {
            if (_patrolIndex + _patrolStep >= _patrol.Count || _patrolIndex + _patrolStep < 0) _patrolStep = -_patrolStep;
            _patrolIndex += _patrolStep;
        }
        else
        {
            _patrolIndex = (_patrolIndex + 1) % _patrol.Count;
        }
    }

    private void Follow(float dt)
    {
        var target = _forced ?? FindTarget();
        if (target is null) return;
        if (Owner.DistanceTo(target) <= StopDistance * 3) { ClearPath(); return; }
        Seek(target.X, target.Y, Speed, dt);
    }

    private void ChaseStep(float dt)
    {
        if (Seen is not { Alive: true } target) { SetState(Behavior); return; }
        if (Owner.DistanceTo(target) <= StopDistance) { ClearPath(); return; }
        Seek(target.X, target.Y, Run, dt);
    }

    private void FleeStep(float dt)
    {
        if (Seen is not { Alive: true } target) { SetState(Behavior); return; }
        var dist = Owner.DistanceTo(target);
        if (dist >= SafeDistance) { ClearPath(); return; }
        // Um ponto do lado oposto ao alvo. Se ele for parede, tenta virando para os lados.
        var away = MathF.Atan2(Owner.Y - target.Y, Owner.X - target.X);
        foreach (var turn in new[] { 0f, 0.6f, -0.6f, 1.2f, -1.2f, 1.8f, -1.8f })
        {
            var x = Owner.X + MathF.Cos(away + turn) * Cell * 4;
            var y = Owner.Y + MathF.Sin(away + turn) * Cell * 4;
            if (x < 0 || y < 0 || x > Runtime.RoomWidth || y > Runtime.RoomHeight) continue;
            if (Solid is not null && CanisMajor.Pathfinding.IsBlocked(x, y, Solid, Cell)) continue;
            Step(x, y, Run, dt);
            return;
        }
    }

    private float Run => RunSpeed > 0 ? RunSpeed : Speed * 2;

    private bool Waiting(float dt)
    {
        if (_waitLeft <= 0) return false;
        _waitLeft -= dt;
        return true;
    }

    // Vai até um ponto que se mexe (o alvo): refaz o caminho de tempos em tempos.
    private void Seek(float x, float y, float speed, float dt)
    {
        _repathIn -= dt;
        var straight = Solid is null || !Pathfinding || !CanisMajor.Pathfinding.LineBlocked(Owner.X, Owner.Y, x, y, Solid, Cell);
        if (straight)
        {
            ClearPath();
            Step(x, y, speed, dt);
            return;
        }
        if (_path is null || _repathIn <= 0)
        {
            SetDestination(x, y);
            _repathIn = 0.4f;
        }
        MoveAlong(speed, dt);
    }

    private void SetDestination(float x, float y)
    {
        _destination = (x, y);
        _path = Solid is not null && Pathfinding ? CanisMajor.Pathfinding.Find(Owner.X, Owner.Y, x, y, Solid, Cell) : [(x, y)];
        _pathIndex = 0;
        // Sem caminho possível, desiste deste destino.
        if (_path is null) _destination = null;
    }

    private void ClearPath()
    {
        _destination = null;
        _path = null;
        _pathIndex = 0;
    }

    // Anda pelo caminho. Devolve true ao chegar no fim.
    private bool MoveAlong(float speed, float dt)
    {
        if (_path is null || _pathIndex >= _path.Count) return true;
        var (x, y) = _path[_pathIndex];
        if (Step(x, y, speed, dt)) _pathIndex++;
        return _pathIndex >= _path.Count;
    }

    // Um passo em direção ao ponto. Devolve true se chegou nele.
    private bool Step(float x, float y, float speed, float dt)
    {
        var dx = x - Owner.X;
        var dy = y - Owner.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        var move = speed * dt;
        if (dist <= Math.Max(1, move)) {
            Owner.Move(dx, dy, Solid);
            return true;
        }
        var (hitX, hitY) = Owner.Move(dx / dist * move, dy / dist * move, Solid);
        if (FlipSprite && MathF.Abs(dx) > 0.5f) Owner.ScaleX = MathF.Abs(Owner.ScaleX) * MathF.Sign(dx);
        // Encalhou numa parede: refaz o caminho na próxima vez.
        if (hitX && hitY) _repathIn = 0;
        return false;
    }

    private void SetState(string state)
    {
        state = state.ToLowerInvariant();
        if (state == State) return;
        var old = State;
        State = state;
        ClearPath();
        _waitLeft = 0;
        StateChanged?.Invoke(old, state);
    }

    public override void OnDraw()
    {
        if (!Debug) return;
        if (SightRange > 0) Draw.Circle(Owner.X, Owner.Y, SightRange, Color.Hex("#FFFFFF").WithAlpha(0.08f));
        if (_path is { Count: > 0 } path)
        {
            var (px, py) = (Owner.X, Owner.Y);
            for (var i = _pathIndex; i < path.Count; i++)
            {
                Draw.Line(px, py, path[i].X, path[i].Y, Color.Hex("#50DC78"), 1);
                (px, py) = path[i];
            }
        }
        Draw.Text(State, Owner.X, Owner.Y - 20, size: 8, align: Align.Center);
    }

    /// <summary>Lê "x,y; x,y" (aceita também | e quebra de linha entre os pontos).</summary>
    public static List<(float X, float Y)> ParsePoints(string text)
    {
        var points = new List<(float X, float Y)>();
        foreach (var part in text.Split([';', '|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var xy = part.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (xy.Length >= 2
                && float.TryParse(xy[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                && float.TryParse(xy[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                points.Add((x, y));
        }
        return points;
    }
}
