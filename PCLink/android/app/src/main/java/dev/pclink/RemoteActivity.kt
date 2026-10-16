package dev.pclink

import android.app.Activity
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.text.InputType
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import android.widget.EditText
import android.widget.LinearLayout
import java.util.concurrent.Executors
import kotlin.math.abs

/**
 * Touchpad, keyboard and dictation for the PC. Tip: tap the mic on your
 * keyboard to dictate a prompt straight into Claude Code on the PC.
 */
class RemoteActivity : Activity() {
    /** One thread keeps input events in order. */
    private val sender = Executors.newSingleThreadExecutor()

    private fun input(vararg pairs: Pair<String, Any?>) {
        val link = LinkService.current ?: return
        sender.execute {
            try {
                link.request("input", jsonOf(*pairs), 5000)
            } catch (_: Exception) {
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val root = Ui.column(this).apply {
            val p = Ui.dp(this@RemoteActivity, 12)
            setPadding(p, p, p, p)
            setBackgroundColor(Ui.bgColor(this@RemoteActivity))
        }

        // Text to type on the PC.
        val field = EditText(this).apply {
            hint = "Type or dictate, then Send (types on the PC)"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            imeOptions = EditorInfo.IME_ACTION_SEND
            maxLines = 4
        }
        root.addView(field)
        val sendRow = Ui.row(this)
        fun sendText(enter: Boolean) {
            val t = field.text.toString()
            if (t.isNotEmpty()) input("kind" to "text", "text" to t)
            if (enter) input("kind" to "key", "key" to "enter")
            field.setText("")
        }
        sendRow.addView(Ui.button(this, "Send") { sendText(false) }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        sendRow.addView(Ui.button(this, "Send + Enter", primary = true) { sendText(true) }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        root.addView(sendRow)

        val keys = listOf(
            "Esc" to listOf("esc"), "Tab" to listOf("tab"), "⌫" to listOf("backspace"), "Enter" to listOf("enter"),
            "↑" to listOf("up"), "↓" to listOf("down"), "←" to listOf("left"), "→" to listOf("right"),
            "Ctrl+C" to listOf("c", "ctrl"), "Ctrl+V" to listOf("v", "ctrl"), "Alt+Tab" to listOf("tab", "alt"), "Win" to listOf("win"),
        )
        root.addView(Ui.grid(this, keys.map { (label, spec) ->
            Ui.button(this, label) {
                input("kind" to "key", "key" to spec[0], "modifiers" to org.json.JSONArray(spec.drop(1)))
            }
        }, perRow = 4))

        // Touchpad: drag to move, tap to click, two-finger drag to scroll.
        val pad = View(this).apply {
            background = GradientDrawable().apply {
                setColor(if (Ui.isDark(this@RemoteActivity)) Color.rgb(40, 46, 54) else Color.rgb(225, 230, 237))
                cornerRadius = Ui.dp(this@RemoteActivity, 16).toFloat()
            }
            setOnTouchListener(TouchpadListener())
        }
        root.addView(pad, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply {
            topMargin = Ui.dp(this@RemoteActivity, 8)
        })
        val clicks = Ui.row(this)
        clicks.addView(Ui.button(this, "Left") { input("kind" to "click", "button" to "left") }, LinearLayout.LayoutParams(0, Ui.dp(this, 56), 1f))
        clicks.addView(Ui.button(this, "Right") { input("kind" to "click", "button" to "right") }, LinearLayout.LayoutParams(0, Ui.dp(this, 56), 1f))
        root.addView(clicks)
        setContentView(root)
    }

    override fun onDestroy() {
        sender.shutdown()
        super.onDestroy()
    }

    private inner class TouchpadListener : View.OnTouchListener {
        private var lastX = 0f
        private var lastY = 0f
        private var downTime = 0L
        private var moved = 0f
        private var fingers = 0
        private var accX = 0f
        private var accY = 0f
        private var scrollAcc = 0f
        private val speed = 1.6f

        override fun onTouch(v: View, e: MotionEvent): Boolean {
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    lastX = e.x; lastY = e.y; downTime = System.currentTimeMillis(); moved = 0f; fingers = 1
                }
                MotionEvent.ACTION_POINTER_DOWN -> {
                    fingers = e.pointerCount
                    lastY = e.getY(0)
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = e.x - lastX
                    val dy = e.y - lastY
                    lastX = e.x; lastY = e.y
                    moved += abs(dx) + abs(dy)
                    if (fingers >= 2) {
                        scrollAcc += dy
                        val steps = (scrollAcc / 40).toInt()
                        if (steps != 0) {
                            scrollAcc -= steps * 40
                            input("kind" to "scroll", "dy" to -steps)
                        }
                    } else {
                        // Send integer deltas, carrying the remainder for smoothness.
                        accX += dx * speed; accY += dy * speed
                        val ix = accX.toInt(); val iy = accY.toInt()
                        if (ix != 0 || iy != 0) {
                            accX -= ix; accY -= iy
                            input("kind" to "move", "dx" to ix, "dy" to iy)
                        }
                    }
                }
                MotionEvent.ACTION_UP -> {
                    val quick = System.currentTimeMillis() - downTime < 250
                    if (quick && moved < 20) {
                        input("kind" to "click", "button" to if (fingers >= 2) "right" else "left")
                    }
                    fingers = 0
                }
            }
            return true
        }
    }
}
