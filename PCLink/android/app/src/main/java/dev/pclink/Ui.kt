package dev.pclink

import android.app.Activity
import android.content.Context
import android.content.res.ColorStateList
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import kotlin.concurrent.thread

/** Tiny helpers for building the UI in code (no XML layouts, no AndroidX). */
object Ui {
    fun dp(ctx: Context, v: Int): Int =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), ctx.resources.displayMetrics).toInt()

    fun isDark(ctx: Context): Boolean =
        ctx.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES

    fun accent(ctx: Context) = if (isDark(ctx)) Color.rgb(91, 140, 255) else Color.rgb(47, 111, 237)
    fun cardColor(ctx: Context) = if (isDark(ctx)) Color.rgb(32, 37, 44) else Color.WHITE
    fun bgColor(ctx: Context) = if (isDark(ctx)) Color.rgb(17, 20, 24) else Color.rgb(244, 246, 249)
    fun muted(ctx: Context) = if (isDark(ctx)) Color.rgb(152, 163, 177) else Color.rgb(95, 107, 122)

    /**
     * Android 15 draws apps edge to edge, under the status and navigation
     * bars. Pad [v] by the bars (and the keyboard, when it's open) so
     * nothing ends up behind them.
     */
    fun fitSystemBars(v: View) {
        val l = v.paddingLeft
        val t = v.paddingTop
        val r = v.paddingRight
        val b = v.paddingBottom
        v.setOnApplyWindowInsetsListener { view, insets ->
            val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
            val ime = insets.getInsets(WindowInsets.Type.ime())
            view.setPadding(l + bars.left, t + bars.top, r + bars.right, b + maxOf(bars.bottom, ime.bottom))
            insets
        }
        v.requestApplyInsets()
    }

    /** A scrolling page; returns the column to add content to. */
    fun page(activity: Activity): LinearLayout {
        val col = column(activity).apply {
            val p = dp(activity, 16)
            setPadding(p, p, p, dp(activity, 32))
        }
        val scroll = ScrollView(activity).apply {
            setBackgroundColor(bgColor(activity))
            isFillViewport = true
            clipToPadding = false
            addView(col)
        }
        fitSystemBars(scroll)
        activity.setContentView(scroll)
        return col
    }

    fun column(ctx: Context) = LinearLayout(ctx).apply { orientation = LinearLayout.VERTICAL }

    fun row(ctx: Context) = LinearLayout(ctx).apply {
        orientation = LinearLayout.HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        // Otherwise buttons with one and two lines of text sit at different heights.
        isBaselineAligned = false
    }

    fun card(ctx: Context, title: String? = null): LinearLayout = column(ctx).apply {
        val p = dp(ctx, 14)
        setPadding(p, p, p, p)
        background = GradientDrawable().apply {
            setColor(cardColor(ctx))
            cornerRadius = dp(ctx, 16).toFloat()
        }
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(ctx, 12)
        }
        if (title != null) addView(text(ctx, title, 17f, bold = true).apply { setPadding(0, 0, 0, dp(ctx, 6)) })
    }

    fun text(ctx: Context, s: String, size: Float = 15f, bold: Boolean = false, muted: Boolean = false) = TextView(ctx).apply {
        text = s
        textSize = size
        if (bold) typeface = Typeface.DEFAULT_BOLD
        if (muted) setTextColor(muted(ctx))
    }

    private fun chipColor(ctx: Context) = if (isDark(ctx)) Color.rgb(44, 51, 61) else Color.rgb(234, 239, 246)
    private fun textColor(ctx: Context) = if (isDark(ctx)) Color.rgb(231, 235, 240) else Color.rgb(27, 31, 36)

    private fun rounded(ctx: Context, color: Int, radius: Int) = RippleDrawable(
        ColorStateList.valueOf(if (isDark(ctx)) 0x33FFFFFF else 0x22000000),
        GradientDrawable().apply {
            setColor(color)
            cornerRadius = dp(ctx, radius).toFloat()
        },
        null,
    )

    /**
     * A full-width button. No minimum width (the platform default of 88dp
     * pushes rows of small buttons off screen); long labels wrap to two lines.
     */
    fun button(ctx: Context, label: String, primary: Boolean = false, onClick: (View) -> Unit) = Button(ctx).apply {
        text = label
        isAllCaps = false
        textSize = if (primary) 16f else 14f
        minWidth = 0
        minimumWidth = 0
        minHeight = dp(ctx, if (primary) 56 else 46)
        minimumHeight = minHeight
        maxLines = 2
        ellipsize = TextUtils.TruncateAt.END
        gravity = Gravity.CENTER
        stateListAnimator = null
        val h = dp(ctx, 10)
        setPadding(h, dp(ctx, 8), h, dp(ctx, 8))
        if (primary) {
            setTextColor(Color.WHITE)
            typeface = Typeface.DEFAULT_BOLD
            background = rounded(ctx, accent(ctx), 14)
        } else {
            setTextColor(textColor(ctx))
            background = rounded(ctx, chipColor(ctx), 12)
        }
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(ctx, 4)
            bottomMargin = dp(ctx, 4)
        }
        setOnClickListener(onClick)
    }

    /** Equal-width, equal-height cells in rows of [perRow], with even gaps. */
    fun grid(ctx: Context, views: List<View>, perRow: Int = 2): LinearLayout = column(ctx).apply {
        val gap = dp(ctx, 3)
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        for (chunk in views.chunked(perRow)) {
            val r = row(ctx)
            for (v in chunk) {
                r.addView(v, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
                    setMargins(gap, gap, gap, gap)
                })
            }
            repeat(perRow - chunk.size) {
                r.addView(View(ctx), LinearLayout.LayoutParams(0, 1, 1f).apply { setMargins(gap, gap, gap, gap) })
            }
            addView(r, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                leftMargin = -gap
                rightMargin = -gap
            })
        }
    }

    /** Buttons side by side, sharing the width. */
    fun buttonRow(ctx: Context, vararg buttons: View): LinearLayout = grid(ctx, buttons.toList(), buttons.size)

    /** A tappable list row with a title and an optional detail line. */
    fun listRow(ctx: Context, title: String, detail: String?, onClick: () -> Unit) = LinearLayout(ctx).apply {
        orientation = LinearLayout.VERTICAL
        val p = dp(ctx, 12)
        setPadding(p, dp(ctx, 10), p, dp(ctx, 10))
        minimumHeight = dp(ctx, 48)
        gravity = Gravity.CENTER_VERTICAL
        background = rounded(ctx, Color.TRANSPARENT, 10)
        isClickable = true
        setOnClickListener { onClick() }
        addView(text(ctx, title, 15f).apply {
            maxLines = 2
            ellipsize = TextUtils.TruncateAt.MIDDLE
        })
        if (detail != null) addView(text(ctx, detail, 12f, muted = true))
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
    }

    fun toast(ctx: Context, msg: String) {
        android.os.Handler(android.os.Looper.getMainLooper()).post {
            Toast.makeText(ctx.applicationContext, msg, Toast.LENGTH_SHORT).show()
        }
    }

    /** Runs [work] off the main thread and toasts its message or error. */
    fun background(ctx: Context, work: () -> String?) {
        thread {
            val msg = try {
                work()
            } catch (e: Exception) {
                e.message ?: e.toString()
            }
            if (msg != null) toast(ctx, msg)
        }
    }

    fun size(bytes: Long): String {
        if (bytes < 1024) return "$bytes B"
        val units = listOf("KB", "MB", "GB", "TB")
        var v = bytes / 1024.0
        var i = 0
        while (v >= 1024 && i < units.size - 1) {
            v /= 1024
            i++
        }
        return "%.1f %s".format(v, units[i])
    }
}
