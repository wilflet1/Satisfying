package dev.pclink

import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.InputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

class RemoteException(message: String) : Exception(message)

/** Accepts exactly one server certificate, identified by SHA-256 fingerprint. */
class PinningTrustManager(private val expected: String?) : X509TrustManager {
    @Volatile var seen: String? = null

    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val fp = sha256Hex(chain[0].encoded)
        seen = fp
        if (expected != null && !MessageDigest.isEqual(fp.toByteArray(), expected.toByteArray())) {
            throw CertificateException("The PC's certificate does not match the paired PC")
        }
    }

    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) =
        throw CertificateException("not a server")

    override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
}

fun sha256Hex(data: ByteArray): String =
    MessageDigest.getInstance("SHA-256").digest(data).joinToString("") { "%02x".format(it) }

/**
 * An authenticated TLS connection to the PC agent: requests and responses
 * in both directions, events, and file transfers.
 */
class Link private constructor(
    private val socket: SSLSocket,
    private val input: DataInputStream,
    val welcome: JSONObject,
    val host: String,
) {
    interface Handler {
        /** Handles a request from the PC; return the response data or throw. */
        fun onRequest(link: Link, op: String, args: JSONObject): Any?
        fun onEvent(link: Link, ev: String, data: JSONObject)
        fun onClosed(link: Link, error: Throwable?)
    }

    private class Incoming(
        val part: File,
        val out: FileOutputStream,
        var finalFile: File?,
        val expected: Long?,
        val done: CompletableFuture<File>,
    ) {
        var received = 0L
    }

    private val out = BufferedOutputStream(socket.outputStream, 128 * 1024)
    private val pending = ConcurrentHashMap<Long, CompletableFuture<JSONObject>>()
    private val incoming = ConcurrentHashMap<Int, Incoming>()
    private val finished = ConcurrentHashMap<Int, CompletableFuture<File>>()
    private val nextId = AtomicLong(1)
    /** The phone allocates even transfer ids, the PC odd ones. */
    private val nextXfer = AtomicInteger(2)
    private val closed = AtomicBoolean(false)
    private val workers = Executors.newCachedThreadPool()
    /** Events go out in order on their own thread: callers are often on the main thread. */
    private val events = Executors.newSingleThreadExecutor()
    private var handler: Handler? = null

    val pcName: String get() = welcome.optString("pc_name", "PC")
    val isOpen: Boolean get() = !closed.get()

    fun start(h: Handler) {
        handler = h
        socket.soTimeout = 70_000
        Thread({ readLoop() }, "pclink-read").start()
        Thread({
            while (isOpen) {
                try {
                    Thread.sleep(20_000)
                    sendJson(jsonOf("t" to "ping"))
                } catch (e: Exception) {
                    break
                }
            }
        }, "pclink-ping").start()
    }

    fun send(bytes: ByteArray) {
        if (closed.get()) throw IOException("not connected")
        synchronized(out) {
            out.write(bytes)
            out.flush()
        }
    }

    fun sendJson(msg: JSONObject) = send(Wire.json(msg))

    /** Sends an event without blocking; safe to call from any thread. */
    fun event(ev: String, data: JSONObject) {
        if (closed.get()) return
        try {
            events.execute {
                try {
                    sendJson(jsonOf("t" to "ev", "ev" to ev, "data" to data))
                } catch (_: Exception) {
                }
            }
        } catch (_: java.util.concurrent.RejectedExecutionException) {
        }
    }

    /** Sends a request to the PC and blocks for the answer. */
    fun request(op: String, args: JSONObject = JSONObject(), timeoutMs: Long = 30_000): JSONObject {
        val id = nextId.getAndIncrement()
        val f = CompletableFuture<JSONObject>()
        pending[id] = f
        try {
            sendJson(jsonOf("t" to "req", "id" to id, "op" to op, "args" to args))
            val res = f.get(timeoutMs, TimeUnit.MILLISECONDS)
            if (!res.optBoolean("ok")) throw RemoteException(res.optString("error", "request failed"))
            return res.optJSONObject("data") ?: JSONObject().put("value", res.opt("data"))
        } catch (e: TimeoutException) {
            throw IOException("PC did not answer in time")
        } catch (e: java.util.concurrent.ExecutionException) {
            throw IOException(e.cause?.message ?: "disconnected")
        } finally {
            pending.remove(id)
        }
    }

    /**
     * Sends a request without waiting for the answer (touchpad and keys).
     * Requests reach the PC in the order they're posted.
     */
    fun post(op: String, args: JSONObject) {
        try {
            sendJson(jsonOf("t" to "req", "id" to nextId.getAndIncrement(), "op" to op, "args" to args))
        } catch (_: IOException) {
        }
    }

    /** Like [request] but for responses whose data is a JSON array. */
    fun requestArray(op: String, args: JSONObject = JSONObject()): org.json.JSONArray =
        request(op, args).optJSONArray("value") ?: org.json.JSONArray()

    fun close(error: Throwable? = null) {
        if (!closed.compareAndSet(false, true)) return
        events.shutdown()
        try {
            socket.close()
        } catch (_: IOException) {
        }
        for (f in pending.values) f.completeExceptionally(IOException("disconnected"))
        pending.clear()
        for ((_, inc) in incoming) {
            try {
                inc.out.close()
            } catch (_: IOException) {
            }
            inc.part.delete()
            inc.done.completeExceptionally(IOException("disconnected"))
        }
        incoming.clear()
        workers.shutdownNow()
        handler?.onClosed(this, error)
    }

    private fun readLoop() {
        var error: Throwable? = null
        try {
            while (true) {
                val frame = Wire.read(input) ?: break
                when (frame) {
                    is Wire.Frame.Data -> onData(frame)
                    is Wire.Frame.Json -> dispatch(frame.msg)
                }
            }
        } catch (e: Throwable) {
            error = e
        }
        close(error)
    }

    private fun dispatch(msg: JSONObject) {
        when (msg.optString("t")) {
            "res" -> pending.remove(msg.optLong("id"))?.complete(msg)
            "req" -> {
                val id = msg.optLong("id")
                val op = msg.optString("op")
                val args = msg.optJSONObject("args") ?: JSONObject()
                workers.execute {
                    val reply = try {
                        val data = handler?.onRequest(this, op, args)
                        jsonOf("t" to "res", "id" to id, "ok" to true, "data" to (data ?: JSONObject()))
                    } catch (e: Exception) {
                        jsonOf("t" to "res", "id" to id, "ok" to false, "error" to (e.message ?: e.toString()))
                    }
                    try {
                        sendJson(reply)
                    } catch (_: IOException) {
                    }
                }
            }
            "ev" -> {
                val ev = msg.optString("ev")
                val data = msg.optJSONObject("data") ?: JSONObject()
                workers.execute { handler?.onEvent(this, ev, data) }
            }
            "xfer_end" -> onXferEnd(msg)
            "ping" -> try {
                sendJson(jsonOf("t" to "pong"))
            } catch (_: IOException) {
            }
        }
    }

    // ---------------------------------------------------------------- files

    fun allocXfer(): Int = nextXfer.getAndAdd(2)

    /** Prepares to receive transfer [xfer] into [dir]. */
    fun expectIncoming(xfer: Int, dir: File, name: String?, expected: Long?): CompletableFuture<File> {
        dir.mkdirs()
        val part = File(dir, ".pclink-$xfer.part")
        val f = CompletableFuture<File>()
        incoming[xfer] = Incoming(
            part,
            FileOutputStream(part),
            name?.let { uniqueFile(dir, sanitizeName(it)) },
            expected,
            f,
        )
        finished[xfer] = f
        return f
    }

    private fun onData(d: Wire.Frame.Data) {
        val inc = incoming[d.xfer] ?: return
        inc.received += d.len
        if (inc.expected != null && inc.received > inc.expected) {
            incoming.remove(d.xfer)
            inc.out.close()
            inc.part.delete()
            inc.done.completeExceptionally(IOException("transfer larger than announced"))
            return
        }
        inc.out.write(d.buf, 0, d.len)
    }

    private fun onXferEnd(msg: JSONObject) {
        val xfer = msg.optInt("xfer")
        val inc = incoming.remove(xfer) ?: return
        try {
            inc.out.close()
            if (!msg.optBoolean("ok")) throw IOException(msg.optString("error", "sender failed"))
            if (inc.expected != null && inc.received != inc.expected) {
                throw IOException("expected ${inc.expected} bytes, got ${inc.received}")
            }
            val dest = inc.finalFile
                ?: uniqueFile(inc.part.parentFile!!, sanitizeName(msg.str("name") ?: "file"))
            if (!inc.part.renameTo(dest)) throw IOException("could not save ${dest.name}")
            inc.done.complete(dest)
        } catch (e: Exception) {
            inc.part.delete()
            inc.done.completeExceptionally(e)
        }
    }

    /** Result of an incoming transfer the PC asks about with `xfer.status`. */
    fun waitIncoming(xfer: Int, timeoutMs: Long): File {
        val f = finished.remove(xfer) ?: throw IOException("unknown transfer $xfer")
        try {
            return f.get(timeoutMs, TimeUnit.MILLISECONDS)
        } catch (e: java.util.concurrent.ExecutionException) {
            throw IOException(e.cause?.message ?: "transfer failed")
        }
    }

    /** Streams [input] to the PC as transfer [xfer]; always sends xfer_end. */
    fun sendStream(xfer: Int, input: InputStream, name: String, progress: ((Long) -> Unit)? = null): Long {
        var sent = 0L
        try {
            input.use { s ->
                val buf = ByteArray(Wire.CHUNK)
                while (true) {
                    val n = s.read(buf)
                    if (n < 0) break
                    if (n == 0) continue
                    send(Wire.data(xfer, buf, 0, n))
                    sent += n
                    progress?.invoke(sent)
                }
            }
            sendJson(jsonOf("t" to "xfer_end", "xfer" to xfer, "ok" to true, "size" to sent, "name" to name))
            return sent
        } catch (e: Exception) {
            try {
                sendJson(jsonOf("t" to "xfer_end", "xfer" to xfer, "ok" to false, "error" to (e.message ?: "failed")))
            } catch (_: IOException) {
            }
            throw e
        }
    }

    /** Downloads a file from the PC into [destDir]. */
    fun pull(pcPath: String, destDir: File, progress: ((Long, Long) -> Unit)? = null): File {
        val xfer = allocXfer()
        val done = expectIncoming(xfer, destDir, null, null)
        val meta = try {
            request("fs.read", jsonOf("path" to pcPath, "offset" to 0, "xfer" to xfer))
        } catch (e: Exception) {
            incoming.remove(xfer)?.let { it.out.close(); it.part.delete() }
            finished.remove(xfer)
            throw e
        }
        val total = meta.optLong("size", -1)
        if (progress != null) {
            while (!done.isDone) {
                progress(incoming[xfer]?.received ?: total, total)
                Thread.sleep(250)
            }
        }
        finished.remove(xfer)
        val file = try {
            done.get(6, TimeUnit.HOURS)
        } catch (e: java.util.concurrent.ExecutionException) {
            throw IOException(e.cause?.message ?: "download failed")
        }
        val want = meta.str("name")?.let { sanitizeName(it) }
        if (want != null && file.name != want) {
            val dest = uniqueFile(destDir, want)
            if (file.renameTo(dest)) return dest
        }
        return file
    }

    /** Uploads to the PC (into [pcDir], or the PC's inbox). Returns the PC path. */
    fun push(
        input: InputStream,
        name: String,
        size: Long?,
        pcDir: String? = null,
        kind: String = "file",
        progress: ((Long) -> Unit)? = null,
    ): String {
        val res = request("fs.write", jsonOf("dir" to pcDir, "name" to name, "size" to size, "kind" to kind))
        val xfer = res.getInt("xfer")
        sendStream(xfer, input, name, progress)
        return request("xfer.status", jsonOf("xfer" to xfer), 120_000).optString("path")
    }

    companion object {
        /**
         * Connects and sends the first message (`hello` or `pair`). Returns the
         * link, the PC's reply and the certificate fingerprint it presented.
         */
        fun open(host: String, port: Int, fingerprint: String?, first: JSONObject): Triple<Link, JSONObject, String> {
            val tm = PinningTrustManager(fingerprint?.takeIf { it.isNotEmpty() })
            val ctx = SSLContext.getInstance("TLS")
            ctx.init(null, arrayOf(tm), null)
            val raw = Socket()
            try {
                raw.tcpNoDelay = true
                raw.connect(InetSocketAddress(host, port), 4000)
                val ssl = ctx.socketFactory.createSocket(raw, host, port, true) as SSLSocket
                ssl.soTimeout = 15_000
                ssl.startHandshake()
                val input = DataInputStream(BufferedInputStream(ssl.inputStream, 128 * 1024))
                synchronized(ssl) {
                    val o = ssl.outputStream
                    o.write(Wire.json(first))
                    o.flush()
                }
                val reply = (Wire.read(input) as? Wire.Frame.Json)?.msg
                    ?: throw IOException("PC closed the connection")
                if (reply.optString("t") == "error") {
                    ssl.close()
                    throw RemoteException(reply.optString("error"))
                }
                return Triple(Link(ssl, input, reply, host), reply, tm.seen ?: "")
            } catch (e: Exception) {
                try {
                    raw.close()
                } catch (_: IOException) {
                }
                throw e
            }
        }

        fun sanitizeName(name: String): String {
            val base = name.substringAfterLast('/').substringAfterLast('\\')
            val cleaned = base.map { c -> if (c in "<>:\"|?*" || c.isISOControl()) '_' else c }
                .joinToString("").trim().trim('.')
            return cleaned.ifEmpty { "file" }
        }

        fun uniqueFile(dir: File, name: String): File {
            var f = File(dir, name)
            if (!f.exists()) return f
            val dot = name.lastIndexOf('.')
            val stem = if (dot > 0) name.substring(0, dot) else name
            val ext = if (dot > 0) name.substring(dot) else ""
            var i = 2
            while (f.exists()) {
                f = File(dir, "$stem ($i)$ext")
                i++
            }
            return f
        }
    }
}
