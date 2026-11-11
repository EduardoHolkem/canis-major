using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

public sealed class ObjectDefTests
{
    private static string NewProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-def-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "objects"));
        Directory.CreateDirectory(Path.Combine(root, "sprites"));
        File.WriteAllText(Path.Combine(root, "projeto.json"), "{ \"name\": \"Teste\" }");
        return root;
    }

    [Fact]
    public void ObjectWithOnlyDefIsListedOnce()
    {
        var root = NewProject();
        try
        {
            File.WriteAllText(Path.Combine(root, "objects", "Moeda.json"), "{ \"sprite\": \"moeda\", \"layer\": 4 }");
            File.WriteAllText(Path.Combine(root, "objects", "Jogador.cs"), "public class Jogador : GameObject { public Jogador() { Sprite = \"heroi\"; } }");
            File.WriteAllText(Path.Combine(root, "objects", "Jogador.json"), "{ \"sprite\": \"heroi2\" }");

            var objects = GameFiles.Objects(root);
            Assert.Equal("moeda", objects["Moeda"].Sprite);
            Assert.Equal(4, objects["Moeda"].Layer);
            // A ficha vale mais que o construtor, e o objeto aponta para o script.
            Assert.Equal("heroi2", objects["Jogador"].Sprite);
            Assert.EndsWith("Jogador.cs", objects["Jogador"].Path);

            var listed = GameFiles.List(root, "object").Select(r => Path.GetFileName(r.Path)).ToList();
            Assert.Equal(["Jogador.cs", "Moeda.json"], listed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
