namespace CanisMajor;

/// <summary>
/// Shaders prontos para começar, sem escrever WGSL do zero. O editor oferece em "Novo shader".
/// Os que usam param(0) dizem no comentário o que cada número faz, para mexer pelo componente
/// ShaderParams (sem código) ou por Game.SetShaderParam.
/// </summary>
public static class ShaderPresets
{
    public sealed record Preset(string Name, string Title, string Use, string Source);

    public static readonly Preset[] All =
    [
        new("basico", "Básico", "sprite ou tela", """
            // A cor do sprite, sem efeito. Ponto de partida para escrever o seu.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                return sample(uv) * color;
            }

            """),
        new("brilho", "Brilho pulsante", "sprite", """
            // Clareia e escurece no ritmo do tempo. param(0).x = velocidade (0 usa 4).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let speed = select(param(0).x, 4.0, param(0).x == 0.0);
                let pixel = sample(uv) * color;
                let glow = 0.85 + 0.25 * sin(time() * speed);
                return vec4<f32>(pixel.rgb * glow, pixel.a);
            }

            """),
        new("contorno", "Contorno", "sprite", """
            // Uma borda de 1 pixel em volta do sprite. param(0).xyz = cor (preto se vazio).
            // O sprite precisa de uma folga transparente em volta para a borda aparecer.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let pixel = sample(uv) * color;
                if (pixel.a > 0.5) {
                    return pixel;
                }
                let size = vec2<f32>(textureDimensions(cm_tex));
                let step = 1.0 / size;
                let around = sample(uv + vec2<f32>(step.x, 0.0)).a + sample(uv - vec2<f32>(step.x, 0.0)).a
                    + sample(uv + vec2<f32>(0.0, step.y)).a + sample(uv - vec2<f32>(0.0, step.y)).a;
                if (around > 0.5) {
                    return vec4<f32>(param(0).xyz, 1.0);
                }
                return pixel;
            }

            """),
        new("dano", "Flash de dano", "sprite", """
            // Pinta o sprite de branco. param(0).x = força de 0 a 1 (ponha 1 ao levar dano e vá baixando).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let pixel = sample(uv) * color;
                return vec4<f32>(mix(pixel.rgb, vec3<f32>(1.0), clamp(param(0).x, 0.0, 1.0)), pixel.a);
            }

            """),
        new("dissolver", "Dissolver", "sprite", """
            // Some aos pedaços, com a borda brilhando. param(0).x = quanto já sumiu (0 a 1).
            fn ruido(p: vec2<f32>) -> f32 {
                return fract(sin(dot(floor(p), vec2<f32>(12.9898, 78.233))) * 43758.5453);
            }

            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let pixel = sample(uv) * color;
                let n = ruido(uv * 64.0);
                let amount = clamp(param(0).x, 0.0, 1.0);
                if (n < amount) {
                    return vec4<f32>(0.0);
                }
                if (n < amount + 0.06) {
                    return vec4<f32>(1.0, 0.6, 0.2, pixel.a);
                }
                return pixel;
            }

            """),
        new("ondas", "Ondas (água ou calor)", "sprite ou tela", """
            // Ondula a imagem, como água ou ar quente. param(0).x = força (0 usa 0.004).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let strength = select(param(0).x, 0.004, param(0).x == 0.0);
                let offset = vec2<f32>(sin(uv.y * 40.0 + time() * 3.0), cos(uv.x * 30.0 + time() * 2.0)) * strength;
                return sample(uv + offset) * color;
            }

            """),
        new("cinza", "Preto e branco", "sprite ou tela", """
            // Tira as cores. param(0).x = quanto (0 usa 1, tudo cinza).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let pixel = sample(uv) * color;
                let gray = dot(pixel.rgb, vec3<f32>(0.299, 0.587, 0.114));
                let amount = select(param(0).x, 1.0, param(0).x == 0.0);
                return vec4<f32>(mix(pixel.rgb, vec3<f32>(gray), amount), pixel.a);
            }

            """),
        new("pixelizar", "Pixelizar", "tela", """
            // Junta os pixels em blocos maiores. param(0).x = tamanho do bloco (0 usa 4).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let block = select(param(0).x, 4.0, param(0).x == 0.0);
                let cells = resolution() / block;
                return sample((floor(uv * cells) + 0.5) / cells) * color;
            }

            """),
        new("crt", "Tela antiga (CRT)", "tela", """
            // Linhas de varredura e cantos escuros, como uma TV de tubo.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let pixel = sample(uv);
                let scan = 0.85 + 0.15 * sin(uv.y * resolution().y * 3.14159);
                let d = uv - 0.5;
                let corner = 1.0 - dot(d, d) * 1.2;
                return vec4<f32>(pixel.rgb * scan * corner, 1.0);
            }

            """),
        new("vinheta", "Vinheta", "tela", """
            // Escurece as bordas da tela. param(0).x = força (0 usa 0.6).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let strength = select(param(0).x, 0.6, param(0).x == 0.0);
                let d = distance(uv, vec2<f32>(0.5));
                let pixel = sample(uv);
                return vec4<f32>(pixel.rgb * (1.0 - smoothstep(0.35, 0.85, d) * strength), pixel.a);
            }

            """),
        new("cromatico", "Aberração cromática", "tela", """
            // Separa o vermelho e o azul nas bordas, como uma lente barata. param(0).x = força (0 usa 0.003).
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let strength = select(param(0).x, 0.003, param(0).x == 0.0);
                let d = (uv - 0.5) * strength * 2.0;
                return vec4<f32>(sample(uv + d).r, sample(uv).g, sample(uv - d).b, sample(uv).a);
            }

            """),
    ];

    public static Preset? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase));
}
