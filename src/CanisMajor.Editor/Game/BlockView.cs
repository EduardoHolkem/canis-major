using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CanisEditor.Theming;

namespace CanisEditor.Game;

// A tela do modo C# Blocos: cada evento com os blocos empilhados. Blocos de controle (se,
// repetir, enquanto) abraçam os de dentro. Arraste pela alça ⠿ para mudar de lugar, inclusive
// para dentro de outro bloco, e clique nos valores para editar.
public sealed class BlockView : ScrollViewer
{
    private readonly StackPanel _root = new() { Margin = new Thickness(24, 16, 24, 80), Spacing = 14 };
    private ObjectScript? _script;
    private Block? _dragging;
    private List<Block>? _dragFrom;
    private Border? _hoverZone;
    private bool _dragMoved;
    private Point _dragStart;

    public BlockView()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Content = _root;
        AddHandler(PointerMovedEvent, OnDragMove, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragEnd, handledEventsToo: true);
    }

    public ObjectScript? Script => _script;

    // Onde o clique na paleta põe o bloco novo: depois do escolhido, ou no fim do evento escolhido.
    public List<Block>? InsertList { get; private set; }
    public int InsertIndex { get; private set; } = -1;
    public Block? Selected { get; private set; }

    // Os blocos mudaram: hora de reescrever o C#.
    public event Action? Changed;

    public void Show(ObjectScript? script, string? message = null)
    {
        _script = script;
        Rebuild(message);
    }

    // Põe um bloco novo no lugar escolhido (ou no fim de "A cada quadro").
    public void Insert(Block block)
    {
        if (_script is null)
            return;
        if (InsertList is null)
        {
            var ev = _script.Find("OnStep") ?? AddEvent("OnStep");
            InsertList = ev.Body;
            InsertIndex = ev.Body.Count;
        }
        var index = InsertIndex < 0 || InsertIndex > InsertList.Count ? InsertList.Count : InsertIndex;
        InsertList.Insert(index, block);
        Select(InsertList, block);
        Commit();
    }

    // Arrastar um bloco novo da paleta.
    public void BeginDragNew(Block block, PointerPressedEventArgs e)
    {
        _dragging = block;
        _dragFrom = null;
        _dragMoved = false;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    // Acrescenta o evento (se ainda não existe) e aponta o lugar dos blocos novos para o começo dele.
    public void AddEventByName(string name)
    {
        if (_script is null)
            return;
        var ev = _script.Find(name) ?? AddEvent(name);
        InsertList = ev.Body;
        InsertIndex = 0;
        Selected = null;
        Commit();
    }

    private EventScript AddEvent(string name)
    {
        var ev = new EventScript { Name = name };
        // Na ordem dos eventos conhecidos, para o código ficar arrumado.
        var order = ObjectScript.KnownEvents.Select(k => k.Name).ToList();
        var at = _script!.Events.FindIndex(e => order.IndexOf(e.Name) > order.IndexOf(name));
        _script.Events.Insert(at < 0 ? _script.Events.Count : at, ev);
        return ev;
    }

    private void Commit()
    {
        Changed?.Invoke();
        Rebuild(null);
    }

    private void Select(List<Block> list, Block block)
    {
        Selected = block;
        InsertList = list;
        InsertIndex = list.IndexOf(block) + 1;
    }

    // ---------- montagem da tela ----------

    private void Rebuild(string? message)
    {
        var offset = Offset;
        _root.Children.Clear();
        var palette = Palette.Current;
        if (_script is null)
        {
            _root.Children.Add(new TextBlock { Text = message ?? "Este arquivo não tem uma classe de objeto para mostrar em blocos.", Foreground = palette.Muted, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (message is not null)
            _root.Children.Add(new TextBlock { Text = message, Foreground = palette.Muted, TextWrapping = TextWrapping.Wrap });

        // Os eventos que ainda não existem ficam numa lista só, para não encher a tela de botões.
        var missing = ObjectScript.KnownEvents.Where(k => _script.Find(k.Name) is null).ToList();
        if (missing.Count > 0)
        {
            var titles = missing.Select(m => m.Title).ToList();
            var add = new ComboBox { ItemsSource = titles, PlaceholderText = "+ Evento", FontSize = 12, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, Tag = "ev.add" };
            ToolTip.SetTip(add, "Acrescenta um evento ao objeto: alarme, colisão, teclado, mouse, interface...");
            add.SelectionChanged += (_, _) =>
            {
                if (add.SelectedIndex < 0)
                    return;
                AddEventByName(missing[add.SelectedIndex].Name);
            };
            _root.Children.Add(add);
        }

        foreach (var (name, title, _) in ObjectScript.KnownEvents)
        {
            var ev = _script.Find(name);
            if (ev is null)
                continue;
            var section = new StackPanel { Spacing = 0 };
            var header = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#C0577A")),
                CornerRadius = new CornerRadius(8, 8, 8, 0),
                Padding = new Thickness(12, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = $"⚑ {title}", Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 13 },
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = "ev." + name,
            };
            ToolTip.SetTip(header, $"{name}: clique para pôr os blocos novos no começo deste evento");
            header.PointerPressed += (_, e) =>
            {
                InsertList = ev.Body;
                InsertIndex = 0;
                Selected = null;
                Rebuild(null);
                e.Handled = true;
            };
            section.Children.Add(header);
            section.Children.Add(BuildList(ev.Body, 1));
            _root.Children.Add(section);
        }
        Offset = offset;
    }

    private Control BuildList(List<Block> list, int depth)
    {
        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(depth == 1 ? 0 : 14, 0, 0, 0) };
        panel.Children.Add(DropZone(list, 0));
        for (var i = 0; i < list.Count; i++)
        {
            panel.Children.Add(BuildBlock(list, list[i], depth));
            panel.Children.Add(DropZone(list, i + 1));
        }
        if (list.Count == 0)
            panel.Children.Add(new TextBlock { Text = "vazio: arraste um bloco para cá", FontSize = 11, Foreground = Palette.Current.Faint, Margin = new Thickness(8, 0, 0, 2) });
        return panel;
    }

    // A faixa fina entre blocos: onde o bloco arrastado cai.
    private Border DropZone(List<Block> list, int index)
    {
        var insertHere = ReferenceEquals(InsertList, list) && InsertIndex == index && Selected is null;
        var zone = new Border
        {
            Height = 6,
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3),
            Background = insertHere ? new SolidColorBrush(Color.Parse("#5582AAFF")) : Brushes.Transparent,
            Tag = new DropTarget(list, index),
        };
        return zone;
    }

    private sealed record DropTarget(List<Block> List, int Index);

    private static (string Color, string Label) Look(Block block) => block switch
    {
        IfBlock => ("#D9822B", "se"),
        WhileBlock => ("#D9822B", "enquanto"),
        RepeatBlock => ("#D9822B", "repetir"),
        ForEachBlock => ("#D9822B", "para cada"),
        ReturnBlock => ("#D9822B", "sair do evento"),
        SetBlock => ("#C65A3A", "mudar"),
        VarBlock => ("#C65A3A", "nova variável"),
        CallBlock c when c.Method.StartsWith("Draw.", StringComparison.Ordinal) || c.Method == "DrawSelf" => ("#8A5BC7", "desenhar"),
        CallBlock c when c.Method.StartsWith("Game.", StringComparison.Ordinal) => ("#3F7FC4", "jogo"),
        CallBlock c when c.Method is "Move" or "Destroy" || c.Method.StartsWith("Add", StringComparison.Ordinal) => ("#3A9A6A", "objeto"),
        CallBlock => ("#2E9AA0", "chamar"),
        CommentBlock => ("#6B7280", "//"),
        _ => ("#4B5563", "C#"),
    };

    // Nomes amigáveis para as chamadas mais usadas, com o que cada valor quer dizer.
    private static readonly Dictionary<string, (string Title, string[] Params)> Friendly = new()
    {
        ["Move"] = ("mover", ["dx", "dy", "sólido"]),
        ["Destroy"] = ("destruir este objeto", []),
        ["DrawSelf"] = ("desenhar o próprio sprite", []),
        ["Draw.Text"] = ("escrever texto", ["texto", "x", "y"]),
        ["Draw.Sprite"] = ("desenhar sprite", ["sprite", "x", "y"]),
        ["Draw.Rect"] = ("desenhar retângulo", ["x", "y", "largura", "altura"]),
        ["Draw.Circle"] = ("desenhar círculo", ["x", "y", "raio"]),
        ["Game.Goto"] = ("ir para a sala", ["sala"]),
        ["Game.Restart"] = ("reiniciar a sala", []),
        ["Game.Quit"] = ("sair do jogo", []),
        ["Game.PlaySound"] = ("tocar som", ["som"]),
        ["Game.Follow"] = ("câmera segue", ["objeto"]),
        ["Game.Spawn"] = ("criar objeto", ["tipo", "x", "y"]),
        ["Lighting.Preset"] = ("luz da sala", ["preset"]),
    };

    private Control BuildBlock(List<Block> list, Block block, int depth)
    {
        var (color, label) = Look(block);
        var brush = new SolidColorBrush(Color.Parse(color));
        var selected = ReferenceEquals(block, Selected);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var handle = new TextBlock { Text = "⠿", Foreground = Brushes.White, Opacity = 0.7, FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeAll) };
        ToolTip.SetTip(handle, "Arraste para mudar de lugar");
        handle.PointerPressed += (_, e) =>
        {
            _dragging = block;
            _dragFrom = list;
            _dragMoved = false;
            _dragStart = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        row.Children.Add(handle);

        void Label(string text) => row.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeight.Medium, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });

        void Field(string value, string tip, Action<string> commit, double min = 40, bool multiline = false)
        {
            var box = new TextBox
            {
                Text = value,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                FontSize = 12,
                MinWidth = min,
                MinHeight = 24,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 2, 6, 2),
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.NoWrap : TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(box, tip);
            var committed = value;
            void Done()
            {
                var text = box.Text ?? "";
                if (text == committed)
                    return;
                committed = text;
                commit(text);
                Changed?.Invoke();
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && !(multiline && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) && !multiline)
                {
                    Done();
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => Done();
            box.GotFocus += (_, _) => { Selected = block; InsertList = list; InsertIndex = list.IndexOf(block) + 1; };
            row.Children.Add(box);
        }

        switch (block)
        {
            case IfBlock b:
                Label("se");
                Field(b.Condition, "A condição, em C#: Keys.Held(\"left\"), vida <= 0, X > 100", t => b.Condition = t, 120);
                break;
            case WhileBlock b:
                Label("enquanto");
                Field(b.Condition, "Repete enquanto isto for verdade", t => b.Condition = t, 120);
                break;
            case RepeatBlock b:
                Label("repetir");
                Field(b.Count, "Quantas vezes", t => b.Count = t);
                Label("vezes, contando em");
                Field(b.Var, "O nome do contador (0, 1, 2...)", t => b.Var = t, 24);
                break;
            case ForEachBlock b:
                Label("para cada");
                Field(b.Var, "O nome de cada item", t => b.Var = t, 40);
                Label("em");
                Field(b.Collection, "A lista, como Game.Instances<Estrela>()", t => b.Collection = t, 140);
                break;
            case ReturnBlock:
                Label("sair do evento");
                break;
            case SetBlock b:
                Field(b.Target, "O que muda: X, Y, Alpha, uma variável...", t => b.Target = t, 50);
                var op = new ComboBox { ItemsSource = SetBlock.Ops, SelectedItem = b.Op, FontSize = 12, Margin = new Thickness(0, 2, 6, 2), MinHeight = 24 };
                ToolTip.SetTip(op, "= troca, += soma, -= subtrai, *= multiplica, /= divide");
                op.SelectionChanged += (_, _) =>
                {
                    if (op.SelectedItem is string chosen && chosen != b.Op)
                    {
                        b.Op = chosen;
                        Changed?.Invoke();
                    }
                };
                row.Children.Add(op);
                Field(b.Value, "O valor, em C#", t => b.Value = t, 60);
                break;
            case VarBlock b:
                Label("nova variável");
                Field(b.Name, "O nome", t => b.Name = t, 50);
                Label("=");
                Field(b.Value, "O valor inicial, em C#", t => b.Value = t, 60);
                break;
            case CallBlock b:
                var key = Friendly.ContainsKey(b.Method) ? b.Method : b.Method.Split('<')[0];
                if (Friendly.TryGetValue(key, out var friendly))
                    Label(friendly.Title);
                Field(b.Method, "O nome do método, em C#", t => b.Method = t, 60);
                for (var i = 0; i < b.Args.Count; i++)
                {
                    var index = i;
                    var tip = friendly.Params is { } names && index < names.Length ? names[index] : $"valor {index + 1}";
                    Field(b.Args[index], tip, t => b.Args[index] = t, 30);
                }
                var plus = new Button { Classes = { "flat" }, Content = "+", FontSize = 12, Foreground = Brushes.White, Padding = new Thickness(6, 0), Margin = new Thickness(0, 0, 4, 0) };
                ToolTip.SetTip(plus, "Mais um valor");
                plus.Click += (_, _) => { b.Args.Add("0"); Commit(); };
                row.Children.Add(plus);
                if (b.Args.Count > 0)
                {
                    var minus = new Button { Classes = { "flat" }, Content = "−", FontSize = 12, Foreground = Brushes.White, Padding = new Thickness(6, 0) };
                    ToolTip.SetTip(minus, "Tirar o último valor");
                    minus.Click += (_, _) => { b.Args.RemoveAt(b.Args.Count - 1); Commit(); };
                    row.Children.Add(minus);
                }
                break;
            case CommentBlock b:
                Label("//");
                Field(b.Text, "Um comentário (não faz nada, só explica)", t => b.Text = t, 160);
                break;
            case CodeBlock b:
                Label("C#");
                Field(b.Code, "Código C# que não tem bloco próprio. Shift Enter quebra a linha.", t => b.Code = t, 200, multiline: true);
                break;
        }

        var remove = new Button { Classes = { "flat" }, Content = "✕", Foreground = Brushes.White, Opacity = 0.75, FontSize = 11, Padding = new Thickness(6, 0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(remove, "Excluir o bloco (e o que está dentro)");
        remove.Click += (_, _) =>
        {
            list.Remove(block);
            if (ReferenceEquals(Selected, block))
                Selected = null;
            Commit();
        };
        row.Children.Add(remove);

        var head = new Border
        {
            Background = brush,
            CornerRadius = block is IfBlock or WhileBlock or RepeatBlock or ForEachBlock ? new CornerRadius(6, 6, 6, 0) : new CornerRadius(6),
            Padding = new Thickness(8, 3),
            BorderBrush = selected ? Brushes.White : Brushes.Transparent,
            BorderThickness = new Thickness(selected ? 2 : 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
        };
        head.PointerPressed += (_, e) =>
        {
            if (e.Source is TextBox or Button or ComboBox)
                return;
            Select(list, block);
            Rebuild(null);
            e.Handled = true;
        };

        // Blocos que abraçam outros: a barra colorida à esquerda e o corpo dentro.
        List<(string? Caption, List<Block> Body)> bodies = block switch
        {
            IfBlock b => b.Else is null ? [(null, b.Then)] : [(null, b.Then), ("senão", b.Else)],
            WhileBlock b => [(null, b.Body)],
            RepeatBlock b => [(null, b.Body)],
            ForEachBlock b => [(null, b.Body)],
            _ => [],
        };
        if (bodies.Count == 0)
            return new StackPanel { Children = { head }, Margin = new Thickness(0, 1) };

        var stack = new StackPanel { Spacing = 0, Margin = new Thickness(0, 1) };
        stack.Children.Add(head);
        foreach (var (caption, body) in bodies)
        {
            if (caption is not null)
            {
                stack.Children.Add(new Border
                {
                    Background = brush,
                    Padding = new Thickness(10, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(0, 6, 6, 0),
                    Child = new TextBlock { Text = caption, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeight.Medium },
                });
            }
            stack.Children.Add(new Border
            {
                BorderBrush = brush,
                BorderThickness = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(4, 0, 0, 0),
                Child = BuildList(body, depth + 1),
            });
        }
        // O "pé" do bloco, e o botão para pôr ou tirar o senão.
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foot.Children.Add(new Border { Background = brush, Width = 40, Height = 8, CornerRadius = new CornerRadius(0, 0, 6, 6) });
        if (block is IfBlock ifb)
        {
            var toggle = new Button { Classes = { "flat" }, Content = ifb.Else is null ? "+ senão" : "− senão", FontSize = 11, Padding = new Thickness(6, 0) };
            toggle.Click += (_, _) =>
            {
                ifb.Else = ifb.Else is null ? new List<Block>() : null;
                Commit();
            };
            foot.Children.Add(toggle);
        }
        stack.Children.Add(foot);
        return stack;
    }

    // ---------- arrastar ----------

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_dragging is null)
            return;
        var at = e.GetPosition(this);
        if (!_dragMoved && Math.Abs(at.X - _dragStart.X) + Math.Abs(at.Y - _dragStart.Y) < 4)
            return;
        _dragMoved = true;
        var zone = ZoneAt(e.GetPosition(_root));
        if (!ReferenceEquals(zone, _hoverZone))
        {
            if (_hoverZone is not null)
                _hoverZone.Background = Brushes.Transparent;
            _hoverZone = zone;
            if (zone is not null)
                zone.Background = new SolidColorBrush(Color.Parse("#AA82AAFF"));
        }
    }

    private void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is null)
            return;
        e.Pointer.Capture(null);
        var block = _dragging;
        var from = _dragFrom;
        var zone = _hoverZone;
        _dragging = null;
        _dragFrom = null;
        _hoverZone = null;
        if (zone is not null)
            zone.Background = Brushes.Transparent;
        if (!_dragMoved || zone?.Tag is not DropTarget target)
        {
            // Bloco novo da paleta solto fora de uma faixa (ou só clicado): entra no lugar escolhido.
            if (from is null)
                Insert(block);
            return;
        }
        // Um bloco não cai dentro dele mesmo.
        if (Contains(block, target.List))
        {
            Rebuild(null);
            return;
        }
        var index = target.Index;
        if (from is not null)
        {
            var old = from.IndexOf(block);
            from.Remove(block);
            if (ReferenceEquals(from, target.List) && old < index)
                index--;
        }
        target.List.Insert(Math.Clamp(index, 0, target.List.Count), block);
        Select(target.List, block);
        Commit();
    }

    // Move um bloco para outra lista (usado pelos testes e pelo teclado).
    public void MoveBlock(List<Block> from, Block block, List<Block> to, int index)
    {
        if (Contains(block, to))
            return;
        var old = from.IndexOf(block);
        from.Remove(block);
        if (ReferenceEquals(from, to) && old < index)
            index--;
        to.Insert(Math.Clamp(index, 0, to.Count), block);
        Commit();
    }

    private static bool Contains(Block block, List<Block> list) => block switch
    {
        IfBlock b => ReferenceEquals(b.Then, list) || ReferenceEquals(b.Else, list) || b.Then.Concat(b.Else ?? []).Any(c => Contains(c, list)),
        WhileBlock b => ReferenceEquals(b.Body, list) || b.Body.Any(c => Contains(c, list)),
        RepeatBlock b => ReferenceEquals(b.Body, list) || b.Body.Any(c => Contains(c, list)),
        ForEachBlock b => ReferenceEquals(b.Body, list) || b.Body.Any(c => Contains(c, list)),
        _ => false,
    };

    // A faixa mais perto do ponteiro, na vertical, entre as que estão sob ele na horizontal.
    private Border? ZoneAt(Point point)
    {
        Border? best = null;
        var bestDistance = 18.0;
        foreach (var zone in _root.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is DropTarget))
        {
            var origin = zone.TranslatePoint(new Point(0, 0), _root);
            if (origin is not { } o)
                continue;
            if (point.X < o.X - 8 || point.X > o.X + Math.Max(zone.Bounds.Width, 240))
                continue;
            var d = Math.Abs(point.Y - (o.Y + zone.Bounds.Height / 2));
            if (d < bestDistance)
            {
                best = zone;
                bestDistance = d;
            }
        }
        return best;
    }
}
