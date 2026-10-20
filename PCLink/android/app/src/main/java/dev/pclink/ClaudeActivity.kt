package dev.pclink

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.view.View
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import org.json.JSONArray
import org.json.JSONObject
import java.text.DateFormat
import java.util.Date

/**
 * "Claude, do X on my PC": compose a task, watch its progress live, answer
 * follow-ups. Tasks run Claude Code on the PC (see the agent's tasks.rs).
 */
class ClaudeActivity : Activity() {
    private lateinit var root: LinearLayout
    private val main = Handler(Looper.getMainLooper())
    /** Task shown in detail mode, or null for the list. */
    private var taskId: Long? = null
    private var nextSeq = 0
    private var eventsBox: LinearLayout? = null
    private var statusText: TextView? = null
    private var stopButton: View? = null
    private var followUp: LinearLayout? = null
    private var approvalBox: LinearLayout? = null
    /** Approval currently shown, so polling doesn't rebuild it. */
    private var shownApproval: String? = null
    private var draft = ""
    private var ask = true
    private val poll = object : Runnable {
        override fun run() {
            taskId?.let { refreshTask(it) }
            main.postDelayed(this, 4000)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        root = Ui.page(this)
        taskId = intent.getLongExtra(EXTRA_TASK, -1).takeIf { it > 0 }
        render()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        taskId = intent.getLongExtra(EXTRA_TASK, -1).takeIf { it > 0 }
        render()
    }

    override fun onResume() {
        super.onResume()
        current = this
        main.post(poll)
    }

    override fun onPause() {
        current = null
        main.removeCallbacks(poll)
        super.onPause()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (taskId != null) {
            taskId = null
            render()
        } else {
            @Suppress("DEPRECATION")
            super.onBackPressed()
        }
    }

    private fun render() {
        root.removeAllViews()
        eventsBox = null
        statusText = null
        stopButton = null
        followUp = null
        approvalBox = null
        shownApproval = null
        val id = taskId
        if (id == null) renderCompose() else renderTask(id)
    }

    // ------------------------------------------------------------ compose

    private fun renderCompose() {
        root.addView(Ui.text(this, "Claude on your PC", 24f, bold = true))
        root.addView(Ui.text(this, "Tell Claude what to do on ${Prefs(this).pcName.ifEmpty { "your PC" }}. It runs Claude Code there and can use the screen, apps and command line.", 14f, muted = true).apply {
            setPadding(0, 0, 0, Ui.dp(this@ClaudeActivity, 12))
        })
        val card = Ui.card(this)
        val prompt = EditText(this).apply {
            hint = "What should Claude do?"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            minLines = 3
            maxLines = 8
            gravity = android.view.Gravity.TOP or android.view.Gravity.START
            setText(draft)
            addTextChangedListener(object : android.text.TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun afterTextChanged(e: android.text.Editable?) { draft = e?.toString() ?: "" }
            })
        }
        card.addView(prompt)
        card.addView(Ui.text(this, "Tip: tap the mic on your keyboard to dictate.", 12f, muted = true))
        card.addView(Switch(this).apply {
            text = "Ask me before it runs commands or changes files"
            isChecked = ask
            setPadding(0, Ui.dp(this@ClaudeActivity, 8), 0, Ui.dp(this@ClaudeActivity, 4))
            setOnCheckedChangeListener { _, on -> ask = on }
        })
        card.addView(Ui.button(this, "Run on PC", primary = true) {
            val text = prompt.text.toString().trim()
            if (text.isEmpty()) {
                Ui.toast(this, "Type what Claude should do")
                return@button
            }
            start(text, null)
        })
        root.addView(card)

        // One tap, no approval prompts: these run start to finish on their own.
        val quick = Ui.card(this, "One tap")
        quick.addView(Ui.button(this, "🎮  Tarkov: collect flea sales", primary = true) {
            start(TARKOV_FLEA, null, ask = false)
        })
        root.addView(quick)

        val examples = Ui.card(this, "Try")
        for (ex in EXAMPLES) {
            examples.addView(Ui.listRow(this, ex, null) {
                prompt.setText(ex)
                prompt.setSelection(ex.length)
            })
        }
        root.addView(examples)

        val recent = Ui.card(this, "Recent tasks")
        val box = Ui.column(this)
        box.addView(Ui.text(this, "Loading…", 13f, muted = true))
        recent.addView(box)
        root.addView(recent)
        loadRecent(box)
    }

    private fun loadRecent(box: LinearLayout) {
        Thread {
            val list = try {
                LinkService.await(applicationContext).requestArray("tasks.list")
            } catch (e: Exception) {
                runOnUiThread {
                    box.removeAllViews()
                    box.addView(Ui.text(this, e.message ?: "Not connected", 13f, muted = true))
                }
                return@Thread
            }
            runOnUiThread {
                box.removeAllViews()
                if (list.length() == 0) box.addView(Ui.text(this, "No tasks yet.", 13f, muted = true))
                val fmt = DateFormat.getTimeInstance(DateFormat.SHORT)
                for (i in 0 until list.length()) {
                    val t = list.getJSONObject(i)
                    val label = if (t.has("pending") && !t.isNull("pending")) "🔐 Needs your OK" else statusLabel(t.optString("status"))
                    val detail = "$label · ${fmt.format(Date(t.optLong("created")))}" +
                        (t.optDouble("cost_usd", 0.0).takeIf { it > 0 }?.let { " · $%.2f".format(it) } ?: "")
                    box.addView(Ui.listRow(this, t.optString("prompt"), detail) { open(t.getLong("id")) })
                }
            }
        }.start()
    }

    private fun start(prompt: String, resume: Long?, ask: Boolean = this.ask) {
        Ui.toast(this, "Sending to your PC…")
        Thread {
            try {
                val res = LinkService.await(applicationContext).request(
                    "tasks.start",
                    jsonOf("prompt" to prompt, "resume" to resume, "ask" to ask),
                )
                draft = ""
                runOnUiThread { open(res.getLong("id")) }
            } catch (e: Exception) {
                Ui.toast(this, e.message ?: "Couldn't start the task")
            }
        }.start()
    }

    private fun open(id: Long) {
        taskId = id
        render()
    }

    // ------------------------------------------------------------ task view

    private fun renderTask(id: Long) {
        nextSeq = 0
        root.addView(Ui.button(this, "← All tasks") { taskId = null; render() })
        val status = Ui.text(this, "Loading…", 14f, muted = true)
        statusText = status
        root.addView(status)
        val box = Ui.column(this).apply { setPadding(0, Ui.dp(this@ClaudeActivity, 8), 0, 0) }
        eventsBox = box
        root.addView(box)
        val approval = Ui.column(this)
        approvalBox = approval
        root.addView(approval)
        val stop = Ui.button(this, "■  Stop") {
            Ui.background(applicationContext) {
                LinkService.require().request("tasks.stop", jsonOf("id" to id))
                "Stopping…"
            }
        }.apply { visibility = View.GONE }
        stopButton = stop
        root.addView(stop)

        val follow = Ui.card(this, "Follow up")
        val input = EditText(this).apply {
            hint = "Tell Claude what to do next"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            maxLines = 5
        }
        follow.addView(input)
        follow.addView(Ui.button(this, "Send", primary = true) {
            val text = input.text.toString().trim()
            if (text.isNotEmpty()) start(text, id)
        })
        follow.visibility = View.GONE
        followUp = follow
        root.addView(follow)
        refreshTask(id)
    }

    private fun refreshTask(id: Long) {
        val since = nextSeq
        Thread {
            val res = try {
                LinkService.await(applicationContext).request("tasks.get", jsonOf("id" to id, "since" to since))
            } catch (e: Exception) {
                runOnUiThread { statusText?.text = e.message ?: "Not connected" }
                return@Thread
            }
            runOnUiThread {
                if (taskId != id) return@runOnUiThread
                val task = res.getJSONObject("task")
                if (since == 0) showPrompt(task.optString("prompt"))
                showEvents(res.optJSONArray("events") ?: JSONArray())
                showStatus(task.optString("status"), task.optDouble("cost_usd", 0.0))
                showApproval(id, task.optJSONObject("pending"))
            }
        }.start()
    }

    private fun showPrompt(prompt: String) {
        val box = eventsBox ?: return
        if (box.childCount > 0) return
        box.addView(bubble(prompt, you = true))
    }

    private fun showStatus(status: String, cost: Double) {
        val running = status == "running"
        statusText?.text = statusLabel(status) + if (cost > 0) " · $%.2f".format(cost) else ""
        stopButton?.visibility = if (running) View.VISIBLE else View.GONE
        followUp?.visibility = if (running) View.GONE else View.VISIBLE
    }

    private fun showEvents(events: JSONArray) {
        val box = eventsBox ?: return
        var added = false
        for (i in 0 until events.length()) {
            val e = events.getJSONObject(i)
            val seq = e.optInt("seq")
            if (seq < nextSeq) continue
            nextSeq = seq + 1
            val text = e.optString("text")
            val view = when (e.optString("kind")) {
                "text", "result" -> bubble(text, you = false)
                "tool" -> line("▸ $text", mono = true)
                "approval" -> line("🔐 $text")
                "error" -> line("⚠ $text", color = Color.rgb(198, 47, 58))
                else -> line(text)
            }
            box.addView(view)
            added = true
        }
        if (added) (root.parent as? ScrollView)?.let { s -> s.post { s.fullScroll(View.FOCUS_DOWN) } }
    }

    /** "Allow Bash?" with the answer buttons, right in the chat. */
    private fun showApproval(id: Long, pending: JSONObject?) {
        val box = approvalBox ?: return
        val key = pending?.toString()
        if (key == shownApproval) return
        shownApproval = key
        box.removeAllViews()
        if (pending == null) return
        val card = Ui.card(this)
        card.background = GradientDrawable().apply {
            cornerRadius = Ui.dp(this@ClaudeActivity, 14).toFloat()
            setColor(Ui.cardColor(this@ClaudeActivity))
            setStroke(Ui.dp(this@ClaudeActivity, 2), Ui.accent(this@ClaudeActivity))
        }
        card.addView(Ui.text(this, "🔐 " + pending.optString("question", "Allow this?"), 17f, bold = true))
        pending.optString("detail").takeIf { it.isNotBlank() }?.let { detail ->
            card.addView(Ui.text(this, detail, 13f, muted = true).apply {
                typeface = Typeface.MONOSPACE
                maxLines = 12
                ellipsize = android.text.TextUtils.TruncateAt.END
                setTextIsSelectable(true)
                setPadding(0, Ui.dp(this@ClaudeActivity, 6), 0, Ui.dp(this@ClaudeActivity, 6))
            })
        }
        val options = pending.optJSONArray("options") ?: JSONArray()
        val buttons = (0 until options.length()).map { i ->
            val opt = options.getString(i)
            Ui.button(this, opt, primary = i == 0) { answer(id, pending.optLong("approval"), opt) }
        }
        // "Allow all for this task" is long: Allow + Deny on a row, it on its own.
        val short = buttons.filterIndexed { i, _ -> options.getString(i).length <= 10 }
        val long = buttons - short.toSet()
        if (short.isNotEmpty()) card.addView(Ui.grid(this, short, short.size))
        long.forEach { card.addView(Ui.grid(this, listOf(it), 1)) }
        box.addView(card)
        (root.parent as? ScrollView)?.let { s -> s.post { s.fullScroll(View.FOCUS_DOWN) } }
    }

    private fun answer(id: Long, approval: Long, option: String) {
        approvalBox?.removeAllViews()
        shownApproval = null
        Thread {
            try {
                val res = LinkService.await(applicationContext).request("tasks.answer", jsonOf("id" to id, "approval" to approval, "option" to option))
                if (!res.optBoolean("answered")) Ui.toast(this, "That question was already answered")
            } catch (e: Exception) {
                Ui.toast(this, e.message ?: "Couldn't send your answer")
            }
            runOnUiThread { if (taskId == id) refreshTask(id) }
        }.start()
    }

    private fun bubble(text: String, you: Boolean) = TextView(this).apply {
        this.text = text
        textSize = 15f
        setTextIsSelectable(true)
        val p = Ui.dp(this@ClaudeActivity, 12)
        setPadding(p, Ui.dp(this@ClaudeActivity, 10), p, Ui.dp(this@ClaudeActivity, 10))
        background = GradientDrawable().apply {
            cornerRadius = Ui.dp(this@ClaudeActivity, 14).toFloat()
            setColor(if (you) Ui.accent(this@ClaudeActivity) else Ui.cardColor(this@ClaudeActivity))
        }
        if (you) setTextColor(Color.WHITE)
        layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply {
            topMargin = Ui.dp(this@ClaudeActivity, 6)
            bottomMargin = Ui.dp(this@ClaudeActivity, 6)
            if (you) leftMargin = Ui.dp(this@ClaudeActivity, 40) else rightMargin = Ui.dp(this@ClaudeActivity, 24)
        }
    }

    private fun line(text: String, mono: Boolean = false, color: Int? = null) = TextView(this).apply {
        this.text = text
        textSize = 13f
        maxLines = 3
        ellipsize = android.text.TextUtils.TruncateAt.END
        setTextColor(color ?: Ui.muted(this@ClaudeActivity))
        if (mono) typeface = Typeface.MONOSPACE
        setPadding(Ui.dp(this@ClaudeActivity, 4), Ui.dp(this@ClaudeActivity, 2), 0, Ui.dp(this@ClaudeActivity, 2))
    }

    /** Live updates pushed by the PC. */
    private fun onTaskEvent(data: JSONObject) {
        val id = data.optLong("id")
        if (id != taskId) return
        val event = data.getJSONObject("event")
        showEvents(JSONArray().put(event))
        showStatus(data.optString("status"), 0.0)
        // Fetch the pending question (or clear the answered one) right away.
        if (event.optString("kind") == "approval" || data.optString("status") != "running") refreshTask(id)
    }

    companion object {
        const val EXTRA_TASK = "task"
        @Volatile private var current: ClaudeActivity? = null

        val EXAMPLES = listOf(
            "Move my browser window to the main screen and fullscreen the YouTube video",
            "Open Blender and set up the Blender MCP server so you can control it",
            "Free up disk space: list the biggest folders in Downloads and tell me what's safe to delete",
        )

        const val TARKOV_FLEA = """Collect my Escape from Tarkov flea market sales.
1. If Escape from Tarkov is already running, focus it and skip to step 4.
2. Open the Battlestate Games Launcher ("C:\Battlestate Games\BsgLauncher\BsgLauncher.exe") and wait for it to load. If it needs an update or a login, stop and tell me.
3. Make sure Escape from Tarkov is selected in its regular online PvP mode (NOT PvE, NOT Arena), then press Play. Wait for the game to reach the main menu (this can take a few minutes), dismissing any news pop-ups.
4. Open the Messenger, go through every chat with unreceived items (flea market sales arrive from the "Flea Market" sender) and use "Receive all" / take the roubles and items into my stash. Don't buy, sell, list or move anything else.
5. Leave the game open at the main menu and tell me how many sales you collected and the total roubles, if visible. Use notify_phone when done."""

        fun statusLabel(status: String) = when (status) {
            "running" -> "⏳ Working"
            "done" -> "✅ Done"
            "stopped" -> "⏹ Stopped"
            "failed" -> "❌ Failed"
            else -> status
        }

        fun dispatch(data: JSONObject) {
            val a = current ?: return
            a.runOnUiThread { a.onTaskEvent(data) }
        }

        fun intent(ctx: Context, task: Long? = null): Intent =
            Intent(ctx, ClaudeActivity::class.java).apply { if (task != null) putExtra(EXTRA_TASK, task) }
    }
}
