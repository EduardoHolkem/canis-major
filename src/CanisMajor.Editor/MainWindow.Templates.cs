using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CanisEditor.Game;
using CanisEditor.Theming;

namespace CanisEditor;

// A janela de modelos: escolher o tipo de recurso e um modelo pronto, como no GameMaker.
public partial class MainWindow
{
    private string _templateKind = "object";

    public bool IsTemplatePickerOpen => TemplateOverlay.IsVisible;

    public string TemplateKind => _templateKind;

    private void OnNewRoom(object? sender, RoutedEventArgs e) => ShowTemplatePicker("room");
    private void OnNewObject(object? sender, RoutedEventArgs e) => ShowTemplatePicker("object");
    private void OnNewSprite(object? sender, RoutedEventArgs e) => ShowTemplatePicker("sprite");
    private void OnNewShader(object? sender, RoutedEventArgs e) => ShowTemplatePicker("shader");
    private void OnNewScript(object? sender, RoutedEventArgs e) => ShowTemplatePicker("script");
    private void OnNewTileset(object? sender, RoutedEventArgs e) => ShowTemplatePicker("tileset");

    public void ShowTemplatePicker(string kind)
    {
        if (!_isGame)
            return;
        _templateKind = kind;
        BuildTemplateKinds();
        BuildTemplateCards();
        TemplateOverlay.IsVisible = true;
        Dispatcher.UIThread.Post(() => TemplateOverlay.Focus());
    }

    public void CloseTemplatePicker() => TemplateOverlay.IsVisible = false;

    private void OnTemplateCancel(object? sender, RoutedEventArgs e) => CloseTemplatePicker();

    private void OnTemplateVeil(object? sender, PointerPressedEventArgs e) => CloseTemplatePicker();

    private void OnTemplateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        CloseTemplatePicker();
    }

    // Os tipos, à esquerda. O escolhido fica destacado.
    private void BuildTemplateKinds()
    {
        TemplateKinds.Children.Clear();
        foreach (var (kind, title, color, glyph) in ResourceTemplates.Kinds)
        {
            var selected = kind == _templateKind;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Badge(glyph, color, 22));
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal });
            var count = ResourceTemplates.For(kind).Count();
            row.Children.Add(new TextBlock { Text = count.ToString(), Foreground = Palette.Current.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = row,
                Classes = { selected ? "secondary" : "flat" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6),
                Tag = "tk." + kind,
            };
            button.Click += (_, _) =>
            {
                _templateKind = kind;
                BuildTemplateKinds();
                BuildTemplateCards();
            };
            TemplateKinds.Children.Add(button);
        }
    }

    // Um cartão por modelo: a prévia, o nome e o que ele faz.
    private void BuildTemplateCards()
    {
        TemplateCards.Children.Clear();
        var palette = Palette.Current;
        var (_, _, color, glyph) = ResourceTemplates.Kinds.First(k => k.Kind == _templateKind);
        // Os scripts aparecem em lista: uma linha por modelo, com o nome e o que ele traz.
        if (_templateKind == "script")
        {
            foreach (var template in ResourceTemplates.For(_templateKind))
                TemplateCards.Children.Add(TemplateRow(template, glyph, color));
            return;
        }
        foreach (var template in ResourceTemplates.For(_templateKind))
        {
            Control preview = template.Size is { } size ? SizePreview(size.Width, size.Height)
                : template.AskSize ? SizePreview(0, 0)
                : Badge(glyph, color, 40);
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(preview);
            body.Children.Add(new TextBlock { Text = template.Title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = template.Description, Foreground = palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis });
            var card = new Button
            {
                Content = body,
                Classes = { "flat" },
                Width = 204, // três por linha na janela de 860
                Height = 168,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Top,
                Background = palette.Tint,
                CornerRadius = new CornerRadius(10),
                Tag = $"tpl.{template.Kind}.{template.Id}",
            };
            ToolTip.SetTip(card, template.Description);
            card.Click += (_, _) =>
            {
                CloseTemplatePicker();
                _ = CreateFromTemplateAsync(template);
            };
            TemplateCards.Children.Add(card);
        }
    }

    private static Border Badge(string glyph, string color, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 4),
        Background = new SolidColorBrush(Color.Parse(color)),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock
        {
            Text = glyph,
            Foreground = Brushes.White,
            FontSize = size * 0.45,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // Um quadro vazio com o tamanho escrito, crescendo com o sprite (16, 32, 64). Zero é o personalizado.
    private static Control SizePreview(int width, int height)
    {
        var palette = Palette.Current;
        var side = width == 0 ? 44 : Math.Clamp(16 + width / 2.0, 20, 52);
        return new Border
        {
            Width = 56,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Border
            {
                Width = side,
                Height = width == 0 ? 30 : side * height / Math.Max(1, width),
                BorderBrush = palette.Muted,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // O tamanho já está no título; dentro da caixa, só o "L × A" do personalizado.
                Child = width != 0 ? null : new TextBlock
                {
                    Text = "L × A",
                    FontSize = 10,
                    Foreground = palette.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
    }

    // Uma linha da lista de scripts.
    private Button TemplateRow(ResourceTemplate template, string glyph, string color)
    {
        var palette = Palette.Current;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(Badge(glyph, color, 28));
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = template.Title, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        text.Children.Add(new TextBlock { Text = template.Description, Foreground = palette.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var row = new Button
        {
            Content = grid,
            Classes = { "flat" },
            Width = 620,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = palette.Tint,
            CornerRadius = new CornerRadius(8),
            Tag = $"tpl.{template.Kind}.{template.Id}",
        };
        row.Click += (_, _) =>
        {
            CloseTemplatePicker();
            _ = CreateFromTemplateAsync(template);
        };
        return row;
    }

    // Pergunta o nome e cria os arquivos do modelo. Também serve aos testes, sem passar pela janela.
    public async Task CreateFromTemplateAsync(ResourceTemplate template)
    {
        if (!_isGame || RootFolder is not { } root)
            return;
        var kind = template.Kind;
        var suggestion = template.Stem;
        for (var n = 2; GameFiles.CheckName(root, kind, suggestion) is not null && n < 100; n++)
            suggestion = template.Stem + n;
        var section = GameFiles.Section(kind);
        var name = await AskTextAsync($"{template.Title}", $"{template.Description}\n\nO nome do {section.Singular}, em {section.Folder}/.", "Criar",
            text => GameFiles.CheckName(root, kind, text.Trim()), suggestion);
        if (name is null || RootFolder != root)
            return;
        (int Width, int Height)? size = null;
        if (template.AskSize)
        {
            var typed = await AskTextAsync("Tamanho do sprite", "Largura e altura em pixels, como 32x32, 16x24 ou 128x32 (até 2048).", "Criar",
                text => ResourceTemplates.ParseSize(text) is null ? "Escreva a largura e a altura, como 32x32." : null, "32x32");
            if (typed is null || RootFolder != root)
                return;
            size = ResourceTemplates.ParseSize(typed);
        }
        string path;
        try
        {
            path = size is { } s && template.CreateSized is { } sized ? sized(root, name.Trim(), s.Width, s.Height) : template.Create(root, name.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Não foi possível criar: {e.Message}");
            return;
        }
        _gameView = true;
        _closedGameSections.Remove(GameFiles.FolderOf(root, kind));
        UpdateTreeHeader();
        ReloadGameObjects();
        RefreshTree();
        ReindexProject();
        OpenFile(path);
    }

    // O + de cada tipo abre a janela de modelos nele.
    public Task CreateResourceAsync(string kind)
    {
        ShowTemplatePicker(kind);
        return Task.CompletedTask;
    }
}
