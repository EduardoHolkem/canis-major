namespace CanisMajor;

/// <summary>Teclado: <c>Keys.Held("left")</c>, <c>Keys.Pressed("space")</c>, <c>Keys.Axis("a", "d")</c>.</summary>
public static class Keys
{
    private static readonly Dictionary<string, int> Indices = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly byte[] State = new byte[128];

    private static int Index(string name)
    {
        if (Indices.TryGetValue(name, out var i)) return i;
        if (!Runtime.Started) throw new InvalidOperationException("O teclado só funciona com o jogo rodando.");
        i = Native.WithUtf8(name, (p, len) => { unsafe { return Native.cm_key_index((byte*)p, len); } });
        if (i < 0) throw new ArgumentException($"Tecla desconhecida: \"{name}\". Exemplos: \"left\", \"space\", \"a\", \"enter\", \"escape\".");
        Indices[name] = i;
        return i;
    }

    /// <summary>A tecla está apertada agora.</summary>
    public static bool Held(string name) => (State[Index(name)] & 1) != 0;

    /// <summary>A tecla foi apertada neste quadro.</summary>
    public static bool Pressed(string name) => (State[Index(name)] & 2) != 0;

    /// <summary>A tecla foi solta neste quadro.</summary>
    public static bool Released(string name) => (State[Index(name)] & 4) != 0;

    /// <summary>-1, 0 ou 1, conforme as teclas negativa e positiva.</summary>
    public static int Axis(string negative, string positive) => (Held(positive) ? 1 : 0) - (Held(negative) ? 1 : 0);
}

/// <summary>Mouse: posição no mundo do jogo e botões (1 esquerdo, 2 meio, 3 direito).</summary>
public static class Mouse
{
    internal static readonly byte[] State = new byte[4];

    public static float X { get; internal set; }
    public static float Y { get; internal set; }
    public static float ScreenX { get; internal set; }
    public static float ScreenY { get; internal set; }
    public static float Wheel { get; internal set; }

    private static byte Bits(int button) => button is >= 1 and <= 3 ? State[button - 1] : (byte)0;

    public static bool Held(int button = 1) => (Bits(button) & 1) != 0;
    public static bool Pressed(int button = 1) => (Bits(button) & 2) != 0;
    public static bool Released(int button = 1) => (Bits(button) & 4) != 0;
}
