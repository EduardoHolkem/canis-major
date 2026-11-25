using System.Text.Json.Nodes;
using Avalonia.Controls;
using System.Text.RegularExpressions;
using CanisEditor.Game;

namespace CanisEditor;

// Objetos pais (o "parent" da ficha) e o código da sala (rooms/nome.cs), como no GameMaker.
public partial class MainWindow
{
    private const string NoParent = "(nenhum)";

    // O campo "Objeto pai" na ficha. Não lista o próprio objeto nem os filhos dele, para não fechar um círculo.
    private void AddParentChoice(string name, JsonObject def)
    {
        var options = _gameObjects.Keys
            .Where(other => !string.Equals(other, name, StringComparison.OrdinalIgnoreCase) && !DescendsFrom(other, name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        options.Insert(0, NoParent);
        var current = Str(def, "parent") ?? (_gameObjects.TryGetValue(name, out var info) ? info.Parent : null);
        AddChoice("Objeto pai", options, current ?? NoParent, "o.parent", chosen => SetParent(name, chosen == NoParent ? null : chosen));
        if (current is not null)
            AddNote($"Herda de {current}: a ficha, os eventos do script e as colisões. Collide(\"{current}\") e Game.Instances(\"{current}\") também acham este objeto.");
    }

    // Se o objeto descende de outro, seguindo os pais conhecidos.
    private bool DescendsFrom(string name, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        for (var parent = _gameObjects.GetValueOrDefault(name)?.Parent; parent is not null && seen.Add(parent); parent = _gameObjects.GetValueOrDefault(parent)?.Parent)
            if (string.Equals(parent, ancestor, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void SetParent(string name, string? parent)
    {
        var old = Str(LoadDef(name), "parent") ?? _gameObjects.GetValueOrDefault(name)?.Parent;
        // O script, se houver, passa a herdar da classe do pai, para ganhar os eventos dele.
        var script = Path.Combine(GameFiles.FolderOf(RootFolder!, "object"), name + ".cs");
        if (File.Exists(script))
            RewriteBaseClass(script, name, old, ScriptBaseFor(parent));
        EditDef(d => Set(d, "parent", parent));
    }

    // A classe base do script de um objeto com esse pai: a classe do pai, se ele tiver script.
    private string ScriptBaseFor(string? parent) =>
        parent is not null && _gameObjects.TryGetValue(parent, out var info) && info.Script ? info.Class : "GameObject";

    // Troca "class Nome : Velho" por "class Nome : Novo", só quando a base era GameObject ou o pai antigo.
    private void RewriteBaseClass(string path, string className, string? oldParent, string newBase)
    {
        var open = _documents.Find(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase));
        string text;
        try
        {
            text = open?.Document.Text ?? File.ReadAllText(path);
        }
        catch (IOException)
        {
            return;
        }
        var pattern = new Regex($@"(\bclass\s+{Regex.Escape(className)}\s*(?:\([^)]*\))?\s*:\s*)(?:CanisMajor\.)?(?<base>[A-Za-z_]\w*)");
        var match = pattern.Match(text);
        if (!match.Success)
            return;
        var currentBase = match.Groups["base"].Value;
        if (currentBase != "GameObject" && !string.Equals(currentBase, oldParent, StringComparison.OrdinalIgnoreCase))
            return; // a classe herda de outra coisa escolhida à mão: não mexe
        var updated = text[..match.Index] + match.Groups[1].Value + newBase + text[(match.Index + match.Length)..];
        if (updated == text)
            return;
        if (open is not null)
            open.Document.Text = updated;
        else
            File.WriteAllText(path, updated);
    }

    // ---------- código da sala ----------

    private string RoomScriptPath(string room) => Path.Combine(GameFiles.FolderOf(RootFolder!, "room"), room + ".cs");

    private void AddRoomCodeButton(WrapPanel actions, string room)
    {
        var path = RoomScriptPath(room);
        actions.Children.Add(File.Exists(path)
            ? SmallButton("Código da sala", "Abre rooms/" + room + ".cs, que roda quando a sala abre", () => OpenScript(path))
            : SmallButton("Código da sala", "Cria rooms/" + room + ".cs, que roda quando a sala abre (Room Creation Code)", () => CreateRoomScript(room, path)));
    }

    private void CreateRoomScript(string room, string path)
    {
        File.WriteAllText(path, ResourceTemplates.RoomCode(RootFolder!, room, """
                public override void OnCreate()
                {
                }
            """));
        RefreshTree();
        OpenScript(path);
    }
}
