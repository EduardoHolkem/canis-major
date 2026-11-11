namespace CanisMajor;

/// <summary>Desenho imediato, para usar dentro de Draw(). Coordenadas do mundo, ou da tela com screen: true.</summary>
public static class Draw
{
    public static void Sprite(string name, float x, float y, float frame = 0, float scale = 1, float rotation = 0,
                              Color? color = null, float alpha = 1, string? shader = null, bool screen = false) =>
        Runtime.Sprite(name, x, y, frame, scale, scale, rotation, (color ?? Color.White).WithAlpha(alpha), shader, screen);

    public static void Text(string text, float x, float y, float size = 16, Color? color = null, Align align = Align.Left,
                            float alpha = 1, bool screen = false) =>
        Runtime.Text(text, x, y, size, (color ?? Color.White).WithAlpha(alpha), align, screen);

    public static void Rect(float x, float y, float w, float h, Color? color = null, float alpha = 1, float outline = 0,
                            float rotation = 0, bool screen = false) =>
        Runtime.Rect(x, y, w, h, (color ?? Color.White).WithAlpha(alpha), outline, rotation, screen);

    public static void Line(float x1, float y1, float x2, float y2, Color? color = null, float width = 1, float alpha = 1, bool screen = false) =>
        Runtime.Line(x1, y1, x2, y2, (color ?? Color.White).WithAlpha(alpha), width, screen);

    public static void Circle(float x, float y, float radius, Color? color = null, float alpha = 1, bool screen = false) =>
        Runtime.Circle(x, y, radius, (color ?? Color.White).WithAlpha(alpha), screen);

    /// <summary>Uma malha 3D iluminada dentro da cena 2D. Rotação em graus; size em pixels.</summary>
    public static void Mesh(Mesh mesh, float x, float y, float size, float rotationX = 0, float rotationY = 0, float rotationZ = 0,
                            Color? color = null, float alpha = 1, float z = 0, string? sprite = null, float ambient = 0.35f)
    {
        Runtime.MeshCmd(mesh, x, y, size, rotationX, rotationY, rotationZ, (color ?? Color.White).WithAlpha(alpha), z, sprite,
                        (-0.45f, -0.65f, 0.75f), ambient);
    }
}

/// <summary>Malha 3D: <c>Mesh.Cube()</c>, <c>Mesh.Sphere()</c>, <c>Mesh.Plane()</c> ou <c>Mesh.Load("modelos/nave.obj")</c>.</summary>
public sealed class Mesh
{
    private readonly uint _builtin;
    private readonly string? _path;
    private uint _handle;

    private Mesh(uint builtin, string? path)
    {
        _builtin = builtin;
        _path = path;
    }

    public static Mesh Cube() => new(1, null);
    public static Mesh Sphere() => new(2, null);
    public static Mesh Plane() => new(3, null);

    /// <summary>Um .obj, com caminho relativo à pasta do projeto.</summary>
    public static Mesh Load(string path) => new(0, path);

    internal unsafe uint Handle()
    {
        if (_handle != 0) return _handle;
        if (_path is null)
        {
            _handle = Native.cm_mesh_builtin(_builtin);
        }
        else
        {
            var full = Path.IsPathRooted(_path) ? _path : Path.Combine(Runtime.Project!.Root, _path);
            _handle = Native.WithUtf8(full, (p, len) => Native.cm_load_mesh((byte*)p, len));
            if (_handle == 0) throw new GameException(Native.LastError());
        }
        return _handle;
    }
}
