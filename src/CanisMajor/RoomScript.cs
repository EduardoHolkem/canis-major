namespace CanisMajor;

/// <summary>
/// O código da sala (Room Creation Code do GameMaker). Fica em rooms/nome.cs, numa classe com o nome
/// da sala, e roda quando a sala abre, depois do OnCreate de todas as instâncias:
/// <code>
/// public class Noite : RoomScript
/// {
///     public override void OnCreate()
///     {
///         Lighting.Preset("noite");
///         Game.Spawn("Fantasma", 200, 120);
///     }
/// }
/// </code>
/// O nome da classe é comparado sem maiúsculas e sem sinais: a sala "fase-1" usa a classe Fase1.
/// Para escolher outro nome, use o atributo <see cref="RoomAttribute"/>.
/// </summary>
public abstract class RoomScript
{
    /// <summary>A sala deste código.</summary>
    public string Room { get; internal set; } = "";

    /// <summary>Quando a sala abre, depois do OnCreate das instâncias que estão nela.</summary>
    public virtual void OnCreate() { }

    /// <summary>A cada quadro, depois do End Step dos objetos. Bom para regras da fase, como um cronômetro.</summary>
    public virtual void OnStep(float dt) { }

    /// <summary>Desenha a interface da sala, presa à tela, depois do Draw GUI dos objetos.</summary>
    public virtual void OnDrawGui() { }

    /// <summary>Quando o jogo sai da sala (Game.Goto ou Game.Restart), antes de destruir as instâncias.</summary>
    public virtual void OnEnd() { }
}

/// <summary>Liga uma classe de <see cref="RoomScript"/> a uma sala pelo nome do arquivo: <c>[Room("fase-1")]</c>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RoomAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
