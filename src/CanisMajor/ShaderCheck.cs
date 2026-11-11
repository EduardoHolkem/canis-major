namespace CanisMajor;

/// <summary>Um erro de shader, com a linha contada no arquivo do usuário (1 é a primeira).</summary>
public readonly record struct ShaderError(int Line, string Message);

/// <summary>
/// Confere um shader WGSL do jeito que o jogo vai compilar, sem abrir janela nem usar a placa de vídeo.
/// O editor usa enquanto se digita.
/// </summary>
public static class ShaderCheck
{
    public static unsafe IReadOnlyList<ShaderError> Check(string source)
    {
        var count = Native.WithUtf8(source, (p, n) => Native.cm_check_shader((byte*)p, n));
        if (count == 0) return [];
        var errors = new List<ShaderError>();
        foreach (var line in Native.LastError().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && int.TryParse(line[..colon], out var number))
                errors.Add(new ShaderError(Math.Max(1, number), line[(colon + 2)..].Trim()));
            else
                errors.Add(new ShaderError(1, line.Trim()));
        }
        return errors;
    }
}
