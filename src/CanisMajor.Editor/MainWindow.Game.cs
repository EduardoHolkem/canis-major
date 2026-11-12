using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CanisEditor.Editing;
using CanisEditor.Game;
using CanisEditor.Lsp;
using CanisEditor.Theming;

namespace CanisEditor;

// O que faz do editor um editor de jogos: a árvore de recursos, a sala desenhada com o Inspetor,
// o sprite com a origem, a conferência dos shaders e a criação de jogos e recursos.
public partial class MainWindow
{
    private static readonly TimeSpan ShaderDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan SpriteRecheck = TimeSpan.FromSeconds(1);

    private sealed class CachedSprite
    {
        public DateTime PngWrite { get; init; }
        public DateTime MetaWrite { get; init; }
        public SpriteImage? Image { get; init; }
        public DateTime CheckedAt { get; set; }
    }

    private readonly HashSet<string> _roomAsCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _closedGameSections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedSprite> _spriteCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, GameObjectInfo> _gameObjects = new(StringComparer.OrdinalIgnoreCase);
    private bool _isGame;
    private bool _gameView = true;
    private OpenDocument? _roomDocument;
    private RoomModel? _room;
    private bool _writingRoom;
    private DispatcherTimer? _shaderDelay;
    private int _shaderVersion;

    public bool IsGameProject => _isGame;
    public bool IsGameView => _isGame && _gameView;
    public bool IsRoomShown => GameHost.IsVisible && RoomView.IsVisible;
    public bool IsSpriteShown => GameHost.IsVisible && SpriteEditor.IsVisible;
    public RoomCanvas RoomCanvasControl => RoomView;
    public SpriteView SpriteViewControl => SpriteEditor;
    public RoomModel? CurrentRoom => _room;
    public string GameMessageText => GameMessage.IsVisible ? GameMessage.Text ?? "" : "";
    public string? RoomBrush => RoomView.Brush;
    public IReadOnlyList<Control> InspectorControls => Inspector.Children;

    // Termina quando a conferência do shader pedida por último chega. Os testes esperam por ela.
    public Task ShaderChecked { get; private set; } = Task.CompletedTask;

    // Os testes trocam a janela de escolher pasta por uma resposta pronta.
    public Func<Task<string?>> PickParentFolder { get; set; } = null!;

    private void InitializeGame()
    {
        PickParentFolder = PickParentFolderAsync;
        RoomView.SpriteFor = LoadSprite;
        RoomView.TilesetFor = LoadTileset;
        RoomView.Changed += WriteRoom;
        RoomView.SelectionChanged += BuildInspector;
        RoomView.UndoRequested += UndoRoom;
        SpriteEditor.OriginPicked += (x, y) => UpdateSpriteMeta(meta => meta.Origin = [(float)x, (float)y]);
        _shaderDelay = new DispatcherTimer(ShaderDelay, DispatcherPriority.Background, (_, _) =>
        {
            _shaderDelay!.Stop();
            CheckActiveShader();
        });
    }

    // Projeto

    // Chamado ao abrir uma pasta, antes de montar a árvore.
    private void DetectGameProject()
    {
        _isGame = GameFiles.IsGame(RootFolder);
        _gameView = true;
        _roomAsCode.Clear();
        _closedGameSections.Clear();
        _spriteCache.Clear();
        RoomView.Brush = null;
        UpdateTreeHeader();
    }

    private void UpdateTreeHeader()
    {
        TreeModeSwitch.IsVisible = _isGame;
        TreeTitle.IsVisible = !_isGame;
        GameViewButton.Classes.Set("on", IsGameView);
        FilesViewButton.Classes.Set("on", _isGame && !_gameView);
        NewResourceButton.IsVisible = IsGameView;
        NewFileButton.IsVisible = !IsGameView;
        NewFolderButton.IsVisible = !IsGameView;
    }

    private void OnShowGameView(object? sender, RoutedEventArgs e) => SetGameView(true);

    private void OnShowFilesView(object? sender, RoutedEventArgs e) => SetGameView(false);

    public void SetGameView(bool game)
    {
        _gameView = game;
        UpdateTreeHeader();
        RefreshTree();
    }

    // A árvore do jogo: um grupo por tipo de recurso, aberto de começo. O grupo aponta para a pasta dele.
    private void AddGameTree(List<TreeRow> rows)
    {
        var palette = Palette.Current;
        foreach (var section in GameFiles.Sections)
        {
            var folder = Path.Combine(RootFolder!, section.Folder);
            var open = !_closedGameSections.Contains(folder);
            var resources = GameFiles.List(RootFolder!, section.Kind);
            var startRoom = section.Kind == "room" ? StartRoomName() : null;
            rows.Add(new TreeRow(
                section.Title, folder, true, new Thickness(6, 0, 0, 0),
                open ? ChevronOpen : ChevronClosed, FontWeight.Medium, Brushes.Transparent, palette.Muted,
                false, resources.Count > 0 ? resources.Count.ToString(CultureInfo.InvariantCulture) : "", palette.Faint,
                FileIcons.ForFolder(section.Folder, open)));
            if (!open)
                continue;
            foreach (var resource in resources)
            {
                var active = string.Equals(ActiveDocument?.FullPath, resource.Path, StringComparison.OrdinalIgnoreCase);
                var document = _documents.Find(d => string.Equals(d.FullPath, resource.Path, StringComparison.OrdinalIgnoreCase));
                var gitLetter = "";
                IBrush gitBrush = Brushes.Transparent;
                if (_gitMarks.TryGetValue(resource.Path, out var mark))
                {
                    gitLetter = mark.Letter;
                    gitBrush = BrushOf(mark.Kind);
                }
                rows.Add(new TreeRow(
                    string.Equals(resource.Name, startRoom, StringComparison.OrdinalIgnoreCase) ? resource.Name + "  ★" : resource.Name, resource.Path, false, new Thickness(18, 0, 0, 0), null,
                    active ? FontWeight.SemiBold : FontWeight.Normal,
                    active ? palette.Tint : Brushes.Transparent,
                    gitLetter.Length > 0 ? gitBrush : palette.Ink,
                    document?.IsDirty ?? false, gitLetter, gitBrush,
                    FileIcons.For(Path.GetFileName(resource.Path))));
            }
        }
    }

    // Abrir e fechar um grupo da árvore do jogo. Devolve falso se a linha não for um grupo.
    private bool ToggleGameSection(string folder)
    {
        if (!IsGameView || !GameFiles.Sections.Any(s => string.Equals(Path.Combine(RootFolder!, s.Folder), folder, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!_closedGameSections.Remove(folder))
            _closedGameSections.Add(folder);
        return true;
    }

    // Superfície: sala e sprite

    private bool IsRoomDocument => ActiveDocument is { IsImage: false } document && !IsDiffOpen && GameFiles.IsRoom(RootFolder, document.FullPath);

    private bool ShowsRoom => IsRoomDocument && !_roomAsCode.Contains(ActiveDocument!.FullPath);

    private bool IsObjectDocument => ActiveDocument is { IsImage: false } document && !IsDiffOpen && GameFiles.KindOf(RootFolder, document.FullPath) == "object";

    // O script (.cs) abre no código, e o botão mostra a ficha. A ficha sozinha (.json) já abre como ficha.
    private bool ShowsObject => IsObjectDocument && (IsObjectJson(ActiveDocument!.FullPath) ? !_roomAsCode.Contains(ActiveDocument.FullPath) : _roomAsCode.Contains(ActiveDocument.FullPath));

    private bool IsTilesetDocument => ActiveDocument is { IsImage: false } document && !IsDiffOpen && GameFiles.KindOf(RootFolder, document.FullPath) == "tileset";

    private bool ShowsTileset => IsTilesetDocument && !_roomAsCode.Contains(ActiveDocument!.FullPath);

    private static bool IsObjectJson(string path) => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private bool ShowsSprite => ActiveDocument is { IsImage: true } document && !IsDiffOpen && GameFiles.IsSprite(RootFolder, document.FullPath);

    // Chamado sempre que a aba ou a superfície muda.
    private void UpdateGameSurface()
    {
        var room = ShowsRoom;
        var sprite = ShowsSprite;
        var objectForm = ShowsObject;
        var tileset = ShowsTileset;
        RoomCodeToggle.IsVisible = IsRoomDocument || IsObjectDocument || IsTilesetDocument;
        RoomCodeToggle.Content = IsTilesetDocument ? (tileset ? "JSON" : "Tileset") : IsObjectDocument ? (objectForm ? (IsObjectJson(ActiveDocument!.FullPath) ? "JSON" : "Script") : "Objeto") : room ? "Código" : "Sala";
        GameHost.IsVisible = room || sprite || objectForm || tileset || ShowsBlocks;
        UpdateBlocksSurface();
        TilesetHost.IsVisible = tileset;
        if (tileset)
            ShowTileset();
        ObjectView.IsVisible = objectForm;
        RoomHint.IsVisible = room;
        SpriteEditor.IsVisible = sprite;
        GameMessage.IsVisible = false;

        if (room)
        {
            LoadRoomFromDocument();
        }
        else
        {
            _roomDocument = null;
            _room = null;
            RoomView.IsVisible = false;
            RoomView.Model = null;
        }

        if (sprite)
            ShowSprite();
        else
            SpriteEditor.Show(null, 1, 0, 0, 0);

        BuildInspector();
        if (ActiveDocument is { } document && IsShader(document.FullPath))
            CheckActiveShader();
    }

    private void OnRoomCodeToggle(object? sender, RoutedEventArgs e) => ToggleRoomCode();

    public void ToggleRoomCode()
    {
        if (!IsRoomDocument && !IsObjectDocument && !IsTilesetDocument)
            return;
        var path = ActiveDocument!.FullPath;
        if (!_roomAsCode.Remove(path))
            _roomAsCode.Add(path);
        ShowDocumentSurface();
        FocusActiveDocument();
    }

    private void LoadRoomFromDocument()
    {
        var document = ActiveDocument!;
        var newDocument = _roomDocument != document;
        _roomDocument = document;
        if (newDocument)
        {
            ReloadGameObjects();
            var info = GameFiles.Info(RootFolder!);
            RoomView.View = new Size(info.Width, info.Height);
            RoomView.Model = null;
        }

        try
        {
            _room = RoomModel.Parse(document.Document.Text);
            RoomView.Model = _room;
            RoomView.IsVisible = true;
            RoomHint.IsVisible = true;
            GameMessage.IsVisible = false;
        }
        catch (JsonException e)
        {
            _room = null;
            RoomView.Model = null;
            RoomView.IsVisible = false;
            RoomHint.IsVisible = false;
            GameMessage.Text = $"O texto desta sala tem um erro e ela não pode ser desenhada.\n{e.Message}\n\nUse o botão Código, no alto, para corrigir.";
            GameMessage.IsVisible = true;
        }
    }

    private void ReloadGameObjects()
    {
        _gameObjects = RootFolder is null ? new(StringComparer.OrdinalIgnoreCase) : GameFiles.Objects(RootFolder);
        RoomView.Objects = _gameObjects;
        if (RoomView.Brush is { } brush && !_gameObjects.ContainsKey(brush))
            RoomView.Brush = null;
    }

    // O texto do documento mudou: pelo desfazer, pelo Código ou porque o arquivo mudou no disco.
    private void OnGameDocumentChanged(OpenDocument document)
    {
        if (document != ActiveDocument)
            return;
        if (!_writingRoom && document == _roomDocument && ShowsRoom)
        {
            LoadRoomFromDocument();
            BuildInspector();
        }
        // O texto mudou por fora dos blocos (desfazer, disco): os blocos acompanham.
        if (!_writingBlocks && ShowsBlocks)
            LoadBlocks();
        if (IsShader(document.FullPath))
        {
            _shaderDelay?.Stop();
            _shaderDelay?.Start();
        }
    }

    // A sala desenhada mudou: o texto novo vai para o documento, numa única troca que o Ctrl Z desfaz.
    private void WriteRoom()
    {
        if (_room is null || _roomDocument is null)
            return;
        _writingRoom = true;
        try
        {
            _roomDocument.SetTextKeepingPosition(_room.Serialize());
        }
        finally
        {
            _writingRoom = false;
        }
        BuildInspector();
    }

    private void UndoRoom(bool undo)
    {
        if (_roomDocument?.Document.UndoStack is not { } stack)
            return;
        if (undo && stack.CanUndo)
            stack.Undo();
        else if (!undo && stack.CanRedo)
            stack.Redo();
    }

    // O sprite pelo nome, lido do PNG e do JSON ao lado. Confere o disco no máximo uma vez por segundo.
    private SpriteImage? LoadSprite(string name)
    {
        if (RootFolder is null)
            return null;
        var png = Path.Combine(GameFiles.FolderOf(RootFolder, "sprite"), name + ".png");
        var now = DateTime.UtcNow;
        if (_spriteCache.TryGetValue(png, out var cached) && now - cached.CheckedAt < SpriteRecheck)
            return cached.Image;

        var meta = Path.ChangeExtension(png, ".json");
        var pngWrite = File.Exists(png) ? File.GetLastWriteTimeUtc(png) : default;
        var metaWrite = File.Exists(meta) ? File.GetLastWriteTimeUtc(meta) : default;
        if (cached is not null && cached.PngWrite == pngWrite && cached.MetaWrite == metaWrite)
        {
            cached.CheckedAt = now;
            return cached.Image;
        }

        SpriteImage? image = null;
        if (pngWrite != default)
        {
            try
            {
                var info = GameFiles.LoadSpriteMeta(png);
                var data = File.ReadAllBytes(png);
                var bitmap = new Bitmap(new MemoryStream(data));
                var (width, height) = GameFiles.PngSize(data) ?? (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                image = new SpriteImage(bitmap, width, height, info.Frames, info.Origin[0], info.Origin[1]);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                image = null;
            }
        }
        _spriteCache[png] = new CachedSprite { PngWrite = pngWrite, MetaWrite = metaWrite, Image = image, CheckedAt = now };
        return image;
    }

    private void ShowSprite()
    {
        var png = ActiveDocument!.FullPath;
        _spriteCache.Remove(png);
        var image = LoadSprite(Path.GetFileNameWithoutExtension(png));
        var meta = GameFiles.LoadSpriteMeta(png);
        SpriteEditor.Show(image, meta.Frames, meta.Origin[0], meta.Origin[1], meta.Fps);
        LoadSpritePixels(png, meta);
        GameMessage.Text = "Não foi possível abrir esta imagem.";
        GameMessage.IsVisible = image is null;
    }

    public void SetSpriteOrigin(double x, double y) => UpdateSpriteMeta(meta => meta.Origin = [(float)x, (float)y]);

    public void SetSpriteFrames(int frames) => UpdateSpriteMeta(meta => meta.Frames = Math.Max(1, frames));

    private void UpdateSpriteMeta(Action<CanisMajor.SpriteMeta> change)
    {
        if (!ShowsSprite)
            return;
        var png = ActiveDocument!.FullPath;
        var meta = GameFiles.LoadSpriteMeta(png);
        change(meta);
        meta.Frames = Math.Max(1, meta.Frames);
        meta.Fps = Math.Max(0, meta.Fps);
        try
        {
            GameFiles.SaveSpriteMeta(png, meta);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Não foi possível gravar os dados do sprite.");
            return;
        }
        ShowSprite();
        BuildInspector();
    }

    // Inspetor

    private void BuildInspector()
    {
        // Ao trocar de campo com Tab, o campo novo continua com o foco depois de refazer o painel.
        var focusedTag = (FocusManager?.GetFocusedElement() as Control)?.Tag as string;
        Inspector.Children.Clear();
        if (ShowsRoom && _room is not null)
            BuildRoomInspector(_room);
        else if (ShowsSprite)
            BuildSpriteInspector();
        else if (ShowsObject)
            BuildObjectInspector();
        else if (ShowsTileset)
            BuildTilesetInspector();
        else if (ShowsBlocks)
            BuildBlocksPalette();

        if (focusedTag is not null && FindInspectorControl(focusedTag) is { } again)
            Dispatcher.UIThread.Post(() => again.Focus());
    }

    private Control? FindInspectorControl(string tag)
    {
        foreach (var child in Inspector.Children)
        {
            if (Find(child) is { } found)
                return found;
        }
        return null;

        Control? Find(Control control)
        {
            if (control.Tag as string == tag && control.Focusable)
                return control;
            if (control is Panel panel)
            {
                foreach (var item in panel.Children)
                {
                    if (Find(item) is { } inner)
                        return inner;
                }
            }
            return null;
        }
    }

    private void BuildRoomInspector(RoomModel room)
    {
        AddHeading("Pincel", first: true);
        AddBrushPalette();
        BuildLayersSection(room);

        if (RoomView.Selected is { } instance)
        {
            AddHeading("Instância");
            var classes = _gameObjects.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (!classes.Contains(instance.Object, StringComparer.OrdinalIgnoreCase))
                classes.Insert(0, instance.Object);
            var picker = new ComboBox { ItemsSource = classes, SelectedItem = classes.First(c => string.Equals(c, instance.Object, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Tag = "i.object" };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is string chosen && chosen != instance.Object)
                {
                    instance.Object = chosen;
                    RoomView.InvalidateVisual();
                    WriteRoom();
                }
            };
            AddRow("Objeto", picker);
            AddNumberField("X", instance.X, "i.x", value => { instance.X = value; RoomView.InvalidateVisual(); WriteRoom(); });
            AddNumberField("Y", instance.Y, "i.y", value => { instance.Y = value; RoomView.InvalidateVisual(); WriteRoom(); });
            var layerNames = room.EffectiveLayers.Where(l => l.IsInstances).Select(l => l.Name).ToList();
            if (layerNames.Count > 1)
            {
                var current = room.LayerOf(instance)?.Name ?? layerNames[0];
                var layerPicker = new ComboBox { ItemsSource = layerNames, SelectedItem = layerNames.FirstOrDefault(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Tag = "i.layer" };
                layerPicker.SelectionChanged += (_, _) =>
                {
                    if (layerPicker.SelectedItem is string chosen && !string.Equals(chosen, current, StringComparison.OrdinalIgnoreCase))
                    {
                        instance.Layer = string.Equals(chosen, CanisMajor.RoomLayers.DefaultInstanceLayer(room.EffectiveLayers)?.Name, StringComparison.OrdinalIgnoreCase) ? null : chosen;
                        RoomView.InvalidateVisual();
                        WriteRoom();
                        BuildInspector();
                    }
                };
                AddRow("Camada", layerPicker);
            }
            foreach (var (key, value) in instance.Extra.Where(p => p.Key != "layer").ToList())
            {
                var text = value?.ToJsonString() ?? "null";
                AddTextField(key, text, "i.extra." + key, typed =>
                {
                    if (string.IsNullOrWhiteSpace(typed))
                        instance.Extra.Remove(key);
                    else
                        instance.Extra[key] = ParseValue(typed);
                    WriteRoom();
                });
            }
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            actions.Children.Add(SmallButton("Propriedade", "Uma propriedade a mais, como speed: 2. O jogo põe o valor no campo de mesmo nome do objeto.", () => _ = AddInstancePropertyAsync(instance)));
            actions.Children.Add(SmallButton("Duplicar", "Ctrl D", RoomView.DuplicateSelected));
            actions.Children.Add(SmallButton("Excluir", "Delete", () => RoomView.DeleteAt(RoomView.SelectedIndex)));
            if (_gameObjects.TryGetValue(instance.Object, out var info))
                actions.Children.Add(SmallButton("Abrir código", info.Path, () => OpenFile(info.Path)));
            Inspector.Children.Add(actions);
        }

        AddHeading("Sala");
        if (ActiveDocument is { } roomDocument)
            BuildRoomActions(roomDocument.FullPath);
        AddNumberField("Largura", room.Width, "r.width", value => { room.Width = (int)value; RoomView.InvalidateVisual(); WriteRoom(); });
        AddNumberField("Altura", room.Height, "r.height", value => { room.Height = (int)value; RoomView.InvalidateVisual(); WriteRoom(); });
        AddNumberField("Grade", room.Grid, "r.grid", value => { room.Grid = (int)value; RoomView.InvalidateVisual(); WriteRoom(); });
        AddTextField("Fundo", room.Background, "r.background", typed =>
        {
            if (Color.TryParse(typed.Trim(), out _))
                room.Background = typed.Trim();
            RoomView.InvalidateVisual();
            WriteRoom();
        });
        AddTextField("Shaders de tela", string.Join(", ", room.PostShaders), "r.post", typed =>
        {
            room.PostShaders = typed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            WriteRoom();
        });
        BuildLightingSection(room);
        var count = room.Instances.Count;
        AddNote(count == 0 ? "Nenhuma instância ainda. Escolha um objeto no pincel e clique na sala."
            : $"{count} {(count == 1 ? "instância" : "instâncias")}. Salve com Ctrl S; Ctrl Z desfaz.");
    }

    private void AddBrushPalette()
    {
        var palette = Palette.Current;
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        wrap.Children.Add(BrushButton(null, "Escolher e mover", null));
        foreach (var info in _gameObjects.Values.OrderBy(o => o.Class, StringComparer.OrdinalIgnoreCase))
            wrap.Children.Add(BrushButton(info.Class, info.Class, info.Sprite is { } sprite ? LoadSprite(sprite) : null));
        Inspector.Children.Add(wrap);
        AddNote(RoomView.Brush is { } brush ? $"Clique na sala para pôr um {brush}. Arraste para pintar vários." : "Clique numa instância para ver e mudar os dados dela.");
        if (_gameObjects.Count == 0)
            AddNote("Este jogo ainda não tem objetos. Crie um pelo + da lista ao lado.");

        Button BrushButton(string? objectName, string tip, SpriteImage? sprite)
        {
            var selected = string.Equals(RoomView.Brush, objectName, StringComparison.OrdinalIgnoreCase);
            Control content;
            if (objectName is null)
            {
                content = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M0 0L0 13L3.5 9.5L6 15L8 14L5.6 8.6L10 8.6Z"),
                    Fill = palette.Ink,
                    Width = 12,
                    Height = 15,
                    Stretch = Stretch.Uniform,
                };
            }
            else if (sprite is not null)
            {
                content = new SpriteThumb(sprite) { Width = 28, Height = 28 };
            }
            else
            {
                content = new TextBlock { Text = objectName[..1].ToUpperInvariant(), FontWeight = FontWeight.SemiBold, FontSize = 14 };
            }

            var button = new Button
            {
                Classes = { "flat" },
                Width = 44,
                Height = 44,
                Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(8),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = selected ? palette.Selection : palette.Tint,
                BorderBrush = selected ? palette.Accent : Brushes.Transparent,
                BorderThickness = new Thickness(selected ? 1.5 : 0),
                Content = content,
                Tag = "brush." + (objectName ?? ""),
            };
            ToolTip.SetTip(button, tip);
            AutomationProperties.SetName(button, objectName is null ? "Escolher e mover" : $"Pincel {objectName}");
            button.Click += (_, _) => SetRoomBrush(objectName);
            return button;
        }
    }

    public void SetRoomBrush(string? objectName)
    {
        RoomView.Brush = objectName;
        RoomView.InvalidateVisual();
        BuildInspector();
        RoomView.Focus();
    }

    private async Task AddInstancePropertyAsync(RoomInstance instance)
    {
        var name = await AskTextAsync("Nova propriedade", "O nome do campo no objeto, como speed. Depois escreva o valor no Inspetor.", "Acrescentar",
            text => string.IsNullOrWhiteSpace(text) ? "Escreva um nome."
                : text.Trim() is "object" or "x" or "y" or "layer" ? "Esse nome já é usado pela sala."
                : instance.Extra.ContainsKey(text.Trim()) ? "Essa propriedade já existe."
                : null);
        if (name is null)
            return;
        instance.Extra[name.Trim()] = 0;
        WriteRoom();
    }

    private void BuildSpriteInspector()
    {
        var png = ActiveDocument!.FullPath;
        var meta = GameFiles.LoadSpriteMeta(png);
        AddHeading("Sprite", first: true);
        var sprite = SpriteEditor.Sprite;
        AddNote(sprite is null ? Path.GetFileName(png)
            : $"{Path.GetFileName(png)} · {sprite.PixelWidth} × {sprite.PixelHeight} px");
        BuildSpriteTools();
        AddHeading("Dados");
        AddNumberField("Quadros", meta.Frames, "s.frames", value => UpdateSpriteMeta(m => m.Frames = Math.Max(1, (int)value)));
        AddNumberField("Quadros por segundo", meta.Fps, "s.fps", value => UpdateSpriteMeta(m => m.Fps = (float)value));
        AddNumberField("Origem X", meta.Origin[0], "s.ox", value => UpdateSpriteMeta(m => m.Origin = [(float)value, m.Origin[1]]));
        AddNumberField("Origem Y", meta.Origin[1], "s.oy", value => UpdateSpriteMeta(m => m.Origin = [m.Origin[0], (float)value]));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        actions.Children.Add(SmallButton("Origem no centro", "Bom para o que gira ou anda para todo lado",
            () => UpdateSpriteMeta(m => m.Origin = [(float)Math.Floor(SpriteEditor.FrameWidth / 2), (float)Math.Floor(SpriteEditor.FrameHeight / 2)])));
        actions.Children.Add(SmallButton("No canto", "Bom para paredes e blocos na grade", () => UpdateSpriteMeta(m => m.Origin = [0, 0])));
        actions.Children.Add(SmallButton("Editar imagem", "Abre o PNG no seu editor de imagens", () => EditImage(png)));
        Inspector.Children.Add(actions);
        AddNote("Quadros lado a lado, da esquerda para a direita, todos do mesmo tamanho. A origem é o ponto que fica em X e Y.");
        BuildMaskFields(png, meta);

        if (RootFolder is not null)
        {
            var name = Path.GetFileNameWithoutExtension(png);
            var users = GameFiles.Objects(RootFolder).Values.Where(o => string.Equals(o.Sprite, name, StringComparison.OrdinalIgnoreCase)).Select(o => o.Class).ToList();
            AddHeading("Usado por");
            AddNote(users.Count == 0 ? $"Nenhum objeto ainda. Num objeto: Sprite = \"{name}\";" : string.Join(", ", users));
        }
    }

    private void EditImage(string png)
    {
        try
        {
            Process.Start(new ProcessStartInfo(png) { UseShellExecute = true, Verb = "edit" });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try
            {
                Process.Start(new ProcessStartInfo("mspaint.exe", $"\"{png}\"") { UseShellExecute = true });
            }
            catch (Exception again) when (again is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ShowStatus("Nenhum editor de imagens abriu este arquivo.");
            }
        }
    }

    // Peças do Inspetor

    private void AddHeading(string text, bool first = false) =>
        Inspector.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.Current.Muted,
            Margin = new Thickness(0, first ? 0 : 12, 0, 2),
        });

    private void AddNote(string text) =>
        Inspector.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            Foreground = Palette.Current.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

    private void AddRow(string label, Control field)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*") };
        var caption = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(caption, label);
        grid.Children.Add(caption);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        Inspector.Children.Add(grid);
    }

    private void AddTextField(string label, string value, string tag, Action<string> commit)
    {
        var box = new TextBox { Text = value, FontSize = 12, MinHeight = 28, Padding = new Thickness(8, 4), Tag = tag };
        AutomationProperties.SetName(box, label);
        var committed = value;
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
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = committed;
                RoomView.Focus();
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => Commit();
        AddRow(label, box);
    }

    private void AddNumberField(string label, double value, string tag, Action<double> commit) =>
        AddTextField(label, CanisMajor.RoomJson.Number(value), tag, typed =>
        {
            if (double.TryParse(typed.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                commit(number);
            else
                BuildInspector();
        });

    private static Button SmallButton(string text, string tip, Action click)
    {
        var button = new Button { Classes = { "secondary" }, Content = text, FontSize = 12, Margin = new Thickness(0, 0, 6, 6) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    // O valor de uma propriedade a mais: número, true, false, texto entre aspas ou JSON. Sem aspas, vira texto.
    private static JsonNode? ParseValue(string text)
    {
        var trimmed = text.Trim();
        try
        {
            return JsonNode.Parse(trimmed);
        }
        catch (JsonException)
        {
            return JsonValue.Create(trimmed);
        }
    }

    // Shaders

    private static bool IsShader(string path) => string.Equals(Path.GetExtension(path), ".wgsl", StringComparison.OrdinalIgnoreCase);

    private void CheckActiveShader()
    {
        if (ActiveDocument is not { } document || !IsShader(document.FullPath))
            return;
        var version = ++_shaderVersion;
        ShaderChecked = CheckShaderAsync(version, document, document.Document.Text);
    }

    private async Task CheckShaderAsync(int version, OpenDocument document, string text)
    {
        IReadOnlyList<CanisMajor.ShaderError> errors;
        try
        {
            errors = await Task.Run(() => CanisMajor.ShaderCheck.Check(text));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // Sem a canis_core.dll ao lado do editor, os shaders só são conferidos ao rodar o jogo.
            return;
        }
        // Uma conferência mais nova só vale mais se o texto mudou desde esta.
        if (!_documents.Contains(document) || (version != _shaderVersion && document.Document.Text != text))
            return;
        var lines = document.Document.LineCount;
        var list = errors.Select(error =>
        {
            var line = Math.Clamp(error.Line, 1, lines);
            var length = document.Document.GetLineByNumber(line).Length;
            return new LspDiagnostic(line - 1, 0, line - 1, Math.Max(1, length), DiagnosticSeverity.Error, error.Message);
        }).ToList();
        _language.PublishLocal(document.FullPath, list);
    }

    // Criar recursos e jogos

    private void OnNewRoom(object? sender, RoutedEventArgs e) => _ = CreateResourceAsync("room");
    private void OnNewObject(object? sender, RoutedEventArgs e) => _ = CreateResourceAsync("object");
    private void OnNewSprite(object? sender, RoutedEventArgs e) => _ = CreateResourceAsync("sprite");
    private void OnNewShader(object? sender, RoutedEventArgs e) => _ = CreateResourceAsync("shader");
    private void OnNewShaderPreset(object? sender, RoutedEventArgs e) => ChooseShaderPreset();
    private void OnNewTileset(object? sender, RoutedEventArgs e) => _ = CreateResourceAsync("tileset");
    private void OnNewAiObject(object? sender, RoutedEventArgs e) => ChooseAiPreset();

    public async Task CreateResourceAsync(string kind)
    {
        if (!_isGame || RootFolder is not { } root)
            return;
        var (title, body, stem) = kind switch
        {
            "room" => ("Nova sala", "O nome do arquivo em rooms/. No jogo, Game.Goto(\"nome\") leva até ela.", "sala"),
            "object" => ("Novo objeto", "O nome vira a classe em objects/, e as salas usam esse nome para colocar o objeto.", "Objeto"),
            "sprite" => ("Novo sprite", "Um PNG de 16 por 16 em sprites/, pronto para desenhar no seu editor de imagens.", "sprite"),
            "shader" => ("Novo shader", "Um arquivo WGSL em shaders/. Num objeto, Shader = \"nome\" aplica no sprite dele.", "efeito"),
            "tileset" => ("Novo tileset", "Uma imagem de sprites/ cortada em tiles, para pintar salas. Escolha a imagem e o tamanho do tile no Inspetor.", "tiles"),
            _ => throw new ArgumentException(kind),
        };
        var suggestion = stem;
        for (var n = 2; GameFiles.CheckName(root, kind, suggestion) is not null && n < 100; n++)
            suggestion = stem + n;

        var name = await AskTextAsync(title, body, "Criar", text => GameFiles.CheckName(root, kind, text.Trim()), suggestion);
        if (name is null || RootFolder != root)
            return;
        string path;
        try
        {
            path = GameFiles.Create(root, kind, name.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Não foi possível criar: {e.Message}");
            return;
        }
        _gameView = true;
        _closedGameSections.Remove(GameFiles.FolderOf(root, kind));
        UpdateTreeHeader();
        RefreshTree();
        ReindexProject();
        OpenFile(path);
    }

    private void OnNewGame(object? sender, RoutedEventArgs e) => _ = NewGameAsync();

    public async Task NewGameAsync()
    {
        var engine = GameFiles.EngineRoot();
        if (engine is null)
        {
            ShowStatus("Não achei a pasta do Canis Major acima do editor, e o jogo novo precisa dela.");
            return;
        }
        var name = await AskTextAsync("Novo jogo", "O nome do jogo. Em seguida você escolhe a pasta onde ele vai ficar.", "Continuar",
            text => string.IsNullOrWhiteSpace(text) ? "Escreva um nome."
                : SafeFolderName(text).Length == 0 ? "Use pelo menos uma letra ou número."
                : null, "Meu jogo");
        if (name is null)
            return;
        var choice = await AskAsync("Começar de onde?",
            "O exemplo traz um jogador, estrelas para pegar, paredes, um cubo 3D e shaders, bom para aprender. O vazio traz só uma sala.",
            "Exemplo", "Vazio");
        if (choice == DialogChoice.Cancel)
            return;
        var parent = await PickParentFolder();
        if (parent is null)
            return;

        var folder = Path.Combine(parent, SafeFolderName(name));
        try
        {
            CanisMajor.Templates.CreateProject(folder, name.Trim(), choice == DialogChoice.Primary ? "exemplo" : "vazio", engine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus(e.Message);
            return;
        }
        await SwitchFolderAsync(folder);
        var start = Path.Combine(folder, "rooms", "inicio.json");
        if (RootFolder == Path.GetFullPath(folder) && File.Exists(start))
            OpenFile(start);
        ShowStatus("Jogo criado. F5 compila e abre o jogo.");
    }

    private async Task<string?> PickParentFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Onde criar a pasta do jogo", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
    }

    // Exportar

    // O zip da última exportação que terminou bem. Os testes conferem por aqui.
    public string? LastExport { get; private set; }

    private void OnExportGame(object? sender, RoutedEventArgs e) => _ = ExportGameAsync();

    // Roda o dotnet publish no terminal de Execução, para a pessoa ver o andamento e os erros, e no fim faz o zip.
    public async Task ExportGameAsync()
    {
        if (!_isGame || RootFolder is not { } root)
            return;
        if (!await ConfirmStopTaskAsync())
            return;
        foreach (var document in _documents.Where(d => d.IsDirty).ToList())
        {
            if (!await SaveAsync(document))
                return;
        }

        CanisMajor.ExportPlan plan;
        try
        {
            plan = CanisMajor.GameExport.Plan(root);
            CanisMajor.GameExport.Begin(plan);
        }
        catch (Exception e) when (e is CanisMajor.GameException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(e.Message);
            return;
        }
        LastExport = null;
        StartTask(plan.CommandLine, code => FinishExport(plan, code));
    }

    private void FinishExport(CanisMajor.ExportPlan plan, int code)
    {
        if (code != 0)
        {
            ShowStatus("A exportação falhou. O terminal mostra o motivo.");
            return;
        }
        try
        {
            LastExport = CanisMajor.GameExport.Finish(plan);
        }
        catch (Exception e) when (e is CanisMajor.GameException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(e.Message);
            return;
        }
        _task?.Buffer.Feed($"\x1b[32mJogo exportado: {LastExport}\x1b[0m\r\n");
        ShowStatus($"Jogo exportado: {Path.GetFileName(LastExport)}");
        _ = OfferExportFolderAsync(LastExport);
    }

    private async Task OfferExportFolderAsync(string zip)
    {
        var mb = new FileInfo(zip).Length / 1024.0 / 1024.0;
        var choice = await AskAsync("Jogo exportado",
            $"A pasta do jogo e o {Path.GetFileName(zip)} ({mb:0.#} MB) estão em export/. Quem for jogar abre o .exe, sem instalar nada.",
            "Mostrar na pasta", "Fechar");
        if (choice != DialogChoice.Primary)
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = false });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus("Não foi possível abrir o Explorador de Arquivos.");
        }
    }

    // Grava o que a janela mostra num PNG, para conferir a interface por foto.
    public void SaveShot(string path)
    {
        var scale = RenderScaling;
        var size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    // Os comandos de jogo na barra (Ctrl K).
    private void AddGameCommands(List<(string Name, string Hint, Action Run)> actions)
    {
        actions.Add(("Novo jogo", "Canis Major", () => _ = NewGameAsync()));
        if (!_isGame)
            return;
        actions.Add(("Nova sala", "rooms/", () => _ = CreateResourceAsync("room")));
        if (IsRoomDocument && ActiveDocument is { } roomDoc)
        {
            var roomName = Path.GetFileNameWithoutExtension(roomDoc.FullPath);
            actions.Add(("Testar esta sala", roomName, () => RunRoom(roomName)));
            actions.Add(("Tornar sala inicial", roomName, () => SetStartRoom(roomName)));
            actions.Add(("Duplicar sala", roomName, () => _ = DuplicateRoomAsync(roomDoc.FullPath)));
        }
        actions.Add(("Novo objeto", "objects/", () => _ = CreateResourceAsync("object")));
        actions.Add(("Novo sprite", "sprites/", () => _ = CreateResourceAsync("sprite")));
        actions.Add(("Novo shader", "shaders/", () => _ = CreateResourceAsync("shader")));
        actions.Add(("Novo shader de um preset", "brilho, contorno, dissolver, CRT...", ChooseShaderPreset));
        actions.Add(("Novo tileset", "tilesets/", () => _ = CreateResourceAsync("tileset")));
        actions.Add(("Novo objeto com IA", "guarda, perseguidor, medroso, ajudante...", ChooseAiPreset));
        actions.Add(("Exportar jogo", "Pasta e .zip em export/", () => _ = ExportGameAsync()));
        actions.Add((IsGameView ? "Mostrar os arquivos" : "Mostrar os recursos do jogo", "Lista ao lado", () => SetGameView(!IsGameView)));
        if (IsRoomDocument)
            actions.Insert(0, (ShowsRoom ? "Ver o código da sala" : "Ver a sala desenhada", "", ToggleRoomCode));
    }
}
