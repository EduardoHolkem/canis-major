//! Iluminação 2D: luz ambiente, luzes pontuais com queda suave, normal maps e sombras projetadas
//! por retângulos (os objetos que fazem sombra).
//!
//! São dois passes depois da cena:
//! 1. Normais: cada sprite desenha a própria normal map (ou uma normal "para a frente", recortada
//!    pelo alfa do sprite) numa textura à parte. O alfa dessa textura diz se o pixel recebe luz.
//! 2. Luz: um passe de tela cheia multiplica a cena pela soma da luz ambiente com cada luz,
//!    usando a normal para o relevo e testando o caminho até a luz contra os retângulos de sombra.

use bytemuck::{Pod, Zeroable};

pub const MAX_LIGHTS: usize = 32;
pub const MAX_OCCLUDERS: usize = 64;

/// Uma luz como vem do C#. Precisa bater com Native.LightData.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, Pod, Zeroable)]
pub struct LightData {
    pub x: f32,
    pub y: f32,
    pub radius: f32,
    pub intensity: f32,
    pub r: f32,
    pub g: f32,
    pub b: f32,
    pub falloff: f32,
    /// Altura da luz sobre a cena (para o relevo das normal maps). 0 usa um quarto do raio.
    pub height: f32,
    /// 1 projeta sombras.
    pub shadows: f32,
    pub _pad: [f32; 2],
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
pub struct LightUniform {
    /// Câmera (x, y) e tamanho da tela (w, h).
    pub view: [f32; 4],
    /// Cor da luz ambiente já multiplicada pela intensidade; w sem uso.
    pub ambient: [f32; 4],
    /// Quantas luzes e quantos retângulos de sombra; z é a suavidade da sombra.
    pub counts: [f32; 4],
    /// Duas linhas por luz: (x, y, raio, intensidade) e (r, g, b, queda).
    pub lights: [[f32; 4]; MAX_LIGHTS * 2],
    /// Terceira linha por luz: (altura, sombras, -, -).
    pub extra: [[f32; 4]; MAX_LIGHTS],
    /// Retângulos de sombra: (esquerda, topo, direita, base) no mundo.
    pub occluders: [[f32; 4]; MAX_OCCLUDERS],
}

impl Default for LightUniform {
    fn default() -> Self {
        Zeroable::zeroed()
    }
}

impl LightUniform {
    pub fn build(view: [f32; 4], ambient: [f32; 4], lights: &[LightData], occluders: &[[f32; 4]], softness: f32) -> Self {
        let mut u = LightUniform::default();
        u.view = view;
        u.ambient = ambient;
        let n = lights.len().min(MAX_LIGHTS);
        let m = occluders.len().min(MAX_OCCLUDERS);
        u.counts = [n as f32, m as f32, softness, 0.0];
        for (i, l) in lights.iter().take(n).enumerate() {
            u.lights[i * 2] = [l.x, l.y, l.radius.max(1.0), l.intensity];
            u.lights[i * 2 + 1] = [l.r, l.g, l.b, if l.falloff > 0.0 { l.falloff } else { 2.0 }];
            u.extra[i] = [l.height, l.shadows, 0.0, 0.0];
        }
        u.occluders[..m].copy_from_slice(&occluders[..m]);
        u
    }
}

/// Passe das normais: o mesmo vértice dos sprites, e o fragmento pega o alfa do sprite e a cor da
/// normal map. O quarto valor da instância (orot.w) marca o que não recebe luz (interface).
pub const NORMAL: &str = r#"struct Globals {
    proj: mat4x4<f32>,
    resolution: vec2<f32>,
    time: f32,
    _pad: f32,
    params: array<vec4<f32>, 4>,
};
@group(0) @binding(0) var<uniform> cm_u: Globals;
@group(1) @binding(0) var base_tex: texture_2d<f32>;
@group(1) @binding(1) var base_samp: sampler;
@group(2) @binding(0) var normal_tex: texture_2d<f32>;
@group(2) @binding(1) var normal_samp: sampler;

struct NOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) normal_uv: vec2<f32>,
    @location(2) unlit: f32,
    @location(3) rot: f32,
};

@vertex
fn vs(@location(0) corner: vec2<f32>, @location(1) rect: vec4<f32>, @location(2) orot: vec4<f32>,
      @location(3) uvr: vec4<f32>, @location(4) col: vec4<f32>) -> NOut {
    let local = corner * rect.zw - orot.xy;
    let c = cos(orot.z);
    let s = sin(orot.z);
    let world = rect.xy + vec2<f32>(local.x * c - local.y * s, local.x * s + local.y * c);
    var out: NOut;
    out.pos = cm_u.proj * vec4<f32>(world, 0.0, 1.0);
    out.uv = mix(uvr.xy, uvr.zw, corner);
    out.normal_uv = out.uv;
    out.unlit = orot.w;
    out.rot = orot.z;
    return out;
}

@fragment
fn fs(in: NOut) -> @location(0) vec4<f32> {
    let base = textureSampleLevel(base_tex, base_samp, in.uv, 0.0);
    if (base.a < 0.5) {
        discard;
    }
    var n = textureSampleLevel(normal_tex, normal_samp, in.normal_uv, 0.0).xyz * 2.0 - 1.0;
    // A normal gira junto com o sprite.
    let c = cos(in.rot);
    let s = sin(in.rot);
    n = vec3<f32>(n.x * c - n.y * s, n.x * s + n.y * c, n.z);
    let lit = select(1.0, 0.0, in.unlit > 0.5);
    return vec4<f32>(n * 0.5 + 0.5, lit);
}
"#;

/// Passe da luz: tela cheia, com a cena no grupo 1 e as normais no grupo 2.
pub const LIGHT: &str = r#"struct LightU {
    view: vec4<f32>,
    ambient: vec4<f32>,
    counts: vec4<f32>,
    lights: array<vec4<f32>, 64>,
    extra: array<vec4<f32>, 32>,
    occluders: array<vec4<f32>, 64>,
};
@group(0) @binding(0) var<uniform> lu: LightU;
@group(1) @binding(0) var scene_tex: texture_2d<f32>;
@group(1) @binding(1) var scene_samp: sampler;
@group(2) @binding(0) var normal_tex: texture_2d<f32>;
@group(2) @binding(1) var normal_samp: sampler;

struct LOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
};

@vertex
fn vs(@builtin(vertex_index) i: u32) -> LOut {
    let corner = vec2<f32>(f32(i & 1u), f32(i >> 1u));
    var out: LOut;
    out.pos = vec4<f32>(corner.x * 2.0 - 1.0, 1.0 - corner.y * 2.0, 0.0, 1.0);
    out.uv = corner;
    return out;
}

// Quanto do caminho entre o pixel e a luz passa livre pelos retângulos de sombra (1 livre, 0 coberto).
fn visibility(p: vec2<f32>, light: vec2<f32>) -> f32 {
    let m = i32(lu.counts.y);
    let d = light - p;
    for (var k = 0; k < m; k = k + 1) {
        let box = lu.occluders[k];
        // O próprio objeto que faz sombra continua iluminado.
        if (p.x >= box.x && p.x <= box.z && p.y >= box.y && p.y <= box.w) {
            continue;
        }
        // Teste do segmento contra o retângulo (método das faixas).
        let inv = vec2<f32>(1.0) / select(d, vec2<f32>(1e-6), abs(d) < vec2<f32>(1e-6));
        let t1 = (box.xy - p) * inv;
        let t2 = (box.zw - p) * inv;
        let tmin = max(min(t1.x, t2.x), min(t1.y, t2.y));
        let tmax = min(max(t1.x, t2.x), max(t1.y, t2.y));
        if (tmax >= max(tmin, 0.0) && tmin <= 1.0) {
            return 0.0;
        }
    }
    return 1.0;
}

@fragment
fn fs(in: LOut) -> @location(0) vec4<f32> {
    let scene = textureSampleLevel(scene_tex, scene_samp, in.uv, 0.0);
    let nd = textureSampleLevel(normal_tex, normal_samp, in.uv, 0.0);
    if (nd.a < 0.5) {
        return scene; // interface e o que não recebe luz
    }
    let n = normalize(vec3<f32>(nd.x * 2.0 - 1.0, -(nd.y * 2.0 - 1.0), nd.z * 2.0 - 1.0));
    let world = lu.view.xy + in.uv * lu.view.zw;
    var light = lu.ambient.rgb;
    let count = i32(lu.counts.x);
    for (var i = 0; i < count; i = i + 1) {
        let a = lu.lights[i * 2];
        let b = lu.lights[i * 2 + 1];
        let e = lu.extra[i];
        let to_light = a.xy - world;
        let dist = length(to_light);
        if (dist >= a.z) {
            continue;
        }
        let atten = pow(clamp(1.0 - dist / a.z, 0.0, 1.0), b.w);
        let height = select(e.x, a.z * 0.25, e.x <= 0.0);
        // Na tela, y cresce para baixo; a normal usa y para cima.
        let dir = normalize(vec3<f32>(to_light.x, -to_light.y, height));
        // Relevo relativo a uma superfície plana: o plano recebe a luz inteira, e a normal map
        // clareia o que está virado para a luz e escurece o que está de costas.
        let ndl = clamp(max(dot(n, dir), 0.0) / max(dir.z, 0.05), 0.0, 2.0);
        var vis = 1.0;
        if (e.y > 0.5) {
            vis = visibility(world, a.xy);
        }
        light = light + b.rgb * a.w * atten * ndl * vis;
    }
    return vec4<f32>(scene.rgb * light, scene.a);
}
"#;

#[cfg(test)]
mod tests {
    use super::*;

    fn validate(src: &str) {
        let module = naga::front::wgsl::parse_str(src).unwrap_or_else(|e| panic!("{}", e.emit_to_string(src)));
        let mut v = naga::valid::Validator::new(naga::valid::ValidationFlags::all(), naga::valid::Capabilities::all());
        v.validate(&module).unwrap_or_else(|e| panic!("{e:?}"));
    }

    #[test]
    fn shaders_validate() {
        validate(NORMAL);
        validate(LIGHT);
    }

    #[test]
    fn uniform_layout_matches_wgsl() {
        // 3 vec4 + 64 + 32 + 64 vec4, 16 bytes cada.
        assert_eq!(std::mem::size_of::<LightUniform>(), (3 + 64 + 32 + 64) * 16);
        assert_eq!(std::mem::size_of::<LightData>(), 48);
    }

    #[test]
    fn build_clamps_counts_and_defaults_falloff() {
        let lights = vec![LightData { radius: 0.0, intensity: 1.0, ..Default::default() }; 40];
        let u = LightUniform::build([0.0; 4], [0.1, 0.1, 0.1, 1.0], &lights, &[[0.0, 0.0, 1.0, 1.0]], 0.0);
        assert_eq!(u.counts[0], MAX_LIGHTS as f32);
        assert_eq!(u.counts[1], 1.0);
        assert_eq!(u.lights[1][3], 2.0);
        assert_eq!(u.lights[0][2], 1.0);
    }
}
