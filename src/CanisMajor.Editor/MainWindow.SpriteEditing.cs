using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CanisEditor.Game;
using CanisEditor.Theming;
using SpriteMask = CanisMajor.SpriteMask;

namespace CanisEditor;

// O editor de sprites por dentro da IDE: desenhar pixels, quadros, animação, tamanho e máscara de
// colisão. Cada traço grava o PNG, e o Ctrl Z do sprite desfaz.
public partial class MainWindow
{
    private static readonly string[] Swatches =
    [
        "#000000", "#FFFFFF", "#7F7F7F", "#C3C3C3", "#880015", "#ED1C24", "#FF7F27", "#FFF200",
        "#22B14C", "#00A2E8", "#3F48CC", "#A349A4", "#B97A57", "#FFAEC9", "#B5E61D", "#99D9EA",
    ];

    private bool _spriteHooked;
    private string? _pixelsPath;
    private DateTime _pixelsWrite;

    private void HookSpriteEditor()
    {
        if (_spriteHooked)
            return;
        _spriteHooked = true;
        SpriteEditor.PixelsChanged += SaveSpritePixels;
        SpriteEditor.ColorPicked += color =>
        {
            SpriteEditor.Color = color;
            SpriteEditor.Tool = SpriteTool.Pencil;
            BuildInspector();
        };
        SpriteEditor.MaskDrawn += (l, t, r, b) =>
            UpdateSpriteMeta(m => m.Mask = new SpriteMask { Mode = "rect", Left = l, Top = t, Right = r, Bottom = b });
        SpriteEditor.FrameChanged += BuildInspector;
    }

    // Carrega os pixels do PNG aberto para editar. O mesmo arquivo continua com o que está na memória.
    private void LoadSpritePixels(string png, CanisMajor.SpriteMeta meta)
    {
        HookSpriteEditor();
        // Mudou por fora (num editor de imagens): recarrega, para não gravar por cima.
        var write = File.Exists(png) ? File.GetLastWriteTimeUtc(png) : default;
        if (!string.Equals(_pixelsPath, png, StringComparison.OrdinalIgnoreCase) || SpriteEditor.Pixels is null || write != _pixelsWrite || SpriteEditor.Pixels.Frames != meta.Frames)
        {
            SpritePixels? pixels = null;
            try
            {
                pixels = SpritePixels.Load(png, meta.Frames);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or ArgumentException)
            {
                pixels = null;
            }
            _pixelsPath = png;
            _pixelsWrite = write;
            SpriteEditor.Edit(pixels);
        }
        SpriteEditor.Mask = MaskRect(png, meta);
    }

    // A máscara para mostrar: o retângulo do arquivo, ou o calculado no modo automático.
    private (double, double, double, double)? MaskRect(string png, CanisMajor.SpriteMeta meta)
    {
        if (meta.Mask is not { } mask)
            return null;
        if (!string.Equals(mask.Mode, "auto", StringComparison.OrdinalIgnoreCase))
            return (mask.Left, mask.Top, mask.Right, mask.Bottom);
        if (SpriteEditor.Pixels is { } p && CanisMajor.Png.OpaqueBox(p.Width, p.Height, p.Rgba, p.Frames) is { } box)
            return (box.Left, box.Top, box.Right, box.Bottom);
        return null;
    }

    private void SaveSpritePixels()
    {
        if (!ShowsSprite || SpriteEditor.Pixels is not { } pixels)
            return;
        var png = ActiveDocument!.FullPath;
        try
        {
            pixels.Save(png);
            _pixelsWrite = File.GetLastWriteTimeUtc(png);
            var meta = GameFiles.LoadSpriteMeta(png);
            if (meta.Frames != pixels.Frames)
            {
                meta.Frames = pixels.Frames;
                GameFiles.SaveSpriteMeta(png, meta);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Não foi possível gravar o PNG.");
            return;
        }
        _spriteCache.Remove(png);
        ShowSprite();
        BuildInspector();
        RoomView.InvalidateVisual();
    }

    // Mudanças que mexem na tira inteira (quadros e tamanho), com desfazer.
    private void EditPixels(Func<SpritePixels, int?> change)
    {
        if (SpriteEditor.Pixels is not { } pixels)
            return;
        SpriteEditor.Remember();
        var frame = change(pixels);
        SpriteEditor.PixelsUpdated();
        if (frame is { } f)
            SpriteEditor.SelectFrame(f);
        SaveSpritePixels();
    }

    private void BuildSpriteTools()
    {
        var palette = Palette.Current;
        if (SpriteEditor.Pixels is not { } pixels)
        {
            AddNote("Esta imagem não dá para editar aqui (PNG entrelaçado ou de um formato raro). Use Editar imagem.");
            return;
        }

        AddHeading("Ferramentas");
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (tool, label, tip) in new[]
        {
            (SpriteTool.Origin, "✛ Origem", "Clique para escolher a origem"),
            (SpriteTool.Pencil, "✎ Lápis", "Esquerdo pinta, direito apaga"),
            (SpriteTool.Eraser, "⌫ Borracha", "Apaga para transparente"),
            (SpriteTool.Fill, "◧ Balde", "Enche a área da mesma cor"),
            (SpriteTool.Picker, "◉ Conta-gotas", "Pega a cor de um pixel"),
            (SpriteTool.Mask, "▭ Máscara", "Arraste para desenhar a máscara de colisão"),
        })
        {
            var selected = SpriteEditor.Tool == tool;
            var button = new Button
            {
                Classes = { "flat" },
                Content = label,
                FontSize = 12,
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(6),
                Background = selected ? palette.Selection : palette.Tint,
                BorderBrush = selected ? palette.Accent : Brushes.Transparent,
                BorderThickness = new Thickness(selected ? 1 : 0),
                Tag = "s.tool." + tool,
            };
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) =>
            {
                SpriteEditor.Tool = tool;
                SpriteEditor.InvalidateVisual();
                BuildInspector();
                SpriteEditor.Focus();
            };
            tools.Children.Add(button);
        }
        Inspector.Children.Add(tools);

        // Cor: o texto em hexadecimal e uma paleta rápida.
        AddTextField("Cor", SpritePixels.FormatColor(SpriteEditor.Color), "s.color", typed =>
        {
            SpriteEditor.Color = SpritePixels.ParseColor(typed);
            BuildInspector();
        });
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var hex in Swatches)
        {
            var color = SpritePixels.ParseColor(hex);
            var swatch = new Button
            {
                Classes = { "flat" },
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 3, 3),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = SpriteEditor.Color == color ? palette.Accent : palette.Muted,
                BorderThickness = new Thickness(SpriteEditor.Color == color ? 2 : 0.5),
                Tag = "s.swatch." + hex,
            };
            ToolTip.SetTip(swatch, hex);
            swatch.Click += (_, _) =>
            {
                SpriteEditor.Color = color;
                if (SpriteEditor.Tool is SpriteTool.Origin or SpriteTool.Picker or SpriteTool.Mask or SpriteTool.Eraser)
                    SpriteEditor.Tool = SpriteTool.Pencil;
                BuildInspector();
            };
            swatches.Children.Add(swatch);
        }
        Inspector.Children.Add(swatches);
        AddNote("Ctrl Z desfaz e Ctrl Y refaz no sprite. As setas trocam de quadro.");

        AddHeading("Quadros e animação");
        var frame = SpriteEditor.CurrentFrame;
        var frames = new WrapPanel { Orientation = Orientation.Horizontal };
        frames.Children.Add(SmallButton(SpriteEditor.Playing ? "⏸ Parar" : "▶ Tocar", "Toca ou para a animação", () => SpriteEditor.SetPlaying(!SpriteEditor.Playing)));
        frames.Children.Add(SmallButton("+ Quadro", "Quadro vazio depois do atual", () => EditPixels(p => p.AddFrame(frame, copy: false))));
        frames.Children.Add(SmallButton("Duplicar", "Copia o quadro atual logo depois dele", () => EditPixels(p => p.AddFrame(frame, copy: true))));
        frames.Children.Add(SmallButton("◀", "Move o quadro para a esquerda", () => EditPixels(p => p.MoveFrame(frame, -1))));
        frames.Children.Add(SmallButton("▶", "Move o quadro para a direita", () => EditPixels(p => p.MoveFrame(frame, +1))));
        var delete = SmallButton("Excluir quadro", "Exclui o quadro atual", () => EditPixels(p => p.DeleteFrame(frame) ? Math.Max(0, frame - 1) : null));
        delete.IsEnabled = pixels.Frames > 1;
        frames.Children.Add(delete);
        Inspector.Children.Add(frames);
        AddCheck("Casca de cebola (quadro anterior clarinho)", SpriteEditor.Onion, "s.onion", on =>
        {
            SpriteEditor.Onion = on;
            SpriteEditor.InvalidateVisual();
        });

        AddHeading("Tamanho do quadro");
        AddNumberField("Largura", pixels.FrameWidth, "s.fw", value => EditPixels(p => { p.Resize((int)value, p.Height); return null; }));
        AddNumberField("Altura", pixels.Height, "s.fh", value => EditPixels(p => { p.Resize(p.FrameWidth, (int)value); return null; }));
        AddNote("Mudar o tamanho mantém o canto de cima à esquerda: corta a sobra ou acrescenta transparente.");
    }

    private void BuildMaskFields(string png, CanisMajor.SpriteMeta meta)
    {
        AddHeading("Máscara de colisão");
        var modes = new List<string> { "Quadro inteiro", "Automática", "Retângulo" };
        var current = meta.Mask is null ? modes[0] : string.Equals(meta.Mask.Mode, "auto", StringComparison.OrdinalIgnoreCase) ? modes[1] : modes[2];
        AddChoice("Tipo", modes, current, "s.mask", chosen => UpdateSpriteMeta(m => m.Mask = chosen switch
        {
            "Automática" => new SpriteMask { Mode = "auto" },
            "Retângulo" => m.Mask is { Mode: "rect" } keep ? keep : new SpriteMask
            {
                Mode = "rect",
                Right = (float)SpriteEditor.FrameWidth,
                Bottom = (float)SpriteEditor.FrameHeight,
            },
            _ => null,
        }));
        if (meta.Mask is { Mode: "rect" } rect)
        {
            AddNumberField("Esquerda", rect.Left, "s.ml", v => UpdateSpriteMeta(m => m.Mask!.Left = (float)v));
            AddNumberField("Topo", rect.Top, "s.mt", v => UpdateSpriteMeta(m => m.Mask!.Top = (float)v));
            AddNumberField("Direita", rect.Right, "s.mr", v => UpdateSpriteMeta(m => m.Mask!.Right = (float)v));
            AddNumberField("Base", rect.Bottom, "s.mb", v => UpdateSpriteMeta(m => m.Mask!.Bottom = (float)v));
        }
        AddNote(meta.Mask is null ? "A colisão usa o quadro inteiro."
            : string.Equals(meta.Mask.Mode, "auto", StringComparison.OrdinalIgnoreCase) ? "A colisão usa só os pixels visíveis de todos os quadros (o verde)."
            : "Arraste com a ferramenta Máscara ou digite os números. O verde é o que colide.");
        _ = png;
    }
}
