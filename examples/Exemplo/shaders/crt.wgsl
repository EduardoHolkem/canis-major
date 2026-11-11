// Shader de tela: linhas de TV antiga e leve curvatura.
fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
    var c = uv * 2.0 - vec2<f32>(1.0);
    c = c * (1.0 + dot(c, c) * 0.04);
    let tela = c * 0.5 + vec2<f32>(0.5);
    if (tela.x < 0.0 || tela.x > 1.0 || tela.y < 0.0 || tela.y > 1.0) {
        return vec4<f32>(0.0, 0.0, 0.0, 1.0);
    }
    let linha = 0.85 + 0.15 * sin(tela.y * resolution().y * 3.14159);
    return vec4<f32>(sample(tela).rgb * linha, 1.0);
}
