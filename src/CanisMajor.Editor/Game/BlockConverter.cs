using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CanisEditor.Game;

// Converte o script C# em blocos e os blocos de volta em C#. O texto fora dos eventos (usings,
// campos, construtor, outros métodos) nunca muda: só o corpo de cada evento é refeito.
public static class BlockConverter
{
    private const string Indent = "    ";

    // Lê o script. Null quando não há uma classe de objeto para mostrar em blocos.
    public static ObjectScript? Parse(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls is null)
            return null;
        var script = new ObjectScript { ClassName = cls.Identifier.Text, IsRoom = IsRoomScript(cls) };
        foreach (var (name, _, _) in script.Known)
        {
            if (FindEvent(cls, name) is not { } method)
                continue;
            var body = method.Body is { } block ? Statements(block.Statements)
                : method.ExpressionBody is { } arrow ? [FromStatement(SyntaxFactory.ExpressionStatement(arrow.Expression))]
                : [];
            script.Events.Add(new EventScript { Name = name, Body = body });
        }
        return script;
    }

    // O código de uma sala (class Fase1 : RoomScript) tem os eventos da sala, não os do objeto.
    private static bool IsRoomScript(ClassDeclarationSyntax cls) =>
        cls.BaseList?.Types.Any(t => t.Type.ToString() is "RoomScript" or "CanisMajor.RoomScript") == true;

    // Se o texto tem erros de C# que impedem ler os blocos com segurança.
    public static IReadOnlyList<string> SyntaxErrors(string code) =>
        CSharpSyntaxTree.ParseText(code).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"linha {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
            .ToList();

    // Escreve os blocos de volta no script original, trocando só o corpo dos eventos.
    public static string Generate(string original, ObjectScript script)
    {
        var tree = CSharpSyntaxTree.ParseText(original);
        var root = tree.GetCompilationUnitRoot();
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("O script não tem uma classe.");
        var newline = original.Contains("\r\n") ? "\r\n" : "\n";
        var memberIndent = MemberIndent(cls, original);

        // De trás para a frente, para as posições do texto continuarem valendo.
        var edits = new List<(int Start, int Length, string Text)>();
        var missing = new List<EventScript>();
        foreach (var ev in script.Events)
        {
            var method = FindEvent(cls, ev.Name);
            if (method is null)
            {
                missing.Add(ev);
                continue;
            }
            var body = BodyText(ev.Body, memberIndent, newline);
            if (method.Body is { } block)
            {
                edits.Add((block.SpanStart, block.Span.Length, body));
            }
            else if (method.ExpressionBody is { } arrow)
            {
                // void OnStep(float dt) => ...;  vira um corpo com chaves.
                var end = method.SemicolonToken.Span.End;
                var start = arrow.SpanStart;
                var before = original[..start].TrimEnd();
                edits.Add((before.Length, end - before.Length, newline + memberIndent + body));
            }
        }
        if (missing.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var ev in missing)
            {
                var signature = script.Known.First(k => k.Name == ev.Name).Signature;
                sb.Append(newline).Append(memberIndent).Append(signature).Append(newline)
                  .Append(memberIndent).Append(BodyText(ev.Body, memberIndent, newline)).Append(newline);
            }
            // Antes da chave que fecha a classe.
            var close = cls.CloseBraceToken.SpanStart;
            var lineStart = original.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
            var insertAt = original[lineStart..close].Trim().Length == 0 ? lineStart : close;
            var text = sb.ToString();
            if (insertAt == lineStart)
                text = text.TrimStart('\r', '\n') + newline;
            // Uma linha em branco separando do último membro.
            if (cls.Members.Count > 0)
                text = newline + text;
            edits.Add((insertAt, 0, text));
        }
        var result = new StringBuilder(original);
        foreach (var (start, length, text) in edits.OrderByDescending(e => e.Start))
            result.Remove(start, length).Insert(start, text);
        return result.ToString();
    }

    // Só os comandos de um evento, sem as chaves, já recuados (para testes e prévias).
    public static string StatementsText(IEnumerable<Block> blocks, string indent = "", string newline = "\n")
    {
        var sb = new StringBuilder();
        foreach (var b in blocks)
            WriteBlock(sb, b, indent, newline);
        return sb.ToString();
    }

    // ---------- C# para blocos ----------

    // ---------- para o Editor de Objetos ----------

    // Acrescenta um evento (vazio ou com os blocos dados) no lugar certo da classe.
    public static string AddEvent(string original, string name, List<Block>? body = null)
    {
        var script = Parse(original) ?? throw new InvalidOperationException("O script não tem uma classe.");
        if (script.Find(name) is not null)
            return original;
        script.Events.Add(new EventScript { Name = name, Body = body ?? [] });
        return Generate(original, script);
    }

    // Tira o método do evento, com os comentários e a linha em branco de antes.
    public static string RemoveEvent(string original, string name)
    {
        var root = CSharpSyntaxTree.ParseText(original).GetCompilationUnitRoot();
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls is null || FindEvent(cls, name) is not { } method)
            return original;
        // O FullSpan leva junto o recuo, os comentários e a linha em branco de antes, e a quebra de linha do fim.
        var result = original[..method.FullSpan.Start] + original[method.FullSpan.End..];
        // Se era o primeiro membro, não deixa uma linha em branco logo depois da chave da classe.
        var brace = cls.OpenBraceToken.Span.End;
        if (method == cls.Members.First())
        {
            var rest = result[brace..];
            var trimmed = rest.TrimStart(' ', '\t');
            if (trimmed.StartsWith("\r\n\r\n") || trimmed.StartsWith("\n\n"))
                result = result[..brace] + rest[(rest.Length - trimmed.Length + (trimmed.StartsWith("\r\n") ? 2 : 1))..];
        }
        return result;
    }

    // A linha (de 1 em diante) onde o corpo do evento começa, para o editor levar o cursor até ela.
    public static int? EventLine(string code, string name)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var cls = tree.GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls is null || FindEvent(cls, name) is not { } method)
            return null;
        var at = method.Body?.OpenBraceToken.Span.End ?? method.SpanStart;
        return tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(at, 0)).StartLinePosition.Line + 2;
    }

    // Os nomes dos campos e propriedades da classe (para não criar uma variável repetida).
    public static HashSet<string> MemberNames(string code)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cls = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls is null)
            return names;
        foreach (var member in cls.Members)
        {
            if (member is FieldDeclarationSyntax field)
                foreach (var v in field.Declaration.Variables)
                    names.Add(v.Identifier.Text);
            else if (member is PropertyDeclarationSyntax prop)
                names.Add(prop.Identifier.Text);
        }
        return names;
    }

    // Põe uma declaração (como "public float vida = 3f;") logo no começo da classe.
    public static string AddMember(string original, string declaration, string? comment = null)
    {
        var root = CSharpSyntaxTree.ParseText(original).GetCompilationUnitRoot();
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("O script não tem uma classe.");
        var newline = original.Contains("\r\n") ? "\r\n" : "\n";
        var indent = MemberIndent(cls, original);
        var at = cls.OpenBraceToken.Span.End;
        var text = newline + (comment is null ? "" : indent + "// " + comment + newline) + indent + declaration;
        // Uma linha em branco separando dos outros membros.
        if (cls.Members.Count > 0)
            text += newline;
        return original[..at] + text + original[at..];
    }

    private static MethodDeclarationSyntax? FindEvent(ClassDeclarationSyntax cls, string name) =>
        cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == name);

    private static List<Block> Statements(IEnumerable<StatementSyntax> statements)
    {
        var list = new List<Block>();
        foreach (var s in statements)
        {
            list.AddRange(Comments(s.GetLeadingTrivia()));
            list.Add(FromStatement(s));
        }
        return list;
    }

    private static List<Block> Body(StatementSyntax statement) =>
        statement is BlockSyntax block ? Statements(block.Statements) : Statements([statement]);

    private static IEnumerable<Block> Comments(SyntaxTriviaList trivia) =>
        trivia.Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia))
              .Select(t => new CommentBlock { Text = t.ToString()[2..].Trim() });

    private static string Text(SyntaxNode node) => node.WithoutTrivia().ToString();

    private static Block FromStatement(StatementSyntax s)
    {
        switch (s)
        {
            case IfStatementSyntax ifs:
                return new IfBlock
                {
                    Condition = Text(ifs.Condition),
                    Then = Body(ifs.Statement),
                    Else = ifs.Else is { } e ? Body(e.Statement) : null,
                };
            case WhileStatementSyntax w:
                return new WhileBlock { Condition = Text(w.Condition), Body = Body(w.Statement) };
            case ForStatementSyntax f when RepeatParts(f) is { } r:
                return new RepeatBlock { Var = r.Var, Count = r.Count, Body = Body(f.Statement) };
            case ForEachStatementSyntax fe when fe.Type.IsVar:
                return new ForEachBlock { Var = fe.Identifier.Text, Collection = Text(fe.Expression), Body = Body(fe.Statement) };
            case ReturnStatementSyntax { Expression: null }:
                return new ReturnBlock();
            case LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } local
                when local.Declaration.Variables[0].Initializer is { } init && local.Modifiers.Count == 0:
                return new VarBlock { Type = Text(local.Declaration.Type), Name = local.Declaration.Variables[0].Identifier.Text, Value = Text(init.Value) };
            case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax a } when SetBlock.Ops.Contains(a.OperatorToken.Text):
                return new SetBlock { Target = Text(a.Left), Op = a.OperatorToken.Text, Value = Text(a.Right) };
            case ExpressionStatementSyntax { Expression: PostfixUnaryExpressionSyntax p } when p.OperatorToken.Text is "++" or "--":
                return new SetBlock { Target = Text(p.Operand), Op = p.OperatorToken.Text == "++" ? "+=" : "-=", Value = "1" };
            case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }:
                return new CallBlock { Method = Text(call.Expression), Args = call.ArgumentList.Arguments.Select(a => Text(a)).ToList() };
            default:
                return new CodeBlock { Code = Dedent(Text(s), s.GetLocation().GetLineSpan().StartLinePosition.Character) };
        }
    }

    // Tira das linhas de baixo o recuo da linha do comando, para o texto não ir andando para a
    // direita a cada ida e volta entre os modos.
    private static string Dedent(string text, int column)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var spaces = 0;
            while (spaces < column && spaces < lines[i].Length && lines[i][spaces] == ' ') spaces++;
            lines[i] = lines[i][spaces..];
        }
        return string.Join("\n", lines);
    }

    // for (var i = 0; i < N; i++)  →  (i, N)
    private static (string Var, string Count)? RepeatParts(ForStatementSyntax f)
    {
        if (f.Declaration is not { Variables.Count: 1 } decl || !decl.Type.IsVar && decl.Type.ToString() != "int")
            return null;
        var v = decl.Variables[0];
        if (v.Initializer?.Value.ToString() != "0")
            return null;
        if (f.Condition is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LessThanExpression } cond || cond.Left.ToString() != v.Identifier.Text)
            return null;
        if (f.Incrementors.Count != 1 || f.Incrementors[0].ToString() is var inc && inc != v.Identifier.Text + "++" && inc != "++" + v.Identifier.Text)
            return null;
        return (v.Identifier.Text, Text(cond.Right));
    }

    // ---------- blocos para C# ----------

    private static string MemberIndent(ClassDeclarationSyntax cls, string original)
    {
        var member = cls.Members.FirstOrDefault();
        if (member is not null)
        {
            var lineStart = original.LastIndexOf('\n', Math.Max(0, member.SpanStart - 1)) + 1;
            var lead = original[lineStart..member.SpanStart];
            if (lead.Trim().Length == 0)
                return lead;
        }
        return Indent;
    }

    private static string BodyText(List<Block> blocks, string memberIndent, string newline)
    {
        var sb = new StringBuilder("{").Append(newline);
        foreach (var b in blocks)
            WriteBlock(sb, b, memberIndent + Indent, newline);
        sb.Append(memberIndent).Append('}');
        return sb.ToString();
    }

    private static void WriteBody(StringBuilder sb, List<Block> blocks, string indent, string newline)
    {
        sb.Append(indent).Append('{').Append(newline);
        foreach (var b in blocks)
            WriteBlock(sb, b, indent + Indent, newline);
        sb.Append(indent).Append('}').Append(newline);
    }

    private static void WriteBlock(StringBuilder sb, Block block, string indent, string newline)
    {
        switch (block)
        {
            case IfBlock ifb:
                sb.Append(indent).Append("if (").Append(ifb.Condition).Append(')').Append(newline);
                WriteBody(sb, ifb.Then, indent, newline);
                // Um "senão" com só um "se" dentro vira "else if".
                var otherwise = ifb.Else;
                while (otherwise is [IfBlock nested])
                {
                    sb.Append(indent).Append("else if (").Append(nested.Condition).Append(')').Append(newline);
                    WriteBody(sb, nested.Then, indent, newline);
                    otherwise = nested.Else;
                }
                if (otherwise is not null)
                {
                    sb.Append(indent).Append("else").Append(newline);
                    WriteBody(sb, otherwise, indent, newline);
                }
                break;
            case WhileBlock w:
                sb.Append(indent).Append("while (").Append(w.Condition).Append(')').Append(newline);
                WriteBody(sb, w.Body, indent, newline);
                break;
            case RepeatBlock r:
                sb.Append(indent).Append($"for (var {r.Var} = 0; {r.Var} < {r.Count}; {r.Var}++)").Append(newline);
                WriteBody(sb, r.Body, indent, newline);
                break;
            case ForEachBlock fe:
                sb.Append(indent).Append($"foreach (var {fe.Var} in {fe.Collection})").Append(newline);
                WriteBody(sb, fe.Body, indent, newline);
                break;
            case SetBlock s:
                sb.Append(indent).Append($"{s.Target} {s.Op} {s.Value};").Append(newline);
                break;
            case VarBlock v:
                sb.Append(indent).Append($"{v.Type} {v.Name} = {v.Value};").Append(newline);
                break;
            case CallBlock c:
                sb.Append(indent).Append($"{c.Method}({string.Join(", ", c.Args)});").Append(newline);
                break;
            case ReturnBlock:
                sb.Append(indent).Append("return;").Append(newline);
                break;
            case CommentBlock cm:
                sb.Append(indent).Append("// ").Append(cm.Text).Append(newline);
                break;
            case CodeBlock code:
                // Um comando de várias linhas mantém o recuo relativo.
                foreach (var line in code.Code.Replace("\r\n", "\n").Split('\n'))
                    sb.Append(indent).Append(line.TrimEnd()).Append(newline);
                break;
        }
    }
}
