using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CanisEditor.Game;
using CanisEditor.Theming;
using ComponentTypes = CanisMajor.ComponentTypes;

namespace CanisEditor;

// A ficha do objeto (objects/Nome.json): sprite, shader, profundidade e componentes, editados no
// Inspetor. O script C# com o mesmo nome é opcional e continua sendo o lugar do código.
public partial class MainWindow
{
    private static readonly JsonSerializerOptions DefWrite = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // As propriedades que o editor mostra de cada componente pronto, pela própria classe da engine.
    private static PropertyInfo[] EditableProps(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod!.IsPublic && p.Name != nameof(CanisMajor.Component.Owner)
                && (p.PropertyType == typeof(float) || p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string)))
            .OrderBy(p => p.Name == nameof(CanisMajor.Component.Enabled) ? 1 : 0)
            .ToArray();

    private static string Snake(string pascal) => JsonNamingPolicy.SnakeCaseLower.ConvertName(pascal);

    private string ActiveObjectName => Path.GetFileNameWithoutExtension(ActiveDocument!.FullPath);

    private string DefPath(string name) => Path.Combine(GameFiles.FolderOf(RootFolder!, "object"), name + ".json");

    private JsonObject LoadDef(string name)
    {
        var path = DefPath(name);
        // A ficha aberta numa aba vale o que está na tela, mesmo sem salvar.
        var open = _documents.Find(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
        try
        {
            var text = open?.Document.Text ?? (File.Exists(path) ? File.ReadAllText(path) : "{}");
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private void SaveDef(string name, JsonObject def)
    {
        var path = DefPath(name);
        var text = def.ToJsonString(DefWrite) + "\n";
        var open = _documents.Find(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (open is not null && open == ActiveDocument)
        {
            // Pela aba, para o Ctrl Z e o salvar funcionarem como num texto.
            CodeEditor.Document.Text = text;
        }
        else
        {
            File.WriteAllText(path, text);
        }
        ReloadGameObjects();
        RefreshTree();
        BuildInspector();
        BuildObjectSummary(name, def);
    }

    private void EditDef(Action<JsonObject> change)
    {
        var name = ActiveObjectName;
        var def = LoadDef(name);
        change(def);
        SaveDef(name, def);
    }

    private void BuildObjectInspector()
    {
        var name = ActiveObjectName;
        var def = LoadDef(name);
        BuildObjectSummary(name, def);

        AddHeading($"Objeto {name}", first: true);
        var sprites = SpriteNames();
        sprites.Insert(0, "(nenhum)");
        AddChoice("Sprite", sprites, Str(def, "sprite") ?? "(nenhum)", "o.sprite", chosen => EditDef(d => Set(d, "sprite", chosen == "(nenhum)" ? null : chosen)));
        var shaders = GameFiles.List(RootFolder!, "shader").Select(s => s.Name).ToList();
        shaders.Insert(0, "(nenhum)");
        AddChoice("Shader", shaders, Str(def, "shader") ?? "(nenhum)", "o.shader", chosen => EditDef(d => Set(d, "shader", chosen == "(nenhum)" ? null : chosen)));
        AddNumberField("Profundidade", Num(def, "layer") ?? 0, "o.layer", value => EditDef(d => d["layer"] = (int)value));
        AddCheck("Visível", Bool(def, "visible") ?? true, "o.visible", on => EditDef(d => Set(d, "visible", on ? null : false)));
        AddCheck("Ordenar por Y", Bool(def, "y_sort") ?? false, "o.ysort", on => EditDef(d => Set(d, "y_sort", on ? true : null)));
        AddNumberField("SortOffset", Num(def, "sort_offset") ?? 0, "o.sortoffset", value => EditDef(d => Set(d, "sort_offset", value == 0 ? null : value)));
        AddTextField("Cor", Str(def, "color") ?? "", "o.color", typed => EditDef(d => Set(d, "color", typed.Trim().Length == 0 ? null : typed.Trim())));
        AddNumberField("Alpha", Num(def, "alpha") ?? 1, "o.alpha", value => EditDef(d => Set(d, "alpha", value == 1 ? null : Math.Clamp(value, 0, 1))));
        AddNumberField("Escala X", Num(def, "scale_x") ?? 1, "o.sx", value => EditDef(d => Set(d, "scale_x", value == 1 ? null : value)));
        AddNumberField("Escala Y", Num(def, "scale_y") ?? 1, "o.sy", value => EditDef(d => Set(d, "scale_y", value == 1 ? null : value)));

        AddHeading("Componentes");
        var components = def["components"] as JsonArray ?? [];
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is JsonObject component)
                BuildComponentFields(i, component, components.Count);
        }
        if (components.Count == 0)
            AddNote("Nenhum componente. Eles dão comportamento sem código, como gravidade, máscara de colisão ou tempo de vida.");

        var types = ComponentTypes.All.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        types.Add("Outro, pelo nome…");
        var add = new ComboBox { ItemsSource = types, PlaceholderText = "+ Componente", HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Tag = "o.addcomponent" };
        add.SelectionChanged += (_, _) =>
        {
            if (add.SelectedItem is not string chosen)
                return;
            if (chosen.StartsWith("Outro", StringComparison.Ordinal))
                _ = AddCustomComponentAsync();
            else
                AddComponent(chosen);
        };
        Inspector.Children.Add(add);

        AddHeading("Script");
        var script = Path.Combine(GameFiles.FolderOf(RootFolder!, "object"), name + ".cs");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (File.Exists(script))
            actions.Children.Add(SmallButton("Abrir script C#", script, () => OpenScript(script)));
        else
            actions.Children.Add(SmallButton("Criar script C#", "Uma classe com o mesmo nome, para os eventos OnCreate, OnStep e OnDraw", () => CreateScript(name, script)));
        Inspector.Children.Add(actions);
        AddNote("A ficha vale mais que o construtor do script: o que estiver aqui é aplicado depois dele.");
    }

    private void BuildComponentFields(int index, JsonObject component, int total)
    {
        var typeName = Str(component, "type") ?? "?";
        var palette = Palette.Current;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 8, 0, 2) };
        header.Children.Add(new TextBlock { Text = "◆ " + typeName, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var up = SmallButton("↑", "Rodar antes", () => MoveComponent(index, -1));
        var down = SmallButton("↓", "Rodar depois", () => MoveComponent(index, +1));
        var remove = SmallButton("✕", "Tirar o componente", () => EditDef(d => (d["components"] as JsonArray)?.RemoveAt(index)));
        up.IsEnabled = index > 0;
        down.IsEnabled = index < total - 1;
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 2);
        Grid.SetColumn(remove, 3);
        header.Children.Add(up);
        header.Children.Add(down);
        header.Children.Add(remove);
        Inspector.Children.Add(header);

        if (ComponentTypes.All.TryGetValue(typeName, out var type))
        {
            var defaults = Activator.CreateInstance(type);
            foreach (var prop in EditableProps(type))
            {
                var key = Snake(prop.Name);
                var tag = $"c.{index}.{key}";
                var current = component[key];
                if (prop.PropertyType == typeof(bool))
                {
                    var value = current is JsonValue v && v.TryGetValue<bool>(out var b) ? b : (bool)prop.GetValue(defaults)!;
                    AddCheck(prop.Name, value, tag, on => EditComponent(index, key, on == (bool)prop.GetValue(defaults)! ? null : on));
                }
                else if (prop.PropertyType == typeof(string))
                {
                    AddTextField(prop.Name, Str(component, key) ?? "", tag, typed => EditComponent(index, key, typed.Trim().Length == 0 ? null : typed.Trim()));
                }
                else
                {
                    var fallback = Convert.ToDouble(prop.GetValue(defaults));
                    AddNumberField(prop.Name, Num(component, key) ?? fallback, tag, value =>
                        EditComponent(index, key, value == fallback ? null : prop.PropertyType == typeof(int) ? (int)value : value));
                }
            }
        }
        else
        {
            // Componente do jogo: o editor não conhece a classe, então mostra o que já está escrito.
            foreach (var (key, value) in component.Where(p => p.Key != "type").ToList())
                AddTextField(key, value?.ToJsonString() ?? "", $"c.{index}.{key}", typed => EditComponent(index, key, string.IsNullOrWhiteSpace(typed) ? null : ParseValue(typed)));
            AddNote($"{typeName} é um componente do jogo. Escreva as propriedades no JSON da ficha, com os nomes em snake_case.");
        }
        _ = palette;
    }

    private void EditComponent(int index, string key, object? value) => EditDef(d =>
    {
        if ((d["components"] as JsonArray)?[index] is JsonObject component)
            Set(component, key, value);
    });

    private void MoveComponent(int index, int direction) => EditDef(d =>
    {
        if (d["components"] is not JsonArray list || index + direction < 0 || index + direction >= list.Count)
            return;
        var item = list[index]!.DeepClone();
        list.RemoveAt(index);
        list.Insert(index + direction, item);
    });

    private void AddComponent(string type) => EditDef(d =>
    {
        if (d["components"] is not JsonArray list)
            d["components"] = list = new JsonArray();
        list.Add(new JsonObject { ["type"] = type });
    });

    private async Task AddCustomComponentAsync()
    {
        var name = await AskTextAsync("Componente do jogo", "O nome da classe, como Piscar. Ela fica num arquivo .cs do jogo e herda de Component.", "Acrescentar",
            text => System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), "^[A-Za-z_][A-Za-z0-9_]*$") ? null : "Use o nome de uma classe C#.");
        if (name is not null)
            AddComponent(name.Trim());
        else
            BuildInspector();
    }

    private void OpenScript(string path)
    {
        OpenFile(path);
        _roomAsCode.Remove(path);
        ShowDocumentSurface();
    }

    private void CreateScript(string name, string path)
    {
        var ns = GameFiles.Namespace(RootFolder!);
        File.WriteAllText(path, $$"""
            using CanisMajor;

            namespace {{ns}};

            // O script do objeto {{name}}. Sprite, shader e componentes ficam na ficha ({{name}}.json), pelo Inspetor.
            public class {{name}} : GameObject
            {
                public override void OnCreate()
                {
                }

                public override void OnStep(float dt)
                {
                }
            }

            """);
        ReloadGameObjects();
        RefreshTree();
        OpenScript(path);
    }

    // O resumo à esquerda: o sprite grande e o que cada componente faz.
    private void BuildObjectSummary(string name, JsonObject def)
    {
        ObjectSummary.Children.Clear();
        var palette = Palette.Current;
        ObjectSummary.Children.Add(new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeight.SemiBold });
        var sprite = Str(def, "sprite") ?? (_gameObjects.TryGetValue(name, out var info) ? info.Sprite : null);
        if (sprite is not null && LoadSprite(sprite) is { } image)
        {
            ObjectSummary.Children.Add(new Border
            {
                Background = palette.Tint,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new SpriteThumb(image) { Width = 128, Height = 128 },
            });
        }
        else
        {
            ObjectSummary.Children.Add(new TextBlock { Text = "Sem sprite. Escolha um no Inspetor.", Foreground = palette.Muted, FontSize = 12 });
        }

        var components = (def["components"] as JsonArray ?? []).OfType<JsonObject>().Select(c => Str(c, "type") ?? "?").ToList();
        ObjectSummary.Children.Add(new TextBlock
        {
            Text = components.Count == 0 ? "Sem componentes." : "Componentes: " + string.Join(", ", components),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        var script = Path.Combine(GameFiles.FolderOf(RootFolder!, "object"), name + ".cs");
        ObjectSummary.Children.Add(new TextBlock
        {
            Text = File.Exists(script) ? $"Script: {name}.cs (botão Script, no alto, abre o código)." : "Sem script. O objeto funciona só com a ficha e os componentes.",
            FontSize = 12,
            Foreground = palette.Muted,
            TextWrapping = TextWrapping.Wrap,
        });
    }

    // Campos e leitura da ficha

    private void AddChoice(string label, List<string> items, string selected, string tag, Action<string> commit)
    {
        var picker = new ComboBox
        {
            ItemsSource = items,
            SelectedItem = items.FirstOrDefault(i => string.Equals(i, selected, StringComparison.OrdinalIgnoreCase)) ?? items[0],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
            Tag = tag,
        };
        var first = picker.SelectedItem as string;
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is string chosen && chosen != first)
                commit(chosen);
        };
        AddRow(label, picker);
    }

    private void AddCheck(string label, bool value, string tag, Action<bool> commit)
    {
        var box = new CheckBox { Content = label, IsChecked = value, FontSize = 12, Tag = tag };
        box.IsCheckedChanged += (_, _) => commit(box.IsChecked == true);
        Inspector.Children.Add(box);
    }

    private static void Set(JsonObject json, string key, object? value)
    {
        if (value is null)
            json.Remove(key);
        else
            json[key] = value switch
            {
                JsonNode node => node,
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                double d => d == Math.Floor(d) && Math.Abs(d) < 1e9 ? JsonValue.Create((long)d) : JsonValue.Create(Math.Round(d, 4)),
                float f => JsonValue.Create(Math.Round((double)f, 4)),
                _ => JsonValue.Create(value.ToString()),
            };
    }

    private static string? Str(JsonObject json, string key) => json[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Num(JsonObject json, string key) => json[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static bool? Bool(JsonObject json, string key) => json[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
}
