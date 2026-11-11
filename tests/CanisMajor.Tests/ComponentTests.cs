using System.Text.Json.Nodes;
using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class ComponentTests
{
    private sealed class Bloco : GameObject
    {
        public Bloco() { Sprite = "bloco"; Layer = 3; }
    }

    private sealed class Contador : Component
    {
        public int Passos { get; private set; }
        public int Inicio { get; set; }
        public override void OnStep(float dt) => Passos++;
    }

    private static void WithFakes(Func<string, ObjectDef?> defs, Action test)
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        Runtime.DefOverride = defs;
        try { test(); }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.DefOverride = null;
            Runtime.Instances = [];
            Runtime.Types.Clear();
        }
    }

    [Fact]
    public void ComponentFromJsonGetsProperties()
    {
        var motion = (Motion)ComponentTypes.Create(new JsonObject { ["type"] = "Motion", ["gravity"] = 400, ["solid"] = "Parede" }, "teste");
        Assert.Equal(400, motion.Gravity);
        Assert.Equal("Parede", motion.Solid);
    }

    [Fact]
    public void UnknownComponentExplainsWhatExists()
    {
        var e = Assert.Throws<GameException>(() => ComponentTypes.Create(new JsonObject { ["type"] = "Voar" }, "objects/X.json"));
        Assert.Contains("Voar", e.Message);
        Assert.Contains("Motion", e.Message);
    }

    [Fact]
    public void DefOverridesConstructorAndAddsComponents()
    {
        var def = new ObjectDef
        {
            Sprite = "outro",
            Layer = 7,
            YSort = true,
            Components = [new JsonObject { ["type"] = "Lifetime", ["seconds"] = 2 }],
        };
        WithFakes(kind => kind == "Bloco" ? def : null, () =>
        {
            var obj = Runtime.Spawn(typeof(Bloco), 0, 0);
            Assert.Equal("outro", obj.Sprite);
            Assert.Equal(7, obj.Layer);
            Assert.True(obj.YSort);
            Assert.Equal(2, obj.Get<Lifetime>()!.Seconds);
            Assert.Same(obj, obj.Get<Lifetime>()!.Owner);
        });
    }

    [Fact]
    public void ObjectWithOnlyDefExists()
    {
        WithFakes(kind => kind == "Moeda" ? new ObjectDef { Sprite = "moeda" } : null, () =>
        {
            var obj = Runtime.Spawn("Moeda", 5, 6);
            Assert.IsType<DefinedObject>(obj);
            Assert.Equal("Moeda", obj.Kind);
            Assert.Equal("moeda", obj.Sprite);
            Assert.Single(Runtime.InstancesOf("moeda"));
        });
    }

    [Fact]
    public void MissingObjectMentionsBothWays()
    {
        WithFakes(_ => null, () =>
        {
            var e = Assert.Throws<GameException>(() => Runtime.Spawn("Fantasma", 0, 0));
            Assert.Contains("objects/Fantasma.json", e.Message);
        });
    }

    [Fact]
    public void ComponentsStepAndStopWhenDestroyed()
    {
        WithFakes(_ => null, () =>
        {
            var obj = new Bloco { Kind = "Bloco" };
            var contador = obj.Add<Contador>();
            obj.StepComponents(0.016f);
            obj.StepComponents(0.016f);
            Assert.Equal(2, contador.Passos);
            contador.Enabled = false;
            obj.StepComponents(0.016f);
            Assert.Equal(2, contador.Passos);
        });
    }

    [Fact]
    public void LifetimeDestroysOwner()
    {
        WithFakes(_ => null, () =>
        {
            var obj = new Bloco { Kind = "Bloco" };
            Runtime.Instances = [obj];
            obj.Add(new Lifetime { Seconds = 0.5f });
            obj.StepComponents(0.3f);
            Assert.True(obj.Alive);
            obj.StepComponents(0.3f);
            Assert.False(obj.Alive);
        });
    }

    [Fact]
    public void MotionFallsAndStopsOnFloor()
    {
        WithFakes(_ => null, () =>
        {
            var caixa = new Bloco { Kind = "Caixa", X = 0, Y = 0 };
            var chao = new Bloco { Kind = "Chao", X = 0, Y = 40 };
            Runtime.Instances = [caixa, chao];
            var motion = caixa.Add(new Motion { Gravity = 1000, Solid = "Chao" });
            for (var i = 0; i < 60; i++) caixa.StepComponents(1 / 60f);
            Assert.InRange(caixa.Y, 23f, 24f); // parou encostado no chão (24 + 16 = 40), sem atravessar
            Assert.Equal(0, motion.SpeedY);
        });
    }

    [Fact]
    public void ColliderReplacesSpriteBox()
    {
        WithFakes(_ => null, () =>
        {
            var obj = new Bloco { Kind = "Bloco", X = 100, Y = 50 };
            obj.Add(new Collider { OffsetX = 4, OffsetY = 10, Width = 8, Height = 6 });
            Assert.Equal((104f, 60f, 112f, 66f), obj.BBox());
        });
    }

    [Fact]
    public void GameComponentsAreRegistered()
    {
        ComponentTypes.Register(typeof(Contador));
        var c = (Contador)ComponentTypes.Create(new JsonObject { ["type"] = "Contador", ["inicio"] = 3 }, "teste");
        Assert.Equal(3, c.Inicio);
        ComponentTypes.All.Remove("Contador");
    }
}
