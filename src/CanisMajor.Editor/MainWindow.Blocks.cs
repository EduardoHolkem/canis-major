using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CanisEditor.Game;
using CanisEditor.Theming;

namespace CanisEditor;

// C# Código e C# Blocos: dois jeitos de escrever o mesmo script. O arquivo .cs é sempre a verdade;
// os blocos são lidos dele e, a cada mudança, escritos de volta nele.
public partial class MainWindow
{
    private readonly HashSet<string> _blocksView = new(StringComparer.OrdinalIgnoreCase);
    private bool _blocksHooked;
    private bool _writingBlocks;

    private bool IsScriptDocument => IsObjectDocument && !IsObjectJson(ActiveDocument!.FullPath);

    public bool ShowsBlocks => IsScriptDocument && _blocksView.Contains(ActiveDocument!.FullPath) && !ShowsObject;

    public BlockView BlockViewControl => BlockEditor;

    // O texto da aba aberta, para os testes conferirem o C# escrito pelos blocos.
    public string ActiveText => CodeEditor.Document.Text;

    private void OnBlocksToggle(object? sender, RoutedEventArgs e) => ToggleBlocks();

    public void ToggleBlocks()
    {
        if (!IsScriptDocument)
            return;
        var path = ActiveDocument!.FullPath;
        if (!_blocksView.Remove(path))
        {
            _blocksView.Add(path);
            _roomAsCode.Remove(path); // sai da ficha, se estava nela
        }
        ShowDocumentSurface();
        FocusActiveDocument();
    }

    private void UpdateBlocksSurface()
    {
        BlocksToggle.IsVisible = IsScriptDocument;
        var blocks = ShowsBlocks;
        BlocksToggle.Content = blocks ? "C# Código" : "C# Blocos";
        BlockEditor.IsVisible = blocks;
        if (!blocks)
            return;
        if (!_blocksHooked)
        {
            _blocksHooked = true;
            BlockEditor.Changed += WriteBlocks;
        }
        LoadBlocks();
    }

    // Lê os blocos do texto da aba.
    private void LoadBlocks()
    {
        var text = CodeEditor.Document.Text;
        var errors = BlockConverter.SyntaxErrors(text);
        if (errors.Count > 0)
        {
            BlockEditor.Show(null, "O C# tem um erro, e os blocos só aparecem com o código certo. Volte para C# Código e corrija: " + errors[0]);
            return;
        }
        BlockEditor.Show(BlockConverter.Parse(text));
    }

    // Os blocos mudaram: o C# da aba é reescrito (com desfazer, como se tivesse digitado).
    private void WriteBlocks()
    {
        if (BlockEditor.Script is not { } script || ActiveDocument is null)
            return;
        var original = CodeEditor.Document.Text;
        string code;
        try
        {
            code = BlockConverter.Generate(original, script);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        if (code == original)
            return;
        _writingBlocks = true;
        try
        {
            CodeEditor.Document.Text = code;
        }
        finally
        {
            _writingBlocks = false;
        }
    }

    // A paleta no Inspetor: clique põe o bloco no lugar escolhido; arraste solta onde quiser.
    private void BuildBlocksPalette()
    {
        AddHeading("Blocos", first: true);
        AddNote("Clique num bloco para pôr depois do escolhido (ou no começo do evento clicado), ou arraste para o lugar certo. Os valores são C#: dá para escrever speed * dt, Keys.Held(\"left\") ou \"texto\".");
        foreach (var (category, color, items) in PaletteItems())
        {
            AddHeading(category);
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, tip, make) in items)
            {
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.Parse(color)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 4, 4),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 12 },
                    Tag = "bp." + label,
                };
                ToolTip.SetTip(chip, tip);
                chip.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
                        return;
                    BlockEditor.BeginDragNew(make(), e);
                    e.Handled = true;
                };
                wrap.Children.Add(chip);
            }
            Inspector.Children.Add(wrap);
        }
        AddNote("O que não tiver bloco próprio continua como um bloco C#, com o texto dentro: nada se perde ao trocar de modo.");
    }

    // Para os testes: o mesmo que clicar num bloco da paleta.
    public void InsertPaletteBlock(string label)
    {
        foreach (var (_, _, items) in PaletteItems())
            foreach (var (name, _, make) in items)
                if (name == label)
                {
                    BlockEditor.Insert(make());
                    return;
                }
    }

    private static IEnumerable<(string Category, string Color, (string Label, string Tip, Func<Block> Make)[] Items)> PaletteItems() =>
    [
        ("Controle", "#D9822B",
        [
            ("se", "Faz o que está dentro só quando a condição é verdade", () => new IfBlock { Condition = "Keys.Pressed(\"space\")" }),
            ("se / senão", "Uma coisa quando é verdade, outra quando não é", () => new IfBlock { Condition = "Keys.Held(\"left\")", Else = new() }),
            ("repetir", "Repete um número de vezes", () => new RepeatBlock { Count = "3" }),
            ("enquanto", "Repete enquanto a condição for verdade", () => new WhileBlock { Condition = "false" }),
            ("para cada", "Faz algo com cada objeto de um tipo", () => new ForEachBlock { Var = "e", Collection = "Game.Instances<Estrela>()" }),
            ("sair do evento", "Para o evento aqui", () => new ReturnBlock()),
        ]),
        ("Variáveis", "#C65A3A",
        [
            ("nova variável", "Guarda um valor para usar depois", () => new VarBlock { Name = "valor", Value = "0" }),
            ("mudar valor", "Troca o valor (X, Y, Alpha, uma variável...)", () => new SetBlock { Target = "X", Op = "=", Value = "100" }),
            ("somar", "Soma ao valor (+=)", () => new SetBlock { Target = "X", Op = "+=", Value = "60 * dt" }),
        ]),
        ("Movimento e objetos", "#3A9A6A",
        [
            ("mover", "Anda, parando nos sólidos", () => new CallBlock { Method = "Move", Args = ["Keys.Axis(\"left\", \"right\") * 100 * dt", "Keys.Axis(\"up\", \"down\") * 100 * dt", "solid: \"Parede\""] }),
            ("destruir", "Tira este objeto do jogo", () => new CallBlock { Method = "Destroy" }),
            ("criar objeto", "Cria outro objeto", () => new CallBlock { Method = "Game.Spawn", Args = ["\"Estrela\"", "X", "Y"] }),
            ("câmera segue", "A câmera acompanha este objeto", () => new CallBlock { Method = "Game.Follow", Args = ["this", "smooth: 0.2f"] }),
            ("ao encostar", "Se encostar num objeto do tipo, faz algo", () => new IfBlock { Condition = "Collide(\"Estrela\") is { } outro", Then = [new CallBlock { Method = "outro.Destroy" }] }),
        ]),
        ("Desenho, som e salas", "#3F7FC4",
        [
            ("desenhar sprite", "Desenha o próprio sprite (use no evento Ao desenhar)", () => new CallBlock { Method = "DrawSelf" }),
            ("escrever texto", "Escreve um texto na tela", () => new CallBlock { Method = "Draw.Text", Args = ["\"Olá\"", "X", "Y - 16", "size: 10"] }),
            ("tocar som", "Toca um som de sounds/", () => new CallBlock { Method = "Game.PlaySound", Args = ["\"coleta\""] }),
            ("ir para sala", "Troca de sala", () => new CallBlock { Method = "Game.Goto", Args = ["\"inicio\""] }),
            ("reiniciar sala", "Começa a sala de novo", () => new CallBlock { Method = "Game.Restart" }),
        ]),
        ("IA e luz", "#2E9AA0",
        [
            ("perseguir o jogador", "Usa o componente AI", () => new CallBlock { Method = "Get<AI>()!.Chase", Args = ["Game.First(\"Jogador\")!"] }),
            ("andar até", "Usa o componente AI", () => new CallBlock { Method = "Get<AI>()!.GoTo", Args = ["200", "120"] }),
            ("luz da sala", "Troca a iluminação: dia, noite, caverna...", () => new CallBlock { Method = "Lighting.Preset", Args = ["\"noite\""] }),
        ]),
        ("Outros", "#6B7280",
        [
            ("comentário", "Uma anotação que não faz nada", () => new CommentBlock { Text = "explique aqui" }),
            ("código C#", "Qualquer comando em C#", () => new CodeBlock { Code = "Game.Data[\"pontos\"] = 0;" }),
        ]),
    ];
}
