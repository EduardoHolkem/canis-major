using System.Text.Json.Nodes;
using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class ParentTests
{
    // Um pai com script e um filho em C#.
    public class Inimigo : GameObject
    {
        public int Passos;
        public Inimigo() { Sprite = "inimigo"; }
        public override void OnStep(float dt) => Passos++;
    }

    public sealed class Morcego : Inimigo
    {
        public bool Voou;
        public override void OnStep(float dt)
        {
            base.OnStep(dt); // o "event_inherited" do GameMaker
            Voou = true;
        }
    }

    public abstract class Criatura : GameObject;

    public sealed class Sapo : Criatura;

    private static readonly Dictionary<string, ObjectDef> Fichas = new(StringComparer.OrdinalIgnoreCase);

    private static void WithWorld(Action test)
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        Runtime.DefOverride = kind => Fichas.GetValueOrDefault(kind);
        foreach (var t in new[] { typeof(Inimigo), typeof(Morcego), typeof(Criatura), typeof(Sapo) })
        {
            Runtime.ObjectClasses[t.Name] = t;
            if (!t.IsAbstract) Runtime.Types[t.Name] = t;
        }
        try { test(); }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.DefOverride = null;
            Runtime.Instances = [];
            Runtime.Types.Clear();
            Runtime.ObjectClasses.Clear();
            Fichas.Clear();
        }
    }

    [Fact]
    public void ChildInheritsTheParentsFicha()
    {
        Fichas["Fruta"] = new ObjectDef
        {
            Sprite = "fruta",
            Alpha = 0.5f,
            Components = [new JsonObject { ["type"] = "Lifetime", ["seconds"] = 2 }, new JsonObject { ["type"] = "Motion", ["gravity"] = 100 }],
        };
        Fichas["Maca"] = new ObjectDef
        {
            Parent = "Fruta",
            Sprite = "maca",
            Components = [new JsonObject { ["type"] = "Lifetime", ["seconds"] = 5 }],
        };
        WithWorld(() =>
        {
            var maca = Runtime.Spawn("Maca", 0, 0);
            Assert.Equal("Fruta", maca.Parent);
            Assert.Equal("maca", maca.Sprite); // o filho troca o que quiser
            Assert.Equal(0.5f, maca.Alpha); // e herda o resto
            Assert.Equal(5, maca.Get<Lifetime>()!.Seconds); // o mesmo componente: vale o do filho
            Assert.Equal(100, maca.Get<Motion>()!.Gravity); // os outros se somam
            Assert.True(maca.Is("Fruta"));
            Assert.True(maca.Is("maca"));
            Assert.False(maca.Is("Inimigo"));
        });
    }

    [Fact]
    public void ParentNameFindsTheChildren()
    {
        Fichas["Fruta"] = new ObjectDef { Sprite = "fruta" };
        Fichas["Maca"] = new ObjectDef { Parent = "Fruta" };
        Fichas["Pera"] = new ObjectDef { Parent = "Fruta" };
        WithWorld(() =>
        {
            var heroi = Runtime.Spawn(typeof(Inimigo), 0, 0);
            var maca = Runtime.Spawn("Maca", 8, 8);
            Runtime.Spawn("Pera", 300, 0);
            Assert.Equal(2, Game.Count("Fruta"));
            Assert.Single(Game.Instances("Pera"));
            Assert.Same(maca, heroi.Collide("Fruta"));
            // Move para nos filhos de um sólido.
            var (hitX, _) = heroi.Move(40, 0, solid: "Fruta");
            Assert.True(hitX);
        });
    }

    [Fact]
    public void FichaChildOfAScriptGetsTheParentsEvents()
    {
        Fichas["Inimigo"] = new ObjectDef { Layer = 4 };
        Fichas["Vampiro"] = new ObjectDef { Parent = "Inimigo", Sprite = "vampiro" };
        WithWorld(() =>
        {
            var vampiro = Runtime.Spawn("Vampiro", 0, 0);
            var inimigo = Assert.IsType<Inimigo>(vampiro); // usa o script do pai
            Assert.Equal("Vampiro", vampiro.Kind);
            Assert.Equal("vampiro", vampiro.Sprite);
            Assert.Equal(4, vampiro.Layer);
            Runtime.StepFrame(1 / 60f);
            Assert.Equal(1, inimigo.Passos);
            Assert.Single(Game.Instances<Inimigo>());
        });
    }

    [Fact]
    public void CSharpChildIsTheParentToo()
    {
        Fichas["Inimigo"] = new ObjectDef { Layer = 9 };
        WithWorld(() =>
        {
            var morcego = (Morcego)Runtime.Spawn("Morcego", 0, 0);
            Assert.Equal(["Morcego", "Inimigo"], morcego.Lineage);
            Assert.Equal(9, morcego.Layer); // a ficha do pai vale para o filho
            Assert.Equal("inimigo", morcego.Sprite); // o construtor do pai também
            Runtime.StepFrame(1 / 60f);
            Assert.True(morcego.Voou);
            Assert.Equal(1, morcego.Passos);
            Assert.Single(Game.Instances("Inimigo"));
        });
    }

    [Fact]
    public void AbstractParentCannotBeCreated()
    {
        WithWorld(() =>
        {
            var e = Assert.Throws<GameException>(() => Runtime.Spawn("Criatura", 0, 0));
            Assert.Contains("abstract", e.Message);
            var sapo = Runtime.Spawn("Sapo", 0, 0);
            Assert.True(sapo.Is("Criatura"));
        });
    }

    [Fact]
    public void CircleAndMissingParentAreExplained()
    {
        Fichas["A"] = new ObjectDef { Parent = "B" };
        Fichas["B"] = new ObjectDef { Parent = "A" };
        Fichas["Orfao"] = new ObjectDef { Parent = "Ninguem" };
        WithWorld(() =>
        {
            Assert.Contains("círculo", Assert.Throws<GameException>(() => Runtime.Spawn("A", 0, 0)).Message);
            var e = Assert.Throws<GameException>(() => Runtime.Spawn("Orfao", 0, 0));
            Assert.Contains("Ninguem", e.Message);
            Assert.Contains("objects/Orfao.json", e.Message);
        });
    }
}

[Collection("Runtime")]
public sealed class RoomScriptTests
{
    public static readonly List<string> Log = [];

    public sealed class Tijolo : GameObject
    {
        public Tijolo() { Sprite = "tijolo"; }
        public override void OnCreate() => Log.Add("tijolo criado");
        public override void OnDestroy() => Log.Add("tijolo destruído");
    }

    public sealed class Fase1 : RoomScript
    {
        public override void OnCreate() => Log.Add($"sala {Room}: {Game.Count("Tijolo")} tijolos");
        public override void OnStep(float dt) => Log.Add("sala step");
        public override void OnEnd() => Log.Add("sala end");
    }

    [Room("chefe final")]
    public sealed class Chefao : RoomScript
    {
        public override void OnCreate() => Log.Add("chefão");
    }

    [Fact]
    public void RoomKeyIgnoresCaseAndSigns()
    {
        Assert.Equal("fase1", Runtime.RoomKey("Fase-1"));
        Assert.Equal(Runtime.RoomKey("fase_1"), Runtime.RoomKey("FASE 1"));
    }

    [Fact]
    public void RoomCodeRunsAfterTheInstancesAndEndsBeforeThem()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "rooms"));
        File.WriteAllText(Path.Combine(tmp.Path, "projeto.json"), "{}");
        const string room = """{ "instances": [ { "object": "Tijolo", "x": 0, "y": 0 }, { "object": "Tijolo", "x": 32, "y": 0 } ] }""";
        File.WriteAllText(Path.Combine(tmp.Path, "rooms", "fase-1.json"), room);
        File.WriteAllText(Path.Combine(tmp.Path, "rooms", "chefe final.json"), "{}");
        File.WriteAllText(Path.Combine(tmp.Path, "rooms", "vazia.json"), "{}");

        var oldProject = Runtime.Project;
        Runtime.Project = GameProject.Load(tmp.Path);
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        Runtime.RegisterTypes([typeof(RoomScriptTests).Assembly]);
        Log.Clear();
        try
        {
            Runtime.LoadRoom("fase-1");
            Assert.Equal(["tijolo criado", "tijolo criado", "sala fase-1: 2 tijolos"], Log);
            Runtime.StepFrame(1 / 60f);
            Assert.Equal("sala step", Log[^1]);

            Log.Clear();
            Runtime.LoadRoom("chefe final");
            Assert.Equal(["sala end", "tijolo destruído", "tijolo destruído", "chefão"], Log);

            Log.Clear();
            Runtime.LoadRoom("vazia"); // sala sem código
            Assert.Null(Runtime.RoomCode);
            Runtime.StepFrame(1 / 60f);
            Assert.Empty(Log);
        }
        finally
        {
            Runtime.Project = oldProject;
            Runtime.SpriteOverride = null;
            Runtime.RoomCode = null;
            Runtime.Instances = [];
            Runtime.Types.Clear();
            Runtime.ObjectClasses.Clear();
            Runtime.RoomScripts.Clear();
        }
    }
}
