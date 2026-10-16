package dev.pclink

import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.net.ConnectivityManager
import android.net.Network
import android.os.BatteryManager
import android.os.IBinder
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.CopyOnWriteArraySet

/**
 * Keeps the phone connected to the paired PC: reconnects with backoff,
 * follows the PC's IP via discovery, and routes the PC's requests to
 * [PhoneOps].
 */
class LinkService : Service(), Link.Handler {
    private lateinit var prefs: Prefs
    private lateinit var discovery: Discovery
    private lateinit var ops: PhoneOps
    @Volatile private var running = false
    private val wake = Object()
    private var batteryReceiver: BroadcastReceiver? = null
    private var netCallback: ConnectivityManager.NetworkCallback? = null
    private var lastBattery = -1

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        prefs = Prefs(this)
        discovery = Discovery(this)
        ops = PhoneOps(this)
        discovery.onFound = { pc -> if (pc.id == prefs.pcId) kick() }
        startForeground(NOTIF_ID, buildNotification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        running = true
        discovery.start()
        Thread({ connectLoop() }, "pclink-connect").start()

        batteryReceiver = object : BroadcastReceiver() {
            override fun onReceive(c: Context, i: Intent) {
                val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
                val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100)
                val pct = if (level >= 0 && scale > 0) level * 100 / scale else -1
                if (pct != lastBattery) {
                    lastBattery = pct
                    current?.event("battery", ops.batteryJson())
                }
            }
        }
        registerReceiver(batteryReceiver, IntentFilter(Intent.ACTION_BATTERY_CHANGED))

        val cm = getSystemService(ConnectivityManager::class.java)
        netCallback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) = kick()
        }
        cm.registerDefaultNetworkCallback(netCallback!!)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                prefs.forget()
                stopSelf()
            }
            ACTION_RECONNECT -> {
                current?.close()
                kick()
            }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        instance = null
        running = false
        discovery.stop()
        current?.close()
        Speaker.stop()
        batteryReceiver?.let { unregisterReceiver(it) }
        netCallback?.let { getSystemService(ConnectivityManager::class.java).unregisterNetworkCallback(it) }
        kick()
        super.onDestroy()
    }

    private fun kick() = synchronized(wake) { wake.notifyAll() }

    private fun sleep(ms: Long) = synchronized(wake) {
        try {
            wake.wait(ms)
        } catch (_: InterruptedException) {
        }
    }

    private fun connectLoop() {
        var backoff = 1000L
        while (running) {
            if (!prefs.paired) {
                sleep(5000)
                continue
            }
            val existing = current
            if (existing != null && existing.isOpen) {
                sleep(30_000)
                continue
            }
            setState("Connecting to ${prefs.pcName.ifEmpty { "PC" }}…")
            val candidates = buildList {
                discovery.found[prefs.pcId]?.let { add(it.host) }
                addAll(prefs.hosts)
            }.distinct()
            var connected = false
            for (host in candidates) {
                try {
                    val hello = jsonOf(
                        "t" to "hello",
                        "device_id" to prefs.deviceId,
                        "device_name" to prefs.deviceName,
                        "token" to prefs.token,
                        "protocol" to 1,
                    )
                    val (link, welcome, _) = Link.open(host, prefs.port, prefs.fingerprint, hello)
                    prefs.rememberHost(host)
                    welcome.str("pc_name")?.let { prefs.pcName = it }
                    welcome.str("mac")?.let { prefs.mac = it }
                    current = link
                    link.start(this)
                    connected = true
                    backoff = 1000L
                    setState(null)
                    link.event("status", ops.statusJson())
                    break
                } catch (e: RemoteException) {
                    // The PC refused us (e.g. it forgot this phone).
                    setState("PC refused the connection: ${e.message}")
                    if (e.message?.contains("pair again") == true) prefs.forget()
                } catch (e: IOException) {
                    setState("Can't reach ${prefs.pcName.ifEmpty { "PC" }}. Retrying…")
                } catch (e: Exception) {
                    setState("Connection error: ${e.message}")
                }
            }
            if (!connected) {
                sleep(backoff)
                backoff = (backoff * 2).coerceAtMost(30_000)
            }
        }
    }

    // ------------------------------------------------------------ Link.Handler

    override fun onRequest(link: Link, op: String, args: JSONObject): Any? = ops.handle(link, op, args)

    override fun onEvent(link: Link, ev: String, data: JSONObject) = ops.onEvent(ev, data)

    override fun onClosed(link: Link, error: Throwable?) {
        if (current === link) {
            current = null
            Speaker.stop()
            setState(if (running) "Disconnected. Reconnecting…" else null)
        }
        kick()
    }

    // ----------------------------------------------------------- notification

    private fun setState(text: String?) {
        stateText = text
        if (running) {
            getSystemService(android.app.NotificationManager::class.java).notify(NOTIF_ID, buildNotification())
        }
        for (l in listeners) l()
    }

    private fun buildNotification(): Notification {
        val link = current
        val title = if (link != null) "Connected to ${link.pcName}" else "PCLink"
        val text = stateText ?: if (link != null) "Tap for controls" else "Not connected"
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE,
        )
        val b = Notification.Builder(this, App.CH_LINK)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .setShowWhen(false)
        if (link != null) {
            b.addAction(
                Notification.Action.Builder(
                    null,
                    if (Speaker.isPlaying) "Stop speaker" else "Phone speaker",
                    ActionReceiver.pending(this, ActionReceiver.SPEAKER_TOGGLE),
                ).build(),
            )
            b.addAction(
                Notification.Action.Builder(null, "Send clipboard", ClipSendActivity.pending(this)).build(),
            )
        }
        return b.build()
    }

    companion object {
        const val NOTIF_ID = 1
        const val ACTION_STOP = "dev.pclink.STOP"
        const val ACTION_RECONNECT = "dev.pclink.RECONNECT"

        @Volatile var current: Link? = null
            private set
        @Volatile var stateText: String? = null
            private set

        /** UI callbacks fired when the connection state changes. */
        val listeners = CopyOnWriteArraySet<() -> Unit>()

        fun start(context: Context) = startSafely(context, Intent(context, LinkService::class.java))

        fun reconnect(context: Context) =
            startSafely(context, Intent(context, LinkService::class.java).setAction(ACTION_RECONNECT))

        /**
         * Android refuses foreground-service starts from some background
         * states; the service then starts next time the app is opened.
         */
        private fun startSafely(context: Context, intent: Intent) {
            try {
                context.startForegroundService(intent)
            } catch (e: IllegalStateException) {
                android.util.Log.w("PCLink", "could not start link service: ${e.message}")
            }
        }

        @Volatile private var instance: LinkService? = null

        /** Re-renders the status notification and tells the UI. */
        fun changed() {
            val svc = instance
            if (svc != null) svc.setState(stateText) else for (l in listeners) l()
        }

        /** The connected link, or an exception with a readable message. */
        fun require(): Link = current ?: throw IOException("Not connected to your PC")

        /**
         * The connected link, starting the service and waiting briefly if the
         * app was just launched (e.g. from the share sheet or a tile).
         * Call off the main thread.
         */
        fun await(context: Context, timeoutMs: Long = 8000): Link {
            current?.let { return it }
            if (!Prefs(context).paired) throw IOException("Pair with your PC in the PCLink app first")
            start(context)
            val end = System.currentTimeMillis() + timeoutMs
            while (System.currentTimeMillis() < end) {
                current?.let { return it }
                Thread.sleep(200)
            }
            throw IOException("Not connected to your PC")
        }
    }
}
