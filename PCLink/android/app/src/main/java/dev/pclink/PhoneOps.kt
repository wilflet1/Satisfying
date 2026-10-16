package dev.pclink

import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.MediaScannerConnection
import android.media.Ringtone
import android.media.RingtoneManager
import android.net.Uri
import android.os.BatteryManager
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.provider.Telephony
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import java.text.DateFormat
import java.util.Date
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicInteger

/** Everything the PC can ask the phone to do. */
class PhoneOps(private val ctx: Context) {
    private val prefs = Prefs(ctx)
    private val main = Handler(Looper.getMainLooper())
    private val nm = ctx.getSystemService(NotificationManager::class.java)

    fun handle(link: Link, op: String, args: JSONObject): Any? = when (op) {
        "ping" -> jsonOf("pong" to true)
        "status" -> statusJson()
        "fs.list" -> listDir(args.str("path"))
        "fs.read" -> {
            val f = resolve(args.str("path") ?: throw IOException("missing path"))
            if (!f.isFile) throw IOException("${f.name} is not a file")
            val xfer = args.getInt("xfer")
            val offset = args.optLong("offset", 0)
            Thread {
                try {
                    val input = FileInputStream(f)
                    if (offset > 0) input.skip(offset)
                    link.sendStream(xfer, input, f.name)
                } catch (_: Exception) {
                }
            }.start()
            jsonOf("name" to f.name, "size" to f.length())
        }
        "fs.write" -> {
            val dir = args.str("dir")?.let { resolve(it) } ?: inbox()
            if (!dir.isDirectory) throw IOException("${dir.path} is not a folder")
            val xfer = link.allocXfer()
            val size = if (args.has("size") && !args.isNull("size")) args.getLong("size") else null
            val done = link.expectIncoming(xfer, dir, args.str("name") ?: "file", size)
            done.thenAccept { file -> onReceived(file) }
            jsonOf("xfer" to xfer)
        }
        "xfer.status" -> jsonOf("path" to link.waitIncoming(args.getInt("xfer"), 60_000).path)
        "clip.get" -> jsonOf("text" to readClipboard()
            ?: throw IOException("Android only lets apps read the clipboard while they are open. Use the \"Send clipboard\" button or tile on the phone."))
        "clip.set" -> {
            setClipboard(args.optString("text"))
            JSONObject()
        }
        "notify" -> {
            // Task results open that task in the Claude screen.
            val task = if (args.has("task")) ClaudeActivity.intent(ctx, args.optLong("task")) else null
            notifyUser(args.optString("title", "PC"), args.optString("text"), App.channelFor(args.str("channel")), args.str("url"), task)
            JSONObject()
        }
        "ask" -> ask(args)
        "find_phone" -> {
            ringPhone()
            JSONObject()
        }
        "open_url" -> {
            val url = args.str("url") ?: throw IOException("missing url")
            if (!url.startsWith("http://") && !url.startsWith("https://")) throw IOException("only http(s) links")
            openUrl(url)
            JSONObject()
        }
        "battery" -> batteryJson()
        "sms.latest_code" -> latestSmsCode()
        "media.latest" -> latestMedia(args.optBoolean("screenshots_only"))
        "notif.reply" -> {
            val l = NotifListener.instance ?: throw IOException("Notification access is off")
            l.reply(args.optString("key"), args.optString("text"))
            JSONObject()
        }
        "notif.dismiss" -> {
            NotifListener.instance?.cancelNotification(args.optString("key"))
            JSONObject()
        }
        "audio.start" -> {
            val key = Base64.decode(args.getString("key"), Base64.DEFAULT)
            val port = Speaker.start(ctx, key, args.getInt("rate"), args.getInt("channels"))
            LinkService.changed()
            jsonOf("port" to port)
        }
        "mic.start" -> jsonOf("message" to Mic.start(ctx))
        "mic.stop" -> {
            Mic.stop(tellPc = false)
            JSONObject()
        }
        "audio.stop" -> {
            Speaker.stop()
            LinkService.changed()
            JSONObject()
        }
        else -> throw IOException("unknown request $op")
    }

    fun onEvent(ev: String, data: JSONObject) {
        when (ev) {
            "clip.changed" -> if (prefs.syncClipboard) setClipboard(data.optString("text"))
            "task.event" -> ClaudeActivity.dispatch(data)
            "action.done" -> {
                val ok = data.optBoolean("ok")
                val title = "${if (ok) "✅" else "❌"} ${data.optString("name")} (exit ${data.optInt("code")})"
                notifyUser(title, data.optString("output").ifBlank { "No output" }, App.CH_BUILD, null)
            }
        }
    }

    // ---------------------------------------------------------------- status

    fun batteryJson(): JSONObject {
        val bm = ctx.getSystemService(BatteryManager::class.java)
        return jsonOf(
            "battery" to bm.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY),
            "charging" to bm.isCharging,
        )
    }

    fun statusJson(): JSONObject = batteryJson().apply {
        put("model", "${Build.MANUFACTURER} ${Build.MODEL}")
        put("android", Build.VERSION.RELEASE)
        put("all_files_access", Environment.isExternalStorageManager())
        put("notification_access", NotifListener.instance != null)
        put("speaker", Speaker.isPlaying)
        put("mic", Mic.isOn)
        val stat = android.os.StatFs(Environment.getExternalStorageDirectory().path)
        put("storage_free", stat.availableBytes)
        put("storage_total", stat.totalBytes)
    }

    // ------------------------------------------------------------------ files

    private val root: File get() = Environment.getExternalStorageDirectory()

    /** Paths from the PC must stay inside shared storage. */
    private fun resolve(path: String): File {
        val f = File(path).canonicalFile
        val r = root.canonicalFile
        if (f != r && !f.path.startsWith(r.path + "/")) throw IOException("access denied: $path")
        if (!Environment.isExternalStorageManager()) {
            throw IOException("Give PCLink \"All files access\" on the phone to browse its files")
        }
        if (!f.exists()) throw IOException("not found: $path")
        return f
    }

    private fun listDir(path: String?): JSONObject {
        val dir = resolve(path ?: root.path)
        if (!dir.isDirectory) throw IOException("${dir.path} is not a folder")
        val entries = (dir.listFiles() ?: arrayOf())
            .filter { !it.name.startsWith(".") }
            .sortedWith(compareBy<File>({ !it.isDirectory }, { it.name.lowercase() }))
            .map {
                jsonOf(
                    "name" to it.name,
                    "path" to it.path,
                    "dir" to it.isDirectory,
                    "size" to if (it.isDirectory) 0 else it.length(),
                    "mtime" to it.lastModified(),
                )
            }
        val parent = dir.parentFile?.takeIf { dir.canonicalFile != root.canonicalFile }
        return jsonOf("path" to dir.path, "parent" to parent?.path, "entries" to JSONArray(entries))
    }

    /** Where files from the PC land. */
    fun inbox(): File {
        val dir = if (Environment.isExternalStorageManager()) {
            File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "PCLink")
        } else {
            File(ctx.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), "PCLink")
        }
        dir.mkdirs()
        return dir
    }

    private fun onReceived(file: File) {
        MediaScannerConnection.scanFile(ctx, arrayOf(file.path), null, null)
        val downloads = Intent(android.app.DownloadManager.ACTION_VIEW_DOWNLOADS)
        notifyUser("Received ${file.name}", "Saved in ${file.parentFile?.name ?: "Downloads"}", App.CH_FILES, null, downloads)
    }

    // -------------------------------------------------------------- clipboard

    private fun onMain(block: () -> Unit) {
        val latch = CountDownLatch(1)
        main.post {
            try {
                block()
            } finally {
                latch.countDown()
            }
        }
        latch.await(5, TimeUnit.SECONDS)
    }

    fun setClipboard(text: String) {
        onMain {
            val cm = ctx.getSystemService(ClipboardManager::class.java)
            cm.setPrimaryClip(ClipData.newPlainText("From PC", text))
        }
    }

    private fun readClipboard(): String? {
        var text: String? = null
        onMain {
            val cm = ctx.getSystemService(ClipboardManager::class.java)
            text = cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(ctx)?.toString()
        }
        return text
    }

    // ---------------------------------------------------------- notifications

    fun notifyUser(title: String, text: String, channel: String, url: String?, intent: Intent? = null) {
        val tap = when {
            url != null -> Intent(Intent.ACTION_VIEW, Uri.parse(url))
            intent != null -> intent
            else -> Intent(ctx, MainActivity::class.java)
        }
        val id = nextNotifId.incrementAndGet()
        val n = Notification.Builder(ctx, channel)
            .setSmallIcon(android.R.drawable.stat_notify_more)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(Notification.BigTextStyle().bigText(text))
            .setAutoCancel(true)
            .setContentIntent(PendingIntent.getActivity(ctx, id, tap, PendingIntent.FLAG_IMMUTABLE))
            .build()
        nm.notify(id, n)
    }

    /** Android blocks activity starts from the background, so links arrive as a tap-to-open notification. */
    private fun openUrl(url: String) {
        if (MainActivity.visible) {
            main.post {
                ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            }
        } else {
            notifyUser("Open link from PC", url, App.CH_GENERAL, url)
        }
    }

    // -------------------------------------------------------------- questions

    private fun ask(args: JSONObject): JSONObject {
        val id = nextAskId.incrementAndGet()
        val options = args.optJSONArray("options") ?: JSONArray()
        val q = Question(
            id,
            args.optString("title", "Question"),
            args.optString("question"),
            args.str("detail"),
            (0 until options.length()).map { options.getString(it) },
        )
        val future = CompletableFuture<String>()
        questions[id] = q
        answers[id] = future
        val timeout = args.optLong("timeout_ms", 120_000)

        val notifId = ASK_NOTIF_BASE + id
        val b = Notification.Builder(ctx, App.channelFor(args.str("channel") ?: "claude"))
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(q.question)
            .setContentText(q.detail ?: q.title)
            .setSubText(q.title)
            .setStyle(Notification.BigTextStyle().bigText(listOfNotNull(q.detail).joinToString("\n")))
            .setCategory(Notification.CATEGORY_REMINDER)
            .setTimeoutAfter(timeout)
            .setContentIntent(AskActivity.pending(ctx, id))
        q.options.take(3).forEachIndexed { i, opt ->
            b.addAction(Notification.Action.Builder(null, opt, ActionReceiver.answer(ctx, id, i)).build())
        }
        nm.notify(notifId, b.build())
        try {
            return jsonOf("answer" to future.get(timeout, TimeUnit.MILLISECONDS))
        } catch (e: TimeoutException) {
            return jsonOf("answer" to null, "timed_out" to true)
        } finally {
            questions.remove(id)
            answers.remove(id)
            nm.cancel(notifId)
            AskActivity.closeIfShowing(id)
        }
    }

    // ------------------------------------------------------------ find phone

    private fun ringPhone() {
        val am = ctx.getSystemService(AudioManager::class.java)
        am.setStreamVolume(AudioManager.STREAM_ALARM, am.getStreamMaxVolume(AudioManager.STREAM_ALARM), 0)
        val uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM)
            ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE)
        main.post {
            ringing?.stop()
            val r = RingtoneManager.getRingtone(ctx, uri) ?: return@post
            r.audioAttributes = AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).build()
            r.isLooping = true
            r.play()
            ringing = r
            main.postDelayed({ stopRinging() }, 45_000)
        }
        val n = Notification.Builder(ctx, App.CH_FIND)
            .setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
            .setContentTitle("Your PC is looking for this phone")
            .setContentText("Tap to stop ringing")
            .setContentIntent(ActionReceiver.pending(ctx, ActionReceiver.STOP_RINGING))
            .setAutoCancel(true)
            .addAction(Notification.Action.Builder(null, "Stop", ActionReceiver.pending(ctx, ActionReceiver.STOP_RINGING)).build())
            .build()
        nm.notify(FIND_NOTIF, n)
    }

    // ------------------------------------------------------------- SMS, media

    private fun latestSmsCode(): JSONObject {
        val since = System.currentTimeMillis() - 10 * 60_000
        val cursor = try {
            ctx.contentResolver.query(
                Telephony.Sms.Inbox.CONTENT_URI,
                arrayOf(Telephony.Sms.ADDRESS, Telephony.Sms.BODY, Telephony.Sms.DATE),
                "${Telephony.Sms.DATE} > ?",
                arrayOf(since.toString()),
                "${Telephony.Sms.DATE} DESC",
            )
        } catch (e: SecurityException) {
            throw IOException("Allow PCLink to read SMS in the app's settings first")
        }
        cursor?.use {
            while (it.moveToNext()) {
                val body = it.getString(1) ?: continue
                val code = Regex("(?<![\\d])(\\d{4,8})(?![\\d])").find(body)?.groupValues?.get(1) ?: continue
                return jsonOf("code" to code, "from" to it.getString(0), "body" to body, "date" to it.getLong(2))
            }
        }
        return jsonOf("code" to null)
    }

    @Suppress("DEPRECATION")
    private fun latestMedia(screenshotsOnly: Boolean): JSONObject {
        val sel = if (screenshotsOnly) "${MediaStore.MediaColumns.RELATIVE_PATH} LIKE ?" else null
        val selArgs = if (screenshotsOnly) arrayOf("%Screenshots%") else null
        ctx.contentResolver.query(
            MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
            arrayOf(MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.DATE_ADDED),
            sel,
            selArgs,
            "${MediaStore.MediaColumns.DATE_ADDED} DESC",
        )?.use {
            if (it.moveToFirst()) {
                val taken = DateFormat.getDateTimeInstance().format(Date(it.getLong(1) * 1000))
                return jsonOf("path" to it.getString(0), "taken" to taken)
            }
        }
        throw IOException("No photos found (does PCLink have All files access?)")
    }

    data class Question(val id: Int, val title: String, val question: String, val detail: String?, val options: List<String>)

    companion object {
        private const val ASK_NOTIF_BASE = 10_000
        private const val FIND_NOTIF = 9
        private val nextNotifId = AtomicInteger(100)
        private val nextAskId = AtomicInteger(0)
        val questions = ConcurrentHashMap<Int, Question>()
        private val answers = ConcurrentHashMap<Int, CompletableFuture<String>>()
        @Volatile private var ringing: Ringtone? = null

        fun answer(id: Int, option: String) {
            answers[id]?.complete(option)
        }

        fun stopRinging() {
            ringing?.stop()
            ringing = null
            App.instance.getSystemService(NotificationManager::class.java).cancel(FIND_NOTIF)
        }
    }
}
