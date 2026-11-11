using CanisMajor;

namespace Exemplo;

public class Jogador : GameObject
{
    private const float Velocidade = 110;

    public Jogador()
    {
        Sprite = "jogador";
        Layer = 10;
    }

    public override void OnStep(float dt)
    {
        float dx = Keys.Axis("left", "right"), dy = Keys.Axis("up", "down");
        if (dx == 0) dx = Keys.Axis("a", "d");
        if (dy == 0) dy = Keys.Axis("w", "s");
        if (dx != 0 && dy != 0)
        {
            dx *= 0.7071f;
            dy *= 0.7071f;
        }
        Move(dx * Velocidade * dt, dy * Velocidade * dt, solid: "Parede");
        if (dx != 0) ScaleX = dx > 0 ? 1 : -1;

        if (Collide<Estrela>() is { } estrela) estrela.Coletar();
        if (Keys.Pressed("r")) Game.Restart();
    }
}
