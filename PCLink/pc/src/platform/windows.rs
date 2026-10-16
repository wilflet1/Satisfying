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

pub fn input(cmd: InputCmd) -> Result<()> {
    let (tx, rx) = mpsc::channel();
    input_thread()
        .lock()
        .unwrap()
        .send((cmd, tx))
        .map_err(|_| anyhow!("input thread stopped"))?;
    rx.recv().map_err(|_| anyhow!("input thread stopped"))?
}

pub fn media(cmd: &str) -> Result<()> {
    match cmd {
        "play_pause" | "next" | "prev" | "vol_up" | "vol_down" | "mute" => input(InputCmd::Key {
            key: cmd.to_string(),
            modifiers: vec![],
        }),
        other => bail!("unknown media command {other}"),
    }
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

/// Active output devices as (id, friendly name).
pub fn output_devices() -> Vec<(String, String)> {
    use windows::Win32::Media::Audio::{eRender, DEVICE_STATE_ACTIVE};
    let run = || -> Result<Vec<(String, String)>> {
        let en = enumerator()?;
        let coll = unsafe { en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)? };
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

pub fn default_output() -> Option<(String, String)> {
    use windows::Win32::Media::Audio::{eConsole, eRender};
    let en = enumerator().ok()?;
    let dev = unsafe { en.GetDefaultAudioEndpoint(eRender, eConsole).ok()? };
    device_id_and_name(&dev).ok()
}

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
