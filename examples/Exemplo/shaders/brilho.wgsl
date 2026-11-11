// Shader de sprite: a estrela pulsa e ganha um brilho dourado.
fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
    let pixel = sample(uv) * color;
    let pulso = 0.75 + 0.25 * sin(time() * 6.0);
    return vec4<f32>(mix(pixel.rgb, vec3<f32>(1.0, 0.92, 0.6), 0.35 * pulso), pixel.a);
}
