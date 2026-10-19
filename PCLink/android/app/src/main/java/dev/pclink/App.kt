package dev.pclink

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager

class App : Application() {
    override fun onCreate() {
        super.onCreate()
        instance = this
        CrashLog.install(this)
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannels(
            listOf(
                NotificationChannel(CH_LINK, "Connection", NotificationManager.IMPORTANCE_LOW).apply {
                    description = "Shows that PCLink is connected to your PC"
                },
                NotificationChannel(CH_CLAUDE, "Claude Code", NotificationManager.IMPORTANCE_HIGH).apply {
                    description = "Claude finished, needs you, or asks for permission"
                },
                NotificationChannel(CH_BUILD, "Commands and builds", NotificationManager.IMPORTANCE_DEFAULT),
                NotificationChannel(CH_GENERAL, "From your PC", NotificationManager.IMPORTANCE_DEFAULT),
                NotificationChannel(CH_FILES, "File transfers", NotificationManager.IMPORTANCE_LOW),
                NotificationChannel(CH_FIND, "Find my phone", NotificationManager.IMPORTANCE_HIGH),
            ),
        )
        if (Prefs(this).paired) LinkService.start(this)
    }

    companion object {
        lateinit var instance: App
            private set

        const val CH_LINK = "link"
        const val CH_CLAUDE = "claude"
        const val CH_BUILD = "build"
        const val CH_GENERAL = "general"
        const val CH_FILES = "files"
        const val CH_FIND = "find"

        fun channelFor(name: String?): String = when (name) {
            "claude" -> CH_CLAUDE
            "build" -> CH_BUILD
            else -> CH_GENERAL
        }
    }
}
