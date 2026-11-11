// Shader de tela: escurece as bordas e separa um pouco as cores nas pontas.
fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
    let centro = uv - vec2<f32>(0.5);
    let d = length(centro);
    let desvio = centro * 0.01 * d;
    let rgb = vec3<f32>(sample(uv + desvio).r, sample(uv).g, sample(uv - desvio).b);
    let vinheta = 1.0 - smoothstep(0.35, 0.85, d);
    return vec4<f32>(rgb * vinheta, 1.0);
}
