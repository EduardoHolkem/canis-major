using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

public sealed class BlockConverterTests
{
    private const string Jogador = """
        using CanisMajor;

        namespace MeuJogo;

        // O herói.
        public class Jogador : GameObject
        {
            private int vida = 3;

            public Jogador()
            {
                Sprite = "jogador";
            }

            public override void OnStep(float dt)
            {
                // anda com as setas
                var dx = Keys.Axis("left", "right");
                Move(dx * 110 * dt, 0, solid: "Parede");
                if (Keys.Pressed("space"))
                {
                    Game.PlaySound("pulo");
                }
                else if (vida <= 0)
                {
                    Game.Restart();
                }
                else
                {
                    vida += 0;
                }
                for (var i = 0; i < 3; i++)
                {
                    Draw.Text("oi", X, Y);
                }
                foreach (var e in Game.Instances<Estrela>())
                {
                    e.Destroy();
                }
                while (vida > 10)
                {
                    vida--;
                }
                if (vida < 0) return;
                switch (vida)
                {
                    case 1:
                        break;
                }
            }

            private void Ajuda() { }
        }
        """;

    [Fact]
    public void ParsesEveryKindOfBlock()
    {
        var script = BlockConverter.Parse(Jogador)!;
        Assert.Equal("Jogador", script.ClassName);
        var step = script.Find("OnStep")!.Body;
        Assert.IsType<CommentBlock>(step[0]);
        Assert.Equal("anda com as setas", ((CommentBlock)step[0]).Text);
        var v = Assert.IsType<VarBlock>(step[1]);
        Assert.Equal(("var", "dx", "Keys.Axis(\"left\", \"right\")"), (v.Type, v.Name, v.Value));
        var move = Assert.IsType<CallBlock>(step[2]);
        Assert.Equal("Move", move.Method);
        Assert.Equal(["dx * 110 * dt", "0", "solid: \"Parede\""], move.Args);
        var ifb = Assert.IsType<IfBlock>(step[3]);
        Assert.Equal("Keys.Pressed(\"space\")", ifb.Condition);
        Assert.IsType<IfBlock>(Assert.Single(ifb.Else!)); // else if
        var rep = Assert.IsType<RepeatBlock>(step[4]);
        Assert.Equal(("i", "3"), (rep.Var, rep.Count));
        Assert.IsType<ForEachBlock>(step[5]);
        var loop = Assert.IsType<WhileBlock>(step[6]);
        var dec = Assert.IsType<SetBlock>(Assert.Single(loop.Body));
        Assert.Equal(("vida", "-=", "1"), (dec.Target, dec.Op, dec.Value));
        var guard = Assert.IsType<IfBlock>(step[7]);
        Assert.IsType<ReturnBlock>(Assert.Single(guard.Then));
        Assert.Contains("switch (vida)", Assert.IsType<CodeBlock>(step[8]).Code);
    }

    [Fact]
    public void RoundTripIsStableAndKeepsEverythingOutsideEvents()
    {
        var script = BlockConverter.Parse(Jogador)!;
        var once = BlockConverter.Generate(Jogador, script);
        Assert.Empty(BlockConverter.SyntaxErrors(once));
        // Fora dos eventos, nada mudou.
        Assert.Contains("private int vida = 3;", once);
        Assert.Contains("Sprite = \"jogador\";", once);
        Assert.Contains("private void Ajuda() { }", once);
        Assert.Contains("// O herói.", once);
        Assert.Contains("else if (vida <= 0)", once);
        // Ida e volta de novo dá exatamente o mesmo texto: os dois modos são equivalentes.
        var twice = BlockConverter.Generate(once, BlockConverter.Parse(once)!);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void EditingABlockChangesOnlyThatLine()
    {
        var script = BlockConverter.Parse(Jogador)!;
        var move = (CallBlock)script.Find("OnStep")!.Body[2];
        move.Args[0] = "dx * 200 * dt";
        var code = BlockConverter.Generate(Jogador, script);
        Assert.Contains("Move(dx * 200 * dt, 0, solid: \"Parede\");", code);
        Assert.DoesNotContain("110", code);
    }

    [Fact]
    public void NewEventIsAddedInsideTheClass()
    {
        var script = BlockConverter.Parse(Jogador)!;
        script.Events.Add(new EventScript { Name = "OnCreate", Body = [new SetBlock { Target = "Alpha", Value = "0.5f" }] });
        var code = BlockConverter.Generate(Jogador, script);
        Assert.Empty(BlockConverter.SyntaxErrors(code));
        Assert.Contains("public override void OnCreate()", code);
        Assert.Contains("Alpha = 0.5f;", code);
        var again = BlockConverter.Parse(code)!;
        Assert.NotNull(again.Find("OnCreate"));
        Assert.Equal(script.Find("OnStep")!.Body.Count, again.Find("OnStep")!.Body.Count);
    }

    [Fact]
    public void ExpressionBodiedEventBecomesBlockBody()
    {
        const string code = """
            public class Bola : GameObject
            {
                public override void OnStep(float dt) => X += 10 * dt;
            }
            """;
        var script = BlockConverter.Parse(code)!;
        var set = Assert.IsType<SetBlock>(Assert.Single(script.Find("OnStep")!.Body));
        Assert.Equal(("X", "+=", "10 * dt"), (set.Target, set.Op, set.Value));
        var back = BlockConverter.Generate(code, script);
        Assert.Empty(BlockConverter.SyntaxErrors(back));
        Assert.Contains("X += 10 * dt;", back);
        Assert.DoesNotContain("=>", back);
    }

    [Fact]
    public void BlocksWriteValidCSharp()
    {
        var blocks = new List<Block>
        {
            new IfBlock { Condition = "Keys.Held(\"left\")", Then = [new SetBlock { Target = "X", Op = "-=", Value = "2" }], Else = [new CallBlock { Method = "Destroy" }] },
            new RepeatBlock { Count = "5", Body = [new CallBlock { Method = "Game.Spawn<Estrela>", Args = ["X", "Y"] }] },
            new CommentBlock { Text = "fim" },
        };
        var text = BlockConverter.StatementsText(blocks);
        Assert.Contains("if (Keys.Held(\"left\"))", text);
        Assert.Contains("X -= 2;", text);
        Assert.Contains("Destroy();", text);
        Assert.Contains("for (var i = 0; i < 5; i++)", text);
        Assert.Contains("Game.Spawn<Estrela>(X, Y);", text);
        Assert.Empty(BlockConverter.SyntaxErrors("class A { void M() {\n" + text + "} }"));
    }

    [Fact]
    public void NewGameMakerEventsWriteTheRightSignature()
    {
        var script = BlockConverter.Parse(Jogador)!;
        script.Events.Add(new EventScript { Name = "OnCollision", Body = [new IfBlock { Condition = "other is Estrela", Then = [new CallBlock { Method = "other.Destroy" }] }] });
        script.Events.Add(new EventScript { Name = "OnAlarm", Body = [new CallBlock { Method = "SetAlarm", Args = ["0", "1f"] }] });
        var code = BlockConverter.Generate(Jogador, script);
        Assert.Empty(BlockConverter.SyntaxErrors(code));
        Assert.Contains("public override void OnCollision(GameObject other)", code);
        Assert.Contains("public override void OnAlarm(int index)", code);
        var again = BlockConverter.Parse(code)!;
        Assert.IsType<IfBlock>(Assert.Single(again.Find("OnCollision")!.Body));
    }

    [Fact]
    public void NoClassMeansNoBlocks()
    {
        Assert.Null(BlockConverter.Parse("// vazio"));
    }
}
