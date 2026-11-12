using System.Runtime.InteropServices;
using System.Text;

namespace CanisMajor;

/// <summary>Um comando de desenho. Precisa bater campo a campo com gfx::DrawCmd no Rust (128 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawCmd
{
    public uint Kind, Texture, Shader, Mesh;
    public float X, Y, W, H, Ox, Oy, Rot, Z;
    public float U0, V0, U1, V1;
    public float R, G, B, A;
    public float Rx, Ry, Rz, Size;
    public uint StrOff, StrLen, Align, Flags;
    public float L0, L1, L2, L3;
}

/// <summary>Uma luz para o núcleo. Precisa bater com light::LightData no Rust (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LightData
{
    public float X, Y, Radius, Intensity, R, G, B, Falloff, Height, Shadows, Pad0, Pad1;
}

/// <summary>Estado da entrada vindo do Rust. Bits: 1 segurando, 2 apertou, 4 soltou.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct InputState
{
    public fixed byte Keys[128];
    public fixed byte Buttons[4];
    public float MouseX, MouseY, Wheel, WinW, WinH, ViewX, ViewY, ViewScale;
    public uint Focused;
}

internal static unsafe partial class Native
{
    private const string Lib = "canis_core";
    public const uint ApiVersion = 2;

    [LibraryImport(Lib)] public static partial uint cm_version();
    [LibraryImport(Lib)] public static partial int cm_init(byte* title, nuint len, uint width, uint height, uint scale, uint vsync, uint pixelArt);
    [LibraryImport(Lib)] public static partial nuint cm_last_error(byte* buf, nuint cap);
    [LibraryImport(Lib)] public static partial int cm_poll(InputState* state);
    [LibraryImport(Lib)] public static partial int cm_key_index(byte* name, nuint len);
    [LibraryImport(Lib)] public static partial uint cm_load_texture(byte* path, nuint len);
    [LibraryImport(Lib)] public static partial uint cm_create_texture(byte* rgba, uint w, uint h);
    [LibraryImport(Lib)] public static partial int cm_texture_size(uint handle, uint* w, uint* h);
    [LibraryImport(Lib)] public static partial int cm_load_shader(byte* source, nuint len);
    [LibraryImport(Lib)] public static partial int cm_check_shader(byte* source, nuint len);
    [LibraryImport(Lib)] public static partial void cm_set_shader_param(uint shader, uint index, float x, float y, float z, float w);
    [LibraryImport(Lib)] public static partial void cm_set_normal_map(uint texture, uint normal);
    [LibraryImport(Lib)]
    public static partial void cm_set_lighting(uint on, float* ambient, float softness, LightData* lights, nuint lightCount, float* occluders, nuint occluderCount);
    [LibraryImport(Lib)] public static partial uint cm_mesh_builtin(uint kind);
    [LibraryImport(Lib)] public static partial uint cm_load_mesh(byte* path, nuint len);
    [LibraryImport(Lib)]
    public static partial int cm_frame(float* clear, float camX, float camY, float time, DrawCmd* cmds, nuint count,
                                       byte* strings, nuint stringsLen, uint* post, nuint postCount);
    [LibraryImport(Lib)] public static partial void cm_measure_text(byte* text, nuint len, float size, float* w, float* h);
    [LibraryImport(Lib)] public static partial uint cm_load_sound(byte* path, nuint len);
    [LibraryImport(Lib)] public static partial void cm_play_sound(uint id, float volume, uint looped);
    [LibraryImport(Lib)] public static partial void cm_stop_sounds();
    [LibraryImport(Lib)] public static partial void cm_set_title(byte* title, nuint len);
    [LibraryImport(Lib)] public static partial int cm_screenshot(byte* path, nuint len);
    [LibraryImport(Lib)] public static partial void cm_shutdown();

    public static string LastError()
    {
        var len = (int)cm_last_error(null, 0);
        if (len == 0) return "";
        var buf = new byte[len];
        fixed (byte* p = buf) cm_last_error(p, (nuint)len);
        return Encoding.UTF8.GetString(buf);
    }

    /// <summary>Chama uma função nativa com um texto em UTF-8.</summary>
    public static T WithUtf8<T>(string text, Func<nint, nuint, T> call)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes) return call((nint)p, (nuint)bytes.Length);
    }
}
