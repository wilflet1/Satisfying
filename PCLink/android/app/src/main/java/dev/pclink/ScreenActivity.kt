package dev.pclink

import android.app.Activity
import android.content.Context
import android.graphics.Color
import android.graphics.SurfaceTexture
import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Bundle
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.util.Base64
import android.view.GestureDetector
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.Surface
import android.view.TextureView
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import org.json.JSONObject
import java.util.concurrent.Executors
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

/**
 * Remote desktop: the PC streams its screen as H.264 (decoded in hardware
 * here), with Chrome-Remote-Desktop-style touch: tap = click, double tap =
 * double click, long press = right click, long press then drag = drag, one
 * finger = pan, two fingers = pinch zoom and pan, two-finger swipe = scroll.
 */
class ScreenActivity : Activity() {
    private val sender = Executors.newSingleThreadExecutor()
    private val frames = LinkedBlockingQueue<JSONObject>(30)
    @Volatile private var running = false
    private var monitor = 0
    private var monitors = 1
    private lateinit var view: ScreenView
    private lateinit var keys: EditText

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
        root.addView(Ui.grid(this, listOf(
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
                startStream()
            },
        ), perRow = 6))
        setContentView(root)
    }

    override fun onResume() {
        super.onResume()
        current = this
        running = true
        thread(name = "pclink-video") { decodeLoop() }
        startStream()
    }

    override fun onPause() {
        current = null
        running = false
        LinkService.current?.let { l -> sender.execute { l.post("screen.stop", JSONObject()) } }
        super.onPause()
    }

    override fun onDestroy() {
        sender.shutdown()
        super.onDestroy()
    }

    private fun startStream() {
        frames.clear()
        Thread {
            try {
                val link = LinkService.await(applicationContext)
                val dm = resources.displayMetrics
                val maxW = maxOf(dm.widthPixels, dm.heightPixels).coerceIn(1280, 1920)
                // The relay is slow (and runs on your web hosting): keep it light there.
                val bitrate = if (link.viaRelay) 1_000_000 else 6_000_000
                val fps = if (link.viaRelay) 15 else 30
                val res = link.request("screen.start", jsonOf("monitor" to monitor, "max_w" to maxW, "fps" to fps, "bitrate" to bitrate), 15_000)
                monitors = res.optInt("monitors", 1)
            } catch (e: Exception) {
                runOnUiThread { Ui.toast(this, e.message ?: "Screen unavailable") }
            }
        }.start()
    }

    /** Frames from the PC (link reader thread): queue for the decoder and ack. */
    private fun onVideo(data: JSONObject) {
        if (!frames.offer(data)) {
            // Decoder fell behind: drop the backlog and restart from a keyframe.
            frames.clear()
            LinkService.current?.event("screen.key", JSONObject())
        }
        LinkService.current?.event("screen.ack", jsonOf("seq" to data.optLong("seq")))
    }

    private fun decodeLoop() {
        var codec: MediaCodec? = null
        var size = 0 to 0
        var pts = 0L
        val info = MediaCodec.BufferInfo()
        fun release() {
            try { codec?.stop() } catch (_: Exception) {}
            try { codec?.release() } catch (_: Exception) {}
            codec = null
        }
        while (running) {
            val f = frames.poll(200, TimeUnit.MILLISECONDS) ?: continue
            val surface = view.surface ?: continue
            val w = f.optInt("w")
            val h = f.optInt("h")
            try {
                if (codec == null || size != w to h) {
                    release()
                    size = w to h
                    runOnUiThread { view.setVideoSize(w, h) }
                    codec = MediaCodec.createDecoderByType("video/avc").apply {
                        val fmt = MediaFormat.createVideoFormat("video/avc", w, h).apply {
                            // Show each frame as soon as it's decoded instead of
                            // holding a few back (that froze still screens).
                            setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
                            setInteger("vendor.qti-ext-dec-low-latency.enable", 1)
                        }
                        configure(fmt, surface, null, 0)
                        start()
                    }
                    LinkService.current?.event("screen.key", JSONObject())
                }
                val c = codec!!
                val bytes = Base64.decode(f.getString("data"), Base64.DEFAULT)
                drain(c, info, 0)
                val idx = c.dequeueInputBuffer(100_000)
                if (idx >= 0) {
                    c.getInputBuffer(idx)!!.apply { clear(); put(bytes) }
                    c.queueInputBuffer(idx, 0, bytes.size, pts, 0)
                    pts += 33_333
                } else {
                    // Dropping a frame breaks the ones after it: resync.
                    LinkService.current?.event("screen.key", JSONObject())
                }
                // Give the decoder a moment so this frame shows now, not with the next one.
                drain(c, info, 15_000)
            } catch (e: Exception) {
                release()
                LinkService.current?.event("screen.key", JSONObject())
            }
        }
        release()
    }

    /** Renders every decoded frame that's ready (waiting up to [timeoutUs] for the first). */
    private fun drain(c: MediaCodec, info: MediaCodec.BufferInfo, timeoutUs: Long) {
        var wait = timeoutUs
        while (true) {
            val out = c.dequeueOutputBuffer(info, wait)
            if (out >= 0) c.releaseOutputBuffer(out, true)
            else if (out != MediaCodec.INFO_OUTPUT_FORMAT_CHANGED && out != MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED) break
            wait = 0
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

    /** Shows the video with zoom/pan and turns touches into pointer actions. */
    private inner class ScreenView(ctx: Context) : FrameLayout(ctx) {
        private val tex = TextureView(ctx)
        @Volatile var surface: Surface? = null
            private set
        private var vw = 0
        private var vh = 0
        private var scale = 1f
        private var tx = 0f
        private var ty = 0f
        private var dragging = false
        /** Left button held on the PC (the hold turned into a drag). */
        private var pressed = false
        private var rightDone = false
        private var holdX = 0f
        private var holdY = 0f
        private val slop = android.view.ViewConfiguration.get(ctx).scaledTouchSlop
        private val rightClick = Runnable {
            if (dragging && !pressed) {
                frac(holdX, holdY)?.let { (fx, fy) -> pointer("right", fx, fy) }
                rightDone = true
                performHapticFeedback(android.view.HapticFeedbackConstants.LONG_PRESS)
            }
        }
        private var scrollAcc = 0f

        init {
            addView(tex, LayoutParams(1, 1))
            tex.pivotX = 0f
            tex.pivotY = 0f
            tex.surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                override fun onSurfaceTextureAvailable(st: SurfaceTexture, w: Int, h: Int) { surface = Surface(st) }
                override fun onSurfaceTextureSizeChanged(st: SurfaceTexture, w: Int, h: Int) {}
                override fun onSurfaceTextureDestroyed(st: SurfaceTexture): Boolean { surface?.release(); surface = null; return true }
                override fun onSurfaceTextureUpdated(st: SurfaceTexture) {}
            }
        }

        fun setVideoSize(w: Int, h: Int) {
            vw = w; vh = h
            tex.layoutParams = LayoutParams(w, h)
            apply()
        }

        fun reset() {
            scale = 1f; tx = 0f; ty = 0f
            apply()
        }

        override fun onSizeChanged(w: Int, h: Int, ow: Int, oh: Int) = apply()

        private fun fit() = if (vw == 0) 1f else minOf(width / vw.toFloat(), height / vh.toFloat())

        private fun apply() {
            if (vw == 0) return
            scale = scale.coerceIn(1f, 8f)
            val s = fit() * scale
            val w = vw * s
            val h = vh * s
            tx = if (w <= width) (width - w) / 2 else tx.coerceIn(width - w, 0f)
            ty = if (h <= height) (height - h) / 2 else ty.coerceIn(height - h, 0f)
            tex.scaleX = s; tex.scaleY = s
            tex.translationX = tx; tex.translationY = ty
        }

        /** View point -> screen fraction (0..1). */
        private fun frac(x: Float, y: Float): Pair<Float, Float>? {
            if (vw == 0) return null
            val s = fit() * scale
            return ((x - tx) / s / vw).coerceIn(0f, 1f) to ((y - ty) / s / vh).coerceIn(0f, 1f)
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
                apply()
                return true
            }
        })

        private val gestures = GestureDetector(ctx, object : GestureDetector.SimpleOnGestureListener() {
            override fun onSingleTapConfirmed(e: MotionEvent): Boolean { act("click", e); return true }
            override fun onDoubleTap(e: MotionEvent): Boolean { act("double", e); return true }
            override fun onLongPress(e: MotionEvent) {
                // Hold: keep still for 2 s for a right click, or move to drag.
                dragging = true
                pressed = false
                rightDone = false
                holdX = e.x; holdY = e.y
                performHapticFeedback(android.view.HapticFeedbackConstants.KEYBOARD_TAP)
                postDelayed(rightClick, 2000L - android.view.ViewConfiguration.getLongPressTimeout())
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
                apply()
                return true
            }
        })

        override fun onTouchEvent(e: MotionEvent): Boolean {
            scaler.onTouchEvent(e)
            gestures.onTouchEvent(e)
            if (dragging) {
                when (e.actionMasked) {
                    MotionEvent.ACTION_MOVE -> {
                        if (!pressed && !rightDone && kotlin.math.hypot(e.x - holdX, e.y - holdY) > slop) {
                            // Moved before the right click: drag from where the hold began.
                            removeCallbacks(rightClick)
                            pressed = true
                            frac(holdX, holdY)?.let { (fx, fy) -> pointer("down", fx, fy) }
                        }
                        if (pressed) act("move", e)
                    }
                    MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                        removeCallbacks(rightClick)
                        when {
                            pressed -> act("up", e)
                            // Let go before 2 s without moving: an ordinary click.
                            !rightDone && e.actionMasked == MotionEvent.ACTION_UP -> act("click", e)
                        }
                        dragging = false
                        pressed = false
                    }
                }
            }
            if (e.actionMasked == MotionEvent.ACTION_UP) scrollAcc = 0f
            return true
        }
    }

    companion object {
        @Volatile private var current: ScreenActivity? = null

        /** `screen.video` events from the PC. */
        fun dispatch(data: JSONObject) {
            current?.onVideo(data)
        }
    }
}
