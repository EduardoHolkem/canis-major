using System.Text.Json.Nodes;
using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

// Variáveis definidas no Editor de Objetos e objetos persistentes.
[Collection("Runtime")]
public sealed class VariableTests
{
    public class Heroi : GameObject
    {
        public float Vida = 1;
        public string Nome { get; set; } = "";
    }

    public sealed class Ninja : Heroi;

    public sealed class Bau : GameObject
    {
        public static int Criados;
        public override void OnCreate() => Criados++;
    }

    private static readonly Dictionary<string, ObjectDef> Fichas = new(StringComparer.OrdinalIgnoreCase);

    private static void WithWorld(Action test)
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        Runtime.DefOverride = kind => Fichas.GetValueOrDefault(kind);
        foreach (var t in new[] { typeof(Heroi), typeof(Ninja), typeof(Bau) })
        {
            Runtime.ObjectClasses[t.Name] = t;
            Runtime.Types[t.Name] = t;
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

    private static VariableDef V(string name, string type, JsonNode? value) => new() { Name = name, Type = type, Value = value };

    [Fact]
    public void Variaveis_vao_para_o_campo_do_script_ou_para_Vars()
    {
        Fichas["Heroi"] = new ObjectDef { Variables = [V("vida", "number", 3), V("nome", "text", "Ana"), V("moedas", "integer", 7)] };
        WithWorld(() =>
        {
            var heroi = (Heroi)Runtime.Spawn("Heroi", 0, 0);
            Assert.Equal(3, heroi.Vida); // campo
            Assert.Equal("Ana", heroi.Nome); // propriedade
            Assert.Equal(7, heroi.Var<int>("moedas")); // sem campo: Vars
            Assert.Equal(0, heroi.Var("nada", 0));
        });
    }

    [Fact]
    public void Filho_herda_as_variaveis_e_pode_trocar_o_valor()
    {
        Fichas["Heroi"] = new ObjectDef { Variables = [V("vida", "number", 3), V("forca", "integer", 2)] };
        Fichas["Ninja"] = new ObjectDef { Variables = [V("vida", "number", 5)] };
        WithWorld(() =>
        {
            var ninja = (Ninja)Runtime.Spawn("Ninja", 0, 0);
            Assert.Equal(5, ninja.Vida);
            Assert.Equal(2, ninja.Var<int>("forca"));
        });
    }

    [Fact]
    public void Valor_que_nao_serve_vira_o_padrao_do_tipo()
    {
        Assert.Equal(0f, VariableDef.Convert("abc", "number"));
        Assert.Equal(12, VariableDef.Convert(12, "integer"));
        Assert.Equal(true, VariableDef.Convert(true, "bool"));
        Assert.Equal("3", VariableDef.Convert(3, "text"));
    }

    [Fact]
    public void Instancia_da_sala_troca_o_valor_e_persistente_passa_de_sala()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "rooms"));
        Directory.CreateDirectory(Path.Combine(tmp.Path, "objects"));
        File.WriteAllText(Path.Combine(tmp.Path, "projeto.json"), "{}");
        File.WriteAllText(Path.Combine(tmp.Path, "objects", "Heroi.json"), """{ "variables": [ { "name": "vida", "type": "number", "value": 3 }, { "name": "moedas", "type": "integer", "value": 1 } ] }""");
        File.WriteAllText(Path.Combine(tmp.Path, "objects", "Bau.json"), """{ "persistent": true }""");
        File.WriteAllText(Path.Combine(tmp.Path, "rooms", "a.json"), """{ "instances": [ { "object": "Heroi", "x": 0, "y": 0, "vida": 9, "moedas": 4, "extra": "oi" }, { "object": "Bau", "x": 0, "y": 0 } ] }""");
        File.WriteAllText(Path.Combine(tmp.Path, "rooms", "b.json"), "{}");

        var oldProject = Runtime.Project;
        Runtime.Project = GameProject.Load(tmp.Path);
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        foreach (var t in new[] { typeof(Heroi), typeof(Bau) })
        {
            Runtime.ObjectClasses[t.Name] = t;
            Runtime.Types[t.Name] = t;
        }
        Bau.Criados = 0;
        try
        {
            Runtime.LoadRoom("a");
            var heroi = Runtime.Instances.OfType<Heroi>().Single();
            Assert.Equal(9, heroi.Vida); // a instância vale mais que a ficha
            Assert.Equal(4, heroi.Var<int>("moedas")); // no tipo definido (integer)
            Assert.Equal("oi", heroi.Var<string>("extra")); // propriedade solta também chega
            var bau = Runtime.Instances.OfType<Bau>().Single();

            Runtime.LoadRoom("b");
            Assert.Same(bau, Assert.Single(Runtime.Instances)); // o baú persistente veio junto
            Assert.True(bau.Alive);
            Assert.Equal(1, Bau.Criados); // sem OnCreate de novo
            Assert.False(heroi.Alive);
        }
        finally
        {
            Runtime.Project = oldProject;
            Runtime.SpriteOverride = null;
            Runtime.RoomCode = null;
            Runtime.Instances = [];
            Runtime.Types.Clear();
            Runtime.ObjectClasses.Clear();
        }
    }
}
