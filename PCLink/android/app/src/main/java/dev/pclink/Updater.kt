package dev.pclink

import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.net.Uri
import android.os.Build
import android.provider.Settings
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.security.MessageDigest

/**
 * Self-update from the repository's `pclink-latest` GitHub release, which
 * CI publishes with `latest.json` (build number + SHA-256 of each file)
 * and `PCLink.apk`. The app's versionCode is that build number.
 */
object Updater {
    const val REPO = "wilflet1/Satisfying"
    private const val BASE = "https://github.com/$REPO/releases/download/pclink-latest"
    @Volatile private var running = false

    fun currentBuild(ctx: Context): Long =
        ctx.packageManager.getPackageInfo(ctx.packageName, 0).longVersionCode

    fun versionName(ctx: Context): String =
        ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "?"

    private fun open(url: String): HttpURLConnection {
        val c = java.net.URI(url).toURL().openConnection() as HttpURLConnection
        c.connectTimeout = 10_000
        c.readTimeout = 60_000
        c.instanceFollowRedirects = true
        if (c.responseCode != 200) throw IOException("HTTP ${c.responseCode} for $url")
        return c
    }

    /**
     * Checks for a newer build and installs it. Call off the main thread.
     * Returns a message for the user (shown when [manual]).
     */
    fun checkAndInstall(ctx: Context, manual: Boolean): String {
        if (running) return "Already checking for updates"
        running = true
        try {
            val manifest = JSONObject(open("$BASE/latest.json").inputStream.bufferedReader().readText())
            val latest = manifest.getLong("build")
            val current = currentBuild(ctx)
            if (current <= 1) return "This is a local build, which doesn't update itself."
            if (latest <= current) return "PCLink is up to date (build $current)."
            val info = manifest.getJSONObject("files").getJSONObject("PCLink.apk")
            if (!ctx.packageManager.canRequestPackageInstalls()) {
                askForInstallPermission(ctx, latest)
                return "Allow PCLink to install updates, then check again."
            }
            val apk = download(ctx, info.getString("sha256"), info.getLong("size"))
            install(ctx, apk)
            return "Installing build $latest…"
        } catch (e: Exception) {
            return "Update check failed: ${e.message}"
        } finally {
            running = false
        }
    }

    private fun download(ctx: Context, sha256: String, size: Long): File {
        val dest = File(ctx.cacheDir, "update.apk")
        val digest = MessageDigest.getInstance("SHA-256")
        var total = 0L
        open("$BASE/PCLink.apk").inputStream.use { input ->
            dest.outputStream().use { out ->
                val buf = ByteArray(64 * 1024)
                while (true) {
                    val n = input.read(buf)
                    if (n < 0) break
                    digest.update(buf, 0, n)
                    out.write(buf, 0, n)
                    total += n
                    if (total > size) break
                }
            }
        }
        val hex = digest.digest().joinToString("") { "%02x".format(it) }
        if (total != size || !hex.equals(sha256, ignoreCase = true)) {
            dest.delete()
            throw IOException("downloaded update failed verification")
        }
        return dest
    }

    private fun install(ctx: Context, apk: File) {
        val installer = ctx.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(ctx.packageName)
            // Once PCLink installed itself, later updates need no tap (Android 12+).
            if (Build.VERSION.SDK_INT >= 31) {
                setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
            }
        }
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            apk.inputStream().use { input ->
                session.openWrite("PCLink.apk", 0, apk.length()).use { out ->
                    input.copyTo(out)
                    session.fsync(out)
                }
            }
            val intent = Intent(ctx, UpdateReceiver::class.java)
            // The installer fills in status extras, so the intent must be mutable.
            val pending = PendingIntent.getBroadcast(ctx, id, intent, PendingIntent.FLAG_MUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            session.commit(pending.intentSender)
        }
    }

    private fun askForInstallPermission(ctx: Context, build: Long) {
        val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${ctx.packageName}"))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        notify(ctx, "PCLink build $build is available", "Tap to allow PCLink to install its updates.", intent)
    }

    fun notify(ctx: Context, title: String, text: String, intent: Intent?) {
        val n = Notification.Builder(ctx, App.CH_GENERAL)
            .setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setContentTitle(title)
            .setContentText(text)
            .setAutoCancel(true)
        if (intent != null) {
            n.setContentIntent(PendingIntent.getActivity(ctx, 7, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT))
        }
        ctx.getSystemService(NotificationManager::class.java).notify(UPDATE_NOTIF, n.build())
    }

    const val UPDATE_NOTIF = 8
}

/** Receives the result of an update install session. */
class UpdateReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                // First self-update (or Android 11): the user confirms in the system dialog.
                @Suppress("DEPRECATION")
                val confirm = intent.getParcelableExtra<Intent>(Intent.EXTRA_INTENT) ?: return
                confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                if (MainActivity.visible) {
                    context.startActivity(confirm)
                } else {
                    Updater.notify(context, "PCLink update ready", "Tap to install the new version.", confirm)
                }
            }
            PackageInstaller.STATUS_SUCCESS -> {}
            else -> {
                val msg = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE) ?: "unknown error"
                Updater.notify(context, "PCLink update failed", msg, null)
            }
        }
    }
}
