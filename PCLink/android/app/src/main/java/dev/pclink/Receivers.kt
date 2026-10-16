package dev.pclink

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** Notification buttons: answers to questions, speaker toggle, stop ringing. */
class ActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ANSWER -> {
                val id = intent.getIntExtra("id", -1)
                val q = PhoneOps.questions[id] ?: return
                val option = q.options.getOrNull(intent.getIntExtra("option", -1)) ?: return
                PhoneOps.answer(id, option)
            }
            STOP_RINGING -> PhoneOps.stopRinging()
            SPEAKER_TOGGLE -> {
                val pending = goAsync()
                Thread {
                    try {
                        toggleSpeaker()
                    } finally {
                        pending.finish()
                    }
                }.start()
            }
        }
    }

    companion object {
        const val ANSWER = "dev.pclink.ANSWER"
        const val STOP_RINGING = "dev.pclink.STOP_RINGING"
        const val SPEAKER_TOGGLE = "dev.pclink.SPEAKER_TOGGLE"

        fun pending(context: Context, action: String): PendingIntent = PendingIntent.getBroadcast(
            context,
            action.hashCode(),
            Intent(context, ActionReceiver::class.java).setAction(action),
            PendingIntent.FLAG_IMMUTABLE,
        )

        fun answer(context: Context, id: Int, option: Int): PendingIntent = PendingIntent.getBroadcast(
            context,
            id * 10 + option,
            Intent(context, ActionReceiver::class.java).setAction(ANSWER).putExtra("id", id).putExtra("option", option),
            PendingIntent.FLAG_IMMUTABLE,
        )

        /** Asks the PC to start or stop streaming its audio to this phone. */
        fun toggleSpeaker(): String = try {
            val link = LinkService.await(App.instance)
            val res = link.request("speaker.set", jsonOf("on" to !Speaker.isPlaying), 20_000)
            LinkService.changed()
            if (res.optBoolean("on")) "Playing PC audio" else "Speaker off"
        } catch (e: Exception) {
            e.message ?: "Failed"
        }
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (Prefs(context).paired) LinkService.start(context)
    }
}
