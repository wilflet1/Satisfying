package dev.pclink

import android.app.Notification
import android.app.NotificationManager
import android.content.Context
import java.util.concurrent.atomic.AtomicInteger

/** Progress notifications for file transfers. */
object Transfers {
    private val ids = AtomicInteger(5000)

    fun <T> track(ctx: Context, title: String, total: Long?, work: (progress: (Long) -> Unit) -> T): T {
        val nm = ctx.getSystemService(NotificationManager::class.java)
        val id = ids.incrementAndGet()
        var last = 0L
        val builder = Notification.Builder(ctx, App.CH_FILES)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle(title)
            .setOnlyAlertOnce(true)
            .setOngoing(true)
        nm.notify(id, builder.setProgress(0, 0, true).build())
        try {
            return work { done ->
                val now = System.currentTimeMillis()
                if (now - last > 500) {
                    last = now
                    if (total != null && total > 0) {
                        builder.setProgress(1000, (done * 1000 / total).toInt(), false)
                            .setContentText("${Ui.size(done)} of ${Ui.size(total)}")
                    } else {
                        builder.setContentText(Ui.size(done))
                    }
                    nm.notify(id, builder.build())
                }
            }
        } finally {
            nm.cancel(id)
        }
    }
}
