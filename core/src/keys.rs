//! Tabela de teclas. O C# pede o índice pelo nome uma vez e depois lê o estado pelo índice.

use winit::keyboard::KeyCode;

pub const MAX_KEYS: usize = 128;

pub const CODES: &[KeyCode] = &[
    KeyCode::ArrowLeft, KeyCode::ArrowRight, KeyCode::ArrowUp, KeyCode::ArrowDown,
    KeyCode::Space, KeyCode::Enter, KeyCode::Escape, KeyCode::Tab, KeyCode::Backspace,
    KeyCode::ShiftLeft, KeyCode::ShiftRight, KeyCode::ControlLeft, KeyCode::ControlRight,
    KeyCode::AltLeft, KeyCode::AltRight,
    KeyCode::KeyA, KeyCode::KeyB, KeyCode::KeyC, KeyCode::KeyD, KeyCode::KeyE, KeyCode::KeyF, KeyCode::KeyG,
    KeyCode::KeyH, KeyCode::KeyI, KeyCode::KeyJ, KeyCode::KeyK, KeyCode::KeyL, KeyCode::KeyM, KeyCode::KeyN,
    KeyCode::KeyO, KeyCode::KeyP, KeyCode::KeyQ, KeyCode::KeyR, KeyCode::KeyS, KeyCode::KeyT, KeyCode::KeyU,
    KeyCode::KeyV, KeyCode::KeyW, KeyCode::KeyX, KeyCode::KeyY, KeyCode::KeyZ,
    KeyCode::Digit0, KeyCode::Digit1, KeyCode::Digit2, KeyCode::Digit3, KeyCode::Digit4,
    KeyCode::Digit5, KeyCode::Digit6, KeyCode::Digit7, KeyCode::Digit8, KeyCode::Digit9,
    KeyCode::F1, KeyCode::F2, KeyCode::F3, KeyCode::F4, KeyCode::F5, KeyCode::F6,
    KeyCode::F7, KeyCode::F8, KeyCode::F9, KeyCode::F10, KeyCode::F11, KeyCode::F12,
    KeyCode::Delete, KeyCode::Insert, KeyCode::Home, KeyCode::End, KeyCode::PageUp, KeyCode::PageDown,
    KeyCode::Minus, KeyCode::Equal, KeyCode::Comma, KeyCode::Period, KeyCode::Slash,
];

const NAMES: &[&str] = &[
    "left", "right", "up", "down",
    "space", "enter", "escape", "tab", "backspace",
    "shift", "rshift", "ctrl", "rctrl", "alt", "ralt",
    "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n",
    "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
    "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
    "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
    "delete", "insert", "home", "end", "pageup", "pagedown",
    "minus", "equal", "comma", "period", "slash",
];

fn alias(name: &str) -> &str {
    match name {
        "return" => "enter",
        "esc" => "escape",
        "espaco" | "espaço" => "space",
        "esquerda" => "left",
        "direita" => "right",
        "cima" => "up",
        "baixo" => "down",
        "lshift" => "shift",
        "lctrl" | "control" => "ctrl",
        "lalt" => "alt",
        other => other,
    }
}

pub fn lookup(name: &str) -> Option<usize> {
    let lower = name.trim().to_lowercase();
    let n = alias(&lower);
    NAMES.iter().position(|k| *k == n)
}

pub fn index_of(code: KeyCode) -> Option<usize> {
    CODES.iter().position(|c| *c == code)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tables_match() {
        assert_eq!(CODES.len(), NAMES.len());
        assert!(CODES.len() <= MAX_KEYS);
        assert_eq!(lookup("Return"), lookup("enter"));
        assert_eq!(lookup("esquerda"), Some(0));
        assert_eq!(lookup("nada"), None);
    }
}
