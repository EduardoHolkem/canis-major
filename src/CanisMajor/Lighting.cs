namespace CanisMajor;

/// <summary>
/// Iluminação 2D da sala: luz ambiente, luzes (componente <see cref="Light"/>), sombras
/// (componente <see cref="ShadowCaster"/> e camadas de tiles com sombra) e normal maps
/// (sprites/nome_n.png ao lado de sprites/nome.png, carregada sozinha).
/// </summary>
public static class Lighting
{
    /// <summary>Ligada, a cena passa pela luz. Desligada, tudo aparece com a cor normal, sem custo.</summary>
    public static bool Enabled { get; set; }

    /// <summary>A cor da luz que existe em todo lugar, mesmo longe das luzes.</summary>
    public static Color Ambient { get; set; } = Color.White;

    /// <summary>Força da luz ambiente (0 escuro total, 1 normal).</summary>
    public static float AmbientIntensity { get; set; } = 1;

    /// <summary>Aplica um preset pronto: "dia", "entardecer", "noite", "caverna", "neon" ou "terror".</summary>
    public static void Preset(string name)
    {
        if (Presets.Find(name) is not { } p) throw new GameException($"O preset de luz '{name}' não existe. Os prontos são {string.Join(", ", Presets.Names)}.");
        Enabled = true;
        Ambient = Color.Hex(p.Ambient);
        AmbientIntensity = p.Intensity;
    }

    internal static void Reset()
    {
        Enabled = false;
        Ambient = Color.White;
        AmbientIntensity = 1;
    }

    /// <summary>Os presets de iluminação, usados pelo jogo e pelo editor.</summary>
    public static class Presets
    {
        public sealed record LightPreset(string Name, string Title, string Ambient, float Intensity, string LightColor, string Description);

        public static readonly LightPreset[] All =
        [
            new("dia", "Dia", "#FFFFFF", 1.0f, "#FFF4D6", "Tudo claro. As luzes quase não aparecem, mas o relevo das normal maps aparece."),
            new("entardecer", "Entardecer", "#FFB87A", 0.65f, "#FFC080", "Laranja quente, sombras longas."),
            new("noite", "Noite", "#3A4A7A", 0.35f, "#FFD9A0", "Azul escuro. Tochas e janelas brilham."),
            new("caverna", "Caverna", "#1A1A22", 0.12f, "#FFB060", "Quase preto. Só se vê perto das luzes."),
            new("neon", "Neon", "#2A1040", 0.3f, "#FF4FD8", "Roxo escuro com luzes coloridas fortes."),
            new("terror", "Terror", "#0A0C10", 0.05f, "#C8D8FF", "Escuridão total. Uma lanterna fria no jogador."),
        ];

        public static IEnumerable<string> Names => All.Select(p => p.Name);

        public static LightPreset? Find(string name) =>
            All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Iluminação da sala no arquivo (rooms/nome.json, em "lighting").</summary>
public sealed class LightingData
{
    public bool Enabled { get; set; } = true;
    public string Ambient { get; set; } = "#3A4A7A";
    public float Intensity { get; set; } = 0.35f;
}

/// <summary>
/// Uma luz presa ao objeto: tocha, lanterna, janela, olhos que brilham. Ilumina num raio, com a
/// queda suave, e pode projetar sombras dos <see cref="ShadowCaster"/>.
/// </summary>
public sealed class Light : Component
{
    public string Color { get; set; } = "#FFD9A0";
    public float Radius { get; set; } = 96;
    public float Intensity { get; set; } = 1;
    /// <summary>Quanto mais alto, mais rápido a luz cai perto da borda (2 é natural).</summary>
    public float Falloff { get; set; } = 2;
    /// <summary>Altura da luz sobre a cena, para o relevo das normal maps (0 usa um quarto do raio).</summary>
    public float Height { get; set; }
    public bool CastShadows { get; set; } = true;
    /// <summary>Deslocamento a partir da posição do objeto, como a chama na ponta da tocha.</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    /// <summary>Tremor da luz (0 a 1), como fogo.</summary>
    public float Flicker { get; set; }

    internal LightData ToData()
    {
        var c = CanisMajor.Color.Hex(Color);
        var intensity = Intensity;
        if (Flicker > 0)
        {
            // Duas ondas fora de compasso, para não parecer um pisca-pisca.
            var t = Game.Time * 9 + Owner.X * 0.13f;
            intensity *= 1 - Flicker * 0.5f * (0.5f + 0.5f * MathF.Sin(t) * MathF.Sin(t * 1.7f + 1.3f));
        }
        return new LightData
        {
            X = Owner.X + OffsetX, Y = Owner.Y + OffsetY, Radius = Radius, Intensity = intensity,
            R = c.R, G = c.G, B = c.B, Falloff = Falloff, Height = Height, Shadows = CastShadows ? 1 : 0,
        };
    }
}

/// <summary>O objeto bloqueia a luz e faz sombra, pelo retângulo de colisão (máscara ou Collider).</summary>
public sealed class ShadowCaster : Component;
