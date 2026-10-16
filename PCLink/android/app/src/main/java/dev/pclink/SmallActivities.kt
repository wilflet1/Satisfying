package dev.pclink

import android.app.Activity
import android.app.PendingIntent
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.view.Gravity
import android.widget.LinearLayout
import java.lang.ref.WeakReference

/**
 * Invisible activity that reads the clipboard (Android only allows that
 * while an activity is focused), sends it to the PC and closes.
 */
class ClipSendActivity : Activity() {
    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) return
        val clip = getSystemService(ClipboardManager::class.java).primaryClip
        val text = clip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.toString()
        if (text.isNullOrEmpty()) {
            Ui.toast(this, "Clipboard is empty")
        } else {
            val app = applicationContext
            Ui.background(app) {
                LinkService.await(app).request("clip.set", jsonOf("text" to text))
                "Clipboard sent to PC"
            }
        }
        finish()
    }

    companion object {
        fun intent(context: Context) = Intent(context, ClipSendActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_ANIMATION)

        fun pending(context: Context): PendingIntent =
            PendingIntent.getActivity(context, 1, intent(context), PendingIntent.FLAG_IMMUTABLE)
    }
}

/** "Share → Send to PC": files and photos go to the PC; links open there; text lands on its clipboard. */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val uris = mutableListOf<Uri>()
        @Suppress("DEPRECATION")
        when (intent.action) {
            Intent.ACTION_SEND -> (intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM))?.let { uris += it }
            Intent.ACTION_SEND_MULTIPLE -> intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)?.let { uris += it }
        }
        val text = intent.getStringExtra(Intent.EXTRA_TEXT)
        val ctx = applicationContext
        if (uris.isNotEmpty()) {
            Ui.toast(ctx, "Sending ${uris.size} file(s) to PC…")
            Ui.background(ctx) { sendUris(ctx, uris, "share") }
        } else if (text != null) {
            val url = Regex("https?://\\S+").find(text)?.value
            Ui.background(ctx) {
                val link = LinkService.await(ctx)
                if (url != null && url.length >= text.trim().length - 1) {
                    link.request("open_url", jsonOf("url" to url))
                    "Opened on PC"
                } else {
                    link.request("clip.set", jsonOf("text" to text))
                    "Copied to PC clipboard"
                }
            }
        }
        finish()
    }

    companion object {
        /** Uploads content URIs to the PC inbox. */
        fun sendUris(ctx: Context, uris: List<Uri>, kind: String = "file"): String {
            val link = LinkService.await(ctx)
            var sent = 0
            for (uri in uris) {
                var name = "file"
                var size: Long? = null
                ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use {
                    if (it.moveToFirst()) {
                        name = it.getString(0) ?: name
                        if (!it.isNull(1)) size = it.getLong(1)
                    }
                }
                val input = ctx.contentResolver.openInputStream(uri) ?: continue
                Transfers.track(ctx, "Sending $name", size) { progress ->
                    link.push(input, name, size, null, kind, progress)
                }
                sent++
            }
            return if (sent == 1) "Sent to PC" else "Sent $sent files to PC"
        }
    }
}

/** Full question view for asks with more than three options or long details. */
class AskActivity : Activity() {
    private var askId = -1

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        render()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        render()
    }

    private fun render() {
        askId = intent.getIntExtra("id", -1)
        current = WeakReference(this)
        val q = PhoneOps.questions[askId]
        if (q == null) {
            Ui.toast(this, "This question has expired")
            finish()
            return
        }
        title = q.title
        val col = Ui.column(this).apply {
            val p = Ui.dp(this@AskActivity, 20)
            setPadding(p, p, p, p)
        }
        col.addView(Ui.text(this, q.question, 19f, bold = true))
        q.detail?.let {
            col.addView(Ui.text(this, it, 14f, muted = true).apply {
                typeface = android.graphics.Typeface.MONOSPACE
                setPadding(0, Ui.dp(this@AskActivity, 8), 0, Ui.dp(this@AskActivity, 8))
                setTextIsSelectable(true)
            })
        }
        q.options.forEachIndexed { i, opt ->
            col.addView(Ui.button(this, opt, primary = i == 0) {
                PhoneOps.answer(askId, opt)
                finish()
            }, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply {
                topMargin = Ui.dp(this@AskActivity, 8)
                gravity = Gravity.CENTER
            })
        }
        setContentView(android.widget.ScrollView(this).apply { addView(col) })
        // Dialog windows aren't edge to edge, so no system-bar padding needed here.
    }

    companion object {
        private var current: WeakReference<AskActivity>? = null

        fun pending(context: Context, id: Int): PendingIntent = PendingIntent.getActivity(
            context,
            20_000 + id,
            Intent(context, AskActivity::class.java).putExtra("id", id).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

        fun closeIfShowing(id: Int) {
            val a = current?.get() ?: return
            if (a.askId == id) a.runOnUiThread { a.finish() }
        }
    }
}
