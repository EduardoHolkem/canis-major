//! Núcleo nativo do Canis Major. O jogo em C# chama estas funções por P/Invoke.
//!
//! O C# é dono do laço do jogo: a cada quadro ele chama `cm_poll` (eventos e entrada) e
//! `cm_frame` (todo o desenho de uma vez, num vetor de comandos). Tudo roda na mesma thread.

mod audio;
mod gfx;
mod keys;
mod mesh;
mod shader;
mod text;

use std::cell::RefCell;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::Arc;
use std::time::Duration;

use winit::application::ApplicationHandler;
use winit::dpi::PhysicalSize;
use winit::event::{ElementState, MouseButton, MouseScrollDelta, WindowEvent};
use winit::event_loop::{ActiveEventLoop, EventLoop};
use winit::keyboard::PhysicalKey;
use winit::platform::pump_events::{EventLoopExtPumpEvents, PumpStatus};
use winit::window::{Icon, Window, WindowAttributes, WindowId};

pub use gfx::DrawCmd;

/// Estado da entrada, copiado para o C# a cada quadro. Bits: 1 segurando, 2 apertou, 4 soltou.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct InputState {
    pub keys: [u8; keys::MAX_KEYS],
    pub buttons: [u8; 4],
    pub mouse_x: f32,
    pub mouse_y: f32,
    pub wheel: f32,
    pub win_w: f32,
    pub win_h: f32,
    pub view_x: f32,
    pub view_y: f32,
    pub view_scale: f32,
    pub focused: u32,
}

impl Default for InputState {
    fn default() -> Self {
        InputState {
            keys: [0; keys::MAX_KEYS], buttons: [0; 4], mouse_x: 0.0, mouse_y: 0.0, wheel: 0.0,
            win_w: 0.0, win_h: 0.0, view_x: 0.0, view_y: 0.0, view_scale: 1.0, focused: 1,
        }
    }
}

struct App {
    window: Option<Arc<Window>>,
    attrs: Option<WindowAttributes>,
    input: InputState,
    quit: bool,
    resized: Option<(u32, u32)>,
    create_error: Option<String>,
}

fn press(bits: &mut u8, pressed: bool, repeat: bool) {
    if pressed {
        *bits |= if repeat { 1 } else { 0b011 };
    } else {
        *bits = (*bits & !1) | 0b100;
    }
}

impl ApplicationHandler for App {
    fn resumed(&mut self, event_loop: &ActiveEventLoop) {
        if self.window.is_none() {
            if let Some(attrs) = self.attrs.take() {
                match event_loop.create_window(attrs) {
                    Ok(w) => self.window = Some(Arc::new(w)),
                    Err(e) => self.create_error = Some(e.to_string()),
                }
            }
        }
    }

    fn window_event(&mut self, _event_loop: &ActiveEventLoop, _id: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => self.quit = true,
            WindowEvent::Resized(s) => self.resized = Some((s.width, s.height)),
            WindowEvent::Focused(f) => self.input.focused = f as u32,
            WindowEvent::KeyboardInput { event, .. } => {
                if let PhysicalKey::Code(code) = event.physical_key {
                    if let Some(i) = keys::index_of(code) {
                        press(&mut self.input.keys[i], event.state == ElementState::Pressed, event.repeat);
                    }
                }
            }
            WindowEvent::CursorMoved { position, .. } => {
                self.input.mouse_x = position.x as f32;
                self.input.mouse_y = position.y as f32;
            }
            WindowEvent::MouseInput { state, button, .. } => {
                let i = match button {
                    MouseButton::Left => 0,
                    MouseButton::Middle => 1,
                    MouseButton::Right => 2,
                    _ => 3,
                };
                press(&mut self.input.buttons[i], state == ElementState::Pressed, false);
            }
            WindowEvent::MouseWheel { delta, .. } => {
                self.input.wheel += match delta {
                    MouseScrollDelta::LineDelta(_, y) => y,
                    MouseScrollDelta::PixelDelta(p) => p.y as f32 / 40.0,
                };
            }
            _ => {}
        }
    }
}

struct Engine {
    event_loop: EventLoop<()>,
    app: App,
    gfx: gfx::Gfx,
    audio: audio::Audio,
}

thread_local! {
    static ENGINE: RefCell<Option<Engine>> = const { RefCell::new(None) };
    static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) };
}

fn set_error(msg: impl Into<String>) {
    LAST_ERROR.with(|e| *e.borrow_mut() = msg.into());
}

unsafe fn text_arg<'a>(ptr: *const u8, len: usize) -> &'a str {
    if ptr.is_null() || len == 0 {
        return "";
    }
    std::str::from_utf8(std::slice::from_raw_parts(ptr, len)).unwrap_or("")
}

/// Roda `f` com a engine, sem deixar um panic atravessar para o C#.
fn with_engine<R>(fallback: R, f: impl FnOnce(&mut Engine) -> R) -> R {
    let result = catch_unwind(AssertUnwindSafe(|| {
        ENGINE.with(|cell| match cell.borrow_mut().as_mut() {
            Some(engine) => Some(f(engine)),
            None => None,
        })
    }));
    match result {
        Ok(Some(r)) => r,
        Ok(None) => {
            set_error("a engine ainda não foi iniciada (cm_init)");
            fallback
        }
        Err(p) => {
            let msg = p.downcast_ref::<String>().cloned().or_else(|| p.downcast_ref::<&str>().map(|s| s.to_string())).unwrap_or_default();
            set_error(format!("erro interno do núcleo: {msg}"));
            fallback
        }
    }
}

fn window_icon() -> Option<Icon> {
    let img = image::load_from_memory(include_bytes!("../../assets/icon.png")).ok()?.into_rgba8();
    let small = image::imageops::resize(&img, 64, 64, image::imageops::FilterType::Triangle);
    Icon::from_rgba(small.into_raw(), 64, 64).ok()
}

// ---------- funções exportadas ----------

/// Abre a janela e a placa de vídeo. Devolve 0 se deu certo; senão, veja cm_last_error.
#[no_mangle]
pub unsafe extern "C" fn cm_init(title: *const u8, title_len: usize, width: u32, height: u32, scale: u32, vsync: u32, pixel_art: u32) -> i32 {
    let title = text_arg(title, title_len).to_string();
    let result = catch_unwind(AssertUnwindSafe(|| -> Result<(), String> {
        if ENGINE.with(|c| c.borrow().is_some()) {
            return Err("a engine já está rodando".into());
        }
        let (width, height) = (width.max(16), height.max(16));
        let mut builder = EventLoop::builder();
        #[cfg(target_os = "windows")]
        {
            use winit::platform::windows::EventLoopBuilderExtWindows;
            builder.with_any_thread(true);
        }
        let mut event_loop = builder.build().map_err(|e| format!("não foi possível criar a janela: {e}"))?;
        let scale = scale.max(1);
        #[allow(unused_mut)]
        let mut attrs = Window::default_attributes()
            .with_title(title)
            .with_inner_size(PhysicalSize::new(width * scale, height * scale))
            .with_min_inner_size(PhysicalSize::new(width.min(320), height.min(180)))
            .with_window_icon(window_icon());
        #[cfg(target_os = "windows")]
        {
            // O .NET deixa a thread principal em COM multithread; o arrastar e soltar do winit
            // exige o outro modo e derrubaria a janela. Jogo não precisa disso.
            use winit::platform::windows::WindowAttributesExtWindows;
            attrs = attrs.with_drag_and_drop(false);
        }
        let mut app = App { window: None, attrs: Some(attrs), input: InputState::default(), quit: false, resized: None, create_error: None };
        for _ in 0..500 {
            let _ = event_loop.pump_app_events(Some(Duration::from_millis(5)), &mut app);
            if app.window.is_some() || app.create_error.is_some() {
                break;
            }
        }
        if let Some(e) = app.create_error.take() {
            return Err(format!("não foi possível criar a janela: {e}"));
        }
        let window = app.window.clone().ok_or("a janela não abriu a tempo")?;
        let gfx = gfx::Gfx::new(window, width, height, pixel_art != 0, vsync != 0)?;
        let audio = audio::Audio::new();
        ENGINE.with(|c| *c.borrow_mut() = Some(Engine { event_loop, app, gfx, audio }));
        Ok(())
    }));
    match result {
        Ok(Ok(())) => 0,
        Ok(Err(e)) => {
            set_error(e);
            1
        }
        Err(_) => {
            set_error("erro interno ao iniciar a engine");
            2
        }
    }
}

/// Copia a última mensagem de erro para `buf`. Devolve o tamanho total da mensagem em bytes.
#[no_mangle]
pub unsafe extern "C" fn cm_last_error(buf: *mut u8, cap: usize) -> usize {
    LAST_ERROR.with(|e| {
        let e = e.borrow();
        let bytes = e.as_bytes();
        if !buf.is_null() && cap > 0 {
            let n = bytes.len().min(cap);
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, n);
        }
        bytes.len()
    })
}

/// Processa os eventos da janela e devolve a entrada. Devolve 0 quando o jogador fecha a janela.
#[no_mangle]
pub unsafe extern "C" fn cm_poll(out: *mut InputState) -> i32 {
    with_engine(0, |e| {
        for k in e.app.input.keys.iter_mut().chain(e.app.input.buttons.iter_mut()) {
            *k &= 1;
        }
        e.app.input.wheel = 0.0;
        let status = e.event_loop.pump_app_events(Some(Duration::ZERO), &mut e.app);
        if let Some((w, h)) = e.app.resized.take() {
            e.gfx.resize(w, h);
        }
        if let Some(window) = &e.app.window {
            let size = window.inner_size();
            e.app.input.win_w = size.width as f32;
            e.app.input.win_h = size.height as f32;
        }
        let (vx, vy, s) = e.gfx.viewport;
        e.app.input.view_x = vx;
        e.app.input.view_y = vy;
        e.app.input.view_scale = s;
        if !out.is_null() {
            *out = e.app.input;
        }
        if e.app.quit || matches!(status, PumpStatus::Exit(_)) { 0 } else { 1 }
    })
}

/// Índice da tecla pelo nome ("left", "space", "a"...), ou -1 se não existir.
#[no_mangle]
pub unsafe extern "C" fn cm_key_index(name: *const u8, len: usize) -> i32 {
    keys::lookup(text_arg(name, len)).map(|i| i as i32).unwrap_or(-1)
}

#[no_mangle]
pub unsafe extern "C" fn cm_load_texture(path: *const u8, len: usize) -> u32 {
    let path = text_arg(path, len).to_string();
    with_engine(0, |e| match image::open(&path) {
        Ok(img) => {
            let rgba = img.into_rgba8();
            let (w, h) = rgba.dimensions();
            e.gfx.create_texture(w, h, Some(&rgba), false, false) as u32
        }
        Err(err) => {
            set_error(format!("não foi possível abrir a imagem {path}: {err}"));
            0
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn cm_create_texture(rgba: *const u8, w: u32, h: u32) -> u32 {
    if rgba.is_null() || w == 0 || h == 0 {
        set_error("textura vazia");
        return 0;
    }
    let data = std::slice::from_raw_parts(rgba, (w * h * 4) as usize).to_vec();
    with_engine(0, |e| e.gfx.create_texture(w, h, Some(&data), false, false) as u32)
}

#[no_mangle]
pub unsafe extern "C" fn cm_texture_size(handle: u32, w: *mut u32, h: *mut u32) -> i32 {
    with_engine(0, |e| match e.gfx.texture_size(handle) {
        Some((tw, th)) => {
            if !w.is_null() { *w = tw; }
            if !h.is_null() { *h = th; }
            1
        }
        None => 0,
    })
}

/// Compila um shader do usuário (só a função effect, em WGSL). Devolve o identificador,
/// ou -1 com os erros em cm_last_error, um por linha no formato "LINHA: mensagem".
#[no_mangle]
pub unsafe extern "C" fn cm_load_shader(source: *const u8, len: usize) -> i32 {
    let source = text_arg(source, len).to_string();
    match shader::check(&source) {
        Err(errors) => {
            set_error(errors.iter().map(|(l, m)| format!("{l}: {m}")).collect::<Vec<_>>().join("\n"));
            -1
        }
        Ok(full) => with_engine(-1, |e| match e.gfx.add_shader(full) {
            Ok(id) => id as i32,
            Err(msg) => {
                set_error(format!("0: {msg}"));
                -1
            }
        }),
    }
}

#[no_mangle]
pub extern "C" fn cm_set_shader_param(shader: u32, index: u32, x: f32, y: f32, z: f32, w: f32) {
    with_engine((), |e| e.gfx.set_param(shader, index, [x, y, z, w]))
}

/// Malha pronta: 1 cubo, 2 esfera, 3 plano.
#[no_mangle]
pub extern "C" fn cm_mesh_builtin(kind: u32) -> u32 {
    with_engine(0, |e| e.gfx.builtin_mesh(kind))
}

#[no_mangle]
pub unsafe extern "C" fn cm_load_mesh(path: *const u8, len: usize) -> u32 {
    let path = text_arg(path, len).to_string();
    let parsed = std::fs::read_to_string(&path).map_err(|e| e.to_string()).and_then(|t| mesh::load_obj(&t));
    match parsed {
        Ok(verts) => with_engine(0, |e| e.gfx.add_mesh(&verts)),
        Err(err) => {
            set_error(format!("não foi possível abrir o modelo {path}: {err}"));
            0
        }
    }
}

/// Desenha um quadro inteiro: limpa, executa os comandos, aplica os shaders de tela e mostra na janela.
#[no_mangle]
pub unsafe extern "C" fn cm_frame(
    clear: *const f32, cam_x: f32, cam_y: f32, time: f32,
    cmds: *const DrawCmd, count: usize, strings: *const u8, strings_len: usize,
    post: *const u32, post_count: usize,
) -> i32 {
    let clear = if clear.is_null() { [0.0, 0.0, 0.0, 1.0] } else { [*clear, *clear.add(1), *clear.add(2), *clear.add(3)] };
    let cmds = if cmds.is_null() { &[][..] } else { std::slice::from_raw_parts(cmds, count) };
    let strings = if strings.is_null() { &[][..] } else { std::slice::from_raw_parts(strings, strings_len) };
    let post = if post.is_null() { &[][..] } else { std::slice::from_raw_parts(post, post_count) };
    with_engine(1, |e| {
        e.gfx.time = time;
        match e.gfx.frame(clear, (cam_x, cam_y), cmds, strings, post) {
            Ok(()) => 0,
            Err(msg) => {
                set_error(msg);
                1
            }
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn cm_measure_text(text: *const u8, len: usize, size: f32, w: *mut f32, h: *mut f32) {
    let text = text_arg(text, len).to_string();
    with_engine((), |e| {
        let (tw, th) = e.gfx.measure(&text, size);
        if !w.is_null() { *w = tw; }
        if !h.is_null() { *h = th; }
    })
}

#[no_mangle]
pub unsafe extern "C" fn cm_load_sound(path: *const u8, len: usize) -> u32 {
    let path = text_arg(path, len).to_string();
    let bytes = match std::fs::read(&path) {
        Ok(b) => b,
        Err(err) => {
            set_error(format!("não foi possível abrir o som {path}: {err}"));
            return 0;
        }
    };
    with_engine(0, |e| match e.audio.load(bytes) {
        Ok(id) => id,
        Err(msg) => {
            set_error(format!("{path}: {msg}"));
            0
        }
    })
}

#[no_mangle]
pub extern "C" fn cm_play_sound(id: u32, volume: f32, looped: u32) {
    with_engine((), |e| e.audio.play(id, volume, looped != 0))
}

#[no_mangle]
pub extern "C" fn cm_stop_sounds() {
    with_engine((), |e| e.audio.stop_all())
}

#[no_mangle]
pub unsafe extern "C" fn cm_set_title(title: *const u8, len: usize) {
    let title = text_arg(title, len).to_string();
    with_engine((), |e| {
        if let Some(w) = &e.app.window {
            w.set_title(&title);
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn cm_screenshot(path: *const u8, len: usize) -> i32 {
    let path = text_arg(path, len).to_string();
    with_engine(1, |e| match e.gfx.screenshot(&path) {
        Ok(()) => 0,
        Err(msg) => {
            set_error(msg);
            1
        }
    })
}

/// Versão do núcleo, para o C# conferir se a DLL bate com a API.
#[no_mangle]
pub extern "C" fn cm_version() -> u32 {
    2
}

#[no_mangle]
pub extern "C" fn cm_shutdown() {
    let _ = catch_unwind(|| ENGINE.with(|c| c.borrow_mut().take()));
}

#[cfg(test)]
mod tests {
    #[test]
    fn draw_cmd_size_matches_csharp() {
        // Native.DrawCmd no C# tem 32 campos de 4 bytes.
        assert_eq!(std::mem::size_of::<super::DrawCmd>(), 128);
        assert_eq!(std::mem::size_of::<super::InputState>(), 128 + 4 + 4 * 9);
    }
}
