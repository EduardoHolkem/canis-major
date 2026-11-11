//! Malhas 3D: cubo, esfera, plano e .obj. Cada vértice tem posição (3), normal (3) e uv (2).

use std::f32::consts::PI;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Vertex {
    pub pos: [f32; 3],
    pub normal: [f32; 3],
    pub uv: [f32; 2],
}

fn v(pos: [f32; 3], normal: [f32; 3], uv: [f32; 2]) -> Vertex {
    Vertex { pos, normal, uv }
}

pub fn cube() -> Vec<Vertex> {
    let faces: [([f32; 3], [f32; 3], [f32; 3]); 6] = [
        ([0., 0., 1.], [1., 0., 0.], [0., 1., 0.]),
        ([0., 0., -1.], [-1., 0., 0.], [0., 1., 0.]),
        ([1., 0., 0.], [0., 0., -1.], [0., 1., 0.]),
        ([-1., 0., 0.], [0., 0., 1.], [0., 1., 0.]),
        ([0., 1., 0.], [1., 0., 0.], [0., 0., -1.]),
        ([0., -1., 0.], [1., 0., 0.], [0., 0., 1.]),
    ];
    let mut out = Vec::with_capacity(36);
    for (n, u, w) in faces {
        let c = [n[0] * 0.5, n[1] * 0.5, n[2] * 0.5];
        let corner = |su: f32, sv: f32| [c[0] + u[0] * su + w[0] * sv, c[1] + u[1] * su + w[1] * sv, c[2] + u[2] * su + w[2] * sv];
        let quad = [
            v(corner(-0.5, -0.5), n, [0., 1.]),
            v(corner(0.5, -0.5), n, [1., 1.]),
            v(corner(0.5, 0.5), n, [1., 0.]),
            v(corner(-0.5, 0.5), n, [0., 0.]),
        ];
        for i in [0, 1, 2, 0, 2, 3] {
            out.push(quad[i]);
        }
    }
    out
}

pub fn sphere(segments: u32, rings: u32) -> Vec<Vertex> {
    let vert = |ring: u32, seg: u32| {
        let theta = PI * ring as f32 / rings as f32;
        let phi = 2.0 * PI * seg as f32 / segments as f32;
        let n = [theta.sin() * phi.cos(), theta.cos(), theta.sin() * phi.sin()];
        v([n[0] * 0.5, n[1] * 0.5, n[2] * 0.5], n, [seg as f32 / segments as f32, ring as f32 / rings as f32])
    };
    let mut out = Vec::new();
    for r in 0..rings {
        for s in 0..segments {
            let (a, b, c, d) = (vert(r, s), vert(r + 1, s), vert(r + 1, s + 1), vert(r, s + 1));
            out.extend_from_slice(&[a, b, c, a, c, d]);
        }
    }
    out
}

pub fn plane() -> Vec<Vertex> {
    let n = [0., 0., 1.];
    let q = [([-0.5, -0.5], [0., 1.]), ([0.5, -0.5], [1., 1.]), ([0.5, 0.5], [1., 0.]), ([-0.5, 0.5], [0., 0.])];
    [0, 1, 2, 0, 2, 3].iter().map(|&i| v([q[i].0[0], q[i].0[1], 0.0], n, q[i].1)).collect()
}

/// Lê um .obj simples (v, vt, vn, f), centraliza e ajusta para caber num cubo de 1.
pub fn load_obj(text: &str) -> Result<Vec<Vertex>, String> {
    let (mut pos, mut uvs, mut normals, mut out) = (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    let num = |s: &str| s.parse::<f32>().map_err(|e| format!("número inválido '{s}': {e}"));
    let idx = |s: &str, len: usize| -> Result<usize, String> {
        let i: i64 = s.parse().map_err(|_| format!("índice inválido '{s}'"))?;
        let i = if i < 0 { len as i64 + i } else { i - 1 };
        if i < 0 || i as usize >= len { Err(format!("índice fora do arquivo: {s}")) } else { Ok(i as usize) }
    };
    for line in text.lines() {
        let parts: Vec<&str> = line.split_whitespace().collect();
        match parts.first() {
            Some(&"v") if parts.len() >= 4 => pos.push([num(parts[1])?, num(parts[2])?, num(parts[3])?]),
            Some(&"vt") if parts.len() >= 3 => uvs.push([num(parts[1])?, num(parts[2])?]),
            Some(&"vn") if parts.len() >= 4 => normals.push([num(parts[1])?, num(parts[2])?, num(parts[3])?]),
            Some(&"f") if parts.len() >= 4 => {
                let corners: Vec<Vec<&str>> = parts[1..].iter().map(|p| p.split('/').collect()).collect();
                for i in 1..corners.len() - 1 {
                    for c in [&corners[0], &corners[i], &corners[i + 1]] {
                        let p = pos[idx(c[0], pos.len())?];
                        let uv = if c.len() > 1 && !c[1].is_empty() { uvs[idx(c[1], uvs.len())?] } else { [0.0, 0.0] };
                        let n = if c.len() > 2 && !c[2].is_empty() { normals[idx(c[2], normals.len())?] } else { [0.0, 0.0, 1.0] };
                        out.push(v(p, n, [uv[0], 1.0 - uv[1]]));
                    }
                }
            }
            _ => {}
        }
    }
    if out.is_empty() {
        return Err("nenhuma face no arquivo".into());
    }
    let mut lo = [f32::MAX; 3];
    let mut hi = [f32::MIN; 3];
    for vert in &out {
        for k in 0..3 {
            lo[k] = lo[k].min(vert.pos[k]);
            hi[k] = hi[k].max(vert.pos[k]);
        }
    }
    let size = (0..3).map(|k| hi[k] - lo[k]).fold(0.0f32, f32::max).max(1e-6);
    for vert in &mut out {
        for k in 0..3 {
            vert.pos[k] = (vert.pos[k] - (lo[k] + hi[k]) / 2.0) / size;
        }
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shapes() {
        assert_eq!(cube().len(), 36);
        assert_eq!(plane().len(), 6);
        assert_eq!(sphere(8, 4).len(), 8 * 4 * 6);
    }

    #[test]
    fn obj_is_normalized() {
        let m = load_obj("v 0 0 0\nv 2 0 0\nv 0 2 0\nvn 0 0 1\nf 1//1 2//1 3//1\n").unwrap();
        assert_eq!(m.len(), 3);
        assert!(m.iter().all(|v| v.pos.iter().all(|c| c.abs() <= 0.5 + 1e-6)));
        assert!(load_obj("o vazio\n").is_err());
    }
}
