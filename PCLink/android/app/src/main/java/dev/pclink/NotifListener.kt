package dev.pclink

import android.app.Notification
import android.app.RemoteInput
import android.content.Intent
import android.os.Bundle
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import java.io.IOException

/** Mirrors phone notifications to the PC and performs replies from the PC. */
class NotifListener : NotificationListenerService() {
    override fun onListenerConnected() {
        instance = this
        LinkService.changed()
    }

    override fun onListenerDisconnected() {
        instance = null
    }

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        if (!Prefs(this).mirrorNotifications) return
        if (sbn.packageName == packageName) return
        val n = sbn.notification
        if (sbn.isOngoing || n.flags and Notification.FLAG_GROUP_SUMMARY != 0) return
        val extras = n.extras
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString() ?: ""
        val text = (extras.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: extras.getCharSequence(Notification.EXTRA_TEXT))
            ?.toString() ?: ""
        if (title.isEmpty() && text.isEmpty()) return
        val link = LinkService.current ?: return
        link.event(
            "notif.posted",
            jsonOf(
                "key" to sbn.key,
                "package" to sbn.packageName,
                "app" to appLabel(sbn.packageName),
                "title" to title,
                "text" to text,
                "time" to sbn.postTime,
                "can_reply" to (replyAction(n) != null),
                // Updates to an existing notification shouldn't pop a new toast on the PC.
                "silent" to (n.flags and Notification.FLAG_ONLY_ALERT_ONCE != 0),
            ),
        )
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification) {
        LinkService.current?.event("notif.removed", jsonOf("key" to sbn.key))
    }

    private fun appLabel(pkg: String): String = try {
        packageManager.getApplicationLabel(packageManager.getApplicationInfo(pkg, 0)).toString()
    } catch (_: Exception) {
        pkg
    }

    private fun replyAction(n: Notification): Notification.Action? =
        n.actions?.firstOrNull { a -> a.remoteInputs?.any { it.allowFreeFormInput } == true }

    fun reply(key: String, text: String) {
        val sbn = activeNotifications.firstOrNull { it.key == key } ?: throw IOException("notification is gone")
        val action = replyAction(sbn.notification) ?: throw IOException("this notification has no reply action")
        val intent = Intent()
        val results = Bundle()
        for (ri in action.remoteInputs) results.putCharSequence(ri.resultKey, text)
        RemoteInput.addResultsToIntent(action.remoteInputs, intent, results)
        action.actionIntent.send(this, 0, intent)
    }

    companion object {
        @Volatile var instance: NotifListener? = null
            private set
    }
}
