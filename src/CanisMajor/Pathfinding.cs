namespace CanisMajor;

/// <summary>
/// Caminhos na sala: A* numa grade, desviando dos objetos sólidos e dos tiles sólidos.
/// <code>
/// var caminho = Pathfinding.Find(X, Y, alvo.X, alvo.Y, solid: "Parede");
/// </code>
/// O caminho vem como pontos no mundo (o centro de cada célula), do primeiro passo até o destino.
/// </summary>
public static class Pathfinding
{
    /// <summary>Tamanho padrão da célula, em pixels.</summary>
    public const int DefaultCell = 16;

    /// <summary>Limite de células visitadas por busca, para um caminho impossível não travar o jogo.</summary>
    public const int MaxVisited = 20000;

    private sealed class Grid
    {
        public required int Cell, Columns, Rows;
        public required bool[] Blocked;
        public float BuiltAt;
        public long Frame;
    }

    private static readonly Dictionary<(string Solid, int Cell, int Margin), Grid> Cache = new();

    /// <summary>De quanto em quanto tempo a grade de bloqueios é refeita (objetos sólidos se mexem).</summary>
    public static float RefreshSeconds { get; set; } = 0.5f;

    /// <summary>Esquece as grades guardadas: vale ao trocar de sala ou depois de mudar muitos tiles.</summary>
    public static void Invalidate() => Cache.Clear();

    /// <summary>Se a célula do ponto está bloqueada para esse tipo de sólido.</summary>
    public static bool IsBlocked(float x, float y, string solid, int cell = DefaultCell, int margin = 0)
    {
        var grid = GridFor(solid, cell, margin);
        var c = (int)MathF.Floor(x / cell);
        var r = (int)MathF.Floor(y / cell);
        return c < 0 || r < 0 || c >= grid.Columns || r >= grid.Rows || grid.Blocked[r * grid.Columns + c];
    }

    /// <summary>
    /// O caminho de (x0, y0) até (x1, y1), ou null se não houver. <paramref name="margin"/> afasta o
    /// caminho das paredes (em pixels), para um objeto largo não raspar nas quinas.
    /// </summary>
    public static List<(float X, float Y)>? Find(float x0, float y0, float x1, float y1, string solid, int cell = DefaultCell, int margin = 0)
    {
        cell = Math.Max(2, cell);
        var grid = GridFor(solid, cell, margin);
        var start = (C: Math.Clamp((int)MathF.Floor(x0 / cell), 0, grid.Columns - 1), R: Math.Clamp((int)MathF.Floor(y0 / cell), 0, grid.Rows - 1));
        var goal = (C: (int)MathF.Floor(x1 / cell), R: (int)MathF.Floor(y1 / cell));
        if (goal.C < 0 || goal.R < 0 || goal.C >= grid.Columns || goal.R >= grid.Rows) return null;
        if (grid.Blocked[goal.R * grid.Columns + goal.C])
        {
            // Destino dentro de uma parede (como o centro de um jogador encostado): vai para a célula livre mais perto.
            if (NearestFree(grid, goal) is not { } free) return null;
            goal = free;
        }
        if (start == goal) return [(x1, y1)];

        var cols = grid.Columns;
        var total = cols * grid.Rows;
        var cameFrom = new int[total];
        var cost = new float[total];
        Array.Fill(cameFrom, -1);
        Array.Fill(cost, float.MaxValue);
        var open = new PriorityQueue<int, float>();
        var startIndex = start.R * cols + start.C;
        var goalIndex = goal.R * cols + goal.C;
        cost[startIndex] = 0;
        open.Enqueue(startIndex, 0);
        var visited = 0;

        Span<(int dc, int dr, float w)> steps =
        [
            (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
            (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f),
        ];
        while (open.TryDequeue(out var current, out _))
        {
            if (current == goalIndex) break;
            if (++visited > MaxVisited) return null;
            var cc = current % cols;
            var cr = current / cols;
            foreach (var (dc, dr, w) in steps)
            {
                var nc = cc + dc;
                var nr = cr + dr;
                if (nc < 0 || nr < 0 || nc >= cols || nr >= grid.Rows) continue;
                var next = nr * cols + nc;
                if (grid.Blocked[next]) continue;
                // Na diagonal, não corta a quina de uma parede.
                if (dc != 0 && dr != 0 && (grid.Blocked[cr * cols + nc] || grid.Blocked[nr * cols + cc])) continue;
                var newCost = cost[current] + w;
                if (newCost >= cost[next]) continue;
                cost[next] = newCost;
                cameFrom[next] = current;
                // Distância octil até o destino.
                var dx = Math.Abs(nc - goal.C);
                var dy = Math.Abs(nr - goal.R);
                open.Enqueue(next, newCost + (dx + dy) + (1.4142f - 2) * Math.Min(dx, dy));
            }
        }
        if (cameFrom[goalIndex] < 0) return null;

        var cells = new List<int>();
        for (var at = goalIndex; at != startIndex; at = cameFrom[at]) cells.Add(at);
        cells.Reverse();
        var path = cells.Select(i => ((i % cols + 0.5f) * cell, (i / cols + 0.5f) * cell)).ToList();
        // O último ponto é o destino exato, quando a célula dele está livre.
        if (!grid.Blocked[(int)MathF.Floor(y1 / cell) * cols + (int)MathF.Floor(x1 / cell)]) path[^1] = (x1, y1);
        return Simplify(path, grid);
    }

    /// <summary>Se o caminho em linha reta entre dois pontos passa por algum sólido.</summary>
    public static bool LineBlocked(float x0, float y0, float x1, float y1, string solid, int cell = DefaultCell)
    {
        var grid = GridFor(solid, cell, 0);
        var dist = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var steps = Math.Max(1, (int)MathF.Ceiling(dist / (cell * 0.25f)));
        for (var i = 1; i < steps; i++)
        {
            var t = (float)i / steps;
            var c = (int)MathF.Floor((x0 + (x1 - x0) * t) / cell);
            var r = (int)MathF.Floor((y0 + (y1 - y0) * t) / cell);
            if (c < 0 || r < 0 || c >= grid.Columns || r >= grid.Rows || grid.Blocked[r * grid.Columns + c]) return true;
        }
        return false;
    }

    // Tira os pontos do meio quando dá para ir direto, para o objeto não andar em zigue-zague.
    private static List<(float X, float Y)> Simplify(List<(float X, float Y)> path, Grid grid)
    {
        if (path.Count <= 2) return path;
        var result = new List<(float X, float Y)> { path[0] };
        var anchor = path[0];
        for (var i = 1; i < path.Count - 1; i++)
        {
            if (Clear(grid, anchor, path[i + 1])) continue;
            result.Add(path[i]);
            anchor = path[i];
        }
        result.Add(path[^1]);
        return result;
    }

    private static bool Clear(Grid grid, (float X, float Y) a, (float X, float Y) b)
    {
        var dist = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        var steps = Math.Max(1, (int)MathF.Ceiling(dist / (grid.Cell * 0.25f)));
        for (var i = 1; i < steps; i++)
        {
            var t = (float)i / steps;
            var x = a.X + (b.X - a.X) * t;
            var y = a.Y + (b.Y - a.Y) * t;
            // Confere as células dos dois lados do centro, para não raspar na quina.
            for (var ox = -0.3f; ox <= 0.31f; ox += 0.6f)
                for (var oy = -0.3f; oy <= 0.31f; oy += 0.6f)
                {
                    var c = (int)MathF.Floor((x + ox * grid.Cell) / grid.Cell);
                    var r = (int)MathF.Floor((y + oy * grid.Cell) / grid.Cell);
                    if (c < 0 || r < 0 || c >= grid.Columns || r >= grid.Rows || grid.Blocked[r * grid.Columns + c]) return false;
                }
        }
        return true;
    }

    private static (int C, int R)? NearestFree(Grid grid, (int C, int R) from)
    {
        for (var radius = 1; radius < 6; radius++)
            for (var dr = -radius; dr <= radius; dr++)
                for (var dc = -radius; dc <= radius; dc++)
                {
                    var c = from.C + dc;
                    var r = from.R + dr;
                    if (c >= 0 && r >= 0 && c < grid.Columns && r < grid.Rows && !grid.Blocked[r * grid.Columns + c]) return (c, r);
                }
        return null;
    }

    private static Grid GridFor(string solid, int cell, int margin)
    {
        var key = (solid.ToLowerInvariant(), cell, margin);
        if (Cache.TryGetValue(key, out var grid) && (grid.Frame == Runtime.FrameCount || Runtime.Time - grid.BuiltAt < RefreshSeconds))
            return grid;
        var cols = Math.Max(1, (int)MathF.Ceiling(Math.Max(1, Runtime.RoomWidth) / (float)cell));
        var rows = Math.Max(1, (int)MathF.Ceiling(Math.Max(1, Runtime.RoomHeight) / (float)cell));
        var blocked = new bool[cols * rows];
        void Mark(float l, float t, float r, float b)
        {
            var c0 = Math.Max(0, (int)MathF.Floor((l - margin) / cell));
            var r0 = Math.Max(0, (int)MathF.Floor((t - margin) / cell));
            var c1 = Math.Min(cols - 1, (int)MathF.Ceiling((r + margin) / cell) - 1);
            var r1 = Math.Min(rows - 1, (int)MathF.Ceiling((b + margin) / cell) - 1);
            for (var y = r0; y <= r1; y++)
                for (var x = c0; x <= c1; x++)
                    blocked[y * cols + x] = true;
        }
        foreach (var o in Runtime.InstancesOf(solid))
        {
            var b = o.BBox();
            if (b.Right > b.Left && b.Bottom > b.Top) Mark(b.Left, b.Top, b.Right, b.Bottom);
        }
        foreach (var layer in Runtime.Layers)
        {
            if (!layer.IsTiles || !string.Equals(layer.Name, solid, StringComparison.OrdinalIgnoreCase)) continue;
            if (Runtime.TilesetOf(layer) is not { Solid.Count: > 0 } set) continue;
            for (var r = 0; r < layer.Rows; r++)
                for (var c = 0; c < layer.Columns; c++)
                    if (layer.TileAt(c, r) is var id and >= 0 && set.Solid.Contains(id))
                        Mark(c * set.TileWidth, r * set.TileHeight, (c + 1) * set.TileWidth, (r + 1) * set.TileHeight);
        }
        grid = new Grid { Cell = cell, Columns = cols, Rows = rows, Blocked = blocked, BuiltAt = Runtime.Time, Frame = Runtime.FrameCount };
        Cache[key] = grid;
        return grid;
    }
}
