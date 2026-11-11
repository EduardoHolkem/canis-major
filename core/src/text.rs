//! Texto com a fonte Geist, rasterizado pelo fontdue num atlas que cresce sob demanda.

use std::collections::HashMap;

pub const ATLAS: u32 = 1024;
static FONT_BYTES: &[u8] = include_bytes!("../../assets/fonts/Geist-Medium.ttf");

#[derive(Clone, Copy)]
pub struct Glyph {
    pub uv: [f32; 4],
    pub w: f32,
    pub h: f32,
    pub xmin: f32,
    pub ymin: f32,
    pub advance: f32,
}

pub struct TextAtlas {
    font: fontdue::Font,
    glyphs: HashMap<(char, u32), Glyph>,
    pen_x: u32,
    pen_y: u32,
    row_h: u32,
    /// Regiões novas a enviar para a textura: (x, y, w, h, rgba)
    pub pending: Vec<(u32, u32, u32, u32, Vec<u8>)>,
}

impl TextAtlas {
    pub fn new() -> Self {
        let font = fontdue::Font::from_bytes(FONT_BYTES, fontdue::FontSettings::default()).expect("fonte embutida inválida");
        TextAtlas { font, glyphs: HashMap::new(), pen_x: 1, pen_y: 1, row_h: 0, pending: Vec::new() }
    }

    pub fn ascent(&self, size: f32) -> f32 {
        self.font.horizontal_line_metrics(size).map(|m| m.ascent).unwrap_or(size * 0.8)
    }

    pub fn line_height(&self, size: f32) -> f32 {
        self.font.horizontal_line_metrics(size).map(|m| m.new_line_size).unwrap_or(size * 1.2)
    }

    pub fn glyph(&mut self, c: char, size: u32) -> Glyph {
        if let Some(g) = self.glyphs.get(&(c, size)) {
            return *g;
        }
        let (metrics, coverage) = self.font.rasterize(c, size as f32);
        let (w, h) = (metrics.width as u32, metrics.height as u32);
        if self.pen_x + w + 1 >= ATLAS {
            self.pen_x = 1;
            self.pen_y += self.row_h + 1;
            self.row_h = 0;
        }
        if self.pen_y + h + 1 >= ATLAS {
            // Atlas cheio: recomeça. Os glifos antigos são recriados quando forem pedidos de novo.
            self.glyphs.clear();
            self.pen_x = 1;
            self.pen_y = 1;
            self.row_h = 0;
        }
        let (x, y) = (self.pen_x, self.pen_y);
        if w > 0 && h > 0 {
            let mut rgba = Vec::with_capacity((w * h * 4) as usize);
            for a in coverage {
                rgba.extend_from_slice(&[255, 255, 255, a]);
            }
            self.pending.push((x, y, w, h, rgba));
        }
        self.pen_x += w + 1;
        self.row_h = self.row_h.max(h);
        let f = ATLAS as f32;
        let g = Glyph {
            uv: [x as f32 / f, y as f32 / f, (x + w) as f32 / f, (y + h) as f32 / f],
            w: w as f32,
            h: h as f32,
            xmin: metrics.xmin as f32,
            ymin: metrics.ymin as f32,
            advance: metrics.advance_width,
        };
        self.glyphs.insert((c, size), g);
        g
    }

    pub fn measure(&mut self, text: &str, size: u32) -> (f32, f32) {
        let mut widest = 0.0f32;
        let mut width = 0.0f32;
        let mut lines = 1;
        for c in text.chars() {
            if c == '\n' {
                widest = widest.max(width);
                width = 0.0;
                lines += 1;
                continue;
            }
            width += self.glyph(c, size).advance;
        }
        (widest.max(width), self.line_height(size as f32) * lines as f32)
    }
}
