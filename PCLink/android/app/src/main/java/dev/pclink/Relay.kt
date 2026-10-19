package dev.pclink

import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URL
import kotlin.concurrent.thread

/**
 * Reaching the PC from anywhere through the PCLink relay (relay/relay.php,
 * plain HTTPS so it runs on ordinary web hosting). The PC waits in a secret
 * room; the phone opens a session there and both sides turn it into a byte
 * pipe: POST to send, long-polled GET to receive. The usual TLS link to the
 * PC (pinned to its certificate) runs through it, so the relay only sees
 * ciphertext. To reuse [Link.open] unchanged, the pipe is exposed as a
 * one-shot local port on 127.0.0.1.
 */
object Relay {
    const val LOCAL_HOST = "127.0.0.1"
    private const val SEND_MAX = 512 * 1024

    private class Reply(val status: Int, val body: ByteArray, val next: Long?)

    private fun call(url: String, method: String, body: ByteArray? = null, readTimeout: Int = 45_000): Reply {
        val c = URL(url).openConnection() as HttpURLConnection
        try {
            c.requestMethod = method
            c.connectTimeout = 15_000
            c.readTimeout = readTimeout
            c.useCaches = false
            c.setRequestProperty("User-Agent", "PCLink")
            if (method == "POST") {
                val data = body ?: ByteArray(0)
                c.doOutput = true
                c.setFixedLengthStreamingMode(data.size)
                c.setRequestProperty("Content-Type", "application/octet-stream")
                c.outputStream.use { it.write(data) }
            }
            val status = c.responseCode
            val stream = if (status >= 400) c.errorStream else c.inputStream
            val bytes = stream?.use { it.readBytes() } ?: ByteArray(0)
            return Reply(status, bytes, c.getHeaderField("X-Next")?.toLongOrNull())
        } finally {
            c.disconnect()
        }
    }

    private fun errorOf(r: Reply): String =
        try {
            JSONObject(String(r.body)).optString("error", "relay error ${r.status}")
        } catch (_: Exception) {
            "relay error ${r.status}"
        }

    /** Opens a session to the PC and returns the local port that leads to it. */
    fun open(base: String, room: String): Int {
        if (!base.startsWith("https://") && !base.startsWith("http://")) throw IOException("Unsupported relay address")
        val r = call("$base?a=open&room=$room", "POST", readTimeout = 30_000)
        if (r.status != 200) throw IOException(errorOf(r))
        val session = JSONObject(String(r.body)).getString("session")
        if (!session.matches(Regex("^[0-9a-f]{32}$"))) throw IOException("Bad relay session")

        val server = ServerSocket(0, 1, InetAddress.getByName(LOCAL_HOST))
        server.soTimeout = 15_000
        thread(name = "pclink-relay") {
            val local = try {
                server.accept()
            } catch (e: IOException) {
                call("$base?a=close&s=$session", "POST")
                return@thread
            } finally {
                server.close()
            }
            local.tcpNoDelay = true
            pipe(base, session, local)
        }
        return server.localPort
    }

    private fun pipe(base: String, session: String, local: Socket) {
        val closeLocal = {
            try {
                local.close()
            } catch (_: IOException) {
            }
        }
        // Local -> relay. While one request is in flight, more bytes collect
        // in the socket buffer and go out together in the next one.
        thread(name = "pclink-relay-up") {
            val buf = ByteArray(SEND_MAX)
            val src = local.getInputStream()
            try {
                while (true) {
                    var n = src.read(buf)
                    if (n < 0) break
                    while (n < buf.size && src.available() > 0) {
                        val m = src.read(buf, n, minOf(src.available(), buf.size - n))
                        if (m <= 0) break
                        n += m
                    }
                    val chunk = buf.copyOf(n)
                    while (true) {
                        val r = call("$base?a=send&s=$session&d=up", "POST", chunk)
                        if (r.status == 200) break
                        // The PC is behind; nothing was stored, so try again.
                        if (r.status == 429) Thread.sleep(250) else throw IOException(errorOf(r))
                    }
                }
            } catch (_: Exception) {
            }
            try {
                call("$base?a=close&s=$session", "POST")
            } catch (_: Exception) {
            }
            closeLocal()
        }
        // Relay -> local.
        val dst = local.getOutputStream()
        var next = 0L
        var failures = 0
        try {
            while (!local.isClosed) {
                val r = try {
                    call("$base?a=recv&s=$session&d=down&from=$next", "GET")
                } catch (e: IOException) {
                    // Receiving is idempotent: ride out a flaky mobile network.
                    if (++failures > 3) throw e
                    Thread.sleep(1000)
                    continue
                }
                failures = 0
                when (r.status) {
                    200 -> {
                        dst.write(r.body)
                        dst.flush()
                        next = r.next ?: next
                    }
                    204 -> next = r.next ?: next
                    else -> break
                }
            }
        } catch (_: Exception) {
        }
        closeLocal()
    }
}
