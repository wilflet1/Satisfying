package dev.pclink

import android.content.Context
import android.net.wifi.WifiManager
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.util.concurrent.ConcurrentHashMap

/** A PC announcing itself on the LAN. */
data class FoundPc(val id: String, val name: String, val host: String, val port: Int, val fingerprint: String, val seenAt: Long)

/** Listens for the PC agent's UDP beacons (port 47811). */
class Discovery(private val context: Context) {
    val found = ConcurrentHashMap<String, FoundPc>()
    @Volatile private var running = false
    private var thread: Thread? = null
    var onFound: ((FoundPc) -> Unit)? = null

    fun start() {
        if (running) return
        running = true
        thread = Thread({ loop() }, "pclink-discovery").apply { start() }
    }

    fun stop() {
        running = false
        thread?.interrupt()
    }

    private fun loop() {
        val wifi = context.applicationContext.getSystemService(WifiManager::class.java)
        // Some phones drop broadcast packets unless a multicast lock is held.
        val lock = wifi?.createMulticastLock("pclink-discovery")?.apply {
            setReferenceCounted(false)
            acquire()
        }
        try {
            DatagramSocket(null).use { sock ->
                sock.reuseAddress = true
                sock.broadcast = true
                sock.bind(InetSocketAddress(PORT))
                sock.soTimeout = 1000
                val buf = ByteArray(2048)
                while (running) {
                    val p = DatagramPacket(buf, buf.size)
                    try {
                        sock.receive(p)
                    } catch (_: SocketTimeoutException) {
                        continue
                    }
                    try {
                        val o = JSONObject(String(p.data, 0, p.length, Charsets.UTF_8))
                        if (!o.has("pclink")) continue
                        val pc = FoundPc(
                            o.optString("id"),
                            o.optString("name", "PC"),
                            p.address.hostAddress ?: continue,
                            o.optInt("port", 47810),
                            o.optString("fp"),
                            System.currentTimeMillis(),
                        )
                        val isNew = found.put(pc.id, pc)?.host != pc.host
                        if (isNew) onFound?.invoke(pc)
                    } catch (_: Exception) {
                    }
                }
            }
        } catch (_: Exception) {
            // Port busy (another listener) or no network: discovery is best-effort.
        } finally {
            lock?.release()
        }
    }

    companion object {
        const val PORT = 47811
    }
}
