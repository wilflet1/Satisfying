package dev.pclink

import org.json.JSONObject
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException

/**
 * Frame format shared with the PC agent (see docs/PROTOCOL.md):
 * u32 big-endian length | u8 kind | payload.
 * kind 0 = JSON object, kind 1 = file data (u32 transfer id | bytes).
 */
object Wire {
    const val KIND_JSON = 0
    const val KIND_DATA = 1
    const val MAX_FRAME = 4 * 1024 * 1024
    const val CHUNK = 64 * 1024

    sealed class Frame {
        class Json(val msg: JSONObject) : Frame()
        class Data(val xfer: Int, val buf: ByteArray, val len: Int) : Frame()
    }

    /** Reads one frame; null on clean end of stream. */
    fun read(input: DataInputStream): Frame? {
        val len = try {
            input.readInt()
        } catch (e: EOFException) {
            return null
        }
        if (len <= 0 || len > MAX_FRAME) throw IOException("bad frame length $len")
        val kind = input.readUnsignedByte()
        return when (kind) {
            KIND_JSON -> {
                val body = ByteArray(len - 1)
                input.readFully(body)
                Frame.Json(JSONObject(String(body, Charsets.UTF_8)))
            }
            KIND_DATA -> {
                if (len < 5) throw IOException("short data frame")
                val xfer = input.readInt()
                val body = ByteArray(len - 5)
                input.readFully(body)
                Frame.Data(xfer, body, body.size)
            }
            else -> throw IOException("unknown frame kind $kind")
        }
    }

    fun json(msg: JSONObject): ByteArray {
        val body = msg.toString().toByteArray(Charsets.UTF_8)
        val out = ByteArray(5 + body.size)
        putInt(out, 0, body.size + 1)
        out[4] = KIND_JSON.toByte()
        System.arraycopy(body, 0, out, 5, body.size)
        return out
    }

    fun data(xfer: Int, buf: ByteArray, off: Int, len: Int): ByteArray {
        val out = ByteArray(9 + len)
        putInt(out, 0, len + 5)
        out[4] = KIND_DATA.toByte()
        putInt(out, 5, xfer)
        System.arraycopy(buf, off, out, 9, len)
        return out
    }

    private fun putInt(b: ByteArray, at: Int, v: Int) {
        b[at] = (v ushr 24).toByte()
        b[at + 1] = (v ushr 16).toByte()
        b[at + 2] = (v ushr 8).toByte()
        b[at + 3] = v.toByte()
    }
}

fun jsonOf(vararg pairs: Pair<String, Any?>): JSONObject {
    val o = JSONObject()
    for ((k, v) in pairs) o.put(k, v ?: JSONObject.NULL)
    return o
}

/** Optional string that treats JSON null and "" as absent. */
fun JSONObject.str(key: String): String? =
    if (has(key) && !isNull(key)) optString(key).takeIf { it.isNotEmpty() } else null
