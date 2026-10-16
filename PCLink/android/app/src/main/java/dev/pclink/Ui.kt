package dev.pclink

import android.app.Activity
import android.content.Context
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
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

    fun page(activity: Activity): LinearLayout {
        val col = column(activity).apply {
            val p = dp(activity, 16)
            setPadding(p, p, p, dp(activity, 40))
        }
        val scroll = ScrollView(activity).apply {
            setBackgroundColor(bgColor(activity))
            isFillViewport = true
            addView(col)
        }
        activity.setContentView(scroll)
        return col
    }

    fun column(ctx: Context) = LinearLayout(ctx).apply { orientation = LinearLayout.VERTICAL }

    fun row(ctx: Context) = LinearLayout(ctx).apply {
        orientation = LinearLayout.HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
    }

    fun card(ctx: Context, title: String? = null): LinearLayout = column(ctx).apply {
        val p = dp(ctx, 16)
        setPadding(p, p, p, p)
        background = GradientDrawable().apply {
            setColor(cardColor(ctx))
            cornerRadius = dp(ctx, 16).toFloat()
        }
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            bottomMargin = dp(ctx, 12)
        }
        if (title != null) addView(text(ctx, title, 17f, bold = true).apply { setPadding(0, 0, 0, dp(ctx, 8)) })
    }

    fun text(ctx: Context, s: String, size: Float = 15f, bold: Boolean = false, muted: Boolean = false) = TextView(ctx).apply {
        text = s
        textSize = size
        if (bold) typeface = Typeface.DEFAULT_BOLD
        if (muted) setTextColor(muted(ctx))
    }

    fun button(ctx: Context, label: String, primary: Boolean = false, onClick: (View) -> Unit) = Button(ctx).apply {
        text = label
        isAllCaps = false
        textSize = 15f
        if (primary) {
            setTextColor(Color.WHITE)
            background = GradientDrawable().apply {
                setColor(accent(ctx))
                cornerRadius = dp(ctx, 12).toFloat()
            }
            minHeight = dp(ctx, 56)
        }
        setOnClickListener(onClick)
    }

    /** Lays buttons out in rows of [perRow] equal-width cells. */
    fun grid(ctx: Context, views: List<View>, perRow: Int = 2): LinearLayout = column(ctx).apply {
        for (chunk in views.chunked(perRow)) {
            val r = row(ctx)
            for (v in chunk) r.addView(v, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            repeat(perRow - chunk.size) { r.addView(View(ctx), LinearLayout.LayoutParams(0, 1, 1f)) }
            addView(r)
        }
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
