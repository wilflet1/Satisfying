package dev.pclink

import android.content.Context
import android.os.Build
import java.util.UUID

/** What the phone remembers about the PC it is paired with. */
class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("pclink", Context.MODE_PRIVATE)

    val deviceId: String
        get() = sp.getString("device_id", null) ?: UUID.randomUUID().toString().also {
            sp.edit().putString("device_id", it).apply()
        }

    val deviceName: String
        get() = sp.getString("device_name", null) ?: "${Build.MANUFACTURER} ${Build.MODEL}".trim()

    var pcId: String
        get() = sp.getString("pc_id", "") ?: ""
        set(v) = sp.edit().putString("pc_id", v).apply()
    var pcName: String
        get() = sp.getString("pc_name", "") ?: ""
        set(v) = sp.edit().putString("pc_name", v).apply()
    /** Hosts to try, most recently working first. */
    var hosts: List<String>
        get() = (sp.getString("hosts", "") ?: "").split(',').filter { it.isNotBlank() }
        set(v) = sp.edit().putString("hosts", v.distinct().take(5).joinToString(",")).apply()
    var port: Int
        get() = sp.getInt("port", 47810)
        set(v) = sp.edit().putInt("port", v).apply()
    var fingerprint: String
        get() = sp.getString("fp", "") ?: ""
        set(v) = sp.edit().putString("fp", v).apply()
    var token: String
        get() = sp.getString("token", "") ?: ""
        set(v) = sp.edit().putString("token", v).apply()
    var mac: String
        get() = sp.getString("mac", "") ?: ""
        set(v) = sp.edit().putString("mac", v).apply()

    /** Relay to reach the PC from anywhere (sent by the PC when connected). */
    var relayUrl: String
        get() = sp.getString("relay_url", "") ?: ""
        set(v) = sp.edit().putString("relay_url", v).apply()
    var relayRoom: String
        get() = sp.getString("relay_room", "") ?: ""
        set(v) = sp.edit().putString("relay_room", v).apply()

    var syncClipboard: Boolean
        get() = sp.getBoolean("sync_clip", true)
        set(v) = sp.edit().putBoolean("sync_clip", v).apply()
    var mirrorNotifications: Boolean
        get() = sp.getBoolean("mirror_notifs", true)
        set(v) = sp.edit().putBoolean("mirror_notifs", v).apply()

    val paired: Boolean get() = token.isNotEmpty() && fingerprint.isNotEmpty()

    fun rememberHost(host: String) {
        hosts = listOf(host) + hosts.filter { it != host }
    }

    fun forget() {
        sp.edit().remove("pc_id").remove("pc_name").remove("hosts").remove("fp").remove("token").remove("mac")
            .remove("relay_url").remove("relay_room").apply()
    }
}
