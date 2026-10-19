package dev.pclink

import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.util.Base64
import java.util.concurrent.ThreadLocalRandom
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLSocket
import javax.net.ssl.SSLSocketFactory
import kotlin.concurrent.thread

/**
 * Reaching the PC from anywhere: a WebSocket to the PCLink relay
 * (relay/server.js), joined to the PC's secret room. The relay only pipes
 * bytes; the usual TLS link to the PC (pinned to its certificate) runs
 * through it. To reuse [Link.open] unchanged, the WebSocket is exposed as a
 * one-shot local port on 127.0.0.1.
 */
object Relay {
    const val LOCAL_HOST = "127.0.0.1"

    /** Connects to the relay and returns the local port that leads to the PC. */
    fun open(base: String, room: String): Int {
        val uri = URI(base.trimEnd('/') + "/" + room + "?role=phone")
        val tls = when (uri.scheme) {
            "wss" -> true
            "ws" -> false
            else -> throw IOException("Relay address must start with wss://")
        }
        val host = uri.host ?: throw IOException("Bad relay address")
        val port = if (uri.port > 0) uri.port else if (tls) 443 else 80
        val raw = Socket()
        val sock: Socket
        try {
            raw.tcpNoDelay = true
            raw.connect(InetSocketAddress(host, port), 8000)
            sock = if (tls) {
                val ssl = (SSLSocketFactory.getDefault() as SSLSocketFactory).createSocket(raw, host, port, true) as SSLSocket
                ssl.soTimeout = 15_000
                ssl.startHandshake()
                if (!HttpsURLConnection.getDefaultHostnameVerifier().verify(host, ssl.session)) {
                    ssl.close()
                    throw IOException("Relay certificate doesn't match $host")
                }
                ssl
            } else {
                raw.soTimeout = 15_000
                raw
            }
        } catch (e: Exception) {
            try {
                raw.close()
            } catch (_: IOException) {
            }
            throw e
        }

        val out = BufferedOutputStream(sock.getOutputStream(), 64 * 1024)
        val input = DataInputStream(BufferedInputStream(sock.getInputStream(), 64 * 1024))
        try {
            val key = Base64.getEncoder().encodeToString(ByteArray(16).also { ThreadLocalRandom.current().nextBytes(it) })
            val path = uri.rawPath.ifEmpty { "/" } + (uri.rawQuery?.let { "?$it" } ?: "")
            val hostHeader = if (uri.port > 0) "$host:${uri.port}" else host
            out.write(
                ("GET $path HTTP/1.1\r\nHost: $hostHeader\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                    "Sec-WebSocket-Key: $key\r\nSec-WebSocket-Version: 13\r\nUser-Agent: PCLink\r\n\r\n").toByteArray(),
            )
            out.flush()
            val head = StringBuilder()
            while (!head.endsWith("\r\n\r\n")) {
                if (head.length > 16_384) throw IOException("Relay sent an oversized response")
                val b = input.read()
                if (b < 0) throw IOException("Relay closed the connection")
                head.append(b.toChar())
            }
            val status = head.lineSequence().first()
            if (!status.contains(" 101")) throw IOException("Relay refused: ${status.trim()}")
            // The tunnel has its own keepalive (the link pings every 20 s,
            // the relay every 25 s), so no read timeout from here on.
            sock.soTimeout = 0
        } catch (e: Exception) {
            sock.close()
            throw e
        }

        val server = ServerSocket(0, 1, InetAddress.getByName(LOCAL_HOST))
        server.soTimeout = 15_000
        thread(name = "pclink-relay") {
            val local = try {
                server.accept()
            } catch (e: IOException) {
                sock.close()
                return@thread
            } finally {
                server.close()
            }
            local.tcpNoDelay = true
            pipe(local, sock, input, out)
        }
        return server.localPort
    }

    private fun pipe(local: Socket, sock: Socket, wsIn: DataInputStream, wsOut: OutputStream) {
        val closeAll = {
            try {
                local.close()
            } catch (_: IOException) {
            }
            try {
                sock.close()
            } catch (_: IOException) {
            }
        }
        // Local -> relay.
        thread(name = "pclink-relay-up") {
            val buf = ByteArray(64 * 1024)
            val src: InputStream = local.getInputStream()
            try {
                while (true) {
                    val n = src.read(buf)
                    if (n < 0) break
                    writeFrame(wsOut, 0x2, buf, n)
                }
                writeFrame(wsOut, 0x8, byteArrayOf(0x03, 0xE8.toByte()), 2)
            } catch (_: IOException) {
            }
            closeAll()
        }
        // Relay -> local.
        val dst = local.getOutputStream()
        try {
            while (true) {
                val b0 = wsIn.readUnsignedByte()
                val b1 = wsIn.readUnsignedByte()
                val op = b0 and 0x0F
                var len = (b1 and 0x7F).toLong()
                if (len == 126L) len = wsIn.readUnsignedShort().toLong() else if (len == 127L) len = wsIn.readLong()
                if (len < 0 || len > 4L * 1024 * 1024) throw IOException("relay frame too big")
                val mask = if (b1 and 0x80 != 0) ByteArray(4).also { wsIn.readFully(it) } else null
                val payload = ByteArray(len.toInt())
                wsIn.readFully(payload)
                if (mask != null) for (i in payload.indices) payload[i] = (payload[i].toInt() xor mask[i and 3].toInt()).toByte()
                when (op) {
                    0x0, 0x1, 0x2 -> {
                        dst.write(payload)
                        dst.flush()
                    }
                    0x8 -> break
                    0x9 -> writeFrame(wsOut, 0xA, payload, payload.size)
                }
            }
        } catch (_: IOException) {
        }
        closeAll()
    }

    /** Client frames must be masked. */
    private fun writeFrame(out: OutputStream, op: Int, data: ByteArray, n: Int) {
        val head = ByteArray(14)
        var h = 0
        head[h++] = (0x80 or op).toByte()
        when {
            n < 126 -> head[h++] = (0x80 or n).toByte()
            n < 65536 -> {
                head[h++] = (0x80 or 126).toByte()
                head[h++] = (n shr 8).toByte()
                head[h++] = n.toByte()
            }
            else -> {
                head[h++] = (0x80 or 127).toByte()
                for (i in 7 downTo 0) head[h++] = (n.toLong() shr (8 * i)).toByte()
            }
        }
        val mask = ByteArray(4).also { ThreadLocalRandom.current().nextBytes(it) }
        System.arraycopy(mask, 0, head, h, 4)
        h += 4
        val body = ByteArray(n)
        for (i in 0 until n) body[i] = (data[i].toInt() xor mask[i and 3].toInt()).toByte()
        synchronized(out) {
            out.write(head, 0, h)
            out.write(body)
            out.flush()
        }
    }
}
