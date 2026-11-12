namespace CanisMajor;

// Junta as luzes e as sombras do quadro e manda para o núcleo.
internal static unsafe partial class Runtime
{
    internal const int MaxLights = 32;
    internal const int MaxOccluders = 64;

    /// <summary>As luzes do quadro, das mais perto da câmera para as mais longe, até o limite.</summary>
    internal static List<LightData> CollectLights()
    {
        var viewW = Project?.Info.Width ?? 480;
        var viewH = Project?.Info.Height ?? 270;
        var cx = CameraX + viewW / 2f;
        var cy = CameraY + viewH / 2f;
        var lights = new List<LightData>();
        foreach (var o in Instances)
        {
            if (!o.Alive) continue;
            foreach (var c in o.Components)
                if (c is Light { Enabled: true } light)
                {
                    var data = light.ToData();
                    // Fora da tela, mais longe que o próprio raio, não ilumina nada visível.
                    if (data.X + data.Radius < CameraX || data.X - data.Radius > CameraX + viewW
                        || data.Y + data.Radius < CameraY || data.Y - data.Radius > CameraY + viewH) continue;
                    lights.Add(data);
                }
        }
        return lights.OrderBy(l => (l.X - cx) * (l.X - cx) + (l.Y - cy) * (l.Y - cy)).Take(MaxLights).ToList();
    }

    /// <summary>
    /// Os retângulos que fazem sombra: objetos com ShadowCaster e tiles sólidos das camadas com sombra.
    /// Tiles vizinhos na mesma linha viram um retângulo só, para caber mais.
    /// </summary>
    internal static List<(float L, float T, float R, float B)> CollectOccluders(float margin)
    {
        var viewW = Project?.Info.Width ?? 480;
        var viewH = Project?.Info.Height ?? 270;
        var (vl, vt, vr, vb) = (CameraX - margin, CameraY - margin, CameraX + viewW + margin, CameraY + viewH + margin);
        var result = new List<(float, float, float, float)>();
        foreach (var o in Instances)
        {
            if (!o.Alive || o.Get<ShadowCaster>() is not { Enabled: true }) continue;
            var b = o.BBox();
            if (b.Right < vl || b.Left > vr || b.Bottom < vt || b.Top > vb) continue;
            result.Add(b);
        }
        foreach (var layer in Layers)
        {
            if (!layer.IsTiles || !layer.CastShadows || !layer.Visible || TilesetOf(layer) is not { Solid.Count: > 0 } set) continue;
            var c0 = Math.Max(0, (int)MathF.Floor(vl / set.TileWidth));
            var c1 = Math.Min(layer.Columns - 1, (int)MathF.Floor(vr / set.TileWidth));
            var r0 = Math.Max(0, (int)MathF.Floor(vt / set.TileHeight));
            var r1 = Math.Min(layer.Rows - 1, (int)MathF.Floor(vb / set.TileHeight));
            for (var r = r0; r <= r1; r++)
            {
                var start = -1;
                for (var c = c0; c <= c1 + 1; c++)
                {
                    var solid = c <= c1 && layer.TileAt(c, r) is var id and >= 0 && set.Solid.Contains(id);
                    if (solid && start < 0) start = c;
                    else if (!solid && start >= 0)
                    {
                        result.Add((start * set.TileWidth, r * set.TileHeight, c * set.TileWidth, (r + 1) * set.TileHeight));
                        start = -1;
                    }
                }
            }
        }
        return result.Take(MaxOccluders).ToList();
    }

    private static void SubmitLighting()
    {
        if (!Lighting.Enabled)
        {
            Native.cm_set_lighting(0, null, 0, null, 0, null, 0);
            return;
        }
        var lights = CollectLights();
        var margin = lights.Count == 0 ? 0 : lights.Max(l => l.Radius);
        var occluders = lights.Any(l => l.Shadows > 0) ? CollectOccluders(margin) : [];
        var ambient = stackalloc float[4] { Lighting.Ambient.R, Lighting.Ambient.G, Lighting.Ambient.B, Lighting.AmbientIntensity };
        var lightArray = lights.ToArray();
        var occArray = new float[occluders.Count * 4];
        for (var i = 0; i < occluders.Count; i++)
            (occArray[i * 4], occArray[i * 4 + 1], occArray[i * 4 + 2], occArray[i * 4 + 3]) = occluders[i];
        fixed (LightData* l = lightArray)
        fixed (float* o = occArray)
            Native.cm_set_lighting(1, ambient, 0, l, (nuint)lightArray.Length, o, (nuint)occluders.Count);
    }
}
