using System.Text.Json;
using CanisEditor.Game;
using GameProject = CanisMajor.GameProject;
using TilesetData = CanisMajor.TilesetData;

namespace CanisEditor;

// Tilesets: a imagem cortada em tiles. No editor do tileset, o clique marca os tiles sólidos.
public partial class MainWindow
{
    private bool _tilesetHooked;

    // Lê um tileset pelo nome, preferindo o texto da aba aberta.
    public TilesetData? LoadTileset(string? name)
    {
        if (name is null || RootFolder is null)
            return null;
        var path = Path.Combine(GameFiles.FolderOf(RootFolder, "tileset"), name + ".json");
        return ParseTileset(path);
    }

    private TilesetData? ParseTileset(string path)
    {
        var open = _documents.Find(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
        try
        {
            var text = open?.Document.Text ?? (File.Exists(path) ? File.ReadAllText(path) : null);
            return text is null ? null : JsonSerializer.Deserialize<TilesetData>(text, GameProject.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<string> TilesetNames() =>
        RootFolder is null ? [] : GameFiles.List(RootFolder, "tileset").Select(t => t.Name).ToList();

    private TilesetData? ActiveTileset => ActiveDocument is { } document ? ParseTileset(document.FullPath) : null;

    private void ShowTileset()
    {
        if (!_tilesetHooked)
        {
            _tilesetHooked = true;
            TilesetEditor.TileClicked += ToggleSolid;
        }
        var set = ActiveTileset;
        TilesetEditor.Selected = -1;
        TilesetEditor.Show(set is null ? null : LoadSprite(set.Sprite), set);
    }

    private void ToggleSolid(int id)
    {
        if (!ShowsTileset)
            return;
        EditTileset(set =>
        {
            if (!set.Solid.Remove(id))
                set.Solid.Add(id);
            set.Solid.Sort();
        });
    }

    // A mudança vai pelo texto da aba, para o Ctrl Z e o salvar valerem como num arquivo.
    private void EditTileset(Action<TilesetData> change)
    {
        if (ActiveTileset is not { } set)
            return;
        change(set);
        CodeEditor.Document.Text = JsonSerializer.Serialize(set, GameProject.Json) + "\n";
        ShowTileset();
        BuildInspector();
        RoomView.InvalidateVisual();
    }

    private void BuildTilesetInspector()
    {
        var set = ActiveTileset;
        AddHeading("Tileset", first: true);
        if (set is null)
        {
            AddNote("O arquivo não é um tileset válido. Use o botão JSON no alto para corrigir.");
            return;
        }
        var sprites = SpriteNames();
        if (sprites.Count == 0)
            sprites.Add("");
        AddChoice("Imagem", sprites, set.Sprite, "t.sprite", chosen => EditTileset(s => s.Sprite = chosen));
        AddNumberField("Largura do tile", set.TileWidth, "t.w", value => EditTileset(s => s.TileWidth = Math.Max(1, (int)value)));
        AddNumberField("Altura do tile", set.TileHeight, "t.h", value => EditTileset(s => s.TileHeight = Math.Max(1, (int)value)));
        AddNote($"{set.Solid.Count} {(set.Solid.Count == 1 ? "tile sólido" : "tiles sólidos")}. Clique num tile para marcar ou desmarcar: os vermelhos bloqueiam o movimento.");
        AddNote("Na sala, crie uma camada com + Tiles, escolha este tileset e pinte. No código, Move(dx, dy, solid: \"NomeDaCamada\") para nos tiles vermelhos.");
    }
}
