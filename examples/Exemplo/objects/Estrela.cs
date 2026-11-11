using CanisMajor;

namespace Exemplo;

public class Estrela : GameObject
{
    private float _baseY;
    private float _fase;

    public Estrela()
    {
        Sprite = "estrela";
        Shader = "brilho";
        Layer = 5;
        ScaleX = ScaleY = 2;
    }

    public override void OnCreate()
    {
        _baseY = Y;
        _fase = Random.Shared.NextSingle() * 6.28f;
    }

    public override void OnStep(float dt)
    {
        Y = _baseY + MathF.Sin(Game.Time * 3 + _fase) * 3;
        Rotation += 40 * dt;
    }

    public void Coletar()
    {
        var pontos = Game.Data.TryGetValue("pontos", out var p) ? (int)p! : 0;
        Game.Data["pontos"] = pontos + 1;
        Game.PlaySound("coleta");
        Destroy();
        // Nasce outra num lugar livre.
        for (var i = 0; i < 50; i++)
        {
            var nova = Game.Spawn<Estrela>(Random.Shared.Next(32, Game.RoomWidth - 32), Random.Shared.Next(32, Game.RoomHeight - 32));
            if (nova.Collide("Parede") is null) break;
            nova.Destroy();
        }
    }
}
