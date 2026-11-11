// Shader de tela: a imagem ondula, como vista através da água.
fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
    let onda = vec2<f32>(sin(uv.y * 30.0 + time() * 3.0), cos(uv.x * 25.0 + time() * 2.0)) * 0.003;
    return sample(uv + onda);
}
