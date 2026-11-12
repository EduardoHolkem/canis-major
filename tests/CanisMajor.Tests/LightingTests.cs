using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class LightingTests
{
    private sealed class Coisa : GameObject
    {
        public Coisa() { Sprite = "coisa"; }
    }

    private static void WithWorld(Action test)
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        try { test(); }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.TilesetOverride = null;
            Runtime.Instances = [];
            Runtime.Layers = [];
            Runtime.CameraX = Runtime.CameraY = 0;
        }
    }

    [Fact]
    public void EveryShaderPresetCompiles()
    {
        foreach (var preset in ShaderPresets.All)
        {
            var errors = ShaderCheck.Check(preset.Source);
            Assert.True(errors.Count == 0, $"{preset.Title}: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void LightingPresetsExistAndApply()
    {
        Lighting.Preset("noite");
        Assert.True(Lighting.Enabled);
        Assert.Equal(0.35f, Lighting.AmbientIntensity);
        Assert.Throws<GameException>(() => Lighting.Preset("marte"));
        Lighting.Reset();
        Assert.False(Lighting.Enabled);
    }

    [Fact]
    public void LightsOutsideTheViewAreSkippedAndNearestComeFirst()
    {
        WithWorld(() =>
        {
            var perto = new Coisa { Kind = "A", X = 240, Y = 135 };
            perto.Add(new Light { Radius = 50, Color = "#FF0000", OffsetY = -4 });
            var longe = new Coisa { Kind = "B", X = 20, Y = 20 };
            longe.Add(new Light { Radius = 50 });
            var fora = new Coisa { Kind = "C", X = 2000, Y = 2000 };
            fora.Add(new Light { Radius = 50 });
            Runtime.Instances = [longe, fora, perto];
            var lights = Runtime.CollectLights();
            Assert.Equal(2, lights.Count);
            Assert.Equal(240, lights[0].X);
            Assert.Equal(131, lights[0].Y);
            Assert.Equal(1f, lights[0].R);
            Assert.Equal(0f, lights[0].G);
        });
    }

    [Fact]
    public void ShadowCastersAndSolidTilesBecomeOccluders()
    {
        WithWorld(() =>
        {
            var parede = new Coisa { Kind = "Parede", X = 32, Y = 32 };
            parede.Add<ShadowCaster>();
            var semSombra = new Coisa { Kind = "Moeda", X = 64, Y = 64 };
            Runtime.Instances = [parede, semSombra];
            Runtime.TilesetOverride = _ => new TilesetData { Sprite = "t", TileWidth = 16, TileHeight = 16, Solid = [1] };
            var chao = new LayerData { Name = "Chao", Kind = "tiles", Tileset = "t", Columns = 10, CastShadows = true };
            for (var c = 2; c < 6; c++) chao.SetTile(c, 5, 1); // quatro tiles seguidos viram um retângulo só
            chao.SetTile(8, 5, 0); // decorativo, não faz sombra
            Runtime.Layers = [chao];

            var occluders = Runtime.CollectOccluders(0);
            Assert.Equal(2, occluders.Count);
            Assert.Contains((32f, 32f, 48f, 48f), occluders);
            Assert.Contains((32f, 80f, 96f, 96f), occluders);
        });
    }

    [Fact]
    public void LightComponentComesFromObjectDef()
    {
        var light = (Light)ComponentTypes.Create(new System.Text.Json.Nodes.JsonObject { ["type"] = "Light", ["radius"] = 140, ["flicker"] = 0.4, ["cast_shadows"] = false }, "teste");
        Assert.Equal(140, light.Radius);
        Assert.Equal(0.4f, light.Flicker);
        Assert.False(light.CastShadows);
    }
}
