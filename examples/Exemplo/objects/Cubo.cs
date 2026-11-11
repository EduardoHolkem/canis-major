using CanisMajor;

namespace Exemplo;

/// <summary>Um elemento 3D dentro do jogo 2D: um cubo iluminado que gira.</summary>
public class Cubo : GameObject
{
    private readonly Mesh _malha = Mesh.Cube();

    public Cubo()
    {
        Layer = 4;
    }

    public override void OnDraw()
    {
        var t = Game.Time;
        Draw.Mesh(_malha, X, Y, 34, rotationX: t * 40, rotationY: t * 65, rotationZ: 15, color: "#9DB8E0");
    }
}
