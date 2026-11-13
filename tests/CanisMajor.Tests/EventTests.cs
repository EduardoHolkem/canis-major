using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class EventTests
{
    // Um objeto que anota cada evento que recebe, na ordem.
    private sealed class Gravador : GameObject
    {
        public readonly List<string> Log = [];
        public Gravador() { Sprite = "coisa"; }
        public override void OnBeginStep(float dt) => Log.Add("begin");
        public override void OnStep(float dt) => Log.Add("step");
        public override void OnEndStep(float dt) => Log.Add("end");
        public override void OnAlarm(int index) => Log.Add($"alarm{index}");
        public override void OnCollision(GameObject other) => Log.Add($"collision:{other.Kind}");
        public override void OnKeyPressed(string key) => Log.Add($"press:{key}");
        public override void OnKeyReleased(string key) => Log.Add($"release:{key}");
        public override void OnClick(int button) => Log.Add($"click{button}");
        public override void OnMouseEnter() => Log.Add("enter");
        public override void OnMouseLeave() => Log.Add("leave");
        public override void OnDrawGui() => Draw.Text("oi", 4, 4);
    }

    private sealed class Pedra : GameObject
    {
        public Pedra() { Sprite = "coisa"; }
    }

    private static void WithWorld(Action test)
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        Array.Clear(Keys.State);
        Array.Clear(Mouse.State);
        Mouse.X = Mouse.Y = -100;
        try { test(); }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.Instances = [];
            Array.Clear(Keys.State);
            Array.Clear(Mouse.State);
        }
    }

    [Fact]
    public void StepsRunInGameMakerOrder()
    {
        WithWorld(() =>
        {
            var a = new Gravador { Kind = "A" };
            Runtime.Instances = [a];
            Runtime.StepFrame(1 / 60f);
            Assert.Equal(["begin", "step", "end"], a.Log);
        });
    }

    [Fact]
    public void AlarmFiresOnceAfterTheTime()
    {
        WithWorld(() =>
        {
            var a = new Gravador { Kind = "A" };
            Runtime.Instances = [a];
            a.SetAlarm(3, 0.5f);
            for (var i = 0; i < 20; i++) Runtime.StepFrame(1 / 60f);
            Assert.DoesNotContain("alarm3", a.Log);
            Assert.True(a.GetAlarm(3) > 0);
            for (var i = 0; i < 20; i++) Runtime.StepFrame(1 / 60f);
            Assert.Single(a.Log, l => l == "alarm3");
            Assert.Equal(-1, a.GetAlarm(3));
            // O alarme roda antes do Step daquele quadro.
            var at = a.Log.IndexOf("alarm3");
            Assert.Equal("begin", a.Log[at - 1]);
            Assert.Equal("step", a.Log[at + 1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.SetAlarm(12, 1));
        });
    }

    [Fact]
    public void CollisionReportsTheOtherObject()
    {
        WithWorld(() =>
        {
            var a = new Gravador { Kind = "A", X = 0, Y = 0 };
            var pedra = new Pedra { Kind = "Pedra", X = 8, Y = 8 };
            var longe = new Pedra { Kind = "Longe", X = 200, Y = 0 };
            Runtime.Instances = [a, pedra, longe];
            Runtime.StepFrame(1 / 60f);
            Assert.Contains("collision:Pedra", a.Log);
            Assert.DoesNotContain("collision:Longe", a.Log);
            // Depois do Step e antes do End Step.
            Assert.True(a.Log.IndexOf("collision:Pedra") > a.Log.IndexOf("step"));
            Assert.True(a.Log.IndexOf("collision:Pedra") < a.Log.IndexOf("end"));
        });
    }

    [Fact]
    public void KeysArriveByName()
    {
        WithWorld(() =>
        {
            var a = new Gravador { Kind = "A" };
            Runtime.Instances = [a];
            Keys.State[Array.IndexOf(Keys.Names, "space")] = 1 | 2; // apertou agora
            Keys.State[Array.IndexOf(Keys.Names, "left")] = 4; // soltou agora
            Runtime.StepFrame(1 / 60f);
            Assert.Contains("press:space", a.Log);
            Assert.Contains("release:left", a.Log);
            Assert.DoesNotContain("press:left", a.Log);
        });
    }

    [Fact]
    public void MouseEntersClicksAndLeaves()
    {
        WithWorld(() =>
        {
            var a = new Gravador { Kind = "A", X = 100, Y = 100 };
            Runtime.Instances = [a];
            Mouse.X = 108;
            Mouse.Y = 108;
            Mouse.State[0] = 1 | 2;
            Runtime.StepFrame(1 / 60f);
            Assert.Contains("enter", a.Log);
            Assert.Contains("click1", a.Log);
            Mouse.State[0] = 1;
            Runtime.StepFrame(1 / 60f);
            Assert.Single(a.Log, l => l == "enter"); // não entra de novo
            Mouse.X = 0;
            Runtime.StepFrame(1 / 60f);
            Assert.Contains("leave", a.Log);
        });
    }

    [Fact]
    public void KeyNamesMatchTheCore()
    {
        // A tabela do C# precisa bater com a do núcleo em Rust, índice por índice.
        for (var i = 0; i < Keys.Names.Length; i++)
        {
            var name = Keys.Names[i];
            var index = Keys.CoreIndexOf(name);
            Assert.True(index == i, $"{name}: C# {i}, núcleo {index}");
        }
    }
}
