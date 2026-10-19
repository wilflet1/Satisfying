<?php
// PCLink relay over plain HTTPS, for shared hosting without WebSockets.
//
// The phone and PC each turn their usual connection into a byte pipe made of
// HTTP requests: data goes up with POST `send`, comes down with long-polled
// GET `recv`. The relay only stores and forwards those bytes; the phone's TLS
// session (pinned to the PC's certificate) runs end to end through it, so
// nothing here can read or change the traffic.
//
//   PC:    POST ?a=wait&room=R        long-poll; {"session": S} when a phone opens one
//   Phone: POST ?a=open&room=R        {"session": S} once the PC has picked it up
//   Both:  POST ?a=send&s=S&d=up|down body = bytes   (phone writes up, PC writes down)
//          GET  ?a=recv&s=S&d=..&from=N              bytes of chunks N.., header X-Next
//          POST ?a=close&s=S
//   GET ?a=ping (or no parameters): health check

declare(strict_types=1);

const POLL_SECS = 20;        // long-poll length (stays under proxy timeouts)
const OPEN_WAIT_SECS = 15;   // how long a phone waits for the PC to pick up
const PC_SEEN_SECS = 60;     // a PC that hasn't polled for this long is offline
const IDLE_SECS = 75;        // a side that stops polling for this long is gone
const MAX_RECV = 1048576;    // bytes returned per recv
const MAX_SEND = 2097152;    // bytes accepted per send
const MAX_QUEUED = 400;      // chunks waiting in one direction

$DATA = dirname(__DIR__) . '/agentrelay-data';
set_time_limit(POLL_SECS + 20);
header('Cache-Control: no-store');
header('X-Accel-Buffering: no');

function done(int $code, string $body = '', string $type = 'text/plain'): void {
    http_response_code($code);
    header("Content-Type: $type");
    header('Content-Length: ' . strlen($body));
    echo $body;
    exit;
}
function json_done(int $code, array $o): void { done($code, json_encode($o), 'application/json'); }
function param(string $k, string $re): string {
    $v = $_GET[$k] ?? '';
    if (!preg_match($re, $v)) json_done(400, ['error' => "bad $k"]);
    return $v;
}
function ensure_dir(string $d): void { if (!is_dir($d)) @mkdir($d, 0700, true); }
function rmtree(string $d): void {
    if (!is_dir($d)) return;
    foreach (scandir($d) as $f) {
        if ($f === '.' || $f === '..') continue;
        $p = "$d/$f";
        is_dir($p) ? rmtree($p) : @unlink($p);
    }
    @rmdir($d);
}
function age(string $f): float { clearstatcache(true, $f); return is_file($f) ? microtime(true) - filemtime($f) : INF; }
function nap(): void { usleep(40000); }

function cleanup(string $DATA): void {
    foreach (glob("$DATA/sessions/*", GLOB_ONLYDIR) ?: [] as $s) {
        if (age("$s/last-up") > 600 && age("$s/last-down") > 600 && age("$s/created") > 600) rmtree($s);
    }
    foreach (glob("$DATA/rooms/*/*") ?: [] as $p) {
        if (age($p) > 120) @unlink($p);
    }
}

ensure_dir("$DATA/sessions");
ensure_dir("$DATA/rooms");
if (!is_file("$DATA/.htaccess")) @file_put_contents("$DATA/.htaccess", "Require all denied\n");
if (mt_rand(1, 40) === 1) cleanup($DATA);

$a = $_GET['a'] ?? 'ping';
$ROOM_RE = '/^[0-9a-f]{64}$/';
$SESS_RE = '/^[0-9a-f]{32}$/';

switch ($a) {
case 'ping':
    done(200, "PCLink relay OK\n");

case 'wait': {
    $room = param('room', $ROOM_RE);
    $rd = "$DATA/rooms/$room";
    ensure_dir($rd);
    $end = microtime(true) + POLL_SECS;
    do {
        touch("$rd/.pc");
        foreach (scandir($rd) as $f) {
            if (!preg_match($SESS_RE, $f)) continue;
            // rename is atomic: only one waiting PC gets each phone.
            if (@rename("$rd/$f", "$rd/.claimed-$f")) {
                @unlink("$rd/.claimed-$f");
                touch("$DATA/sessions/$f/claimed");
                touch("$DATA/sessions/$f/last-down");
                json_done(200, ['session' => $f]);
            }
        }
        nap();
    } while (microtime(true) < $end && !connection_aborted());
    done(204);
}

case 'open': {
    $room = param('room', $ROOM_RE);
    $rd = "$DATA/rooms/$room";
    if (age("$rd/.pc") > PC_SEEN_SECS) json_done(404, ['error' => 'PC is not connected to the relay']);
    $s = bin2hex(random_bytes(16));
    $sd = "$DATA/sessions/$s";
    ensure_dir("$sd/up");
    ensure_dir("$sd/down");
    touch("$sd/created");
    touch("$sd/last-up");
    touch("$rd/$s");
    $end = microtime(true) + OPEN_WAIT_SECS;
    while (microtime(true) < $end) {
        clearstatcache(true, "$sd/claimed");
        if (is_file("$sd/claimed")) json_done(200, ['session' => $s]);
        nap();
    }
    @unlink("$rd/$s");
    rmtree($sd);
    json_done(504, ['error' => 'PC did not answer through the relay']);
}

case 'send': {
    $s = param('s', $SESS_RE);
    $d = param('d', '/^(up|down)$/');
    $sd = "$DATA/sessions/$s";
    if (!is_dir($sd) || is_file("$sd/closed")) json_done(410, ['error' => 'session closed']);
    $body = file_get_contents('php://input');
    if ($body === false || strlen($body) > MAX_SEND) json_done(413, ['error' => 'too big']);
    touch("$sd/last-$d");
    if ($body === '') json_done(200, ['ok' => true]);
    $lock = fopen("$sd/$d.lock", 'c+');
    flock($lock, LOCK_EX);
    $next = (int)@file_get_contents("$sd/$d.next");
    $oldest = (int)@file_get_contents("$sd/$d.acked");
    if ($next - $oldest > MAX_QUEUED) { flock($lock, LOCK_UN); json_done(429, ['error' => 'reader is behind']); }
    file_put_contents("$sd/$d/$next.tmp", $body);
    rename("$sd/$d/$next.tmp", "$sd/$d/$next");
    file_put_contents("$sd/$d.next", (string)($next + 1));
    flock($lock, LOCK_UN);
    json_done(200, ['ok' => true]);
}

case 'recv': {
    $s = param('s', $SESS_RE);
    $d = param('d', '/^(up|down)$/');
    $from = (int)param('from', '/^\d{1,12}$/');
    $sd = "$DATA/sessions/$s";
    if (!is_dir($sd)) done(410);
    // The reader of `up` is the PC, of `down` the phone.
    $me = $d === 'up' ? 'down' : 'up';
    $peer = $d;
    // Everything before `from` has been delivered.
    $acked = (int)@file_get_contents("$sd/$d.acked");
    for ($i = $acked; $i < $from; $i++) @unlink("$sd/$d/$i");
    if ($from > $acked) file_put_contents("$sd/$d.acked", (string)$from);
    $end = microtime(true) + POLL_SECS;
    do {
        touch("$sd/last-$me");
        clearstatcache();
        if (is_file("$sd/$d/$from")) {
            $out = '';
            $n = $from;
            while (strlen($out) < MAX_RECV && is_file("$sd/$d/$n")) {
                $out .= file_get_contents("$sd/$d/$n");
                $n++;
            }
            header("X-Next: $n");
            done(200, $out, 'application/octet-stream');
        }
        if (is_file("$sd/closed") || age("$sd/last-$peer") > IDLE_SECS) {
            touch("$sd/closed");
            done(410);
        }
        nap();
    } while (microtime(true) < $end && !connection_aborted());
    header("X-Next: $from");
    done(204);
}

case 'close': {
    $s = param('s', $SESS_RE);
    $sd = "$DATA/sessions/$s";
    if (is_dir($sd)) touch("$sd/closed");
    json_done(200, ['ok' => true]);
}

default:
    json_done(400, ['error' => 'unknown action']);
}
