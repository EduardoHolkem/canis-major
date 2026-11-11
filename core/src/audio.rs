//! Som com o rodio. Sem placa de som, tudo vira silêncio em vez de erro.

use std::io::Cursor;
use std::sync::Arc;

use rodio::{Decoder, OutputStream, OutputStreamHandle, Sink, Source};

pub struct Audio {
    _stream: Option<OutputStream>,
    handle: Option<OutputStreamHandle>,
    sounds: Vec<Arc<[u8]>>,
    playing: Vec<Sink>,
}

impl Audio {
    pub fn new() -> Self {
        match OutputStream::try_default() {
            Ok((stream, handle)) => Audio { _stream: Some(stream), handle: Some(handle), sounds: Vec::new(), playing: Vec::new() },
            Err(_) => Audio { _stream: None, handle: None, sounds: Vec::new(), playing: Vec::new() },
        }
    }

    pub fn load(&mut self, bytes: Vec<u8>) -> Result<u32, String> {
        Decoder::new(Cursor::new(bytes.clone())).map_err(|e| format!("formato de som não suportado: {e}"))?;
        self.sounds.push(bytes.into());
        Ok(self.sounds.len() as u32)
    }

    pub fn play(&mut self, id: u32, volume: f32, looped: bool) {
        let Some(handle) = &self.handle else { return };
        let Some(bytes) = self.sounds.get(id.wrapping_sub(1) as usize) else { return };
        let Ok(sink) = Sink::try_new(handle) else { return };
        let Ok(source) = Decoder::new(Cursor::new(bytes.clone())) else { return };
        sink.set_volume(volume.max(0.0));
        if looped {
            sink.append(source.repeat_infinite());
        } else {
            sink.append(source);
        }
        self.playing.retain(|s| !s.empty());
        self.playing.push(sink);
    }

    pub fn stop_all(&mut self) {
        for s in self.playing.drain(..) {
            s.stop();
        }
    }
}
