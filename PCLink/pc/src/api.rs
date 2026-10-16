//! Local HTTP API on 127.0.0.1, used by the dashboard, the `pclink` CLI,
//! Claude Code hooks and the MCP server. Every /api route needs the local
//! token (from the `local-token` file) as a bearer token or `?token=`.

use crate::agent::Agent;
use crate::util;
use axum::body::Body;
use axum::extract::{DefaultBodyLimit, Query, Request, State};
use axum::http::{header, HeaderMap, StatusCode};
use axum::middleware::{self, Next};
use axum::response::{Html, IntoResponse, Response};
use axum::routing::{get, post};
use axum::{Json, Router};
use futures_util::StreamExt;
use serde::Deserialize;
use serde_json::{json, Value};
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;
use tokio::io::AsyncWriteExt;

type AppState = Arc<Agent>;

pub fn router(agent: Arc<Agent>) -> Router {
    let api = Router::new()
        .route("/status", get(status))
        .route("/pair", post(pair))
        .route("/devices/forget", post(forget))
        .route("/phone", post(phone_request))
        .route("/notify", post(notify))
        .route("/ask", post(ask))
        .route("/pull", post(pull))
        .route("/push", post(push))
        .route("/upload", post(upload))
        .route("/download", get(download))
        .route("/notifications", get(notifications))
        .route("/notifications/reply", post(notif_reply))
        .route("/notifications/dismiss", post(notif_dismiss))
        .route("/speaker", post(speaker))
        .route("/clipboard/send", post(clipboard_send))
        .route("/mic", post(mic))
        .route("/cable/open", post(cable_open))
        .route("/actions/run", post(action_run))
        .route("/activity", get(activity))
        .route("/open_inbox", post(open_inbox))
        .route("/update", post(update))
        .route("/tasks", get(tasks_list).post(tasks_start))
        .route("/tasks/get", get(tasks_get))
        .route("/tasks/stop", post(tasks_stop))
        .route("/tasks/approve", post(tasks_approve))
        .route("/tasks/answer", post(tasks_answer))
        .layer(DefaultBodyLimit::disable())
        .layer(middleware::from_fn_with_state(agent.clone(), auth));
    Router::new()
        .route("/", get(index))
        .nest("/api", api)
        .with_state(agent)
}

async fn index() -> Html<&'static str> {
    Html(include_str!("dashboard.html"))
}

async fn auth(State(agent): State<AppState>, req: Request, next: Next) -> Response {
    // Refuse requests whose Host isn't loopback (defends against DNS rebinding).
    let host_ok = req
        .headers()
        .get(header::HOST)
        .and_then(|h| h.to_str().ok())
        .map(|h| {
            let host = h.rsplit_once(':').map(|(h, _)| h).unwrap_or(h);
            matches!(host, "127.0.0.1" | "localhost" | "[::1]")
        })
        .unwrap_or(false);
    let bearer = req
        .headers()
        .get(header::AUTHORIZATION)
        .and_then(|h| h.to_str().ok())
        .and_then(|h| h.strip_prefix("Bearer "))
        .map(str::to_string);
    let query_token = req.uri().query().and_then(|q| {
        q.split('&')
            .find_map(|kv| kv.strip_prefix("token="))
            .map(str::to_string)
    });
    let given = bearer.or(query_token).unwrap_or_default();
    if !host_ok || !util::ct_eq(given.as_bytes(), agent.local_token.as_bytes()) {
        return (StatusCode::UNAUTHORIZED, Json(json!({"error": "unauthorized"}))).into_response();
    }
    next.run(req).await
}

struct ApiError(StatusCode, String);

impl IntoResponse for ApiError {
    fn into_response(self) -> Response {
        (self.0, Json(json!({"error": self.1}))).into_response()
    }
}

impl From<anyhow::Error> for ApiError {
    fn from(e: anyhow::Error) -> Self {
        let msg = format!("{e:#}");
        let code = if msg.contains("not connected") || msg.contains("disconnected") {
            StatusCode::SERVICE_UNAVAILABLE
        } else {
            StatusCode::BAD_REQUEST
        };
        ApiError(code, msg)
    }
}

type ApiResult = Result<Json<Value>, ApiError>;

async fn status(State(a): State<AppState>) -> Json<Value> {
    Json(a.status_json())
}

async fn activity(State(a): State<AppState>) -> Json<Value> {
    Json(json!(a.activity.lock().unwrap().iter().cloned().collect::<Vec<_>>()))
}

async fn pair(State(a): State<AppState>) -> Json<Value> {
    Json(a.start_pairing())
}

#[derive(Deserialize)]
struct IdBody {
    id: String,
}

async fn forget(State(a): State<AppState>, Json(b): Json<IdBody>) -> ApiResult {
    a.forget_device(&b.id)?;
    Ok(Json(json!({})))
}

#[derive(Deserialize)]
struct PhoneReq {
    op: String,
    #[serde(default)]
    args: Value,
    #[serde(default)]
    timeout_ms: Option<u64>,
}

/// Passes any request straight to the phone.
async fn phone_request(State(a): State<AppState>, Json(b): Json<PhoneReq>) -> ApiResult {
    let link = a.phone()?;
    let timeout = Duration::from_millis(b.timeout_ms.unwrap_or(30_000).min(15 * 60_000));
    let args = if b.args.is_null() { json!({}) } else { b.args };
    Ok(Json(link.request(&b.op, args, timeout).await?))
}

#[derive(Deserialize)]
struct NotifyBody {
    #[serde(default)]
    title: Option<String>,
    #[serde(default)]
    text: String,
    /// "claude", "build" or "general": picks the phone's notification channel.
    #[serde(default)]
    channel: Option<String>,
    #[serde(default)]
    url: Option<String>,
}

async fn notify(State(a): State<AppState>, Json(b): Json<NotifyBody>) -> ApiResult {
    let link = a.phone()?;
    let title = b.title.unwrap_or_else(|| a.cfg.pc_name.clone());
    a.log("notify", format!("→ phone: {title}"));
    link.request(
        "notify",
        json!({"title": title, "text": b.text, "channel": b.channel.unwrap_or_else(|| "general".into()), "url": b.url}),
        Duration::from_secs(10),
    )
    .await?;
    Ok(Json(json!({"sent": true})))
}

#[derive(Deserialize)]
struct AskBody {
    #[serde(default)]
    title: Option<String>,
    question: String,
    #[serde(default)]
    detail: Option<String>,
    options: Vec<String>,
    #[serde(default)]
    timeout_s: Option<u64>,
    #[serde(default)]
    channel: Option<String>,
}

/// Asks the user a multiple-choice question on the phone and waits.
async fn ask(State(a): State<AppState>, Json(b): Json<AskBody>) -> ApiResult {
    let link = a.phone()?;
    if b.options.is_empty() || b.options.len() > 6 {
        return Err(ApiError(StatusCode::BAD_REQUEST, "give 1 to 6 options".into()));
    }
    let timeout = b.timeout_s.unwrap_or(120).clamp(5, 3600);
    a.log("ask", format!("→ phone: {}", b.question));
    let res = link
        .request(
            "ask",
            json!({
                "title": b.title.unwrap_or_else(|| "Question".into()),
                "question": b.question,
                "detail": b.detail,
                "options": b.options,
                "timeout_ms": timeout * 1000,
                "channel": b.channel.unwrap_or_else(|| "claude".into()),
            }),
            Duration::from_secs(timeout + 5),
        )
        .await;
    match res {
        Ok(v) => Ok(Json(v)),
        Err(e) if e.to_string().contains("in time") => Ok(Json(json!({"answer": null, "timed_out": true}))),
        Err(e) => Err(e.into()),
    }
}

#[derive(Deserialize)]
struct PullBody {
    path: String,
    #[serde(default)]
    dest: Option<PathBuf>,
}

async fn pull(State(a): State<AppState>, Json(b): Json<PullBody>) -> ApiResult {
    let link = a.phone()?;
    let dest = b.dest.unwrap_or_else(|| a.inbox());
    let path = link.pull(&b.path, &dest).await?;
    a.log("files", format!("phone → PC: {}", path.display()));
    Ok(Json(json!({"path": crate::files::display(&path)})))
}

#[derive(Deserialize)]
struct PushBody {
    path: PathBuf,
    #[serde(default)]
    dest_dir: Option<String>,
}

async fn push(State(a): State<AppState>, Json(b): Json<PushBody>) -> ApiResult {
    let link = a.phone()?;
    let phone_path = link.push(&b.path, b.dest_dir.as_deref()).await?;
    a.log("files", format!("PC → phone: {phone_path}"));
    Ok(Json(json!({"path": phone_path})))
}

/// Dashboard upload: the browser posts raw file bytes, which are staged in
/// a temp folder and then sent to the phone.
async fn upload(
    State(a): State<AppState>,
    Query(q): Query<HashMap<String, String>>,
    body: Body,
) -> ApiResult {
    let link = a.phone()?;
    let name = crate::link::sanitize_name(q.get("name").map(String::as_str).unwrap_or("upload"));
    let staging = std::env::temp_dir().join(format!("pclink-upload-{}", util::random_hex(6)));
    tokio::fs::create_dir_all(&staging).await.map_err(anyhow::Error::from)?;
    let path = staging.join(&name);
    let result: anyhow::Result<String> = async {
        let mut f = tokio::fs::File::create(&path).await?;
        let mut stream = body.into_data_stream();
        while let Some(chunk) = stream.next().await {
            f.write_all(&chunk?).await?;
        }
        f.flush().await?;
        drop(f);
        link.push(&path, q.get("dir").map(String::as_str)).await
    }
    .await;
    let _ = tokio::fs::remove_dir_all(&staging).await;
    let phone_path = result?;
    a.log("files", format!("PC → phone: {phone_path}"));
    Ok(Json(json!({"path": phone_path})))
}

/// Dashboard download: pulls a file from the phone and streams it to the browser.
async fn download(
    State(a): State<AppState>,
    Query(q): Query<HashMap<String, String>>,
) -> Result<Response, ApiError> {
    let link = a.phone()?;
    let phone_path = q
        .get("path")
        .ok_or_else(|| ApiError(StatusCode::BAD_REQUEST, "missing path".into()))?;
    let staging = std::env::temp_dir().join(format!("pclink-dl-{}", util::random_hex(6)));
    let path = link.pull(phone_path, &staging).await?;
    let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("file").to_string();
    let file = tokio::fs::File::open(&path).await.map_err(anyhow::Error::from)?;
    let len = file.metadata().await.map_err(anyhow::Error::from)?.len();
    // Unlinking an open file fails on Windows; clean up after streaming.
    let stream = tokio_util::io::ReaderStream::new(file).chain(futures_util::stream::once(async move {
        let _ = tokio::fs::remove_dir_all(&staging).await;
        Ok::<_, std::io::Error>(axum::body::Bytes::new())
    }));
    let mut headers = HeaderMap::new();
    headers.insert(header::CONTENT_TYPE, "application/octet-stream".parse().unwrap());
    headers.insert(header::CONTENT_LENGTH, len.into());
    let disp = format!("attachment; filename=\"{}\"", name.replace('"', ""));
    if let Ok(v) = disp.parse() {
        headers.insert(header::CONTENT_DISPOSITION, v);
    }
    Ok((headers, Body::from_stream(stream)).into_response())
}

async fn notifications(State(a): State<AppState>) -> Json<Value> {
    Json(json!(a.notifications.lock().unwrap().iter().cloned().collect::<Vec<_>>()))
}

#[derive(Deserialize)]
struct ReplyBody {
    key: String,
    #[serde(default)]
    text: String,
}

async fn notif_reply(State(a): State<AppState>, Json(b): Json<ReplyBody>) -> ApiResult {
    let link = a.phone()?;
    link.request("notif.reply", json!({"key": b.key, "text": b.text}), Duration::from_secs(15))
        .await?;
    Ok(Json(json!({})))
}

async fn notif_dismiss(State(a): State<AppState>, Json(b): Json<ReplyBody>) -> ApiResult {
    let link = a.phone()?;
    link.request("notif.dismiss", json!({"key": b.key}), Duration::from_secs(15))
        .await?;
    a.notifications.lock().unwrap().retain(|n| n.key != b.key);
    Ok(Json(json!({})))
}

#[derive(Deserialize)]
struct SpeakerBody {
    /// true/false, or omitted to toggle.
    #[serde(default)]
    on: Option<bool>,
}

async fn speaker(State(a): State<AppState>, Json(b): Json<SpeakerBody>) -> ApiResult {
    let on = match b.on {
        Some(on) => a.set_speaker(on).await?,
        None => a.toggle_speaker().await?,
    };
    Ok(Json(json!({"on": on})))
}

async fn clipboard_send(State(a): State<AppState>) -> ApiResult {
    let link = a.phone()?;
    let text = crate::platform::clipboard_get_text().unwrap_or_default();
    if text.is_empty() {
        return Err(ApiError(StatusCode::BAD_REQUEST, "The PC clipboard has no text to send.".into()));
    }
    *a.clip_last.lock().unwrap() = text.clone();
    link.request("clip.set", json!({"text": text}), Duration::from_secs(10))
        .await?;
    a.log("clipboard", "PC → phone");
    Ok(Json(json!({"chars": text.chars().count()})))
}

#[derive(Deserialize)]
struct ActionBody {
    name: String,
    #[serde(default)]
    wait: bool,
}

async fn action_run(State(a): State<AppState>, Json(b): Json<ActionBody>) -> ApiResult {
    Ok(Json(a.run_action(&b.name, b.wait).await?))
}

async fn open_inbox(State(a): State<AppState>) -> ApiResult {
    tokio::fs::create_dir_all(a.inbox()).await.map_err(anyhow::Error::from)?;
    crate::platform::open_path(&a.inbox())?;
    Ok(Json(json!({})))
}

#[derive(Deserialize)]
struct UpdateBody {
    #[serde(default)]
    install: bool,
}

/// Checks for (and optionally installs) an update. Installing restarts the
/// agent, so it happens just after this response is sent.
async fn update(State(a): State<AppState>, Json(b): Json<UpdateBody>) -> ApiResult {
    let agent = a.clone();
    let check = tokio::task::spawn_blocking(crate::update::check)
        .await
        .map_err(anyhow::Error::from)??;
    let msg = match check {
        crate::update::Check::Available(m) if b.install => {
            let build = m.build;
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_millis(500));
                agent.install_update(&m);
            });
            format!("Installing build {build}. PCLink will restart in a few seconds.")
        }
        _ => tokio::task::spawn_blocking(move || agent.check_update(false))
            .await
            .map_err(anyhow::Error::from)??,
    };
    Ok(Json(json!({"message": msg})))
}

async fn tasks_list(State(a): State<AppState>) -> Json<Value> {
    Json(json!(a.tasks.summaries()))
}

#[derive(Deserialize)]
struct TaskStart {
    prompt: String,
    #[serde(default)]
    resume: Option<u64>,
    #[serde(default = "yes")]
    ask: bool,
}

fn yes() -> bool {
    true
}

async fn tasks_start(State(a): State<AppState>, Json(b): Json<TaskStart>) -> ApiResult {
    let id = a.start_task(&b.prompt, b.resume, b.ask)?;
    Ok(Json(json!({"id": id})))
}

async fn tasks_get(State(a): State<AppState>, Query(q): Query<HashMap<String, String>>) -> ApiResult {
    let id = q.get("id").and_then(|v| v.parse().ok()).ok_or_else(|| ApiError(StatusCode::BAD_REQUEST, "missing id".into()))?;
    let since = q.get("since").and_then(|v| v.parse().ok()).unwrap_or(0);
    a.tasks
        .get(id, since)
        .map(Json)
        .ok_or_else(|| ApiError(StatusCode::NOT_FOUND, format!("no task {id}")))
}

#[derive(Deserialize)]
struct IdNum {
    id: u64,
}

async fn tasks_stop(State(a): State<AppState>, Json(b): Json<IdNum>) -> ApiResult {
    Ok(Json(json!({"stopped": a.tasks.stop(b.id)})))
}

#[derive(Deserialize)]
struct Approve {
    task: u64,
    tool_name: String,
    #[serde(default)]
    tool_input: Value,
}

/// Called by the task's PermissionRequest hook; answers with the hook output.
async fn tasks_approve(State(a): State<AppState>, Json(b): Json<Approve>) -> Json<Value> {
    Json(a.approve_task_tool(b.task, &b.tool_name, &b.tool_input).await)
}

#[derive(Deserialize)]
struct Answer {
    id: u64,
    #[serde(default)]
    approval: Option<u64>,
    option: String,
}

/// The dashboard answering a task's pending approval.
async fn tasks_answer(State(a): State<AppState>, Json(b): Json<Answer>) -> Json<Value> {
    Json(json!({"answered": a.tasks.answer(b.id, b.approval, &b.option)}))
}

async fn mic(State(a): State<AppState>, Json(b): Json<SpeakerBody>) -> ApiResult {
    let on = b.on.unwrap_or(!a.mic_on());
    Ok(Json(json!({"on": a.set_mic(on).await?})))
}

async fn cable_open() -> ApiResult {
    Ok(Json(json!({"message": crate::cable::open_download_page()?})))
}
