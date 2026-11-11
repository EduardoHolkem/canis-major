using CanisMajor;

namespace Exemplo;

public class Placar : GameObject
{
    public Placar()
    {
        Layer = 100;
    }

    public override void OnDraw()
    {
        var pontos = Game.Data.TryGetValue("pontos", out var p) ? (int)p! : 0;
        Draw.Rect(8, 8, 150, 22, "#0D0F13", alpha: 0.6f, screen: true);
        Draw.Text($"Estrelas: {pontos}", 16, 11, size: 14, color: "#ECEEF2", screen: true);
        Draw.Text($"{Game.Fps:0} fps", Game.Width - 12, 11, size: 12, color: "#A3A8B3", align: Align.Right, screen: true);
    }
}
