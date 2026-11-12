namespace CanisEditor.Game;

// Os blocos do modo C# Blocos. Cada um corresponde a um comando de C#, e os valores são texto de
// C# de verdade (uma expressão como speed * dt), para os dois modos dizerem exatamente a mesma coisa.
public abstract class Block
{
    public abstract Block Clone();
}

// se (condição) { então } senão { senão }
public sealed class IfBlock : Block
{
    public string Condition { get; set; } = "true";
    public List<Block> Then { get; set; } = new();
    // Null: sem "senão".
    public List<Block>? Else { get; set; }

    public override Block Clone() => new IfBlock { Condition = Condition, Then = Then.Select(b => b.Clone()).ToList(), Else = Else?.Select(b => b.Clone()).ToList() };
}

// enquanto (condição) { ... }
public sealed class WhileBlock : Block
{
    public string Condition { get; set; } = "true";
    public List<Block> Body { get; set; } = new();

    public override Block Clone() => new WhileBlock { Condition = Condition, Body = Body.Select(b => b.Clone()).ToList() };
}

// repetir N vezes, com o contador em Var: for (var i = 0; i < N; i++)
public sealed class RepeatBlock : Block
{
    public string Var { get; set; } = "i";
    public string Count { get; set; } = "10";
    public List<Block> Body { get; set; } = new();

    public override Block Clone() => new RepeatBlock { Var = Var, Count = Count, Body = Body.Select(b => b.Clone()).ToList() };
}

// para cada item em lista: foreach (var item in lista)
public sealed class ForEachBlock : Block
{
    public string Var { get; set; } = "item";
    public string Collection { get; set; } = "Game.Instances()";
    public List<Block> Body { get; set; } = new();

    public override Block Clone() => new ForEachBlock { Var = Var, Collection = Collection, Body = Body.Select(b => b.Clone()).ToList() };
}

// mudar alvo para valor: X = 10, X += speed * dt
public sealed class SetBlock : Block
{
    public string Target { get; set; } = "X";
    public string Op { get; set; } = "=";
    public string Value { get; set; } = "0";

    public static readonly string[] Ops = ["=", "+=", "-=", "*=", "/="];

    public override Block Clone() => new SetBlock { Target = Target, Op = Op, Value = Value };
}

// nova variável: var vida = 3
public sealed class VarBlock : Block
{
    public string Type { get; set; } = "var";
    public string Name { get; set; } = "valor";
    public string Value { get; set; } = "0";

    public override Block Clone() => new VarBlock { Type = Type, Name = Name, Value = Value };
}

// Uma chamada: Move(dx, dy, solid: "Parede"), Game.Goto("fim"), Destroy()
public sealed class CallBlock : Block
{
    public string Method { get; set; } = "Destroy";
    public List<string> Args { get; set; } = new();

    public override Block Clone() => new CallBlock { Method = Method, Args = Args.ToList() };
}

// return (sair do evento)
public sealed class ReturnBlock : Block
{
    public override Block Clone() => new ReturnBlock();
}

// // comentário
public sealed class CommentBlock : Block
{
    public string Text { get; set; } = "";

    public override Block Clone() => new CommentBlock { Text = Text };
}

// Qualquer outro comando de C#, guardado como texto, para nada se perder na conversão.
public sealed class CodeBlock : Block
{
    public string Code { get; set; } = "";

    public override Block Clone() => new CodeBlock { Code = Code };
}

// Um evento do objeto (OnCreate, OnStep, OnDraw, OnDestroy) e os blocos dentro dele.
public sealed class EventScript
{
    public string Name { get; set; } = "OnStep";
    public List<Block> Body { get; set; } = new();
}

// O script inteiro. O texto fora dos eventos (usings, campos, outros métodos) fica como está.
public sealed class ObjectScript
{
    public string ClassName { get; set; } = "";
    public List<EventScript> Events { get; set; } = new();

    // Os eventos que o modo blocos oferece, com o nome em português e a assinatura em C#.
    public static readonly (string Name, string Title, string Signature)[] KnownEvents =
    [
        ("OnCreate", "Ao criar", "public override void OnCreate()"),
        ("OnStep", "A cada quadro", "public override void OnStep(float dt)"),
        ("OnDraw", "Ao desenhar", "public override void OnDraw()"),
        ("OnDestroy", "Ao destruir", "public override void OnDestroy()"),
    ];

    public EventScript? Find(string name) => Events.FirstOrDefault(e => e.Name == name);
}
