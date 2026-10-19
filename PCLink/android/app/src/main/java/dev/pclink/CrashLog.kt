package dev.pclink

import android.content.Context
import android.os.Build
import android.util.Log
import java.io.File
import java.text.DateFormat
import java.util.Date

/**
 * Keeps the last crash on disk and hands it to the PC on the next connect,
 * so crashes show up in the PC's activity log and agent.log.
 */
object CrashLog {
    private fun file(ctx: Context) = File(ctx.filesDir, "last-crash.txt")

    fun install(ctx: Context) {
        val app = ctx.applicationContext
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, e ->
            try {
                val version = app.packageManager.getPackageInfo(app.packageName, 0).longVersionCode
                file(app).writeText(
                    "PCLink build $version on ${Build.MANUFACTURER} ${Build.MODEL} (Android ${Build.VERSION.RELEASE})\n" +
                        "${DateFormat.getDateTimeInstance().format(Date())}, thread ${thread.name}\n" +
                        Log.getStackTraceString(e).take(12_000),
                )
            } catch (_: Throwable) {
            }
            previous?.uncaughtException(thread, e)
        }
    }

    /** The saved crash report, removed once taken. */
    fun take(ctx: Context): String? {
        val f = file(ctx)
        if (!f.exists()) return null
        return try {
            f.readText().also { f.delete() }
        } catch (_: Exception) {
            null
        }
    }
}
