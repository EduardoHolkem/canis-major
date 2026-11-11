//! Shaders da engine em WGSL. O usuário escreve só a função `effect`; a engine completa o resto.
//!
//! ```wgsl
//! fn effect(uv: vec2f, color: vec4f) -> vec4f {
//!     return sample(uv) * color;
//! }
//! ```
//!
//! Ajudas disponíveis: `sample(uv)`, `time()`, `resolution()` e `param(i)` (quatro vec4f por shader,
//! preenchidos pelo jogo). O uv (0, 0) é o canto de cima à esquerda.

pub const HEADER: &str = r#"struct Globals {
    proj: mat4x4<f32>,
    resolution: vec2<f32>,
    time: f32,
    _pad: f32,
    params: array<vec4<f32>, 4>,
};
@group(0) @binding(0) var<uniform> cm_u: Globals;
@group(1) @binding(0) var cm_tex: texture_2d<f32>;
@group(1) @binding(1) var cm_samp: sampler;

struct CmOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
};

@vertex
fn cm_vs_sprite(@location(0) corner: vec2<f32>, @location(1) rect: vec4<f32>, @location(2) orot: vec4<f32>,
                @location(3) uvr: vec4<f32>, @location(4) col: vec4<f32>) -> CmOut {
    let local = corner * rect.zw - orot.xy;
    let c = cos(orot.z);
    let s = sin(orot.z);
    let world = rect.xy + vec2<f32>(local.x * c - local.y * s, local.x * s + local.y * c);
    var out: CmOut;
    out.pos = cm_u.proj * vec4<f32>(world, 0.0, 1.0);
    out.uv = mix(uvr.xy, uvr.zw, corner);
    out.color = col;
    return out;
}

@vertex
fn cm_vs_full(@builtin(vertex_index) i: u32) -> CmOut {
    let corner = vec2<f32>(f32(i & 1u), f32(i >> 1u));
    var out: CmOut;
    out.pos = vec4<f32>(corner.x * 2.0 - 1.0, 1.0 - corner.y * 2.0, 0.0, 1.0);
    out.uv = corner;
    out.color = vec4<f32>(1.0);
    return out;
}

fn sample(uv: vec2<f32>) -> vec4<f32> { return textureSampleLevel(cm_tex, cm_samp, uv, 0.0); }
fn time() -> f32 { return cm_u.time; }
fn resolution() -> vec2<f32> { return cm_u.resolution; }
fn param(i: i32) -> vec4<f32> { return cm_u.params[clamp(i, 0, 3)]; }
"#;

pub const FOOTER: &str = r#"
@fragment
fn cm_fs(in: CmOut) -> @location(0) vec4<f32> {
    return effect(in.uv, in.color);
}
"#;

pub const DEFAULT_EFFECT: &str = "fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {\n    return sample(uv) * color;\n}\n";

pub const MESH: &str = r#"struct MeshU {
    mvp: mat4x4<f32>,
    normal_mat: mat4x4<f32>,
    color: vec4<f32>,
    light: vec4<f32>,
    flags: vec4<f32>,
};
@group(0) @binding(0) var<uniform> m: MeshU;
@group(1) @binding(0) var m_tex: texture_2d<f32>;
@group(1) @binding(1) var m_samp: sampler;

struct MOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) uv: vec2<f32>,
};

@vertex
fn vs(@location(0) pos: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>) -> MOut {
    var out: MOut;
    out.pos = m.mvp * vec4<f32>(pos, 1.0);
    out.normal = normalize((m.normal_mat * vec4<f32>(normal, 0.0)).xyz);
    out.uv = uv;
    return out;
}

@fragment
fn fs(in: MOut) -> @location(0) vec4<f32> {
    var base = m.color;
    if (m.flags.x > 0.5) {
        base = base * textureSampleLevel(m_tex, m_samp, in.uv, 0.0);
    }
    let diffuse = max(dot(normalize(in.normal), normalize(m.light.xyz)), 0.0);
    let ambient = m.light.w;
    return vec4<f32>(base.rgb * (ambient + (1.0 - ambient) * diffuse), base.a);
}
"#;

pub fn header_lines() -> usize {
    HEADER.lines().count()
}

/// Monta o módulo completo com o código do usuário no meio.
pub fn compose(user: &str) -> String {
    let body = if user.contains("fn effect") { user } else { DEFAULT_EFFECT };
    format!("{HEADER}{body}\n{FOOTER}")
}

/// Confere o WGSL com o naga antes de chegar à GPU, para dar a linha certa do arquivo do usuário.
pub fn check(user: &str) -> Result<String, Vec<(usize, String)>> {
    let full = compose(user);
    let offset = header_lines();
    let user_lines = user.lines().count().max(1);
    let to_user = |line: u32| -> usize {
        let l = line as usize;
        if l > offset && l <= offset + user_lines { l - offset } else { 0 }
    };
    let module = match naga::front::wgsl::parse_str(&full) {
        Ok(m) => m,
        Err(e) => {
            let line = e.location(&full).map(|l| to_user(l.line_number)).unwrap_or(0);
            let mut msg = e.message().to_string();
            if let Some((_, label)) = e.labels().next() {
                if !label.is_empty() {
                    msg = format!("{msg} ({label})");
                }
            }
            return Err(vec![(line, msg)]);
        }
    };
    let mut validator = naga::valid::Validator::new(naga::valid::ValidationFlags::all(), naga::valid::Capabilities::all());
    if let Err(e) = validator.validate(&module) {
        let line = e.spans().next().map(|(span, _)| to_user(span.location(&full).line_number)).unwrap_or(0);
        let mut msg = e.as_inner().to_string();
        let mut source: Option<&dyn std::error::Error> = std::error::Error::source(e.as_inner());
        while let Some(s) = source {
            msg = format!("{msg}: {s}");
            source = s.source();
        }
        return Err(vec![(line, msg)]);
    }
    Ok(full)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_effect_validates() {
        assert!(check(DEFAULT_EFFECT).is_ok());
        assert!(check("// sem effect, usa o padrão\n").is_ok());
    }

    #[test]
    fn error_points_at_user_line() {
        let src = "fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {\n    let x = 1.0;\n    return cor;\n}\n";
        let errs = check(src).unwrap_err();
        assert_eq!(errs[0].0, 3, "{errs:?}");
    }

    #[test]
    fn helpers_work() {
        let src = "fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {\n    return sample(uv + vec2<f32>(sin(time()), 0.0) / resolution()) * param(0);\n}\n";
        assert!(check(src).is_ok());
    }
}
