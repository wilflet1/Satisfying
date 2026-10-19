package dev.pclink

import android.app.Activity
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.os.Bundle
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.util.Base64
import android.view.GestureDetector
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.LinearLayout
import java.util.concurrent.Executors
import kotlin.concurrent.thread

/**
 * Remote desktop: the PC's screen as a stream of JPEG frames, with
 * Chrome-Remote-Desktop-style touch: tap = click, double tap = double click,
 * long press = right click, long press then drag = drag, one finger = pan,
 * two fingers = pinch zoom and pan, two-finger vertical swipe = scroll.
 */
class ScreenActivity : Activity() {
    private companion object {
        const val PIPELINE = 3
    }

    private val sender = Executors.newSingleThreadExecutor()
    @Volatile private var running = false
    private var monitor = 0
    private var monitors = 1
    private lateinit var view: ScreenView
    private lateinit var keys: EditText
    private var lastFrame = 0L

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val root = Ui.column(this).apply { setBackgroundColor(Color.BLACK) }
        Ui.fitSystemBars(root)
        view = ScreenView(this)
        root.addView(view, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))
        // Hidden field that receives the soft keyboard's typing.
        keys = object : EditText(this) {
            override fun onKeyDown(code: Int, e: KeyEvent): Boolean {
                if (code == KeyEvent.KEYCODE_DEL) { key("backspace"); return true }
                if (code == KeyEvent.KEYCODE_ENTER) { key("enter"); return true }
                return super.onKeyDown(code, e)
            }
        }.apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            imeOptions = EditorInfo.IME_FLAG_NO_EXTRACT_UI or EditorInfo.IME_ACTION_NONE
            setText(" ")
            alpha = 0f
            addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun afterTextChanged(e: Editable) {
                    // Keep one placeholder char so backspace always has something to delete.
                    val t = e.toString()
                    if (t == " ") return
                    if (t.isEmpty()) key("backspace") else t.removePrefix(" ").takeIf { it.isNotEmpty() }?.let { typed ->
                        if (typed == "\n") key("enter") else input("kind" to "text", "text" to typed)
                    }
                    removeTextChangedListener(this)
                    setText(" ")
                    setSelection(1)
                    addTextChangedListener(this)
                }
            })
        }
        root.addView(keys, LinearLayout.LayoutParams(1, 1))
        val bar = Ui.grid(this, listOf(
            Ui.button(this, "⌨") {
                keys.requestFocus()
                getSystemService(InputMethodManager::class.java).showSoftInput(keys, 0)
            },
            Ui.button(this, "Esc") { key("esc") },
            Ui.button(this, "Tab") { key("tab") },
            Ui.button(this, "Win") { key("win") },
            Ui.button(this, "Ctrl+Alt+Del") { key("delete", "ctrl", "alt") },
            Ui.button(this, "🖥") {
                monitor = (monitor + 1) % monitors.coerceAtLeast(1)
                view.reset()
            },
        ), perRow = 6)
        root.addView(bar)
        setContentView(root)
    }

    override fun onResume() {
        super.onResume()
        running = true
        // Several requests in flight hide the round trip and let the PC
        // capture and encode in parallel: this is what gets to 30+ fps.
        repeat(PIPELINE) { thread(name = "pclink-screen-$it") { frameLoop() } }
    }

    override fun onPause() {
        running = false
        super.onPause()
    }

    override fun onDestroy() {
        sender.shutdown()
        super.onDestroy()
    }

    /** Asks for the next frame as soon as the last one arrives (natural backpressure). */
    private fun frameLoop() {
        while (running) {
            try {
                val link = LinkService.await(applicationContext)
                // Zoomed in: ask for more pixels so text stays sharp.
                val dm = resources.displayMetrics
                val maxW = (maxOf(dm.widthPixels, dm.heightPixels) * view.scale).toInt().coerceIn(960, 2560)
                val q = if (link.viaRelay) 35 else 50
                val res = link.request("screen.frame", jsonOf("monitor" to monitor, "max_w" to maxW, "quality" to q), 15_000)
                monitors = res.optInt("monitors", 1)
                val t = res.optLong("t")
                val bytes = Base64.decode(res.getString("jpeg"), Base64.DEFAULT)
                val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: continue
                // Pipelined replies can arrive out of order: keep the newest.
                // (Checked on the UI thread, where frames are shown.)
                runOnUiThread {
                    if (t >= lastFrame) {
                        lastFrame = t
                        view.show(bmp)
                    } else {
                        bmp.recycle()
                    }
                }
            } catch (e: Exception) {
                runOnUiThread { Ui.toast(this, e.message ?: "Screen unavailable") }
                Thread.sleep(2000)
            }
        }
    }

    private fun input(vararg pairs: Pair<String, Any?>) {
        val link = LinkService.current ?: return
        val args = jsonOf(*pairs)
        sender.execute { link.post("input", args) }
    }

    private fun key(key: String, vararg mods: String) =
        input("kind" to "key", "key" to key, "modifiers" to org.json.JSONArray(mods.toList()))

    private fun pointer(action: String, fx: Float, fy: Float, dy: Int = 0) {
        val link = LinkService.current ?: return
        val args = jsonOf("monitor" to monitor, "x" to fx, "y" to fy, "action" to action, "dy" to dy)
        sender.execute { link.post("screen.pointer", args) }
    }

    /** Draws the latest frame with zoom/pan and turns touches into pointer actions. */
    private inner class ScreenView(ctx: Context) : View(ctx) {
        private var bmp: Bitmap? = null
        private val m = Matrix()
        /** Zoom relative to fit-to-view. */
        var scale = 1f
            private set
        private var tx = 0f
        private var ty = 0f
        private var dragging = false
        private var scrollAcc = 0f

        fun show(b: Bitmap) {
            val old = bmp
            bmp = b
            if (old == null || old.width != b.width || old.height != b.height) clamp()
            old?.recycle()
            invalidate()
        }

        fun reset() {
            scale = 1f; tx = 0f; ty = 0f
            invalidate()
        }

        private fun fit(): Float {
            val b = bmp ?: return 1f
            return minOf(width / b.width.toFloat(), height / b.height.toFloat())
        }

        private fun clamp() {
            val b = bmp ?: return
            scale = scale.coerceIn(1f, 8f)
            val s = fit() * scale
            val w = b.width * s
            val h = b.height * s
            tx = if (w <= width) (width - w) / 2 else tx.coerceIn(width - w, 0f)
            ty = if (h <= height) (height - h) / 2 else ty.coerceIn(height - h, 0f)
        }

        override fun onDraw(c: Canvas) {
            val b = bmp ?: return
            clamp()
            val s = fit() * scale
            m.setScale(s, s)
            m.postTranslate(tx, ty)
            c.drawBitmap(b, m, null)
        }

        /** Screen point -> frame fraction (0..1). */
        private fun frac(x: Float, y: Float): Pair<Float, Float>? {
            val b = bmp ?: return null
            val s = fit() * scale
            return ((x - tx) / s / b.width).coerceIn(0f, 1f) to ((y - ty) / s / b.height).coerceIn(0f, 1f)
        }

        private fun act(action: String, e: MotionEvent) {
            frac(e.x, e.y)?.let { (fx, fy) -> pointer(action, fx, fy) }
        }

        private val scaler = ScaleGestureDetector(ctx, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
            override fun onScale(d: ScaleGestureDetector): Boolean {
                val ns = (scale * d.scaleFactor).coerceIn(1f, 8f)
                val k = ns / scale
                tx = d.focusX - (d.focusX - tx) * k
                ty = d.focusY - (d.focusY - ty) * k
                scale = ns
                invalidate()
                return true
            }
        })

        private val gestures = GestureDetector(ctx, object : GestureDetector.SimpleOnGestureListener() {
            override fun onSingleTapConfirmed(e: MotionEvent): Boolean { act("click", e); return true }
            override fun onDoubleTap(e: MotionEvent): Boolean { act("double", e); return true }
            override fun onLongPress(e: MotionEvent) {
                // Long press starts a drag; releasing without moving is a right click.
                dragging = true
                act("down", e)
                performHapticFeedback(android.view.HapticFeedbackConstants.LONG_PRESS)
            }
            override fun onScroll(e1: MotionEvent?, e2: MotionEvent, dx: Float, dy: Float): Boolean {
                if (dragging) return true
                if (e2.pointerCount >= 2 && !scaler.isInProgress) {
                    // Two-finger vertical swipe scrolls the PC (one notch per 40 px).
                    scrollAcc += dy
                    val notches = (scrollAcc / 40).toInt()
                    if (notches != 0) {
                        scrollAcc -= notches * 40
                        frac(e2.x, e2.y)?.let { (fx, fy) -> pointer("scroll", fx, fy, -notches) }
                    }
                }
                tx -= dx; ty -= dy
                invalidate()
                return true
            }
        })

        private var moved = false

        override fun onTouchEvent(e: MotionEvent): Boolean {
            scaler.onTouchEvent(e)
            gestures.onTouchEvent(e)
            if (dragging) {
                when (e.actionMasked) {
                    MotionEvent.ACTION_MOVE -> { moved = true; act("move", e) }
                    MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                        if (moved) act("up", e) else { act("up", e); act("right", e) }
                        dragging = false
                        moved = false
                    }
                }
            }
            if (e.actionMasked == MotionEvent.ACTION_UP) scrollAcc = 0f
            return true
        }
    }
}
