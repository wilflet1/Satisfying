use super::InputCmd;
use crate::audio::{to_i16_stereo, AudioFormat};
use anyhow::{anyhow, bail, Context, Result};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{self, Sender};
use std::sync::{Arc, Mutex, OnceLock};

// ---------------------------------------------------------------------------
// Notifications

pub fn notify(title: &str, body: &str) {
    use tauri_winrt_notification::Toast;
    let res = Toast::new(Toast::POWERSHELL_APP_ID)
        .title(title)
        .text1(body)
        .sound(None)
        .show();
    if let Err(e) = res {
        tracing::warn!("toast failed: {e:?}");
    }
}

/// Opens a URL, file or folder with its default handler.
pub fn shell_execute(target: &str) -> Result<()> {
    use windows::core::{w, HSTRING};
    use windows::Win32::UI::Shell::ShellExecuteW;
    use windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
    com_init();
    let file = HSTRING::from(target);
    let result = unsafe { ShellExecuteW(None, w!("open"), &file, None, None, SW_SHOWNORMAL) };
    // Values above 32 mean success.
    if result.0 as usize > 32 {
        Ok(())
    } else {
        bail!("could not open {target} (error {})", result.0 as usize)
    }
}

// ---------------------------------------------------------------------------
// Keyboard and mouse. Enigo is driven from one dedicated thread.

type InputJob = (InputCmd, mpsc::Sender<Result<()>>);

fn input_thread() -> &'static Mutex<mpsc::Sender<InputJob>> {
    static TX: OnceLock<Mutex<mpsc::Sender<InputJob>>> = OnceLock::new();
    TX.get_or_init(|| {
        let (tx, rx) = mpsc::channel::<InputJob>();
        std::thread::Builder::new()
            .name("pclink-input".into())
            .spawn(move || {
                use enigo::{Enigo, Settings};
                let mut enigo = match Enigo::new(&Settings::default()) {
                    Ok(e) => e,
                    Err(e) => {
                        for (_, reply) in rx {
                            let _ = reply.send(Err(anyhow!("input unavailable: {e:?}")));
                        }
                        return;
                    }
                };
                for (cmd, reply) in rx {
                    let _ = reply.send(run_input(&mut enigo, cmd));
                }
            })
            .expect("input thread");
        Mutex::new(tx)
    })
}

fn run_input(e: &mut enigo::Enigo, cmd: InputCmd) -> Result<()> {
    use enigo::{Axis, Coordinate, Direction, Keyboard, Mouse};
    let err = |x: enigo::InputError| anyhow!("{x:?}");
    match cmd {
        InputCmd::Move { dx, dy } => e.move_mouse(dx, dy, Coordinate::Rel).map_err(err)?,
        InputCmd::Click { button, double } => {
            let b = mouse_button(&button)?;
            e.button(b, Direction::Click).map_err(err)?;
            if double {
                e.button(b, Direction::Click).map_err(err)?;
            }
        }
        InputCmd::Button { button, down } => e
            .button(
                mouse_button(&button)?,
                if down { Direction::Press } else { Direction::Release },
            )
            .map_err(err)?,
        InputCmd::Scroll { dx, dy } => {
            if dy != 0 {
                e.scroll(dy, Axis::Vertical).map_err(err)?;
            }
            if dx != 0 {
                e.scroll(dx, Axis::Horizontal).map_err(err)?;
            }
        }
        InputCmd::Text(t) => e.text(&t).map_err(err)?,
        InputCmd::Key { key, modifiers } => {
            let mods: Vec<enigo::Key> = modifiers
                .iter()
                .map(|m| named_key(m))
                .collect::<Result<_>>()?;
            let k = named_key(&key)?;
            for m in &mods {
                e.key(*m, Direction::Press).map_err(err)?;
            }
            let r = e.key(k, Direction::Click).map_err(err);
            for m in mods.iter().rev() {
                let _ = e.key(*m, Direction::Release);
            }
            r?;
        }
    }
    Ok(())
}

fn mouse_button(name: &str) -> Result<enigo::Button> {
    Ok(match name {
        "left" | "" => enigo::Button::Left,
        "right" => enigo::Button::Right,
        "middle" => enigo::Button::Middle,
        other => bail!("unknown mouse button {other}"),
    })
}

fn named_key(name: &str) -> Result<enigo::Key> {
    use enigo::Key::*;
    let lower = name.to_ascii_lowercase();
    Ok(match lower.as_str() {
        "enter" | "return" => Return,
        "backspace" => Backspace,
        "delete" | "del" => Delete,
        "tab" => Tab,
        "esc" | "escape" => Escape,
        "space" => Space,
        "up" => UpArrow,
        "down" => DownArrow,
        "left" => LeftArrow,
        "right" => RightArrow,
        "home" => Home,
        "end" => End,
        "pageup" => PageUp,
        "pagedown" => PageDown,
        "ctrl" | "control" => Control,
        "shift" => Shift,
        "alt" => Alt,
        "win" | "meta" | "super" => Meta,
        "f1" => F1, "f2" => F2, "f3" => F3, "f4" => F4, "f5" => F5, "f6" => F6,
        "f7" => F7, "f8" => F8, "f9" => F9, "f10" => F10, "f11" => F11, "f12" => F12,
        "play_pause" => MediaPlayPause,
        "next" => MediaNextTrack,
        "prev" => MediaPrevTrack,
        "vol_up" => VolumeUp,
        "vol_down" => VolumeDown,
        "mute" => VolumeMute,
        _ => {
            let chars: Vec<char> = name.chars().collect();
            if chars.len() == 1 {
                Unicode(chars[0])
            } else {
                bail!("unknown key {name}")
            }
        }
    })
}

/// Queues an input event without waiting for it to run. Events run in
/// the order they're queued.
pub fn input_queue(cmd: InputCmd) -> Result<()> {
    let (tx, _rx) = mpsc::channel();
    input_thread()
        .lock()
        .unwrap()
        .send((cmd, tx))
        .map_err(|_| anyhow!("input thread stopped"))
}

/// Seconds since the last keyboard/mouse input.
pub fn idle_secs() -> Option<u64> {
    use windows::Win32::System::SystemInformation::GetTickCount;
    use windows::Win32::UI::Input::KeyboardAndMouse::{GetLastInputInfo, LASTINPUTINFO};
    let mut info = LASTINPUTINFO {
        cbSize: std::mem::size_of::<LASTINPUTINFO>() as u32,
        dwTime: 0,
    };
    unsafe {
        if !GetLastInputInfo(&mut info).as_bool() {
            return None;
        }
        Some(GetTickCount().wrapping_sub(info.dwTime) as u64 / 1000)
    }
}

// ---------------------------------------------------------------------------
// Power and session commands

pub fn sys(cmd: &str) -> Result<()> {
    use windows::Win32::System::Power::SetSuspendState;
    use windows::Win32::System::Shutdown::LockWorkStation;
    match cmd {
        "lock" => unsafe { LockWorkStation().context("locking") },
        "sleep" => {
            if unsafe { SetSuspendState(false, false, false) } {
                Ok(())
            } else {
                bail!("sleep was refused")
            }
        }
        "hibernate" => {
            if unsafe { SetSuspendState(true, false, false) } {
                Ok(())
            } else {
                bail!("hibernate was refused (is it enabled?)")
            }
        }
        "shutdown" => run_shutdown(&["/s", "/t", "0"]),
        "restart" => run_shutdown(&["/r", "/t", "0"]),
        "signout" => run_shutdown(&["/l"]),
        "display_off" => display_off(),
        other => bail!("unknown command {other}"),
    }
}

fn run_shutdown(args: &[&str]) -> Result<()> {
    std::process::Command::new("shutdown.exe").args(args).spawn()?;
    Ok(())
}

fn display_off() -> Result<()> {
    use windows::Win32::Foundation::{LPARAM, WPARAM};
    use windows::Win32::UI::WindowsAndMessaging::{
        SendMessageTimeoutW, HWND_BROADCAST, SC_MONITORPOWER, SMTO_ABORTIFHUNG, WM_SYSCOMMAND,
    };
    unsafe {
        SendMessageTimeoutW(
            HWND_BROADCAST,
            WM_SYSCOMMAND,
            WPARAM(SC_MONITORPOWER as usize),
            LPARAM(2),
            SMTO_ABORTIFHUNG,
            1000,
            None,
        );
    }
    Ok(())
}

// ---------------------------------------------------------------------------
// Audio endpoints: listing, and switching Windows' default output.
//
// Switching uses IPolicyConfig, an undocumented COM interface that Windows'
// own Sound control panel uses. It has been stable since Windows 7 and is
// what tools like AudioDeviceCmdlets and SoundSwitch rely on.

mod policy {
    #![allow(non_snake_case)]
    use windows::core::{interface, IUnknown, IUnknown_Vtbl, GUID, HRESULT, PCWSTR};
    use windows::Win32::Media::Audio::ERole;

    pub const CLSID_POLICY_CONFIG_CLIENT: GUID =
        GUID::from_u128(0x870af99c_171d_4f9e_af0d_e63df40c2bc9);

    #[interface("f8679f50-850a-41cf-9c72-430f290290c8")]
    pub unsafe trait IPolicyConfig: IUnknown {
        // Ten methods we never call; only their vtable slots matter.
        fn GetMixFormat(&self) -> HRESULT;
        fn GetDeviceFormat(&self) -> HRESULT;
        fn ResetDeviceFormat(&self) -> HRESULT;
        fn SetDeviceFormat(&self) -> HRESULT;
        fn GetProcessingPeriod(&self) -> HRESULT;
        fn SetProcessingPeriod(&self) -> HRESULT;
        fn GetShareMode(&self) -> HRESULT;
        fn SetShareMode(&self) -> HRESULT;
        fn GetPropertyValue(&self) -> HRESULT;
        fn SetPropertyValue(&self) -> HRESULT;
        pub fn SetDefaultEndpoint(&self, device_id: PCWSTR, role: ERole) -> HRESULT;
    }
}

fn com_init() {
    use windows::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};
    // Fails harmlessly if this thread already initialized COM differently.
    let _ = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
}

fn enumerator() -> Result<windows::Win32::Media::Audio::IMMDeviceEnumerator> {
    use windows::Win32::Media::Audio::MMDeviceEnumerator;
    use windows::Win32::System::Com::{CoCreateInstance, CLSCTX_ALL};
    com_init();
    Ok(unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)? })
}

fn device_id_and_name(dev: &windows::Win32::Media::Audio::IMMDevice) -> Result<(String, String)> {
    use windows::Win32::Devices::FunctionDiscovery::PKEY_Device_FriendlyName;
    use windows::Win32::System::Com::StructuredStorage::PropVariantToStringAlloc;
    use windows::Win32::System::Com::{CoTaskMemFree, STGM_READ};
    unsafe {
        let id_ptr = dev.GetId()?;
        let id = id_ptr.to_string()?;
        CoTaskMemFree(Some(id_ptr.0 as *const _));
        let store = dev.OpenPropertyStore(STGM_READ)?;
        let value = store.GetValue(&PKEY_Device_FriendlyName)?;
        let name_ptr = PropVariantToStringAlloc(&value)?;
        let name = name_ptr.to_string()?;
        CoTaskMemFree(Some(name_ptr.0 as *const _));
        Ok((id, name))
    }
}

fn devices(flow: windows::Win32::Media::Audio::EDataFlow) -> Vec<(String, String)> {
    use windows::Win32::Media::Audio::DEVICE_STATE_ACTIVE;
    let run = || -> Result<Vec<(String, String)>> {
        let en = enumerator()?;
        let coll = unsafe { en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE)? };
        let mut out = vec![];
        for i in 0..unsafe { coll.GetCount()? } {
            let dev = unsafe { coll.Item(i)? };
            if let Ok(pair) = device_id_and_name(&dev) {
                out.push(pair);
            }
        }
        Ok(out)
    };
    run().unwrap_or_default()
}

fn default_device(flow: windows::Win32::Media::Audio::EDataFlow) -> Option<(String, String)> {
    use windows::Win32::Media::Audio::eConsole;
    let en = enumerator().ok()?;
    let dev = unsafe { en.GetDefaultAudioEndpoint(flow, eConsole).ok()? };
    device_id_and_name(&dev).ok()
}

/// Active output devices as (id, friendly name).
pub fn output_devices() -> Vec<(String, String)> {
    devices(windows::Win32::Media::Audio::eRender)
}

pub fn default_output() -> Option<(String, String)> {
    default_device(windows::Win32::Media::Audio::eRender)
}

/// Active recording devices (microphones) as (id, friendly name).
pub fn input_devices() -> Vec<(String, String)> {
    devices(windows::Win32::Media::Audio::eCapture)
}

pub fn default_input() -> Option<(String, String)> {
    default_device(windows::Win32::Media::Audio::eCapture)
}

/// Mutes or unmutes an output device. Returns whether it was muted before.
pub fn set_output_mute(id: &str, mute: bool) -> Result<bool> {
    use windows::core::HSTRING;
    use windows::Win32::Media::Audio::Endpoints::IAudioEndpointVolume;
    use windows::Win32::System::Com::CLSCTX_ALL;
    let en = enumerator()?;
    unsafe {
        let dev = en.GetDevice(&HSTRING::from(id))?;
        let vol: IAudioEndpointVolume = dev.Activate(CLSCTX_ALL, None)?;
        let was = vol.GetMute()?.as_bool();
        vol.SetMute(mute, std::ptr::null())?;
        Ok(was)
    }
}

/// Makes `id` (an output or a recording device) Windows' default for all roles.
pub fn set_default_output(id: &str) -> Result<()> {
    use policy::{IPolicyConfig, CLSID_POLICY_CONFIG_CLIENT};
    use windows::core::{HSTRING, PCWSTR};
    use windows::Win32::Media::Audio::{eCommunications, eConsole, eMultimedia};
    use windows::Win32::System::Com::{CoCreateInstance, CLSCTX_ALL};
    com_init();
    let policy: IPolicyConfig = unsafe { CoCreateInstance(&CLSID_POLICY_CONFIG_CLIENT, None, CLSCTX_ALL)? };
    let wide = HSTRING::from(id);
    for role in [eConsole, eMultimedia, eCommunications] {
        unsafe { policy.SetDefaultEndpoint(PCWSTR(wide.as_ptr()), role).ok()? };
    }
    Ok(())
}

// ---------------------------------------------------------------------------
// Loopback capture: everything the PC plays on an output device.

/// Starts capturing `device_name` (or the default output) on a dedicated
/// thread and forwards 16-bit PCM blocks to `sink` until `stop` is set.
pub fn start_capture(
    device_name: Option<String>,
    sink: Sender<Vec<i16>>,
    stop: Arc<AtomicBool>,
) -> Result<AudioFormat> {
    let (ready_tx, ready_rx) = mpsc::channel::<Result<AudioFormat>>();
    std::thread::Builder::new()
        .name("pclink-audio-capture".into())
        .spawn(move || {
            let stream = match open_loopback(device_name, sink) {
                Ok((stream, fmt)) => {
                    let _ = ready_tx.send(Ok(fmt));
                    stream
                }
                Err(e) => {
                    let _ = ready_tx.send(Err(e));
                    return;
                }
            };
            while !stop.load(Ordering::Relaxed) {
                std::thread::sleep(std::time::Duration::from_millis(100));
            }
            drop(stream);
        })?;
    ready_rx
        .recv_timeout(std::time::Duration::from_secs(5))
        .map_err(|_| anyhow!("audio capture did not start"))?
}

fn open_loopback(
    device_name: Option<String>,
    sink: Sender<Vec<i16>>,
) -> Result<(cpal::Stream, AudioFormat)> {
    use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
    let host = cpal::default_host();
    let device = match &device_name {
        Some(want) => host
            .output_devices()?
            .find(|d| d.name().map(|n| &n == want).unwrap_or(false))
            .ok_or_else(|| anyhow!("output device `{want}` not found"))?,
        None => host
            .default_output_device()
            .ok_or_else(|| anyhow!("no default output device"))?,
    };
    let supported = device.default_output_config()?;
    let config: cpal::StreamConfig = supported.config();
    let in_ch = config.channels as usize;
    let fmt = AudioFormat {
        rate: config.sample_rate.0,
        channels: config.channels.min(2),
    };
    let err_fn = |e| tracing::warn!("audio capture error: {e}");
    // Building an *input* stream on an *output* device makes cpal open it in
    // WASAPI loopback mode.
    let stream = match supported.sample_format() {
        cpal::SampleFormat::F32 => device.build_input_stream(
            &config,
            move |data: &[f32], _| {
                let mut out = Vec::with_capacity(data.len());
                to_i16_stereo(data, in_ch, &mut out);
                let _ = sink.send(out);
            },
            err_fn,
            None,
        )?,
        cpal::SampleFormat::I16 => device.build_input_stream(
            &config,
            move |data: &[i16], _| {
                let keep = in_ch.min(2);
                let mut out = Vec::with_capacity(data.len());
                for frame in data.chunks_exact(in_ch.max(1)) {
                    out.extend_from_slice(&frame[..keep]);
                }
                let _ = sink.send(out);
            },
            err_fn,
            None,
        )?,
        other => bail!("unsupported capture format {other:?}"),
    };
    stream.play()?;
    Ok((stream, fmt))
}

// ---------------------------------------------------------------------------
// Desktop control for Claude's PC tools: screenshots, monitors, windows,
// cursor and app launching. Coordinates are physical virtual-desktop pixels
// (the process is made per-monitor DPI aware first).

use super::{Monitor, WindowInfo};
use windows::Win32::Foundation::{HWND, LPARAM, RECT};

/// Makes coordinates and screenshots use real pixels on scaled displays.
pub fn dpi_aware() {
    use windows::Win32::UI::HiDpi::{SetProcessDpiAwarenessContext, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2};
    let _ = unsafe { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) };
}

pub fn monitors() -> Vec<Monitor> {
    use windows::core::BOOL;
    use windows::Win32::Graphics::Gdi::{EnumDisplayMonitors, GetMonitorInfoW, HDC, HMONITOR, MONITORINFO};
    unsafe extern "system" fn cb(m: HMONITOR, _: HDC, _: *mut RECT, data: LPARAM) -> BOOL {
        let out = unsafe { &mut *(data.0 as *mut Vec<Monitor>) };
        let mut info = MONITORINFO { cbSize: std::mem::size_of::<MONITORINFO>() as u32, ..Default::default() };
        if unsafe { GetMonitorInfoW(m, &mut info) }.as_bool() {
            let r = info.rcMonitor;
            out.push(Monitor {
                index: out.len(),
                primary: info.dwFlags & 1 != 0, // MONITORINFOF_PRIMARY
                x: r.left,
                y: r.top,
                width: r.right - r.left,
                height: r.bottom - r.top,
            });
        }
        true.into()
    }
    let mut out: Vec<Monitor> = Vec::new();
    unsafe {
        let _ = EnumDisplayMonitors(None, None, Some(cb), LPARAM(&mut out as *mut _ as isize));
    }
    // Primary first, so "monitor 0" is the main screen.
    out.sort_by_key(|m| (!m.primary, m.x, m.y));
    for (i, m) in out.iter_mut().enumerate() {
        m.index = i;
    }
    out
}

/// Captures a rectangle of the desktop.
pub fn capture(x: i32, y: i32, w: i32, h: i32) -> Result<image::RgbaImage> {
    use windows::Win32::Graphics::Gdi::*;
    if w <= 0 || h <= 0 {
        bail!("empty capture area");
    }
    unsafe {
        let screen = GetDC(None);
        let mem = CreateCompatibleDC(Some(screen));
        let bmp = CreateCompatibleBitmap(screen, w, h);
        let old = SelectObject(mem, bmp.into());
        let blit = BitBlt(mem, 0, 0, w, h, Some(screen), x, y, SRCCOPY | CAPTUREBLT);
        let mut info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: w,
                biHeight: -h, // top-down
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                ..Default::default()
            },
            ..Default::default()
        };
        let mut buf = vec![0u8; (w * h * 4) as usize];
        let lines = GetDIBits(mem, bmp, 0, h as u32, Some(buf.as_mut_ptr() as *mut _), &mut info, DIB_RGB_COLORS);
        SelectObject(mem, old);
        let _ = DeleteObject(bmp.into());
        let _ = DeleteDC(mem);
        ReleaseDC(None, screen);
        blit.context("capturing the screen")?;
        if lines == 0 {
            bail!("could not read the screen pixels");
        }
        // BGRA -> RGBA, opaque.
        for px in buf.chunks_exact_mut(4) {
            px.swap(0, 2);
            px[3] = 255;
        }
        image::RgbaImage::from_raw(w as u32, h as u32, buf).ok_or_else(|| anyhow!("bad capture buffer"))
    }
}

/// Captures a screen area scaled to `dw`x`dh` (GDI halftone stretch, much
/// faster than scaling afterwards). Returns top-down BGRA pixels.
pub fn capture_bgra(x: i32, y: i32, w: i32, h: i32, dw: i32, dh: i32) -> Result<Vec<u8>> {
    use windows::Win32::Graphics::Gdi::*;
    if w <= 0 || h <= 0 || dw <= 0 || dh <= 0 {
        bail!("empty capture area");
    }
    unsafe {
        let screen = GetDC(None);
        let mem = CreateCompatibleDC(Some(screen));
        let bmp = CreateCompatibleBitmap(screen, dw, dh);
        let old = SelectObject(mem, bmp.into());
        let ok = if (dw, dh) == (w, h) {
            BitBlt(mem, 0, 0, w, h, Some(screen), x, y, SRCCOPY | CAPTUREBLT).is_ok()
        } else {
            SetStretchBltMode(mem, HALFTONE);
            StretchBlt(mem, 0, 0, dw, dh, Some(screen), x, y, w, h, SRCCOPY | CAPTUREBLT).as_bool()
        };
        let mut info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: dw,
                biHeight: -dh,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                ..Default::default()
            },
            ..Default::default()
        };
        let mut buf = vec![0u8; (dw * dh * 4) as usize];
        let lines = GetDIBits(mem, bmp, 0, dh as u32, Some(buf.as_mut_ptr() as *mut _), &mut info, DIB_RGB_COLORS);
        SelectObject(mem, old);
        let _ = DeleteObject(bmp.into());
        let _ = DeleteDC(mem);
        ReleaseDC(None, screen);
        if !ok || lines == 0 {
            bail!("could not capture the screen");
        }
        Ok(buf)
    }
}

fn hwnd(id: u64) -> HWND {
    HWND(id as usize as *mut core::ffi::c_void)
}

fn window_title(h: HWND) -> String {
    use windows::Win32::UI::WindowsAndMessaging::GetWindowTextW;
    let mut buf = [0u16; 512];
    let n = unsafe { GetWindowTextW(h, &mut buf) };
    String::from_utf16_lossy(&buf[..n.max(0) as usize])
}

fn process_name(h: HWND) -> String {
    use windows::Win32::Foundation::CloseHandle;
    use windows::Win32::System::Threading::{OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION};
    use windows::Win32::UI::WindowsAndMessaging::GetWindowThreadProcessId;
    unsafe {
        let mut pid = 0u32;
        GetWindowThreadProcessId(h, Some(&mut pid));
        let Ok(proc_handle) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) else { return String::new() };
        let mut buf = [0u16; 1024];
        let mut len = buf.len() as u32;
        let ok = QueryFullProcessImageNameW(proc_handle, PROCESS_NAME_WIN32, windows::core::PWSTR(buf.as_mut_ptr()), &mut len).is_ok();
        let _ = CloseHandle(proc_handle);
        if !ok {
            return String::new();
        }
        let full = String::from_utf16_lossy(&buf[..len as usize]);
        full.rsplit('\\').next().unwrap_or(&full).to_string()
    }
}

fn is_cloaked(h: HWND) -> bool {
    use windows::Win32::Graphics::Dwm::{DwmGetWindowAttribute, DWMWA_CLOAKED};
    let mut cloaked = 0u32;
    unsafe { DwmGetWindowAttribute(h, DWMWA_CLOAKED, &mut cloaked as *mut _ as *mut _, 4).is_ok() && cloaked != 0 }
}

fn monitor_index(r: &RECT, mons: &[Monitor]) -> usize {
    let cx = (r.left + r.right) / 2;
    let cy = (r.top + r.bottom) / 2;
    mons.iter()
        .find(|m| cx >= m.x && cx < m.x + m.width && cy >= m.y && cy < m.y + m.height)
        .map(|m| m.index)
        .unwrap_or(0)
}

/// Visible top-level application windows, front to back.
pub fn list_windows() -> Vec<WindowInfo> {
    use windows::core::BOOL;
    use windows::Win32::UI::WindowsAndMessaging::*;
    unsafe extern "system" fn cb(h: HWND, data: LPARAM) -> BOOL {
        let out = unsafe { &mut *(data.0 as *mut Vec<HWND>) };
        out.push(h);
        true.into()
    }
    let mut handles: Vec<HWND> = Vec::new();
    unsafe {
        let _ = EnumWindows(Some(cb), LPARAM(&mut handles as *mut _ as isize));
    }
    let mons = monitors();
    let mut out = Vec::new();
    for h in handles {
        unsafe {
            if !IsWindowVisible(h).as_bool() || is_cloaked(h) {
                continue;
            }
            // Skip owned windows (dialogs, tooltips) and tool windows.
            if GetWindow(h, GW_OWNER).map(|o| !o.0.is_null()).unwrap_or(false) {
                continue;
            }
            let ex = GetWindowLongW(h, GWL_EXSTYLE) as u32;
            if ex & WS_EX_TOOLWINDOW.0 != 0 {
                continue;
            }
            let title = window_title(h);
            if title.is_empty() {
                continue;
            }
            let mut r = RECT::default();
            let _ = GetWindowRect(h, &mut r);
            let state = if IsIconic(h).as_bool() {
                "minimized"
            } else if IsZoomed(h).as_bool() {
                "maximized"
            } else {
                "normal"
            };
            out.push(WindowInfo {
                id: h.0 as usize as u64,
                title,
                process: process_name(h),
                x: r.left,
                y: r.top,
                width: r.right - r.left,
                height: r.bottom - r.top,
                monitor: monitor_index(&r, &mons),
                state: state.into(),
            });
        }
    }
    out
}

/// Brings a window to the front. Windows only lets the foreground app do
/// that, so first tap Alt (the standard workaround).
fn focus(h: HWND) {
    use windows::Win32::UI::WindowsAndMessaging::*;
    unsafe {
        if IsIconic(h).as_bool() {
            let _ = ShowWindow(h, SW_RESTORE);
        }
        let _ = input_blocking(InputCmd::Key { key: "alt".into(), modifiers: vec![] });
        let _ = SetForegroundWindow(h);
        let _ = BringWindowToTop(h);
    }
}

/// `action`: focus, maximize, minimize, restore, close, or move (to `monitor`, maximized).
pub fn window_action(id: u64, action: &str, monitor: Option<usize>) -> Result<()> {
    use windows::Win32::UI::WindowsAndMessaging::*;
    let h = hwnd(id);
    if !list_windows().iter().any(|w| w.id == id) {
        bail!("no window with id {id}; call list_windows again");
    }
    unsafe {
        match action {
            "focus" => focus(h),
            "maximize" => {
                let _ = ShowWindow(h, SW_MAXIMIZE);
                focus(h);
            }
            "minimize" => {
                let _ = ShowWindow(h, SW_MINIMIZE);
            }
            "restore" => {
                let _ = ShowWindow(h, SW_RESTORE);
                focus(h);
            }
            "close" => PostMessageW(Some(h), WM_CLOSE, Default::default(), Default::default())?,
            "move" => {
                let mons = monitors();
                let target = monitor.unwrap_or(0);
                let m = mons.get(target).ok_or_else(|| anyhow!("no monitor {target}; there are {}", mons.len()))?;
                // A maximized window must be restored before it can move.
                let _ = ShowWindow(h, SW_RESTORE);
                SetWindowPos(h, None, m.x + 40, m.y + 40, m.width * 3 / 4, m.height * 3 / 4, SWP_NOZORDER | SWP_NOACTIVATE)?;
                let _ = ShowWindow(h, SW_MAXIMIZE);
                focus(h);
            }
            other => bail!("unknown window action {other}"),
        }
    }
    Ok(())
}

pub fn set_cursor(x: i32, y: i32) -> Result<()> {
    unsafe { windows::Win32::UI::WindowsAndMessaging::SetCursorPos(x, y).context("moving the cursor") }
}

/// Runs an input event and waits for it to finish.
pub fn input_blocking(cmd: InputCmd) -> Result<()> {
    let (tx, rx) = mpsc::channel();
    input_thread()
        .lock()
        .unwrap()
        .send((cmd, tx))
        .map_err(|_| anyhow!("input thread stopped"))?;
    rx.recv().map_err(|_| anyhow!("input thread stopped"))?
}

/// Opens an app, file, folder or URL, with optional arguments.
pub fn launch(target: &str, args: Option<&str>) -> Result<()> {
    use windows::core::{w, HSTRING};
    use windows::Win32::UI::Shell::ShellExecuteW;
    use windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
    com_init();
    let file = HSTRING::from(target);
    let params = args.map(HSTRING::from);
    let result = unsafe {
        match &params {
            Some(p) => ShellExecuteW(None, w!("open"), &file, p, None, SW_SHOWNORMAL),
            None => ShellExecuteW(None, w!("open"), &file, None, None, SW_SHOWNORMAL),
        }
    };
    if result.0 as usize > 32 {
        Ok(())
    } else {
        bail!("could not open {target} (error {})", result.0 as usize)
    }
}

// ---------------------------------------------------------------------------
// Phone mic: play received mic audio into an output device (a virtual
// cable), whose recording side apps then use as a microphone.

/// Plays mono 16-bit audio pulled from `buffer` on `device_name` until
/// `stop` is set. Underruns play silence.
pub fn start_playback(
    device_name: String,
    buffer: Arc<Mutex<std::collections::VecDeque<i16>>>,
    rate: u32,
    stop: Arc<AtomicBool>,
) -> Result<()> {
    let (ready_tx, ready_rx) = mpsc::channel::<Result<()>>();
    std::thread::Builder::new()
        .name("pclink-mic-playback".into())
        .spawn(move || {
            let stream = match open_playback(&device_name, buffer, rate) {
                Ok(s) => {
                    let _ = ready_tx.send(Ok(()));
                    s
                }
                Err(e) => {
                    let _ = ready_tx.send(Err(e));
                    return;
                }
            };
            while !stop.load(Ordering::Relaxed) {
                std::thread::sleep(std::time::Duration::from_millis(100));
            }
            drop(stream);
        })?;
    ready_rx
        .recv_timeout(std::time::Duration::from_secs(5))
        .map_err(|_| anyhow!("mic playback did not start"))?
}

fn open_playback(
    device_name: &str,
    buffer: Arc<Mutex<std::collections::VecDeque<i16>>>,
    src_rate: u32,
) -> Result<cpal::Stream> {
    use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
    let host = cpal::default_host();
    let device = host
        .output_devices()?
        .find(|d| d.name().map(|n| n == device_name).unwrap_or(false))
        .ok_or_else(|| anyhow!("output device `{device_name}` not found"))?;
    let supported = device.default_output_config()?;
    let config: cpal::StreamConfig = supported.config();
    let channels = config.channels as usize;
    let out_rate = config.sample_rate.0 as f64;
    // Linear resampling from the phone's rate to the device's.
    let step = src_rate as f64 / out_rate;
    let mut pos = 0.0f64;
    let mut prev = 0i16;
    let mut next_sample = move |buf: &mut std::collections::VecDeque<i16>| -> f32 {
        pos += step;
        while pos >= 1.0 {
            prev = buf.pop_front().unwrap_or(0);
            pos -= 1.0;
        }
        let next = buf.front().copied().unwrap_or(prev);
        (prev as f64 + (next as f64 - prev as f64) * pos) as f32 / 32768.0
    };
    let err_fn = |e| tracing::warn!("mic playback error: {e}");
    let stream = match supported.sample_format() {
        cpal::SampleFormat::F32 => device.build_output_stream(
            &config,
            move |data: &mut [f32], _| {
                let mut buf = buffer.lock().unwrap();
                for frame in data.chunks_mut(channels) {
                    let v = next_sample(&mut buf);
                    frame.iter_mut().for_each(|s| *s = v);
                }
            },
            err_fn,
            None,
        )?,
        cpal::SampleFormat::I16 => device.build_output_stream(
            &config,
            move |data: &mut [i16], _| {
                let mut buf = buffer.lock().unwrap();
                for frame in data.chunks_mut(channels) {
                    let v = (next_sample(&mut buf) * 32767.0) as i16;
                    frame.iter_mut().for_each(|s| *s = v);
                }
            },
            err_fn,
            None,
        )?,
        other => bail!("unsupported playback format {other:?}"),
    };
    stream.play()?;
    Ok(stream)
}
