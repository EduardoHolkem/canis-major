using CanisMajor;

namespace Exemplo;

// O código da sala noite. Roda quando ela abre, depois do OnCreate das instâncias (Room Creation Code).
[Room("noite")]
public class SalaNoite : RoomScript
{
    public override void OnCreate()
    {
        // O fantasma azul é filho do Fantasma: herda a IA e a ficha e só troca a cor e a luz.
        Game.Spawn("FantasmaAzul", 100, 200);
    }

    public override void OnDrawGui()
    {
        // Game.Count("Fantasma") conta também os filhos, como o azul.
        Draw.Text($"Fantasmas: {Game.Count("Fantasma")}", Game.Width - 12, Game.Height - 24, size: 12, color: "#C8D0FF", align: Align.Right);
    }
}
