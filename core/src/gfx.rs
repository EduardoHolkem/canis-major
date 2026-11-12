//! Renderização com wgpu: sprites em lote, texto, malhas 3D, pós-processamento e letterbox.

use std::sync::Arc;

use bytemuck::{Pod, Zeroable};
use glam::{Mat4, Vec3};
use winit::window::Window;

use crate::mesh::{self, Vertex};
use crate::light;
use crate::shader;
use crate::text::{TextAtlas, ATLAS};

const SCENE_FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8Unorm;
const DEPTH_FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Depth32Float;
const MESH_STRIDE: u64 = 256;

/// Um comando de desenho vindo do C#. Precisa bater campo a campo com Native.DrawCmd.
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct DrawCmd {
    pub kind: u32, // 1 quad, 2 texto, 3 malha
    pub texture: u32,
    pub shader: u32,
    pub mesh: u32,
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
    pub ox: f32,
    pub oy: f32,
    pub rot: f32,
    pub z: f32,
    pub u0: f32,
    pub v0: f32,
    pub u1: f32,
    pub v1: f32,
    pub r: f32,
    pub g: f32,
    pub b: f32,
    pub a: f32,
    pub rx: f32,
    pub ry: f32,
    pub rz: f32,
    pub size: f32,
    pub str_off: u32,
    pub str_len: u32,
    pub align: u32,
    pub flags: u32,
    pub light: [f32; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct Instance {
    rect: [f32; 4],
    orot: [f32; 4],
    uv: [f32; 4],
    color: [f32; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct Globals {
    proj: [[f32; 4]; 4],
    resolution: [f32; 2],
    time: f32,
    _pad: f32,
    params: [[f32; 4]; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct MeshUniform {
    mvp: [[f32; 4]; 4],
    normal_mat: [[f32; 4]; 4],
    color: [f32; 4],
    light: [f32; 4],
    flags: [f32; 4],
}

pub struct Texture {
    pub texture: wgpu::Texture,
    pub view: wgpu::TextureView,
    pub bind: wgpu::BindGroup,
    pub w: u32,
    pub h: u32,
}

struct ShaderEntry {
    globals: wgpu::Buffer,
    globals_bind: wgpu::BindGroup,
    sprite: wgpu::RenderPipeline,
    post: wgpu::RenderPipeline,
    params: [[f32; 4]; 4],
}

struct MeshEntry {
    buffer: wgpu::Buffer,
    count: u32,
}

enum Batch {
    Sprites { shader: usize, tex: usize, start: u32, count: u32 },
    Mesh { mesh: usize, tex: usize, offset: u32 },
}

pub struct Gfx {
    surface: wgpu::Surface<'static>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    config: wgpu::SurfaceConfiguration,
    pub width: u32,
    pub height: u32,
    pixel_art: bool,
    tex_layout: wgpu::BindGroupLayout,
    globals_layout: wgpu::BindGroupLayout,
    mesh_u_layout: wgpu::BindGroupLayout,
    sprite_layout: wgpu::PipelineLayout,
    sampler_main: wgpu::Sampler,
    sampler_linear: wgpu::Sampler,
    textures: Vec<Texture>,
    shaders: Vec<ShaderEntry>,
    blit: wgpu::RenderPipeline,
    mesh_pipeline: wgpu::RenderPipeline,
    meshes: Vec<MeshEntry>,
    quad: wgpu::Buffer,
    inst_buf: wgpu::Buffer,
    inst_cap: usize,
    mesh_ubuf: wgpu::Buffer,
    mesh_cap: usize,
    mesh_bind: wgpu::BindGroup,
    scene: usize,
    ping: [usize; 2],
    depth_view: wgpu::TextureView,
    pub atlas: TextAtlas,
    atlas_tex: usize,
    instances: Vec<Instance>,
    batches: Vec<Batch>,
    mesh_uniforms: Vec<u8>,
    proj: Mat4,
    pub time: f32,
    pub viewport: (f32, f32, f32),
    last_output: usize,
    // Iluminação 2D (light.rs).
    light_on: bool,
    light_ambient: [f32; 4],
    light_softness: f32,
    lights: Vec<light::LightData>,
    occluders: Vec<[f32; 4]>,
    light_buf: wgpu::Buffer,
    light_bind: wgpu::BindGroup,
    normal_pipeline: wgpu::RenderPipeline,
    light_pipeline: wgpu::RenderPipeline,
    normal_target: usize,
    lit_target: usize,
    flat_normal: usize,
    normal_maps: std::collections::HashMap<usize, usize>,
}

fn err<E: std::fmt::Display>(what: &str) -> impl FnOnce(E) -> String + '_ {
    move |e| format!("{what}: {e}")
}

impl Gfx {
    pub fn new(window: Arc<Window>, width: u32, height: u32, pixel_art: bool, vsync: bool) -> Result<Gfx, String> {
        // Vulkan primeiro: cria os pipelines bem mais rápido que o DirectX 12 com o compilador FXC.
        // CANIS_BACKEND=vulkan|dx12|gl força um deles.
        let wanted = std::env::var("CANIS_BACKEND").unwrap_or_default().to_lowercase();
        let order: Vec<wgpu::Backends> = match wanted.as_str() {
            "vulkan" => vec![wgpu::Backends::VULKAN],
            "dx12" => vec![wgpu::Backends::DX12],
            "gl" => vec![wgpu::Backends::GL],
            _ => vec![wgpu::Backends::VULKAN, wgpu::Backends::DX12, wgpu::Backends::GL],
        };
        let mut found = None;
        let mut last_error = String::from("nenhum backend tentado");
        for backends in order {
            let instance = wgpu::Instance::new(&wgpu::InstanceDescriptor { backends, ..Default::default() });
            let surface = match instance.create_surface(window.clone()) {
                Ok(s) => s,
                Err(e) => {
                    last_error = e.to_string();
                    continue;
                }
            };
            match pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
                power_preference: wgpu::PowerPreference::HighPerformance,
                compatible_surface: Some(&surface),
                force_fallback_adapter: false,
            })) {
                Ok(adapter) => {
                    found = Some((surface, adapter));
                    break;
                }
                Err(e) => last_error = e.to_string(),
            }
        }
        let (surface, adapter) = found.ok_or_else(|| format!("nenhuma placa de vídeo compatível (Vulkan, DirectX 12 ou OpenGL): {last_error}"))?;
        if std::env::var_os("CANIS_DEBUG").is_some() {
            let info = adapter.get_info();
            eprintln!("Canis Major: {} via {:?}", info.name, info.backend);
        }
        let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
            label: Some("canis"),
            required_features: wgpu::Features::empty(),
            required_limits: wgpu::Limits::default().using_resolution(adapter.limits()),
            memory_hints: wgpu::MemoryHints::Performance,
            trace: wgpu::Trace::Off,
        }))
        .map_err(err("não foi possível abrir a placa de vídeo"))?;
        device.on_uncaptured_error(Box::new(|e| eprintln!("Erro do wgpu: {e}")));

        let caps = surface.get_capabilities(&adapter);
        let format = caps.formats.iter().copied().find(|f| !f.is_srgb()).unwrap_or(caps.formats[0]);
        let size = window.inner_size();
        let config = wgpu::SurfaceConfiguration {
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
            format,
            width: size.width.max(1),
            height: size.height.max(1),
            present_mode: if vsync { wgpu::PresentMode::AutoVsync } else { wgpu::PresentMode::AutoNoVsync },
            alpha_mode: caps.alpha_modes[0],
            view_formats: vec![],
            desired_maximum_frame_latency: 2,
        };
        surface.configure(&device, &config);

        let tex_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("textura"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 1,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                    count: None,
                },
            ],
        });
        let uniform_entry = |dynamic: bool| wgpu::BindGroupLayoutEntry {
            binding: 0,
            visibility: wgpu::ShaderStages::VERTEX | wgpu::ShaderStages::FRAGMENT,
            ty: wgpu::BindingType::Buffer { ty: wgpu::BufferBindingType::Uniform, has_dynamic_offset: dynamic, min_binding_size: None },
            count: None,
        };
        let globals_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor { label: Some("globais"), entries: &[uniform_entry(false)] });
        let mesh_u_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor { label: Some("malha"), entries: &[uniform_entry(true)] });
        let sprite_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("sprite"),
            bind_group_layouts: &[&globals_layout, &tex_layout],
            push_constant_ranges: &[],
        });
        let mesh_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("malha"),
            bind_group_layouts: &[&mesh_u_layout, &tex_layout],
            push_constant_ranges: &[],
        });

        let filter = if pixel_art { wgpu::FilterMode::Nearest } else { wgpu::FilterMode::Linear };
        let make_sampler = |f: wgpu::FilterMode| device.create_sampler(&wgpu::SamplerDescriptor {
            mag_filter: f,
            min_filter: f,
            address_mode_u: wgpu::AddressMode::ClampToEdge,
            address_mode_v: wgpu::AddressMode::ClampToEdge,
            ..Default::default()
        });
        let sampler_main = make_sampler(filter);
        let sampler_linear = make_sampler(wgpu::FilterMode::Linear);

        let default_module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("padrão"),
            source: wgpu::ShaderSource::Wgsl(shader::compose(shader::DEFAULT_EFFECT).into()),
        });
        let blit = Self::fullscreen_pipeline(&device, &sprite_layout, &default_module, format);
        let mesh_module = device.create_shader_module(wgpu::ShaderModuleDescriptor { label: Some("malha"), source: wgpu::ShaderSource::Wgsl(shader::MESH.into()) });
        let mesh_pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("malha"),
            layout: Some(&mesh_layout),
            vertex: wgpu::VertexState {
                module: &mesh_module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[wgpu::VertexBufferLayout {
                    array_stride: std::mem::size_of::<Vertex>() as u64,
                    step_mode: wgpu::VertexStepMode::Vertex,
                    attributes: &wgpu::vertex_attr_array![0 => Float32x3, 1 => Float32x3, 2 => Float32x2],
                }],
            },
            fragment: Some(wgpu::FragmentState {
                module: &mesh_module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: SCENE_FORMAT, blend: Some(wgpu::BlendState::ALPHA_BLENDING), write_mask: wgpu::ColorWrites::ALL })],
            }),
            primitive: wgpu::PrimitiveState { topology: wgpu::PrimitiveTopology::TriangleList, cull_mode: None, ..Default::default() },
            depth_stencil: Some(wgpu::DepthStencilState {
                format: DEPTH_FORMAT,
                depth_write_enabled: true,
                depth_compare: wgpu::CompareFunction::Less,
                stencil: Default::default(),
                bias: Default::default(),
            }),
            multisample: Default::default(),
            multiview: None,
            cache: None,
        });

        let quad = device.create_buffer(&wgpu::BufferDescriptor { label: Some("quad"), size: 32, usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST, mapped_at_creation: false });
        queue.write_buffer(&quad, 0, bytemuck::cast_slice(&[0.0f32, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 1.0]));
        let inst_cap = 4096;
        let inst_buf = Self::instance_buffer(&device, inst_cap);
        let mesh_cap = 64;
        let mesh_ubuf = Self::mesh_buffer(&device, mesh_cap);
        let mesh_bind = Self::mesh_bind_group(&device, &mesh_u_layout, &mesh_ubuf);
        let depth = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("profundidade"),
            size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: DEPTH_FORMAT,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
            view_formats: &[],
        });
        let depth_view = depth.create_view(&Default::default());

        // Iluminação: um passe das normais (com o vértice dos sprites) e um de luz em tela cheia.
        let light_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("luz"),
            bind_group_layouts: &[&globals_layout, &tex_layout, &tex_layout],
            push_constant_ranges: &[],
        });
        let normal_module = device.create_shader_module(wgpu::ShaderModuleDescriptor { label: Some("normais"), source: wgpu::ShaderSource::Wgsl(light::NORMAL.into()) });
        let normal_pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("normais"),
            layout: Some(&light_layout),
            vertex: wgpu::VertexState {
                module: &normal_module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[
                    wgpu::VertexBufferLayout { array_stride: 8, step_mode: wgpu::VertexStepMode::Vertex, attributes: &wgpu::vertex_attr_array![0 => Float32x2] },
                    wgpu::VertexBufferLayout {
                        array_stride: std::mem::size_of::<Instance>() as u64,
                        step_mode: wgpu::VertexStepMode::Instance,
                        attributes: &wgpu::vertex_attr_array![1 => Float32x4, 2 => Float32x4, 3 => Float32x4, 4 => Float32x4],
                    },
                ],
            },
            fragment: Some(wgpu::FragmentState {
                module: &normal_module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: SCENE_FORMAT, blend: None, write_mask: wgpu::ColorWrites::ALL })],
            }),
            primitive: wgpu::PrimitiveState { topology: wgpu::PrimitiveTopology::TriangleStrip, ..Default::default() },
            depth_stencil: None,
            multisample: Default::default(),
            multiview: None,
            cache: None,
        });
        let light_module = device.create_shader_module(wgpu::ShaderModuleDescriptor { label: Some("luz"), source: wgpu::ShaderSource::Wgsl(light::LIGHT.into()) });
        let light_pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("luz"),
            layout: Some(&light_layout),
            vertex: wgpu::VertexState { module: &light_module, entry_point: Some("vs"), compilation_options: Default::default(), buffers: &[] },
            fragment: Some(wgpu::FragmentState {
                module: &light_module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: SCENE_FORMAT, blend: None, write_mask: wgpu::ColorWrites::ALL })],
            }),
            primitive: wgpu::PrimitiveState { topology: wgpu::PrimitiveTopology::TriangleStrip, ..Default::default() },
            depth_stencil: None,
            multisample: Default::default(),
            multiview: None,
            cache: None,
        });
        let light_buf = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("luz"),
            size: std::mem::size_of::<light::LightUniform>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let light_bind = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("luz"),
            layout: &globals_layout,
            entries: &[wgpu::BindGroupEntry { binding: 0, resource: light_buf.as_entire_binding() }],
        });

        let mut gfx = Gfx {
            surface, device, queue, config, width, height, pixel_art,
            tex_layout, globals_layout, mesh_u_layout, sprite_layout, sampler_main, sampler_linear,
            textures: Vec::new(), shaders: Vec::new(), blit, mesh_pipeline, meshes: Vec::new(),
            quad, inst_buf, inst_cap, mesh_ubuf, mesh_cap, mesh_bind,
            scene: 0, ping: [0, 0], depth_view, atlas: TextAtlas::new(), atlas_tex: 0,
            instances: Vec::new(), batches: Vec::new(), mesh_uniforms: Vec::new(),
            proj: Mat4::IDENTITY, time: 0.0, viewport: (0.0, 0.0, 1.0), last_output: 0,
            light_on: false, light_ambient: [1.0, 1.0, 1.0, 1.0], light_softness: 0.0, lights: Vec::new(), occluders: Vec::new(),
            light_buf, light_bind, normal_pipeline, light_pipeline,
            normal_target: 0, lit_target: 0, flat_normal: 0, normal_maps: std::collections::HashMap::new(),
        };
        gfx.create_texture(1, 1, Some(&[255, 255, 255, 255]), false, false); // índice 0: branca
        gfx.scene = gfx.create_texture(width, height, None, true, false) - 1;
        gfx.ping = [gfx.create_texture(width, height, None, true, false) - 1, gfx.create_texture(width, height, None, true, false) - 1];
        gfx.atlas_tex = gfx.create_texture(ATLAS, ATLAS, None, false, true) - 1;
        gfx.normal_target = gfx.create_texture(width, height, None, true, false) - 1;
        gfx.lit_target = gfx.create_texture(width, height, None, true, false) - 1;
        // A normal "para a frente", usada pelos sprites sem normal map.
        gfx.flat_normal = gfx.create_texture(1, 1, Some(&[128, 128, 255, 255]), false, false) - 1;
        gfx.add_shader(shader::compose(shader::DEFAULT_EFFECT))?; // índice 0: padrão
        Ok(gfx)
    }

    fn instance_buffer(device: &wgpu::Device, cap: usize) -> wgpu::Buffer {
        device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("instâncias"),
            size: (cap * std::mem::size_of::<Instance>()) as u64,
            usage: wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        })
    }

    fn mesh_buffer(device: &wgpu::Device, cap: usize) -> wgpu::Buffer {
        device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("malhas"),
            size: cap as u64 * MESH_STRIDE,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        })
    }

    fn mesh_bind_group(device: &wgpu::Device, layout: &wgpu::BindGroupLayout, buf: &wgpu::Buffer) -> wgpu::BindGroup {
        device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("malha"),
            layout,
            entries: &[wgpu::BindGroupEntry {
                binding: 0,
                resource: wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                    buffer: buf,
                    offset: 0,
                    size: wgpu::BufferSize::new(std::mem::size_of::<MeshUniform>() as u64),
                }),
            }],
        })
    }

    fn fullscreen_pipeline(device: &wgpu::Device, layout: &wgpu::PipelineLayout, module: &wgpu::ShaderModule, format: wgpu::TextureFormat) -> wgpu::RenderPipeline {
        device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("tela"),
            layout: Some(layout),
            vertex: wgpu::VertexState { module, entry_point: Some("cm_vs_full"), compilation_options: Default::default(), buffers: &[] },
            fragment: Some(wgpu::FragmentState {
                module,
                entry_point: Some("cm_fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format, blend: None, write_mask: wgpu::ColorWrites::ALL })],
            }),
            primitive: wgpu::PrimitiveState { topology: wgpu::PrimitiveTopology::TriangleStrip, ..Default::default() },
            depth_stencil: None,
            multisample: Default::default(),
            multiview: None,
            cache: None,
        })
    }

    fn sprite_pipeline(&self, module: &wgpu::ShaderModule) -> wgpu::RenderPipeline {
        self.device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("sprite"),
            layout: Some(&self.sprite_layout),
            vertex: wgpu::VertexState {
                module,
                entry_point: Some("cm_vs_sprite"),
                compilation_options: Default::default(),
                buffers: &[
                    wgpu::VertexBufferLayout { array_stride: 8, step_mode: wgpu::VertexStepMode::Vertex, attributes: &wgpu::vertex_attr_array![0 => Float32x2] },
                    wgpu::VertexBufferLayout {
                        array_stride: std::mem::size_of::<Instance>() as u64,
                        step_mode: wgpu::VertexStepMode::Instance,
                        attributes: &wgpu::vertex_attr_array![1 => Float32x4, 2 => Float32x4, 3 => Float32x4, 4 => Float32x4],
                    },
                ],
            },
            fragment: Some(wgpu::FragmentState {
                module,
                entry_point: Some("cm_fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: SCENE_FORMAT, blend: Some(wgpu::BlendState::ALPHA_BLENDING), write_mask: wgpu::ColorWrites::ALL })],
            }),
            primitive: wgpu::PrimitiveState { topology: wgpu::PrimitiveTopology::TriangleStrip, ..Default::default() },
            depth_stencil: Some(wgpu::DepthStencilState {
                format: DEPTH_FORMAT,
                depth_write_enabled: false,
                depth_compare: wgpu::CompareFunction::Always,
                stencil: Default::default(),
                bias: Default::default(),
            }),
            multisample: Default::default(),
            multiview: None,
            cache: None,
        })
    }

    /// Cria uma textura e devolve o identificador (índice + 1).
    pub fn create_texture(&mut self, w: u32, h: u32, rgba: Option<&[u8]>, target: bool, linear: bool) -> usize {
        let mut usage = wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST;
        if target {
            usage |= wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC;
        }
        let size = wgpu::Extent3d { width: w.max(1), height: h.max(1), depth_or_array_layers: 1 };
        let texture = self.device.create_texture(&wgpu::TextureDescriptor {
            label: None,
            size,
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: SCENE_FORMAT,
            usage,
            view_formats: &[],
        });
        if let Some(data) = rgba {
            self.queue.write_texture(
                wgpu::TexelCopyTextureInfo { texture: &texture, mip_level: 0, origin: wgpu::Origin3d::ZERO, aspect: wgpu::TextureAspect::All },
                data,
                wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(4 * size.width), rows_per_image: Some(size.height) },
                size,
            );
        }
        let view = texture.create_view(&Default::default());
        let sampler = if linear { &self.sampler_linear } else { &self.sampler_main };
        let bind = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout: &self.tex_layout,
            entries: &[
                wgpu::BindGroupEntry { binding: 0, resource: wgpu::BindingResource::TextureView(&view) },
                wgpu::BindGroupEntry { binding: 1, resource: wgpu::BindingResource::Sampler(sampler) },
            ],
        });
        self.textures.push(Texture { texture, view, bind, w: size.width, h: size.height });
        self.textures.len()
    }

    pub fn texture_size(&self, handle: u32) -> Option<(u32, u32)> {
        self.textures.get(handle.wrapping_sub(1) as usize).map(|t| (t.w, t.h))
    }

    /// Compila um shader já conferido pelo naga. Devolve o identificador (índice no vetor).
    pub fn add_shader(&mut self, full_source: String) -> Result<u32, String> {
        self.device.push_error_scope(wgpu::ErrorFilter::Validation);
        let module = self.device.create_shader_module(wgpu::ShaderModuleDescriptor { label: Some("usuário"), source: wgpu::ShaderSource::Wgsl(full_source.into()) });
        let sprite = self.sprite_pipeline(&module);
        let post = Self::fullscreen_pipeline(&self.device, &self.sprite_layout, &module, SCENE_FORMAT);
        if let Some(e) = pollster::block_on(self.device.pop_error_scope()) {
            return Err(e.to_string());
        }
        let globals = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("globais"),
            size: std::mem::size_of::<Globals>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let globals_bind = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: None,
            layout: &self.globals_layout,
            entries: &[wgpu::BindGroupEntry { binding: 0, resource: globals.as_entire_binding() }],
        });
        self.shaders.push(ShaderEntry { globals, globals_bind, sprite, post, params: [[0.0; 4]; 4] });
        Ok((self.shaders.len() - 1) as u32)
    }

    /// Liga a normal map (outra textura) a um sprite. 0 desliga.
    pub fn set_normal_map(&mut self, texture: u32, normal: u32) {
        let tex = self.texture_index(texture);
        if normal == 0 || texture == 0 {
            self.normal_maps.remove(&tex);
        } else {
            self.normal_maps.insert(tex, self.texture_index(normal));
        }
    }

    /// Luzes deste quadro em diante. Desligada, a cena sai como está, sem custo nenhum.
    pub fn set_lighting(&mut self, on: bool, ambient: [f32; 4], softness: f32, lights: &[light::LightData], occluders: &[[f32; 4]]) {
        self.light_on = on;
        self.light_ambient = ambient;
        self.light_softness = softness;
        self.lights.clear();
        self.lights.extend_from_slice(&lights[..lights.len().min(light::MAX_LIGHTS)]);
        self.occluders.clear();
        self.occluders.extend_from_slice(&occluders[..occluders.len().min(light::MAX_OCCLUDERS)]);
    }

    pub fn set_param(&mut self, shader: u32, index: u32, value: [f32; 4]) {
        if let Some(s) = self.shaders.get_mut(shader as usize) {
            if (index as usize) < 4 {
                s.params[index as usize] = value;
            }
        }
    }

    pub fn add_mesh(&mut self, vertices: &[Vertex]) -> u32 {
        use wgpu::util::DeviceExt;
        let buffer = self.device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("malha"),
            contents: bytemuck::cast_slice(vertices),
            usage: wgpu::BufferUsages::VERTEX,
        });
        self.meshes.push(MeshEntry { buffer, count: vertices.len() as u32 });
        self.meshes.len() as u32
    }

    pub fn builtin_mesh(&mut self, kind: u32) -> u32 {
        let verts = match kind {
            1 => mesh::cube(),
            2 => mesh::sphere(24, 16),
            _ => mesh::plane(),
        };
        self.add_mesh(&verts)
    }

    pub fn resize(&mut self, w: u32, h: u32) {
        if w > 0 && h > 0 {
            self.config.width = w;
            self.config.height = h;
            self.surface.configure(&self.device, &self.config);
        }
    }

    // ---------- quadro ----------

    fn texture_index(&self, handle: u32) -> usize {
        let i = handle as usize;
        if i == 0 || i > self.textures.len() { 0 } else { i - 1 }
    }

    fn push_quad(&mut self, tex: usize, shader: usize, inst: Instance) {
        let start = self.instances.len() as u32;
        self.instances.push(inst);
        if let Some(Batch::Sprites { shader: s, tex: t, count, .. }) = self.batches.last_mut() {
            if *s == shader && *t == tex {
                *count += 1;
                return;
            }
        }
        self.batches.push(Batch::Sprites { shader, tex, start, count: 1 });
    }

    pub fn measure(&mut self, text: &str, size: f32) -> (f32, f32) {
        self.atlas.measure(text, size.round().max(1.0) as u32)
    }

    fn push_text(&mut self, text: &str, c: &DrawCmd) {
        let size = c.size.round().max(1.0) as u32;
        let ascent = self.atlas.ascent(size as f32);
        let line_h = self.atlas.line_height(size as f32);
        let color = [c.r, c.g, c.b, c.a];
        for (i, line) in text.split('\n').enumerate() {
            let width = self.atlas.measure(line, size).0;
            let mut pen = match c.align {
                1 => c.x - width / 2.0,
                2 => c.x - width,
                _ => c.x,
            };
            let baseline = c.y + ascent + line_h * i as f32;
            for ch in line.chars() {
                let g = self.atlas.glyph(ch, size);
                if g.w > 0.0 {
                    let top = baseline - g.h - g.ymin;
                    self.push_quad(self.atlas_tex, 0, Instance {
                        rect: [(pen + g.xmin).round(), top.round(), g.w, g.h],
                        orot: [0.0; 4],
                        uv: g.uv,
                        color,
                    });
                }
                pen += g.advance;
            }
        }
    }

    pub fn frame(&mut self, clear: [f32; 4], cam: (f32, f32), cmds: &[DrawCmd], strings: &[u8], post: &[u32]) -> Result<(), String> {
        self.proj = Mat4::orthographic_rh(cam.0, cam.0 + self.width as f32, cam.1 + self.height as f32, cam.1, -1000.0, 1000.0);
        self.instances.clear();
        self.batches.clear();
        self.mesh_uniforms.clear();

        for c in cmds {
            match c.kind {
                1 => {
                    let shader = if (c.shader as usize) < self.shaders.len() { c.shader as usize } else { 0 };
                    let tex = self.texture_index(c.texture);
                    self.push_quad(tex, shader, Instance {
                        rect: [c.x, c.y, c.w, c.h],
                        // O quarto valor marca o que não recebe luz (bit 1 de flags).
                        orot: [c.ox, c.oy, c.rot, if c.flags & 1 != 0 { 1.0 } else { 0.0 }],
                        uv: [c.u0, c.v0, c.u1, c.v1],
                        color: [c.r, c.g, c.b, c.a],
                    });
                }
                2 => {
                    let start = c.str_off as usize;
                    let end = start.saturating_add(c.str_len as usize).min(strings.len());
                    if start < end {
                        if let Ok(text) = std::str::from_utf8(&strings[start..end]) {
                            let text = text.to_owned();
                            self.push_text(&text, c);
                        }
                    }
                }
                3 => {
                    let index = c.mesh as usize;
                    if index == 0 || index > self.meshes.len() {
                        continue;
                    }
                    let model = Mat4::from_translation(Vec3::new(c.x, c.y, c.z))
                        * Mat4::from_rotation_z(c.rz.to_radians())
                        * Mat4::from_rotation_y(c.ry.to_radians())
                        * Mat4::from_rotation_x(c.rx.to_radians())
                        * Mat4::from_scale(Vec3::new(c.w, c.h, c.size));
                    let u = MeshUniform {
                        mvp: (self.proj * model).to_cols_array_2d(),
                        normal_mat: model.inverse().transpose().to_cols_array_2d(),
                        color: [c.r, c.g, c.b, c.a],
                        light: c.light,
                        flags: [if c.texture != 0 { 1.0 } else { 0.0 }, 0.0, 0.0, 0.0],
                    };
                    let offset = self.mesh_uniforms.len() as u32;
                    self.mesh_uniforms.extend_from_slice(bytemuck::bytes_of(&u));
                    self.mesh_uniforms.resize(self.mesh_uniforms.len() + (MESH_STRIDE as usize - std::mem::size_of::<MeshUniform>()), 0);
                    let tex = self.texture_index(c.texture);
                    self.batches.push(Batch::Mesh { mesh: index - 1, tex, offset });
                }
                _ => {}
            }
        }

        // Envia o que mudou para a GPU.
        let atlas = &self.textures[self.atlas_tex].texture;
        for (x, y, w, h, data) in self.atlas.pending.drain(..) {
            self.queue.write_texture(
                wgpu::TexelCopyTextureInfo { texture: atlas, mip_level: 0, origin: wgpu::Origin3d { x, y, z: 0 }, aspect: wgpu::TextureAspect::All },
                &data,
                wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(4 * w), rows_per_image: Some(h) },
                wgpu::Extent3d { width: w, height: h, depth_or_array_layers: 1 },
            );
        }
        for s in &self.shaders {
            let g = Globals {
                proj: self.proj.to_cols_array_2d(),
                resolution: [self.width as f32, self.height as f32],
                time: self.time,
                _pad: 0.0,
                params: s.params,
            };
            self.queue.write_buffer(&s.globals, 0, bytemuck::bytes_of(&g));
        }
        if self.instances.len() > self.inst_cap {
            self.inst_cap = self.instances.len().next_power_of_two();
            self.inst_buf = Self::instance_buffer(&self.device, self.inst_cap);
        }
        if !self.instances.is_empty() {
            self.queue.write_buffer(&self.inst_buf, 0, bytemuck::cast_slice(&self.instances));
        }
        let mesh_count = self.mesh_uniforms.len() / MESH_STRIDE as usize;
        if mesh_count > self.mesh_cap {
            self.mesh_cap = mesh_count.next_power_of_two();
            self.mesh_ubuf = Self::mesh_buffer(&self.device, self.mesh_cap);
            self.mesh_bind = Self::mesh_bind_group(&self.device, &self.mesh_u_layout, &self.mesh_ubuf);
        }
        if !self.mesh_uniforms.is_empty() {
            self.queue.write_buffer(&self.mesh_ubuf, 0, &self.mesh_uniforms);
        }

        let mut encoder = self.device.create_command_encoder(&wgpu::CommandEncoderDescriptor { label: Some("quadro") });
        {
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("cena"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &self.textures[self.scene].view,
                    resolve_target: None,
                    ops: wgpu::Operations {
                        load: wgpu::LoadOp::Clear(wgpu::Color { r: clear[0] as f64, g: clear[1] as f64, b: clear[2] as f64, a: clear[3] as f64 }),
                        store: wgpu::StoreOp::Store,
                    },
                })],
                depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                    view: &self.depth_view,
                    depth_ops: Some(wgpu::Operations { load: wgpu::LoadOp::Clear(1.0), store: wgpu::StoreOp::Discard }),
                    stencil_ops: None,
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
            });
            for batch in &self.batches {
                match *batch {
                    Batch::Sprites { shader, tex, start, count } => {
                        let s = &self.shaders[shader];
                        pass.set_pipeline(&s.sprite);
                        pass.set_bind_group(0, &s.globals_bind, &[]);
                        pass.set_bind_group(1, &self.textures[tex].bind, &[]);
                        pass.set_vertex_buffer(0, self.quad.slice(..));
                        pass.set_vertex_buffer(1, self.inst_buf.slice(..));
                        pass.draw(0..4, start..start + count);
                    }
                    Batch::Mesh { mesh, tex, offset } => {
                        let m = &self.meshes[mesh];
                        pass.set_pipeline(&self.mesh_pipeline);
                        pass.set_bind_group(0, &self.mesh_bind, &[offset]);
                        pass.set_bind_group(1, &self.textures[tex].bind, &[]);
                        pass.set_vertex_buffer(0, m.buffer.slice(..));
                        pass.draw(0..m.count, 0..1);
                    }
                }
            }
        }

        let mut source = self.scene;
        if self.light_on {
            let uniform = light::LightUniform::build(
                [cam.0, cam.1, self.width as f32, self.height as f32],
                self.light_ambient, &self.lights, &self.occluders, self.light_softness);
            self.queue.write_buffer(&self.light_buf, 0, bytemuck::bytes_of(&uniform));
            // 1. As normais de cada sprite. O fundo é plano e recebe luz.
            {
                let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                    label: Some("normais"),
                    color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                        view: &self.textures[self.normal_target].view,
                        resolve_target: None,
                        ops: wgpu::Operations { load: wgpu::LoadOp::Clear(wgpu::Color { r: 0.5, g: 0.5, b: 1.0, a: 1.0 }), store: wgpu::StoreOp::Store },
                    })],
                    depth_stencil_attachment: None,
                    timestamp_writes: None,
                    occlusion_query_set: None,
                });
                pass.set_pipeline(&self.normal_pipeline);
                pass.set_bind_group(0, &self.shaders[0].globals_bind, &[]);
                pass.set_vertex_buffer(0, self.quad.slice(..));
                pass.set_vertex_buffer(1, self.inst_buf.slice(..));
                for batch in &self.batches {
                    if let Batch::Sprites { tex, start, count, .. } = *batch {
                        let normal = self.normal_maps.get(&tex).copied().unwrap_or(self.flat_normal);
                        pass.set_bind_group(1, &self.textures[tex].bind, &[]);
                        pass.set_bind_group(2, &self.textures[normal].bind, &[]);
                        pass.draw(0..4, start..start + count);
                    }
                }
            }
            // 2. A luz: cena vezes (ambiente + cada luz, com relevo e sombra).
            {
                let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                    label: Some("luz"),
                    color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                        view: &self.textures[self.lit_target].view,
                        resolve_target: None,
                        ops: wgpu::Operations { load: wgpu::LoadOp::Clear(wgpu::Color::BLACK), store: wgpu::StoreOp::Store },
                    })],
                    depth_stencil_attachment: None,
                    timestamp_writes: None,
                    occlusion_query_set: None,
                });
                pass.set_pipeline(&self.light_pipeline);
                pass.set_bind_group(0, &self.light_bind, &[]);
                pass.set_bind_group(1, &self.textures[self.scene].bind, &[]);
                pass.set_bind_group(2, &self.textures[self.normal_target].bind, &[]);
                pass.draw(0..4, 0..1);
            }
            source = self.lit_target;
        }
        for (i, &shader) in post.iter().enumerate() {
            let Some(s) = self.shaders.get(shader as usize) else { continue };
            let target = self.ping[i % 2];
            {
                let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                    label: Some("pós"),
                    color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                        view: &self.textures[target].view,
                        resolve_target: None,
                        ops: wgpu::Operations { load: wgpu::LoadOp::Clear(wgpu::Color::BLACK), store: wgpu::StoreOp::Store },
                    })],
                    depth_stencil_attachment: None,
                    timestamp_writes: None,
                    occlusion_query_set: None,
                });
                pass.set_pipeline(&s.post);
                pass.set_bind_group(0, &s.globals_bind, &[]);
                pass.set_bind_group(1, &self.textures[source].bind, &[]);
                pass.draw(0..4, 0..1);
            }
            source = target;
        }
        self.last_output = source;

        let (win_w, win_h) = (self.config.width as f32, self.config.height as f32);
        let mut factor = (win_w / self.width as f32).min(win_h / self.height as f32);
        if self.pixel_art && factor >= 1.0 {
            factor = factor.floor();
        }
        let (vw, vh) = (self.width as f32 * factor, self.height as f32 * factor);
        let (vx, vy) = (((win_w - vw) / 2.0).floor(), ((win_h - vh) / 2.0).floor());
        self.viewport = (vx, vy, factor);

        let frame = match self.surface.get_current_texture() {
            Ok(f) => f,
            Err(wgpu::SurfaceError::Outdated) | Err(wgpu::SurfaceError::Lost) => {
                self.surface.configure(&self.device, &self.config);
                self.queue.submit(Some(encoder.finish()));
                return Ok(());
            }
            Err(wgpu::SurfaceError::Timeout) => {
                self.queue.submit(Some(encoder.finish()));
                return Ok(());
            }
            Err(e) => return Err(format!("a janela não aceitou o quadro: {e}")),
        };
        let view = frame.texture.create_view(&Default::default());
        {
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("janela"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &view,
                    resolve_target: None,
                    ops: wgpu::Operations { load: wgpu::LoadOp::Clear(wgpu::Color::BLACK), store: wgpu::StoreOp::Store },
                })],
                depth_stencil_attachment: None,
                timestamp_writes: None,
                occlusion_query_set: None,
            });
            if vw >= 1.0 && vh >= 1.0 {
                pass.set_viewport(vx.max(0.0), vy.max(0.0), vw.min(win_w), vh.min(win_h), 0.0, 1.0);
                pass.set_pipeline(&self.blit);
                pass.set_bind_group(0, &self.shaders[0].globals_bind, &[]);
                pass.set_bind_group(1, &self.textures[source].bind, &[]);
                pass.draw(0..4, 0..1);
            }
        }
        self.queue.submit(Some(encoder.finish()));
        frame.present();
        Ok(())
    }

    /// Salva o último quadro do jogo (já com o pós-processamento) num PNG.
    pub fn screenshot(&self, path: &str) -> Result<(), String> {
        let tex = &self.textures[self.last_output];
        let (w, h) = (tex.w, tex.h);
        let padded = (4 * w).div_ceil(256) * 256;
        let buffer = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("foto"),
            size: (padded * h) as u64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.copy_texture_to_buffer(
            wgpu::TexelCopyTextureInfo { texture: &tex.texture, mip_level: 0, origin: wgpu::Origin3d::ZERO, aspect: wgpu::TextureAspect::All },
            wgpu::TexelCopyBufferInfo { buffer: &buffer, layout: wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(padded), rows_per_image: Some(h) } },
            wgpu::Extent3d { width: w, height: h, depth_or_array_layers: 1 },
        );
        self.queue.submit(Some(encoder.finish()));
        let slice = buffer.slice(..);
        slice.map_async(wgpu::MapMode::Read, |_| {});
        self.device.poll(wgpu::PollType::Wait).map_err(err("a placa de vídeo não respondeu"))?;
        let data = slice.get_mapped_range();
        let mut pixels = Vec::with_capacity((w * h * 4) as usize);
        for row in 0..h as usize {
            let start = row * padded as usize;
            pixels.extend_from_slice(&data[start..start + 4 * w as usize]);
        }
        drop(data);
        buffer.unmap();
        image::save_buffer(path, &pixels, w, h, image::ColorType::Rgba8).map_err(err("não foi possível salvar a foto"))
    }
}
