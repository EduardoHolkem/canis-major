namespace CanisEditor;

// Novo objeto com IA: os papéis prontos (guarda, perseguidor, medroso...) agora são modelos de
// objeto na janela de modelos (Game/ResourceTemplates.cs), com o nome começando por "IA:".
public partial class MainWindow
{
    public void ChooseAiPreset() => ShowTemplatePicker("object");
}
