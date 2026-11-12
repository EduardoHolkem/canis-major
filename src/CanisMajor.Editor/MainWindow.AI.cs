using System.Text.Json.Nodes;
using CanisEditor.Game;

namespace CanisEditor;

// Novo objeto com IA: a ficha já nasce com o componente AI ajustado para um papel comum.
public partial class MainWindow
{
    private sealed record AiPreset(string Name, string Title, string Description, JsonObject Ai);

    private static AiPreset[] AiPresets() =>
    [
        new("Guarda", "Guarda que patrulha", "Anda entre pontos e persegue o jogador quando vê. Edite os pontos em PatrolPoints.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "patrol", ["patrol_points"] = "64,64; 192,64", ["on_sight"] = "chase", ["sight_range"] = 120, ["speed"] = 40 }),
        new("Perseguidor", "Perseguidor", "Anda à toa e persegue o jogador quando vê, desviando das paredes.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "wander", ["on_sight"] = "chase", ["sight_range"] = 140, ["speed"] = 45 }),
        new("Sentinela", "Sentinela", "Fica parada até ver o jogador, aí persegue.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "idle", ["on_sight"] = "chase", ["sight_range"] = 160, ["speed"] = 50 }),
        new("Medroso", "Medroso que foge", "Anda à toa e foge do jogador quando ele chega perto.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "wander", ["on_sight"] = "flee", ["sight_range"] = 90, ["speed"] = 40, ["safe_distance"] = 150 }),
        new("Ajudante", "Ajudante que segue", "Acompanha o jogador de perto, sem atacar.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "follow", ["on_sight"] = "none", ["speed"] = 70, ["stop_distance"] = 16 }),
        new("Andarilho", "Andarilho", "Só anda à toa perto de onde nasceu, como um animal ou um morador.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "wander", ["on_sight"] = "none", ["speed"] = 30, ["wander_radius"] = 80 }),
    ];

    public void ChooseAiPreset()
    {
        if (!_isGame)
            return;
        var items = AiPresets()
            .Select(p => (p.Title, p.Description, (Action)(() => _ = CreateAiObjectAsync(p))))
            .ToList();
        OpenPalette();
        _paletteOverride = items;
        RefreshPalette();
    }

    private async Task CreateAiObjectAsync(AiPreset preset)
    {
        if (RootFolder is not { } root)
            return;
        var suggestion = preset.Name;
        for (var n = 2; GameFiles.CheckName(root, "object", suggestion) is not null && n < 100; n++)
            suggestion = preset.Name + n;
        var name = await AskTextAsync($"Novo objeto: {preset.Title}", preset.Description + " Escolha o sprite e ajuste a IA na ficha.", "Criar",
            text => GameFiles.CheckName(root, "object", text.Trim()), suggestion);
        if (name is null || RootFolder != root)
            return;
        var def = new JsonObject { ["layer"] = 5, ["y_sort"] = true, ["components"] = new JsonArray(preset.Ai.DeepClone()) };
        // Um sprite de mesmo nome, se já existir, entra sozinho.
        if (GameFiles.List(root, "sprite").FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } sprite)
            def["sprite"] = sprite.Name;
        var path = Path.Combine(GameFiles.FolderOf(root, "object"), name.Trim() + ".json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, def.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Não foi possível criar: {e.Message}");
            return;
        }
        _gameView = true;
        ReloadGameObjects();
        RefreshTree();
        OpenFile(path);
    }
}
