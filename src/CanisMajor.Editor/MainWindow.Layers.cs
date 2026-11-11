using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CanisEditor.Game;
using CanisEditor.Theming;
using LayerData = CanisMajor.LayerData;
using RoomLayers = CanisMajor.RoomLayers;

namespace CanisEditor;

// Camadas da sala, como no GameMaker: uma pilha de camadas de instâncias e de fundo. A de cima
// fica na frente. Cada camada pode ordenar as instâncias pela posição Y, para o personagem passar
// atrás do poste sem código nenhum.
public partial class MainWindow
{
    private void BuildLayersSection(RoomModel room)
    {
        AddHeading("Camadas");
        var layers = room.EffectiveLayers;
        if (room.FindLayer(RoomView.ActiveLayer) is null)
            RoomView.ActiveLayer = RoomLayers.DefaultInstanceLayer(layers)?.Name ?? layers[0].Name;

        // De cima (na frente) para baixo (atrás), como a lista do GameMaker.
        var list = new StackPanel { Spacing = 2 };
        foreach (var layer in layers.OrderByDescending(l => l.Depth).ToList())
            list.Children.Add(LayerRow(room, layer));
        Inspector.Children.Add(list);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(SmallButton("+ Instâncias", "Nova camada para objetos", () => _ = AddLayerAsync(room, background: false)));
        actions.Children.Add(SmallButton("+ Fundo", "Nova camada de fundo, com cor ou imagem", () => _ = AddLayerAsync(room, background: true)));
        actions.Children.Add(SmallButton("+ Tiles", "Nova camada para pintar tiles de um tileset", () => _ = AddTileLayerAsync(room)));
        actions.Children.Add(SmallButton("Subir", "Traz a camada ativa para a frente", () => MoveLayer(room, +1)));
        actions.Children.Add(SmallButton("Descer", "Manda a camada ativa para trás", () => MoveLayer(room, -1)));
        actions.Children.Add(SmallButton("Excluir", "Exclui a camada ativa", () => _ = DeleteLayerAsync(room)));
        Inspector.Children.Add(actions);

        var lockBox = new CheckBox { Content = "Só a camada ativa recebe cliques", IsChecked = RoomView.LockToActiveLayer, FontSize = 12, Tag = "l.lock" };
        lockBox.IsCheckedChanged += (_, _) =>
        {
            RoomView.LockToActiveLayer = lockBox.IsChecked == true;
            RoomView.InvalidateVisual();
        };
        Inspector.Children.Add(lockBox);

        if (room.FindLayer(RoomView.ActiveLayer) is { } active)
            BuildActiveLayerFields(room, active);
    }

    private Control LayerRow(RoomModel room, LayerData layer)
    {
        var palette = Palette.Current;
        var isActive = string.Equals(layer.Name, RoomView.ActiveLayer, StringComparison.OrdinalIgnoreCase);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto") };

        var eye = new Button
        {
            Classes = { "flat" },
            Content = layer.Visible ? "👁" : "–",
            FontSize = 12,
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Tag = "l.eye." + layer.Name,
        };
        ToolTip.SetTip(eye, layer.Visible ? "Esconder a camada" : "Mostrar a camada");
        AutomationProperties.SetName(eye, (layer.Visible ? "Esconder " : "Mostrar ") + layer.Name);
        eye.Click += (_, _) =>
        {
            room.EnsureLayers();
            if (room.FindLayer(layer.Name) is { } real)
                real.Visible = !real.Visible;
            RoomChanged();
        };
        grid.Children.Add(eye);

        var name = new Button
        {
            Classes = { "flat" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = isActive ? palette.Selection : Brushes.Transparent,
            BorderBrush = isActive ? palette.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(isActive ? 1 : 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3),
            Content = new TextBlock
            {
                Text = (layer.IsBackground ? "▦ " : layer.IsTiles ? "▤ " : "◆ ") + layer.Name + (layer.YSort ? "  ↕Y" : ""),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = layer.Visible ? 1 : 0.5,
            },
            Tag = "l.name." + layer.Name,
        };
        ToolTip.SetTip(name, layer.IsBackground ? "Camada de fundo" : layer.IsTiles ? "Camada de tiles. Clique para pintar nela." : "Camada de instâncias. Clique para o pincel colocar aqui.");
        name.Click += (_, _) =>
        {
            RoomView.ActiveLayer = layer.Name;
            RoomView.InvalidateVisual();
            BuildInspector();
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var depth = new TextBlock { Text = layer.Depth.ToString(), FontSize = 11, Foreground = palette.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
        ToolTip.SetTip(depth, "Profundidade. Maior fica na frente.");
        Grid.SetColumn(depth, 2);
        grid.Children.Add(depth);
        return grid;
    }

    // Camada de tiles: o tileset, a paleta para escolher o tile do pincel e a colisão à vista.
    private void BuildTileLayerFields(RoomModel room, LayerData layer)
    {
        var names = TilesetNames();
        if (names.Count == 0)
        {
            AddNote("Este jogo ainda não tem tileset. Crie um pelo + da lista ao lado (Novo tileset).");
            return;
        }
        AddChoice("Tileset", names, layer.Tileset ?? names[0], "l.f.tileset", chosen => EditLayer(room, layer.Name, l => l.Tileset = chosen));
        var set = LoadTileset(layer.Tileset);
        if (set is null)
        {
            AddNote("Não achei esse tileset. Escolha outro acima.");
            return;
        }

        AddNote("Clique no tile para pôr no pincel. Na sala, o botão esquerdo pinta e o direito apaga, e arrastar continua.");
        var palette = new TilesetView { EditSolid = RoomView.ShowCollision, Selected = RoomView.TileBrush, MaxScale = 3, HorizontalAlignment = HorizontalAlignment.Left, Tag = "l.f.palette" };
        palette.Show(LoadSprite(set.Sprite), set);
        palette.TileClicked += id =>
        {
            RoomView.TileBrush = id;
            RoomView.Focus();
        };
        Inspector.Children.Add(palette);

        var collision = new CheckBox { Content = "Mostrar os tiles sólidos em vermelho", IsChecked = RoomView.ShowCollision, FontSize = 12, Tag = "l.f.collision" };
        collision.IsCheckedChanged += (_, _) =>
        {
            RoomView.ShowCollision = collision.IsChecked == true;
            RoomView.InvalidateVisual();
            BuildInspector();
        };
        Inspector.Children.Add(collision);

        var used = layer.Tiles.Count(t => t >= 0);
        AddNote($"{used} {(used == 1 ? "tile pintado" : "tiles pintados")}, mapa de {layer.Columns} colunas. No código, Move(dx, dy, solid: \"{layer.Name}\") para nos tiles sólidos, e Game.SetTile(\"{layer.Name}\", x, y, -1) apaga um.");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(SmallButton("Ajustar ao tamanho da sala", "Recalcula as colunas pela largura da sala", () => EditLayer(room, layer.Name, l => ResizeTiles(l, room, set))));
        actions.Children.Add(SmallButton("Limpar tiles", "Apaga todos os tiles desta camada (Ctrl Z desfaz)", () => EditLayer(room, layer.Name, l => l.Tiles.Clear())));
        Inspector.Children.Add(actions);
    }

    // Muda o número de colunas sem embaralhar os tiles já pintados.
    private static void ResizeTiles(LayerData layer, RoomModel room, CanisMajor.TilesetData set)
    {
        var columns = Math.Max(1, (int)Math.Ceiling(room.Width / (double)set.TileWidth));
        if (columns == layer.Columns)
            return;
        var old = layer.Tiles.ToList();
        var oldColumns = Math.Max(1, layer.Columns);
        var rows = layer.Rows;
        layer.Tiles.Clear();
        layer.Columns = columns;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < Math.Min(columns, oldColumns); c++)
            {
                var index = r * oldColumns + c;
                if (index < old.Count && old[index] >= 0)
                    layer.SetTile(c, r, old[index]);
            }
    }

    private async Task AddTileLayerAsync(RoomModel room)
    {
        var tilesets = TilesetNames();
        if (tilesets.Count == 0)
        {
            ShowStatus("Crie um tileset primeiro, pelo + da lista ao lado.");
            return;
        }
        var suggestion = "Tiles";
        for (var n = 2; room.FindLayer(suggestion) is not null; n++)
            suggestion = "Tiles" + n;
        var name = await AskTextAsync("Nova camada de tiles", "O nome aparece na lista e no código, como em Move(dx, dy, solid: \"nome\").", "Criar",
            text => string.IsNullOrWhiteSpace(text) ? "Escreva um nome."
                : room.FindLayer(text.Trim()) is not null ? "Já existe uma camada com esse nome."
                : null,
            suggestion);
        if (name is null)
            return;
        room.EnsureLayers();
        var set = LoadTileset(tilesets[0]) ?? new CanisMajor.TilesetData();
        // Tiles nascem logo atrás das instâncias.
        var depth = room.Layers.Where(l => l.IsInstances).Select(l => l.Depth).DefaultIfEmpty(0).Min() - 10;
        room.Layers.Add(new LayerData
        {
            Name = name.Trim(),
            Kind = "tiles",
            Depth = depth,
            Tileset = tilesets[0],
            Columns = Math.Max(1, (int)Math.Ceiling(room.Width / (double)set.TileWidth)),
        });
        RoomView.ActiveLayer = name.Trim();
        RoomView.TileBrush = 0;
        RoomChanged();
    }

    private void BuildActiveLayerFields(RoomModel room, LayerData layer)
    {
        AddHeading($"Camada {layer.Name}");
        AddTextField("Nome", layer.Name, "l.f.name", typed => RenameLayer(room, layer.Name, typed.Trim()));
        AddNumberField("Profundidade", layer.Depth, "l.f.depth", value => EditLayer(room, layer.Name, l => l.Depth = (int)value));

        if (layer.IsTiles)
        {
            BuildTileLayerFields(room, layer);
            return;
        }
        if (layer.IsInstances)
        {
            var ysort = new CheckBox { Content = "Ordenar por Y (quem está mais embaixo fica na frente)", IsChecked = layer.YSort, FontSize = 12, Tag = "l.f.ysort" };
            ysort.IsCheckedChanged += (_, _) => EditLayer(room, layer.Name, l => l.YSort = ysort.IsChecked == true);
            Inspector.Children.Add(ysort);
            var count = room.Instances.Count(i => ReferenceEquals(room.LayerOf(i), layer) || string.Equals(room.LayerOf(i)?.Name, layer.Name, StringComparison.OrdinalIgnoreCase));
            AddNote($"{count} {(count == 1 ? "instância" : "instâncias")} nesta camada. Um objeto pode mudar a ordem sozinho com SortOffset ou Layer no código.");
            return;
        }

        AddTextField("Cor", layer.Color ?? "", "l.f.color", typed =>
        {
            var text = typed.Trim();
            EditLayer(room, layer.Name, l => l.Color = text.Length == 0 ? null : Color.TryParse(text, out _) ? text : l.Color);
        });

        var sprites = SpriteNames();
        sprites.Insert(0, "(nenhum)");
        var picker = new ComboBox
        {
            ItemsSource = sprites,
            SelectedItem = layer.Sprite is { } s && sprites.Contains(s) ? s : "(nenhum)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
            Tag = "l.f.sprite",
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is string chosen)
                EditLayer(room, layer.Name, l => l.Sprite = chosen == "(nenhum)" ? null : chosen);
        };
        AddRow("Imagem", picker);

        var tiles = new WrapPanel { Orientation = Orientation.Horizontal };
        var tileX = new CheckBox { Content = "Repetir na horizontal", IsChecked = layer.TileX, FontSize = 12, Margin = new Thickness(0, 0, 12, 0), Tag = "l.f.tilex" };
        tileX.IsCheckedChanged += (_, _) => EditLayer(room, layer.Name, l => l.TileX = tileX.IsChecked == true);
        var tileY = new CheckBox { Content = "Repetir na vertical", IsChecked = layer.TileY, FontSize = 12, Tag = "l.f.tiley" };
        tileY.IsCheckedChanged += (_, _) => EditLayer(room, layer.Name, l => l.TileY = tileY.IsChecked == true);
        tiles.Children.Add(tileX);
        tiles.Children.Add(tileY);
        Inspector.Children.Add(tiles);
        AddNumberField("Paralaxe", layer.Parallax, "l.f.parallax", value => EditLayer(room, layer.Name, l => l.Parallax = (float)Math.Clamp(value, 0, 1)));
        AddNote("Paralaxe 0 fica presa ao mundo, 1 acompanha a câmera. Entre os dois, o fundo anda mais devagar e dá profundidade.");
    }

    // Salas: a inicial, duplicar e testar só uma, sem abrir o projeto.json nem o terminal.

    private string? StartRoomName()
    {
        if (RootFolder is null)
            return null;
        try
        {
            return CanisMajor.GameProject.Load(RootFolder).Info.StartRoom;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or CanisMajor.GameException)
        {
            return null;
        }
    }

    public void SetStartRoom(string room)
    {
        if (RootFolder is null)
            return;
        var path = Path.Combine(RootFolder, CanisMajor.GameProject.FileName);
        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) as System.Text.Json.Nodes.JsonObject ?? new();
            json["start_room"] = room;
            File.WriteAllText(path, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            ShowStatus($"O jogo começa na sala {room}.");
            RefreshTree();
            BuildInspector();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowStatus("Não foi possível mudar o projeto.json.");
        }
    }

    private async Task DuplicateRoomAsync(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var suggestion = baseName + "_2";
        for (var n = 3; File.Exists(Path.Combine(folder, suggestion + ".json")); n++)
            suggestion = $"{baseName}_{n}";
        var name = await AskTextAsync("Duplicar sala", "A cópia leva as camadas, as instâncias e as configurações.", "Duplicar",
            text => string.IsNullOrWhiteSpace(text) || text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "Escreva um nome válido."
                : File.Exists(Path.Combine(folder, text.Trim() + ".json")) ? "Já existe uma sala com esse nome."
                : null,
            suggestion);
        if (name is null)
            return;
        var target = Path.Combine(folder, name.Trim() + ".json");
        // Copia o que está na tela, mesmo sem salvar.
        File.WriteAllText(target, _room?.Serialize() ?? File.ReadAllText(path));
        RefreshTree();
        OpenFile(target);
    }

    private void RunRoom(string room) => StartTask($"dotnet run -- --room {room}", null);

    private void BuildRoomActions(string roomPath)
    {
        var name = Path.GetFileNameWithoutExtension(roomPath);
        var isStart = string.Equals(StartRoomName(), name, StringComparison.OrdinalIgnoreCase);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        actions.Children.Add(SmallButton("▶ Testar esta sala", "Abre o jogo direto nesta sala", () => RunRoom(name)));
        if (!isStart)
            actions.Children.Add(SmallButton("★ Sala inicial", "O jogo passa a começar por esta sala", () => SetStartRoom(name)));
        actions.Children.Add(SmallButton("Duplicar sala", "Cria uma cópia desta sala", () => _ = DuplicateRoomAsync(roomPath)));
        Inspector.Children.Add(actions);
        if (isStart)
            AddNote("★ O jogo começa por esta sala.");
    }

    private List<string> SpriteNames()
    {
        if (RootFolder is null)
            return [];
        var folder = Path.Combine(RootFolder, "sprites");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    private void RoomChanged()
    {
        RoomView.InvalidateVisual();
        WriteRoom();
        BuildInspector();
    }

    private void EditLayer(RoomModel room, string name, Action<LayerData> change)
    {
        room.EnsureLayers();
        if (room.FindLayer(name) is not { } layer)
            return;
        change(layer);
        RoomChanged();
    }

    private void RenameLayer(RoomModel room, string oldName, string newName)
    {
        if (newName.Length == 0 || newName == oldName)
        {
            BuildInspector();
            return;
        }
        if (room.FindLayer(newName) is not null)
        {
            ShowStatus("Já existe uma camada com esse nome.");
            BuildInspector();
            return;
        }
        room.EnsureLayers();
        if (room.FindLayer(oldName) is not { } layer)
            return;
        var wasDefault = ReferenceEquals(RoomLayers.DefaultInstanceLayer(room.Layers), layer);
        layer.Name = newName;
        // As instâncias que apontavam para o nome antigo vão junto.
        foreach (var instance in room.Instances.Where(i => string.Equals(i.Layer, oldName, StringComparison.OrdinalIgnoreCase)))
            instance.Layer = wasDefault ? null : newName;
        if (string.Equals(RoomView.ActiveLayer, oldName, StringComparison.OrdinalIgnoreCase))
            RoomView.ActiveLayer = newName;
        RoomChanged();
    }

    private async Task AddLayerAsync(RoomModel room, bool background)
    {
        var suggestion = background ? "Fundo" : "Instancias";
        for (var n = 2; room.FindLayer(suggestion) is not null; n++)
            suggestion = (background ? "Fundo" : "Instancias") + n;
        var name = await AskTextAsync(background ? "Nova camada de fundo" : "Nova camada de instâncias",
            "O nome aparece na lista e no código, em Game.Layer(\"nome\").", "Criar",
            text => string.IsNullOrWhiteSpace(text) ? "Escreva um nome."
                : room.FindLayer(text.Trim()) is not null ? "Já existe uma camada com esse nome."
                : null,
            suggestion);
        if (name is null)
            return;
        room.EnsureLayers();
        // Fundo nasce atrás de tudo; instâncias, na frente de tudo.
        var depth = background ? room.Layers.Min(l => l.Depth) - 100 : room.Layers.Max(l => l.Depth) + 100;
        room.Layers.Add(new LayerData
        {
            Name = name.Trim(),
            Kind = background ? "background" : "instances",
            Depth = depth,
            Color = background ? room.Background : null,
        });
        RoomView.ActiveLayer = name.Trim();
        RoomChanged();
    }

    // Troca a profundidade com a camada vizinha, para subir ou descer uma posição na pilha.
    private void MoveLayer(RoomModel room, int direction)
    {
        room.EnsureLayers();
        if (room.FindLayer(RoomView.ActiveLayer) is not { } layer)
            return;
        var ordered = room.Layers.OrderBy(l => l.Depth).ToList();
        var index = ordered.IndexOf(layer) + direction;
        if (index < 0 || index >= ordered.Count)
            return;
        var other = ordered[index];
        (layer.Depth, other.Depth) = (other.Depth, layer.Depth);
        if (layer.Depth == other.Depth)
            layer.Depth += direction;
        RoomChanged();
    }

    private async Task DeleteLayerAsync(RoomModel room)
    {
        room.EnsureLayers();
        if (room.FindLayer(RoomView.ActiveLayer) is not { } layer)
            return;
        if (layer.IsInstances && room.Layers.Count(l => l.IsInstances) == 1)
        {
            ShowStatus("A sala precisa de pelo menos uma camada de instâncias.");
            return;
        }
        var inside = room.Instances.Where(i => ReferenceEquals(room.LayerOf(i), layer)).ToList();
        if (inside.Count > 0)
        {
            var choice = await AskAsync($"Excluir a camada {layer.Name}?",
                $"Ela tem {inside.Count} {(inside.Count == 1 ? "instância" : "instâncias")}, que serão excluídas junto. Ctrl Z desfaz.", "Excluir", "Cancelar");
            if (choice != DialogChoice.Primary)
                return;
        }
        foreach (var instance in inside)
            room.Instances.Remove(instance);
        room.Layers.Remove(layer);
        // Se a padrão mudou, quem apontava para ela pelo nome continua certo; quem não tinha nome vai para a nova padrão.
        RoomView.ActiveLayer = RoomLayers.DefaultInstanceLayer(room.Layers)?.Name;
        RoomView.Select(-1);
        RoomChanged();
    }
}
