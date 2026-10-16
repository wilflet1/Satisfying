//! Task plumbing with a stand-in `claude` CLI: arguments, prompt on stdin,
//! stream-json parsing into the feed, follow-ups and stopping.
#![cfg(unix)]

use pclink::config::Config;
use std::os::unix::fs::PermissionsExt;
use std::time::Duration;

const FAKE_CLAUDE: &str = r#"#!/bin/sh
dir="$(dirname "$0")"
echo "$@" >> "$dir/args.log"
cat >> "$dir/prompts.log"
echo >> "$dir/prompts.log"
echo '{"type":"system","subtype":"init","session_id":"s1","model":"fake-opus"}'
echo '{"type":"assistant","parent_tool_use_id":null,"message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"echo hi"}}]}}'
echo '{"type":"assistant","parent_tool_use_id":"sub","message":{"content":[{"type":"text","text":"subagent noise"}]}}'
case "$(tail -n 2 "$dir/prompts.log")" in *slow*) sleep 30 ;; esac
echo '{"type":"assistant","parent_tool_use_id":null,"message":{"content":[{"type":"text","text":"All done."}]}}'
echo '{"type":"result","subtype":"success","is_error":false,"result":"All done.","total_cost_usd":0.01,"session_id":"s1"}'
"#;

async fn wait_status(agent: &pclink::agent::Agent, id: u64, want: &str) -> serde_json::Value {
    for _ in 0..100 {
        let v = agent.tasks.get(id, 0).unwrap();
        if v["task"]["status"] == want {
            return v;
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    panic!("task {id} never reached {want}: {}", agent.tasks.get(id, 0).unwrap());
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn tasks_run_follow_up_and_stop() {
    let home = tempfile::tempdir().unwrap();
    std::env::set_var("PCLINK_HOME", home.path());
    let cli = home.path().join("claude");
    std::fs::write(&cli, FAKE_CLAUDE).unwrap();
    std::fs::set_permissions(&cli, std::fs::Permissions::from_mode(0o755)).unwrap();

    let mut cfg = Config { port: 0, api_port: 0, clipboard_sync: false, ..Config::default() };
    cfg.claude.cli = Some(cli.clone());
    cfg.claude.task_dir = Some(home.path().to_path_buf());
    let running = pclink::run::start_with(cfg, false).await.unwrap();
    let agent = running.agent.clone();

    // A task runs, the prompt goes in on stdin, the stream becomes a feed.
    let id = agent.start_task("Move my browser to the main screen", None, true).unwrap();
    let v = wait_status(&agent, id, "done").await;
    let events: Vec<(String, String)> = v["events"]
        .as_array()
        .unwrap()
        .iter()
        .map(|e| (e["kind"].as_str().unwrap().to_string(), e["text"].as_str().unwrap().to_string()))
        .collect();
    assert!(events.contains(&("tool".into(), "Bash: echo hi".into())), "{events:?}");
    assert!(events.contains(&("text".into(), "All done.".into())));
    assert!(!events.iter().any(|(_, t)| t.contains("subagent noise")));
    // The final result repeats the last message, so it isn't shown twice.
    assert_eq!(events.iter().filter(|(_, t)| t == "All done.").count(), 1);
    assert_eq!(v["task"]["session_id"], "s1");
    let prompts = std::fs::read_to_string(home.path().join("prompts.log")).unwrap();
    assert!(prompts.contains("Move my browser to the main screen"));
    let args = std::fs::read_to_string(home.path().join("args.log")).unwrap();
    for want in ["-p", "--output-format stream-json", "--model opus", "--mcp-config", "--settings", "--permission-prompts none", "--allowedTools mcp__pclink"] {
        assert!(args.contains(want), "missing {want} in {args}");
    }

    // A follow-up resumes the same Claude session.
    let id2 = agent.start_task("Now make it fullscreen", Some(id), true).unwrap();
    wait_status(&agent, id2, "done").await;
    let args = std::fs::read_to_string(home.path().join("args.log")).unwrap();
    assert!(args.lines().last().unwrap().contains("--resume s1"));

    // Stopping a running task.
    let id3 = agent.start_task("slow task", None, false).unwrap();
    tokio::time::sleep(Duration::from_millis(500)).await;
    assert!(agent.tasks.stop(id3));
    wait_status(&agent, id3, "stopped").await;

    // Empty prompts and unknown follow-ups are refused.
    assert!(agent.start_task("  ", None, true).is_err());
    assert!(agent.start_task("x", Some(999), true).is_err());
    assert_eq!(agent.tasks.summaries().len(), 3);
}
