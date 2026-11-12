using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class AITests
{
    private sealed class Coisa : GameObject
    {
        public Coisa() { Sprite = "coisa"; }
    }

    // Sala de 320x160 com uma parede vertical em x de 144 a 160, do topo até y 128 (passagem embaixo).
    private static void WithRoom(Action<List<GameObject>> test)
    {
        // Sprite de 8x8 com a origem no meio, como um personagem pequeno.
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 8, 8, 1, 4, 4, 8);
        Runtime.RoomWidth = 320;
        Runtime.RoomHeight = 160;
        Pathfinding.Invalidate();
        var walls = new List<GameObject>();
        for (var y = 0; y < 128; y += 8)
            walls.Add(new Coisa { Kind = "Parede", X = 148, Y = y + 4 });
        Runtime.Instances = [.. walls];
        try { test(walls); }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.Instances = [];
            Runtime.Layers = [];
            Pathfinding.Invalidate();
        }
    }

    private static void Run(GameObject o, int frames)
    {
        for (var i = 0; i < frames; i++) o.StepComponents(1 / 60f);
    }

    private static Coisa Spawn(string kind, float x, float y)
    {
        var o = new Coisa { Kind = kind, X = x, Y = y };
        Runtime.Instances.Add(o);
        return o;
    }

    [Fact]
    public void PathGoesAroundTheWall()
    {
        WithRoom(_ =>
        {
            var path = Pathfinding.Find(40, 40, 280, 40, "Parede");
            Assert.NotNull(path);
            Assert.Equal((280f, 40f), path![^1]);
            // Para passar, tem que descer abaixo da parede (y > 128) em algum ponto.
            Assert.Contains(path, p => p.Y > 128);
            Assert.True(Pathfinding.LineBlocked(40, 40, 280, 40, "Parede"));
            Assert.False(Pathfinding.LineBlocked(40, 150, 280, 150, "Parede"));
        });
    }

    [Fact]
    public void ClosedWallHasNoPath()
    {
        WithRoom(walls =>
        {
            for (var y = 128; y < 160; y += 8) Spawn("Parede", 148, y + 4);
            Pathfinding.Invalidate();
            Assert.Null(Pathfinding.Find(40, 40, 280, 40, "Parede"));
        });
    }

    [Fact]
    public void PathsWorkWithSolidTiles()
    {
        WithRoom(_ =>
        {
            Runtime.Instances = [];
            Runtime.TilesetOverride = _ => new TilesetData { Sprite = "t", TileWidth = 16, TileHeight = 16, Solid = [1] };
            var muro = new LayerData { Name = "Muro", Kind = "tiles", Tileset = "t", Columns = 20 };
            for (var r = 0; r < 8; r++) muro.SetTile(9, r, 1);
            Runtime.Layers = [muro];
            Pathfinding.Invalidate();
            var path = Pathfinding.Find(40, 40, 280, 40, "Muro");
            Assert.NotNull(path);
            Assert.Contains(path!, p => p.Y > 128);
            Runtime.TilesetOverride = null;
        });
    }

    [Fact]
    public void ChaserGoesAroundWallAndReachesPlayer()
    {
        WithRoom(_ =>
        {
            var jogador = Spawn("Jogador", 280, 40);
            var inimigo = Spawn("Inimigo", 40, 40);
            var ai = inimigo.Add(new AI { Behavior = "idle", OnSight = "chase", SightRange = 1000, LineOfSight = false, Speed = 60, Solid = "Parede", StopDistance = 10 });
            ai.OnCreate();
            Run(inimigo, 60 * 8);
            Assert.Equal("chase", ai.State);
            Assert.True(inimigo.DistanceTo(jogador) <= 14, $"ficou a {inimigo.DistanceTo(jogador)}");
        });
    }

    [Fact]
    public void LineOfSightHidesPlayerBehindWall()
    {
        WithRoom(_ =>
        {
            var jogador = Spawn("Jogador", 200, 40);
            var guarda = Spawn("Guarda", 100, 40);
            var ai = guarda.Add(new AI { Behavior = "idle", SightRange = 200, LineOfSight = true });
            ai.OnCreate();
            Assert.False(ai.CanSee(jogador));
            jogador.X = 120; // do mesmo lado da parede
            Assert.True(ai.CanSee(jogador));
            Run(guarda, 1);
            Assert.Equal("chase", ai.State);
        });
    }

    [Fact]
    public void ForgetsAfterLosingSight()
    {
        WithRoom(_ =>
        {
            var jogador = Spawn("Jogador", 60, 140);
            var guarda = Spawn("Guarda", 40, 140);
            var ai = guarda.Add(new AI { Behavior = "idle", SightRange = 50, ForgetSeconds = 0.5f, Speed = 0, RunSpeed = 0.001f });
            ai.OnCreate();
            Run(guarda, 1);
            Assert.Equal("chase", ai.State);
            jogador.X = 300; // longe demais
            Run(guarda, 20);
            Assert.Equal("chase", ai.State); // ainda lembra
            Run(guarda, 30);
            Assert.Equal("idle", ai.State);
        });
    }

    [Fact]
    public void FleeingMovesAway()
    {
        WithRoom(_ =>
        {
            var jogador = Spawn("Jogador", 60, 140);
            var coelho = Spawn("Coelho", 80, 140);
            var ai = coelho.Add(new AI { Behavior = "idle", OnSight = "flee", SightRange = 100, Speed = 40, SafeDistance = 90 });
            ai.OnCreate();
            Run(coelho, 60);
            Assert.Equal("flee", ai.State);
            Assert.True(coelho.DistanceTo(jogador) > 40, $"ficou a {coelho.DistanceTo(jogador)}");
        });
    }

    [Fact]
    public void PatrolVisitsPointsInOrder()
    {
        WithRoom(_ =>
        {
            var guarda = Spawn("Guarda", 20, 140);
            var ai = guarda.Add(new AI { Behavior = "patrol", PatrolPoints = "100,140; 20,140", Wait = 0, Speed = 80, SightRange = 0 });
            var visited = new List<float>();
            ai.OnCreate();
            for (var i = 0; i < 60 * 6; i++)
            {
                guarda.StepComponents(1 / 60f);
                if (MathF.Abs(guarda.X - 100) < 1 && (visited.Count == 0 || visited[^1] != 100)) visited.Add(100);
                if (MathF.Abs(guarda.X - 20) < 1 && visited.Count > 0 && visited[^1] != 20) visited.Add(20);
            }
            Assert.True(visited.Count >= 3, string.Join(",", visited));
            Assert.Equal(100, visited[0]);
            Assert.Equal(20, visited[1]);
        });
    }

    [Fact]
    public void WanderStaysNearHome()
    {
        WithRoom(_ =>
        {
            var gato = Spawn("Gato", 60, 60);
            var ai = gato.Add(new AI { Behavior = "wander", WanderRadius = 30, Wait = 0, Speed = 60, SightRange = 0 });
            ai.OnCreate();
            var far = 0f;
            for (var i = 0; i < 60 * 10; i++)
            {
                gato.StepComponents(1 / 60f);
                far = MathF.Max(far, MathF.Sqrt((gato.X - 60) * (gato.X - 60) + (gato.Y - 60) * (gato.Y - 60)));
            }
            Assert.True(far > 3, "não andou");
            Assert.True(far <= 34, $"foi longe demais: {far}");
        });
    }

    [Fact]
    public void CodeCommandsOverrideDecisions()
    {
        WithRoom(_ =>
        {
            var npc = Spawn("Npc", 40, 140);
            var ai = npc.Add(new AI { Behavior = "idle", SightRange = 0, Speed = 100 });
            var changes = new List<string>();
            ai.StateChanged += (_, now) => changes.Add(now);
            ai.OnCreate();
            ai.GoTo(120, 140);
            Run(npc, 120);
            Assert.True(MathF.Abs(npc.X - 120) < 1.5f);
            Assert.Equal("idle", ai.State);
            Assert.Equal(["goto", "idle"], changes);
        });
    }

    [Fact]
    public void PatrolPointsParse()
    {
        Assert.Equal([(10f, 20f), (30.5f, 40f)], AI.ParsePoints("10,20; 30.5, 40"));
        Assert.Equal([(1f, 2f), (3f, 4f)], AI.ParsePoints("1 2 | 3 4"));
        Assert.Empty(AI.ParsePoints("nada"));
    }
}
