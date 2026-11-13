using System.Reflection;

namespace CanisMajor;

// Os eventos do quadro, na ordem do GameMaker: Begin Step, alarmes, teclado e mouse, Step (e os
// componentes), colisões, End Step. O Draw GUI vem depois do desenho do mundo.
internal static partial class Runtime
{
    /// <summary>Durante o OnDrawGui, todo desenho é preso à tela e fica fora da luz.</summary>
    internal static bool InGui;

    // Quais eventos opcionais cada tipo escreveu. Os outros nem são chamados, para não gastar à toa.
    [Flags]
    private enum Hooks { None = 0, Collision = 1, Keys = 2, Mouse = 4, Gui = 8 }

    private static readonly Dictionary<Type, Hooks> HookCache = new();

    private static Hooks HooksOf(GameObject o)
    {
        var type = o.GetType();
        if (HookCache.TryGetValue(type, out var hooks)) return hooks;
        bool Overrides(string name) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == name && m.DeclaringType != typeof(GameObject));
        hooks = Hooks.None;
        if (Overrides(nameof(GameObject.OnCollision))) hooks |= Hooks.Collision;
        if (Overrides(nameof(GameObject.OnKeyPressed)) || Overrides(nameof(GameObject.OnKeyReleased))) hooks |= Hooks.Keys;
        if (Overrides(nameof(GameObject.OnClick)) || Overrides(nameof(GameObject.OnMouseEnter)) || Overrides(nameof(GameObject.OnMouseLeave))) hooks |= Hooks.Mouse;
        if (Overrides(nameof(GameObject.OnDrawGui))) hooks |= Hooks.Gui;
        HookCache[type] = hooks;
        return hooks;
    }

    /// <summary>Roda os eventos de um quadro. Separado do laço para os testes chamarem sem janela.</summary>
    internal static void StepFrame(float dt)
    {
        var list = Instances.ToArray();

        foreach (var o in list)
            if (o.Alive) o.OnBeginStep(dt);

        foreach (var o in list)
            if (o.Alive) o.TickAlarms(dt);

        // Teclado: as teclas que mudaram neste quadro.
        var pressed = new List<string>();
        var released = new List<string>();
        for (var i = 0; i < Keys.Names.Length && i < Keys.State.Length; i++)
        {
            if ((Keys.State[i] & 2) != 0) pressed.Add(Keys.Names[i]);
            if ((Keys.State[i] & 4) != 0) released.Add(Keys.Names[i]);
        }
        if (pressed.Count > 0 || released.Count > 0)
        {
            foreach (var o in list)
            {
                if (!o.Alive || (HooksOf(o) & Hooks.Keys) == 0) continue;
                foreach (var key in pressed) if (o.Alive) o.OnKeyPressed(key);
                foreach (var key in released) if (o.Alive) o.OnKeyReleased(key);
            }
        }

        // Mouse em cima dos objetos: entrar, sair e clicar.
        foreach (var o in list)
        {
            if (!o.Alive || (HooksOf(o) & Hooks.Mouse) == 0) continue;
            var b = o.BBox();
            var inside = Mouse.X >= b.Left && Mouse.X < b.Right && Mouse.Y >= b.Top && Mouse.Y < b.Bottom;
            if (inside && !o.MouseInside) o.OnMouseEnter();
            else if (!inside && o.MouseInside) o.OnMouseLeave();
            o.MouseInside = inside;
            if (!inside || !o.Alive) continue;
            for (var button = 1; button <= 3; button++)
                if (Mouse.Pressed(button) && o.Alive) o.OnClick(button);
        }

        foreach (var o in list)
        {
            if (o.Alive) o.OnStep(dt);
            if (o.Alive) o.StepComponents(dt);
        }

        // Colisões, depois de todos andarem. Cada par aparece para os dois lados que pediram.
        var current = Instances.ToArray();
        foreach (var o in current)
        {
            if (!o.Alive || (HooksOf(o) & Hooks.Collision) == 0) continue;
            foreach (var other in current)
            {
                if (!o.Alive) break;
                if (ReferenceEquals(o, other) || !other.Alive) continue;
                if (o.Collides(other)) o.OnCollision(other);
            }
        }

        foreach (var o in Instances.ToArray())
            if (o.Alive) o.OnEndStep(dt);
    }

    /// <summary>O Draw GUI: na ordem das camadas, depois do mundo, preso à tela.</summary>
    internal static void DrawGui()
    {
        InGui = true;
        DrawFlags = 1;
        try
        {
            foreach (var o in Instances.Where(o => o.Alive && o.Visible && (HooksOf(o) & Hooks.Gui) != 0).OrderBy(o => o.Layer).ToArray())
                o.OnDrawGui();
        }
        finally
        {
            InGui = false;
            DrawFlags = 0;
        }
    }
}
