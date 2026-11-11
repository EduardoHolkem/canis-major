using System.Globalization;

namespace CanisMajor;

/// <summary>Cor de 0 a 1. Aceita texto: <c>Color = "#9DB8E0"</c>.</summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static readonly Color White = new(1, 1, 1);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Transparent = new(0, 0, 0, 0);

    /// <summary>Lê "#RGB", "#RRGGBB" ou "#RRGGBBAA".</summary>
    public static Color Hex(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length is not (6 or 8) || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Cor inválida: \"{hex}\". Use algo como \"#9DB8E0\".");
        if (h.Length == 6) v = (v << 8) | 0xFF;
        return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }

    public static implicit operator Color(string hex) => Hex(hex);

    public Color WithAlpha(float alpha) => this with { A = A * alpha };

    public override string ToString() =>
        $"#{(int)MathF.Round(R * 255):X2}{(int)MathF.Round(G * 255):X2}{(int)MathF.Round(B * 255):X2}{(A < 1 ? ((int)MathF.Round(A * 255)).ToString("X2") : "")}";
}
