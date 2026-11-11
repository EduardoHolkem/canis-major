namespace CanisMajor;

// Camadas de tiles: desenho só do pedaço que a câmera vê e colisão com os tiles sólidos.
internal static partial class Runtime
{
    private static readonly Dictionary<string, TilesetData> Tilesets = new(StringComparer.OrdinalIgnoreCase);
    public static Func<string, TilesetData?>? TilesetOverride; // para os testes

    public static TilesetData? TilesetOf(LayerData layer)
    {
        if (layer.Tileset is not { } name) return null;
        if (TilesetOverride != null) return TilesetOverride(name);
        if (!Tilesets.TryGetValue(name, out var set))
            Tilesets[name] = set = Project!.LoadTileset(name);
        return set;
    }

    private static void DrawTiles(LayerData layer)
    {
        if (TilesetOf(layer) is not { } set || SpriteInfo(set.Sprite) is not { } image) return;
        var tw = set.TileWidth;
        var th = set.TileHeight;
        var imageW = image.Width * image.Frames;
        var columnsInImage = set.ColumnsIn((int)imageW);
        var rowsInImage = Math.Max(1, (int)image.Height / Math.Max(1, th));
        // Só o que aparece na tela, com uma folga de um tile.
        var viewW = Project!.Info.Width;
        var viewH = Project.Info.Height;
        var c0 = Math.Max(0, (int)MathF.Floor(CameraX / tw));
        var r0 = Math.Max(0, (int)MathF.Floor(CameraY / th));
        var c1 = Math.Min(layer.Columns - 1, (int)MathF.Floor((CameraX + viewW) / tw));
        var r1 = Math.Min(layer.Rows - 1, (int)MathF.Floor((CameraY + viewH) / th));
        for (var r = r0; r <= r1; r++)
        {
            for (var c = c0; c <= c1; c++)
            {
                var id = layer.TileAt(c, r);
                if (id < 0) continue;
                var col = id % columnsInImage;
                var row = id / columnsInImage;
                if (row >= rowsInImage) continue;
                var uv = (col * tw / imageW, row * th / image.Height, (col + 1) * tw / imageW, (row + 1) * th / image.Height);
                Quad(image.Texture, c * tw, r * th, tw, th, 0, 0, 0, uv, Color.White, 0);
            }
        }
    }

    /// <summary>
    /// Se o retângulo encosta num tile sólido da camada de tiles com esse nome.
    /// É o que faz <c>Move(dx, dy, solid: "Chao")</c> parar nos tiles quando "Chao" é uma camada de tiles.
    /// </summary>
    public static bool TilesBlock(string? layerName, (float Left, float Top, float Right, float Bottom) box)
    {
        if (layerName is null) return false;
        foreach (var layer in Layers)
        {
            if (!layer.IsTiles || !string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase)) continue;
            if (TilesetOf(layer) is not { } set || set.Solid.Count == 0) continue;
            var c0 = (int)MathF.Floor(box.Left / set.TileWidth);
            var r0 = (int)MathF.Floor(box.Top / set.TileHeight);
            // O lado direito e a base são abertos: encostar não é invadir.
            var c1 = (int)MathF.Ceiling(box.Right / set.TileWidth) - 1;
            var r1 = (int)MathF.Ceiling(box.Bottom / set.TileHeight) - 1;
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                {
                    var id = layer.TileAt(c, r);
                    if (id >= 0 && set.Solid.Contains(id)) return true;
                }
        }
        return false;
    }

    public static LayerData? TileLayer(string name) =>
        Layers.FirstOrDefault(l => l.IsTiles && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
}
