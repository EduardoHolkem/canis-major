using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CanisEditor.Game;
using CanisEditor.Theming;

namespace CanisEditor;

// O Editor de Objetos, como o do GameMaker 2: as propriedades ficam no Inspetor, e aqui ficam a lista de
// eventos (com + Adicionar evento) e as variáveis definidas. O script .cs continua sendo a verdade:
// adicionar um evento escreve o método nele, e tirar apaga o método.
public partial class MainWindow
{
    private static readonly (string Type, string Title)[] VariableTypes =
        [("number", "número"), ("integer", "inteiro"), ("text", "texto"), ("bool", "sim ou não")];

    private string ScriptPathOf(string name) => Path.Combine(GameFiles.FolderOf(RootFolder!, "object"), name + ".cs");

    // O texto de um arquivo: o da aba aberta (mesmo sem salvar) ou o do disco. Null se não existir.
    private string? ReadText(string path)
    {
        var open = _documents.Find(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (open is not null)
            return open.Document.Text;
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // Escreve pela aba, se estiver aberta (com desfazer), ou direto no disco.
    private void WriteText(string path, string text)
    {
        var open = _documents.Find(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (open is not null)
            open.Document.Text = text;
        else
            File.WriteAllText(path, text);
    }

    // Os eventos que o script do objeto escreve.
    public List<string> ObjectEvents(string name) =>
        ReadText(ScriptPathOf(name)) is { } text && BlockConverter.Parse(text) is { } script ? script.Events.Select(e => e.Name).ToList() : [];

    // Os eventos que vêm dos pais (e o pai de onde cada um vem), sem os que o objeto já escreve.
    private List<(string Event, string Owner)> InheritedEvents(string name)
    {
        var own = ObjectEvents(name).ToHashSet();
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        for (var parent = _gameObjects.GetValueOrDefault(name)?.Parent; parent is not null && seen.Add(parent); parent = _gameObjects.GetValueOrDefault(parent)?.Parent)
        {
            var path = _gameObjects.TryGetValue(parent, out var info) && info.Script ? info.Path : ScriptPathOf(parent);
            if (ReadText(path) is not { } text || BlockConverter.Parse(text) is not { } script)
                continue;
            foreach (var ev in script.Events)
                if (own.Add(ev.Name))
                    result.Add((ev.Name, parent));
        }
        return result;
    }

    // ---------- painel de eventos ----------

    private void BuildEventsPanel(string name)
    {
        var palette = Palette.Current;
        ObjectSummary.Children.Add(PanelTitle("Eventos", "O que o objeto faz e quando. Cada evento abre em C# Código ou C# Blocos."));
        var scriptPath = ScriptPathOf(name);
        var text = ReadText(scriptPath);
        if (text is not null && BlockConverter.SyntaxErrors(text).Count > 0)
        {
            ObjectSummary.Children.Add(new TextBlock { Text = "O script tem um erro de C#. Abra o código (botão Script, no alto) e corrija para ver e mudar os eventos aqui.", Foreground = palette.Muted, TextWrapping = TextWrapping.Wrap });
            return;
        }

        var own = ObjectEvents(name);
        var missing = ObjectScript.KnownEvents.Where(k => !own.Contains(k.Name)).ToList();
        if (missing.Count > 0)
        {
            var add = new ComboBox { ItemsSource = missing.Select(m => m.Title).ToList(), PlaceholderText = "+ Adicionar evento", MinWidth = 260, FontSize = 12, Tag = "oe.addevent" };
            add.SelectionChanged += (_, _) =>
            {
                if (add.SelectedIndex >= 0)
                    AddObjectEvent(name, missing[add.SelectedIndex].Name);
            };
            ObjectSummary.Children.Add(add);
        }

        foreach (var (ev, title, signature) in ObjectScript.KnownEvents.Where(k => own.Contains(k.Name)))
        {
            var row = EventRow(title, signature.Replace("public override void ", ""), null);
            row.Buttons.Children.Add(SmallButton("Código", "Abre o script no começo deste evento", () => OpenObjectEvent(name, ev, blocks: false)).WithTag("oe.code." + ev));
            row.Buttons.Children.Add(SmallButton("Blocos", "Abre o script em C# Blocos", () => OpenObjectEvent(name, ev, blocks: true)).WithTag("oe.blocks." + ev));
            row.Buttons.Children.Add(SmallButton("✕", "Tira o evento (apaga o método do script)", () => _ = RemoveObjectEventAsync(name, ev)).WithTag("oe.remove." + ev));
            ObjectSummary.Children.Add(row.Card);
        }

        foreach (var (ev, owner) in InheritedEvents(name))
        {
            var known = ObjectScript.KnownEvents.FirstOrDefault(k => k.Name == ev);
            var row = EventRow(known.Title ?? ev, $"herdado de {owner}", palette.Muted);
            row.Buttons.Children.Add(SmallButton("Mudar aqui", $"Escreve o evento neste objeto, começando pelo que {owner} faz (base.{ev})", () => AddObjectEvent(name, ev, inherit: true)).WithTag("oe.inherit." + ev));
            ObjectSummary.Children.Add(row.Card);
        }

        if (own.Count == 0 && InheritedEvents(name).Count == 0)
            ObjectSummary.Children.Add(new TextBlock { Text = "Nenhum evento ainda. Use + Adicionar evento: o editor cria o script e o método para você.", Foreground = palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
    }

    private static Control PanelTitle(string title, string note)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 14, 0, 2) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = note, FontSize = 12, Foreground = Palette.Current.Muted, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static (Border Card, StackPanel Buttons) EventRow(string title, string detail, IBrush? titleBrush)
    {
        var palette = Palette.Current;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "⚑ " + title, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = titleBrush ?? palette.Ink });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = palette.Muted, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace") });
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        var card = new Border { Background = palette.Tint, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 6, 2), Child = grid };
        return (card, buttons);
    }

    // Cria o script do objeto, se ainda não houver, herdando da classe do pai quando ele tem script.
    private string EnsureScript(string name)
    {
        var path = ScriptPathOf(name);
        if (ReadText(path) is not null)
            return path;
        var parent = Str(LoadDef(name), "parent") ?? _gameObjects.GetValueOrDefault(name)?.Parent;
        File.WriteAllText(path, $$"""
            using CanisMajor;

            namespace {{GameFiles.Namespace(RootFolder!)}};

            // O script do objeto {{name}}. Os eventos são criados pelo Editor de Objetos (+ Adicionar evento).
            public class {{name}} : {{ScriptBaseFor(parent)}}
            {
            }

            """.Replace("\r\n", "\n"));
        return path;
    }

    // Acrescenta o evento ao script (criando o script se for preciso). Com inherit, começa chamando o do pai.
    public void AddObjectEvent(string name, string ev, bool inherit = false)
    {
        if (RootFolder is null)
            return;
        var path = EnsureScript(name);
        var text = ReadText(path)!;
        List<Block>? body = null;
        if (inherit)
        {
            var signature = ObjectScript.KnownEvents.First(k => k.Name == ev).Signature;
            var open = signature.IndexOf('(');
            var parameter = signature[(open + 1)..signature.IndexOf(')')].Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            body = [new CallBlock { Method = "base." + ev, Args = parameter is null ? [] : [parameter] }];
        }
        try
        {
            WriteText(path, BlockConverter.AddEvent(text, ev, body));
        }
        catch (InvalidOperationException e)
        {
            ShowStatus(e.Message);
            return;
        }
        ReloadGameObjects();
        RefreshTree();
        BuildObjectSummary(name, LoadDef(name));
    }

    private async Task RemoveObjectEventAsync(string name, string ev)
    {
        var title = ObjectScript.KnownEvents.FirstOrDefault(k => k.Name == ev).Title ?? ev;
        var choice = await AskAsync($"Tirar o evento \"{title}\"?", $"O método {ev} e o código dentro dele saem do script {name}.cs. Ctrl Z na aba do script desfaz.", "Tirar", null);
        if (choice != DialogChoice.Primary)
            return;
        RemoveObjectEvent(name, ev);
    }

    public void RemoveObjectEvent(string name, string ev)
    {
        var path = ScriptPathOf(name);
        if (ReadText(path) is not { } text)
            return;
        WriteText(path, BlockConverter.RemoveEvent(text, ev));
        BuildObjectSummary(name, LoadDef(name));
    }

    // Abre o script no evento: no código, com o cursor no corpo do método; ou em C# Blocos.
    public void OpenObjectEvent(string name, string ev, bool blocks)
    {
        var path = ScriptPathOf(name);
        if (ReadText(path) is not { } text)
            return;
        OpenScript(path);
        if (blocks)
        {
            if (!ShowsBlocks)
                ToggleBlocks();
            return;
        }
        if (ShowsBlocks)
            ToggleBlocks();
        if (BlockConverter.EventLine(text, ev) is { } line)
            GoToLine(line);
    }

    // ---------- painel de variáveis ----------

    // As variáveis definidas para o objeto e os pais: (nome, tipo, valor, de quem). O filho troca a do pai.
    public List<(string Name, string Type, JsonNode? Value, string Owner)> DefinedVariables(string name)
    {
        var chain = new List<string> { name };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        for (var parent = Str(LoadDef(name), "parent") ?? _gameObjects.GetValueOrDefault(name)?.Parent; parent is not null && seen.Add(parent);
             parent = Str(LoadDef(parent), "parent") ?? _gameObjects.GetValueOrDefault(parent)?.Parent)
            chain.Add(parent);
        var all = new Dictionary<string, (string, string, JsonNode?, string)>(StringComparer.OrdinalIgnoreCase);
        for (var i = chain.Count - 1; i >= 0; i--)
            foreach (var v in (LoadDef(chain[i])["variables"] as JsonArray ?? []).OfType<JsonObject>())
                if (Str(v, "name") is { Length: > 0 } varName)
                    all[varName] = (varName, Str(v, "type") ?? "number", v["value"]?.DeepClone(), chain[i]);
        return [.. all.Values];
    }

    private void BuildVariablesPanel(string name, JsonObject def)
    {
        var palette = Palette.Current;
        ObjectSummary.Children.Add(PanelTitle("Variáveis", "Valores que cada instância pode trocar no Inspetor da sala, como vida ou velocidade. No script, vão para o campo de mesmo nome."));
        var list = def["variables"] as JsonArray ?? [];
        var members = ScriptMembers(name);
        var hasScript = ReadText(ScriptPathOf(name)) is not null;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject v)
                continue;
            ObjectSummary.Children.Add(VariableRow(name, i, v, members, hasScript));
        }
        foreach (var (varName, type, value, owner) in DefinedVariables(name).Where(v => !string.Equals(v.Owner, name, StringComparison.OrdinalIgnoreCase)))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
            row.Children.Add(new TextBlock { Text = $"{varName} = {value?.ToJsonString() ?? "?"}   (de {owner}, {TypeTitle(type)})", Foreground = palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var change = SmallButton("Mudar aqui", $"Este objeto passa a ter o próprio valor de {varName}", () => EditDef(d =>
            {
                if (d["variables"] is not JsonArray own)
                    d["variables"] = own = new JsonArray();
                own.Add(new JsonObject { ["name"] = varName, ["type"] = type, ["value"] = value?.DeepClone() });
            })).WithTag("oe.var.override." + varName);
            Grid.SetColumn(change, 1);
            row.Children.Add(change);
            ObjectSummary.Children.Add(row);
        }
        var add = SmallButton("+ Variável", "Uma variável nova, de número, com valor 0", () => AddObjectVariable(name)).WithTag("oe.addvar");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        ObjectSummary.Children.Add(add);
    }

    private static string TypeTitle(string type) => VariableTypes.FirstOrDefault(t => t.Type == type).Title ?? type;

    // Os campos e propriedades do script do objeto e dos pais com script.
    private HashSet<string> ScriptMembers(string name)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var current = (string?)name; current is not null && seen.Add(current); current = _gameObjects.GetValueOrDefault(current)?.Parent)
        {
            var path = _gameObjects.TryGetValue(current, out var info) && info.Script ? info.Path : ScriptPathOf(current);
            if (ReadText(path) is { } text)
                names.UnionWith(BlockConverter.MemberNames(text));
        }
        return names;
    }

    private Control VariableRow(string objectName, int index, JsonObject v, HashSet<string> members, bool hasScript)
    {
        var palette = Palette.Current;
        var name = Str(v, "name") ?? "";
        var type = Str(v, "type") ?? "number";
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,110,*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 2) };

        var nameBox = new TextBox { Text = name, FontSize = 12, Tag = $"oe.var.name.{index}" };
        CommitOn(nameBox, typed =>
        {
            var clean = typed.Trim();
            if (clean.Length == 0 || !clean.All(c => char.IsLetterOrDigit(c) || c == '_') || char.IsDigit(clean[0]))
            {
                ShowStatus("O nome da variável usa letras, números e _, e começa com letra.");
                BuildObjectSummary(objectName, LoadDef(objectName));
                return;
            }
            EditVariable(index, o => o["name"] = clean);
        });
        grid.Children.Add(nameBox);

        var typePicker = new ComboBox { ItemsSource = VariableTypes.Select(t => t.Title).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(VariableTypes, t => t.Type == type)), FontSize = 12, Tag = $"oe.var.type.{index}", HorizontalAlignment = HorizontalAlignment.Stretch };
        typePicker.SelectionChanged += (_, _) =>
        {
            var chosen = VariableTypes[Math.Max(0, typePicker.SelectedIndex)].Type;
            if (chosen == type)
                return;
            EditVariable(index, o =>
            {
                o["type"] = chosen;
                o["value"] = ValueNode(CanisMajor.VariableDef.Convert(o["value"]?.DeepClone(), chosen));
            });
        };
        Grid.SetColumn(typePicker, 1);
        grid.Children.Add(typePicker);

        var valueBox = new TextBox { Text = ValueText(v["value"]), FontSize = 12, Tag = $"oe.var.value.{index}" };
        CommitOn(valueBox, typed => EditVariable(index, o => o["value"] = ParseVariableValue(typed, Str(o, "type") ?? "number")));
        Grid.SetColumn(valueBox, 2);
        grid.Children.Add(valueBox);

        var remove = SmallButton("✕", "Tira a variável", () => EditDef(d => (d["variables"] as JsonArray)?.RemoveAt(index))).WithTag($"oe.var.remove.{index}");
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(grid);
        // Onde o valor chega no script: no campo, ou em Vars (com um botão para criar o campo).
        var csType = CsType(type);
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 0, 0, 4) };
        if (members.Contains(name))
        {
            hint.Children.Add(new TextBlock { Text = $"No script: o campo {name}.", FontSize = 11, Foreground = palette.Muted });
        }
        else
        {
            hint.Children.Add(new TextBlock { Text = $"No script: Var<{csType}>(\"{name}\").", FontSize = 11, Foreground = palette.Muted, VerticalAlignment = VerticalAlignment.Center });
            if (name.Length > 0)
                hint.Children.Add(SmallButton("Criar campo", $"Escreve public {csType} {name} no script, para usar {name} direto no código", () => AddVariableField(objectName, name, type, v["value"])).WithTag($"oe.var.field.{index}"));
        }
        _ = hasScript;
        panel.Children.Add(hint);
        return panel;
    }

    private void EditVariable(int index, Action<JsonObject> change) => EditDef(d =>
    {
        if ((d["variables"] as JsonArray)?[index] is JsonObject v)
            change(v);
    });

    public void AddObjectVariable(string name)
    {
        var used = DefinedVariables(name).Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var varName = "variavel";
        for (var n = 2; used.Contains(varName); n++)
            varName = "variavel" + n;
        EditDef(d =>
        {
            if (d["variables"] is not JsonArray list)
                d["variables"] = list = new JsonArray();
            list.Add(new JsonObject { ["name"] = varName, ["type"] = "number", ["value"] = 0 });
        });
    }

    // Escreve o campo no começo da classe do script (criando o script, se for preciso).
    public void AddVariableField(string objectName, string name, string type, JsonNode? value)
    {
        var path = EnsureScript(objectName);
        var text = ReadText(path)!;
        var literal = type switch
        {
            "integer" => Convert.ToString(CanisMajor.VariableDef.Convert(value, type), CultureInfo.InvariantCulture),
            "text" => "\"" + ((string?)CanisMajor.VariableDef.Convert(value, type) ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            "bool" => (bool)CanisMajor.VariableDef.Convert(value, type)! ? "true" : "false",
            _ => Convert.ToString(CanisMajor.VariableDef.Convert(value, type), CultureInfo.InvariantCulture) + "f",
        };
        WriteText(path, BlockConverter.AddMember(text, $"public {CsType(type)} {name} = {literal};", "Variável do Editor de Objetos: o valor da ficha e da sala vale mais que este."));
        ReloadGameObjects();
        RefreshTree();
        BuildObjectSummary(objectName, LoadDef(objectName));
    }

    private static string CsType(string type) => type switch { "integer" => "int", "text" => "string", "bool" => "bool", _ => "float" };

    private static string ValueText(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "";

    private static JsonNode? ValueNode(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        float f => JsonValue.Create(Math.Round((double)f, 4)),
        string s => JsonValue.Create(s),
        _ => JsonValue.Create(value.ToString()),
    };

    // O que foi digitado, no tipo da variável. Aceita vírgula ou ponto nos números, e sim ou não.
    public static JsonNode? ParseVariableValue(string text, string type)
    {
        var t = text.Trim();
        return type switch
        {
            "integer" => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? JsonValue.Create(i) : JsonValue.Create(0),
            "text" => JsonValue.Create(text),
            "bool" => JsonValue.Create(t.ToLowerInvariant() is "true" or "sim" or "s" or "1" or "verdadeiro"),
            _ => double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(Math.Round(d, 4)) : JsonValue.Create(0),
        };
    }

    // Confirma com Enter ou ao sair do campo, como os campos do Inspetor.
    private static void CommitOn(TextBox box, Action<string> commit)
    {
        var committed = box.Text ?? "";
        void Commit()
        {
            var text = box.Text ?? "";
            if (text == committed)
                return;
            committed = text;
            commit(text);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            Commit();
            e.Handled = true;
        };
        box.LostFocus += (_, _) => Commit();
    }
}

internal static class ControlTagExtensions
{
    // Põe a Tag e devolve o próprio controle, para os testes acharem os botões.
    public static T WithTag<T>(this T control, string tag) where T : Control
    {
        control.Tag = tag;
        return control;
    }
}
