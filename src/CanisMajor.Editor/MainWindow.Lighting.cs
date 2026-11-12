using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Layout;
using CanisEditor.Game;
using Lighting = CanisMajor.Lighting;
using ShaderPresets = CanisMajor.ShaderPresets;

namespace CanisEditor;

// Iluminação da sala no Inspetor e shaders a partir dos presets.
public partial class MainWindow
{
    private void BuildLightingSection(RoomModel room)
    {
        AddHeading("Iluminação");
        var light = room.Properties["lighting"] as JsonObject;
        var on = light is not null && (light["enabled"] is not JsonValue v || !v.TryGetValue<bool>(out var e) || e);
        AddCheck("Ligar a iluminação 2D", on, "r.light.on", value => EditLighting(room, json =>
        {
            if (!value)
                return null;
            return json ?? new JsonObject { ["enabled"] = true, ["ambient"] = "#3A4A7A", ["intensity"] = 0.35 };
        }));
        if (!on || light is null)
        {
            AddNote("Desligada, a sala aparece com as cores normais. Ligada, fica no escuro da luz ambiente, e os objetos com o componente Light iluminam.");
            return;
        }

        var presets = Lighting.Presets.All.Select(p => p.Title).ToList();
        presets.Insert(0, "(escolha um preset)");
        AddChoice("Preset", presets, presets[0], "r.light.preset", chosen =>
        {
            if (Lighting.Presets.Find(chosen) is not { } p)
                return;
            EditLighting(room, json =>
            {
                json!["ambient"] = p.Ambient;
                json["intensity"] = p.Intensity;
                return json;
            });
            ShowStatus($"{p.Title}: {p.Description} Cor sugerida para as luzes: {p.LightColor}.");
        });
        AddTextField("Cor ambiente", light["ambient"]?.GetValue<string>() ?? "#FFFFFF", "r.light.ambient", typed =>
            EditLighting(room, json =>
            {
                if (Avalonia.Media.Color.TryParse(typed.Trim(), out _))
                    json!["ambient"] = typed.Trim();
                return json;
            }));
        var intensity = light["intensity"] is JsonValue iv && iv.TryGetValue<double>(out var d) ? d : 1;
        AddNumberField("Intensidade", intensity, "r.light.intensity", value => EditLighting(room, json =>
        {
            json!["intensity"] = Math.Round(Math.Clamp(value, 0, 2), 3);
            return json;
        }));
        AddNote("Intensidade 0 é escuro total e 1 é normal. As luzes vêm do componente Light nos objetos (tocha, lanterna, janela), e as sombras do componente ShadowCaster ou de uma camada de tiles com \"Fazer sombra\". Um sprite ganha relevo com a normal map ao lado: sprites/nome_n.png.");
        AddNote("O editor mostra a sala sem a luz. Use ▶ Testar esta sala para ver o resultado.");
    }

    private void EditLighting(RoomModel room, Func<JsonObject?, JsonObject?> change)
    {
        var current = room.Properties["lighting"] as JsonObject;
        var result = change(current is null ? null : (JsonObject)current.DeepClone());
        if (result is null)
            room.Properties.Remove("lighting");
        else
            room.Properties["lighting"] = result;
        WriteRoom();
        BuildInspector();
    }

    // Novo shader a partir de um preset: a lista aparece na barra de comando.
    public void ChooseShaderPreset()
    {
        if (!_isGame)
            return;
        var items = ShaderPresets.All
            .Select(p => (p.Title, $"para {p.Use}", (Action)(() => _ = CreateShaderFromPresetAsync(p))))
            .ToList();
        OpenPalette();
        _paletteOverride = items;
        RefreshPalette();
    }

    private async Task CreateShaderFromPresetAsync(ShaderPresets.Preset preset)
    {
        if (RootFolder is not { } root)
            return;
        var suggestion = preset.Name;
        for (var n = 2; GameFiles.CheckName(root, "shader", suggestion) is not null && n < 100; n++)
            suggestion = preset.Name + n;
        var name = await AskTextAsync($"Novo shader: {preset.Title}", $"Bom para {preset.Use}. Num objeto, escolha o shader na ficha; na tela inteira, ponha o nome em \"Shaders de tela\" da sala.", "Criar",
            text => GameFiles.CheckName(root, "shader", text.Trim()), suggestion);
        if (name is null || RootFolder != root)
            return;
        var path = Path.Combine(GameFiles.FolderOf(root, "shader"), name.Trim() + ".wgsl");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, preset.Source.Replace("\r\n", "\n"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Não foi possível criar: {e.Message}");
            return;
        }
        _gameView = true;
        RefreshTree();
        OpenFile(path);
    }
}
