package dev.pclink

import android.Manifest
import android.app.Activity
import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.os.Environment
import android.os.PowerManager
import android.provider.Settings
import android.text.InputType
import android.view.View
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.Switch
import android.widget.TextView
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class MainActivity : Activity() {
    private lateinit var prefs: Prefs
    private lateinit var root: LinearLayout
    private var discovery: Discovery? = null
    private val listener: () -> Unit = { runOnUiThread { render() } }
    private var actionsBox: LinearLayout? = null
    private var speakerReadout: TextView? = null
    private val readoutTick = object : Runnable {
        override fun run() {
            updateReadout()
            root.postDelayed(this, 1000)
        }
    }

    /** Live "is audio arriving?" line under the speaker button. */
    private fun updateReadout() {
        val r = speakerReadout ?: return
        r.text = when {
            Speaker.lastError != null -> "⚠ ${Speaker.lastError}"
            !Speaker.isPlaying -> ""
            Speaker.packets == 0L -> "Waiting for audio from the PC… (0 packets). If this stays at 0, check the PC dashboard's speaker line."
            else -> "Receiving PC audio: ${Speaker.packets} packets${if (Speaker.voiceMode) " · call volume (mic on)" else ""}"
        }
    }
    /** Pairing fields survive re-renders (discovery and status updates redraw the screen). */
    private var codeText = ""
    private var hostText = ""
    /** Control-panel buttons, fetched once per connection. */
    private var actions: org.json.JSONArray? = null
    private var actionsFor: Link? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        root = Ui.page(this)
        handlePairLink(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handlePairLink(intent)
    }

    override fun onResume() {
        super.onResume()
        visible = true
        root.post(readoutTick)
        // Self-update: at launch and whenever the app returns to the front.
        Updater.checkIfDue(this)
        LinkService.listeners += listener
        if (prefs.paired) LinkService.start(this) else startDiscovery()
        val sp = getSharedPreferences("pclink", MODE_PRIVATE)
        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED &&
            !sp.getBoolean("asked_notifications", false)
        ) {
            sp.edit().putBoolean("asked_notifications", true).apply()
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        }
        render()
    }

    override fun onPause() {
        visible = false
        root.removeCallbacks(readoutTick)
        LinkService.listeners -= listener
        discovery?.stop()
        discovery = null
        super.onPause()
    }

    private fun startDiscovery() {
        if (discovery != null) return
        discovery = Discovery(this).apply {
            onFound = { runOnUiThread { render() } }
            start()
        }
    }

    // ------------------------------------------------------------- pairing

    private fun handlePairLink(intent: Intent?) {
        val uri = intent?.data ?: return
        if (uri.scheme != "pclink" || uri.host != "pair") return
        val hosts = (uri.getQueryParameter("h") ?: "").split(',').filter { it.isNotBlank() }
        val port = uri.getQueryParameter("p")?.toIntOrNull() ?: 47810
        val fp = uri.getQueryParameter("fp") ?: return
        val code = uri.getQueryParameter("c") ?: return
        // The beacon may know a fresher address than the QR code.
        pair(hosts, port, fp, code)
    }

    private fun pair(hosts: List<String>, port: Int, fingerprint: String?, code: String) {
        Ui.toast(this, "Pairing…")
        Ui.background(this) {
            var lastError: Exception? = null
            for (host in hosts) {
                try {
                    val msg = jsonOf(
                        "t" to "pair",
                        "code" to code.trim().uppercase(),
                        "device_id" to prefs.deviceId,
                        "device_name" to prefs.deviceName,
                    )
                    val (link, reply, seenFp) = Link.open(host, port, fingerprint, msg)
                    link.close()
                    prefs.pcId = reply.optString("pc_id")
                    prefs.pcName = reply.optString("pc_name", "PC")
                    prefs.port = port
                    prefs.fingerprint = fingerprint ?: seenFp
                    prefs.mac = reply.optString("mac", "")
                    prefs.rememberHost(host)
                    prefs.token = reply.getString("token")
                    runOnUiThread {
                        LinkService.start(this)
                        render()
                    }
                    return@background "Paired with ${prefs.pcName}"
                } catch (e: Exception) {
                    lastError = e
                }
            }
            "Pairing failed: ${lastError?.message ?: "PC not found"}"
        }
    }

    // -------------------------------------------------------------- render

    private fun render() {
        // Keep the scroll position across redraws.
        val scroll = root.parent as? android.widget.ScrollView
        val y = scroll?.scrollY ?: 0
        root.removeAllViews()
        actionsBox = null
        speakerReadout = null
        if (!prefs.paired) renderPairing() else renderHome()
        scroll?.post { scroll.scrollTo(0, y) }
    }

    private fun EditText.keepText(initial: String, store: (String) -> Unit) = apply {
        setText(initial)
        setSelection(initial.length)
        addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(e: android.text.Editable?) = store(e?.toString() ?: "")
        })
    }

    private fun renderPairing() {
        root.addView(Ui.text(this, "PCLink", 26f, bold = true))
        root.addView(Ui.text(this, "Link this phone to your Windows PC.", muted = true).apply { setPadding(0, 0, 0, Ui.dp(this@MainActivity, 16)) })
        val card = Ui.card(this, "Pair with your PC")
        card.addView(Ui.text(this, "1. On the PC, run PCLink and open Pair (tray icon → Pair a phone).\n2. Scan the QR code with your camera, or pick your PC below and type the code shown on it.", muted = true))
        val codeField = EditText(this).apply {
            hint = "Pairing code (8 letters)"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            filters = arrayOf<android.text.InputFilter>(android.text.InputFilter.LengthFilter(8), android.text.InputFilter.AllCaps())
        }.keepText(codeText) { codeText = it }
        card.addView(codeField)
        val found = discovery?.found?.values?.sortedBy { it.name }.orEmpty()
        if (found.isEmpty()) {
            card.addView(Ui.text(this, "Looking for PCs on this Wi-Fi…", muted = true))
        }
        for (pc in found) {
            card.addView(Ui.button(this, "Pair with ${pc.name} (${pc.host})", primary = true) {
                if (codeField.text.isBlank()) Ui.toast(this, "Type the code shown on the PC first")
                else pair(listOf(pc.host), pc.port, pc.fingerprint, codeField.text.toString())
            })
        }
        val hostField = EditText(this).apply {
            hint = "Or the PC's IP address, e.g. 192.168.1.20"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
        }.keepText(hostText) { hostText = it }
        card.addView(hostField)
        card.addView(Ui.button(this, "Pair by address") {
            val host = hostField.text.toString().trim()
            if (host.isEmpty() || codeField.text.isBlank()) Ui.toast(this, "Enter the address and the code")
            else pair(listOf(host), 47810, null, codeField.text.toString())
        })
        root.addView(card)
    }

    private fun renderHome() {
        val link = LinkService.current
        root.addView(Ui.text(this, prefs.pcName.ifEmpty { "PCLink" }, 26f, bold = true))
        root.addView(Ui.text(this, LinkService.stateText ?: if (link != null) "Connected" else "Not connected", muted = true).apply {
            setPadding(0, 0, 0, Ui.dp(this@MainActivity, 16))
        })

        // Phone as speaker: the headline one-tap feature.
        val speaker = Ui.card(this)
        speaker.addView(Ui.button(this, if (Speaker.isPlaying) "■  Stop playing PC audio" else "🔊  Play PC audio on this phone", primary = true) {
            Ui.background(this) { ActionReceiver.toggleSpeaker().also { runOnUiThread { render() } } }
        }.apply { isEnabled = link != null })
        speaker.addView(Ui.text(this, "Also on the PC (Ctrl+Alt+P), in Quick Settings and in the PCLink notification.", 13f, muted = true))
        speaker.addView(Switch(this).apply {
            text = "🎙  Use my mic on the PC (headset mic when connected)"
            isChecked = Mic.isOn
            isEnabled = link != null
            setPadding(0, Ui.dp(this@MainActivity, 10), 0, 0)
            setOnCheckedChangeListener { view, on -> toggleMic(view as Switch, on) }
        })
        if (Mic.isOn) speaker.addView(Ui.text(this, "Mic: ${Mic.source}. On the PC, apps use the VB-Cable output as their microphone.", 13f, muted = true))
        if (Speaker.isPlaying || Speaker.lastError != null) {
            val readout = Ui.text(this, "", 13f, muted = true)
            speaker.addView(readout)
            speakerReadout = readout
            updateReadout()
        }
        root.addView(speaker)

        val claude = Ui.card(this)
        claude.addView(Ui.button(this, "🤖  Ask Claude to do something on your PC", primary = true) {
            startActivity(ClaudeActivity.intent(this))
        })
        claude.addView(Ui.text(this, "Claude Code runs on the PC and can use its screen, apps and command line.", 13f, muted = true))
        root.addView(claude)

        val quick = Ui.card(this, "Quick actions")
        quick.addView(
            Ui.grid(this, listOf(
                Ui.button(this, "📁 PC files") { startActivity(Intent(this, PcFilesActivity::class.java)) },
                Ui.button(this, "🖱 Touchpad & keys") { startActivity(Intent(this, RemoteActivity::class.java)) },
                Ui.button(this, "📋 Send clipboard") { startActivity(ClipSendActivity.intent(this)) },
                Ui.button(this, "⬆ Send files") { pickFiles() },
                Ui.button(this, "🔒 Lock PC") { sys("lock") },
                Ui.button(this, "🌙 Sleep PC") { confirm("Put the PC to sleep?") { sys("sleep") } },
                Ui.button(this, "🔌 Shut down") { confirm("Shut the PC down?") { sys("shutdown") } },
                Ui.button(this, "⏰ Wake PC") { wakeOnLan() },
            )),
        )
        quick.addView(
            Ui.grid(this, listOf(
                Ui.button(this, "⏮") { media("prev") },
                Ui.button(this, "⏯") { media("play_pause") },
                Ui.button(this, "⏭") { media("next") },
                Ui.button(this, "🔉") { media("vol_down") },
                Ui.button(this, "🔊") { media("vol_up") },
            ).onEach { (it as android.widget.Button).textSize = 20f }, perRow = 5),
        )
        root.addView(quick)

        val panel = Ui.card(this, "Control panel")
        panel.addView(Ui.text(this, "Buttons from [[actions]] in the PC's config.toml. You get a notification with the output when each finishes.", 13f, muted = true))
        val box = Ui.column(this)
        panel.addView(box)
        actionsBox = box
        root.addView(panel)
        if (link != null) loadActions(link) else box.addView(Ui.text(this, "Connect to your PC to see its buttons.", 13f, muted = true))

        renderSetup()
        renderSettings()
    }

    private fun loadActions(link: Link) {
        val cached = actions
        if (cached != null && actionsFor === link) {
            showActions(cached)
            return
        }
        Ui.background(this) {
            val list = link.requestArray("actions.list")
            actions = list
            actionsFor = link
            runOnUiThread { showActions(list) }
            null
        }
    }

    private fun showActions(actions: org.json.JSONArray) {
        val box = actionsBox ?: return
        if (actions.length() == 0) {
            box.addView(Ui.text(this, "No buttons yet. Add [[actions]] to the PC's config.toml.", 13f, muted = true))
            return
        }
        val buttons = (0 until actions.length()).map { i ->
            val a = actions.getJSONObject(i)
            val name = a.getString("name")
            val icon = a.str("icon")
            Ui.button(this, listOfNotNull(icon, name).joinToString(" ")) {
                Ui.background(this) {
                    LinkService.require().request("actions.run", jsonOf("name" to name))
                    "Started $name"
                }
            }
        }
        box.addView(Ui.grid(this, buttons))
    }

    private fun renderSetup() {
        val items = mutableListOf<Pair<String, () -> Unit>>()
        if (!Environment.isExternalStorageManager()) {
            items += "Allow All files access (browse and send files)" to {
                startActivity(Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:$packageName")))
            }
        }
        val listenerOn = Settings.Secure.getString(contentResolver, "enabled_notification_listeners")
            ?.contains(ComponentName(this, NotifListener::class.java).flattenToString()) == true
        if (!listenerOn) {
            items += "Allow notification access (mirror notifications to PC)" to {
                startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
            }
        }
        val pm = getSystemService(PowerManager::class.java)
        if (!pm.isIgnoringBatteryOptimizations(packageName)) {
            items += "Keep PCLink connected in the background" to {
                startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
            }
        }
        if (checkSelfPermission(Manifest.permission.READ_SMS) != PackageManager.PERMISSION_GRANTED) {
            items += "Optional: let the PC read SMS codes (for Claude's 2FA tool)" to {
                requestPermissions(arrayOf(Manifest.permission.READ_SMS), 2)
            }
        }
        if (items.isEmpty()) return
        val card = Ui.card(this, "Finish setup")
        for ((label, action) in items) card.addView(Ui.button(this, label) { action() })
        root.addView(card)
    }

    private fun renderSettings() {
        val card = Ui.card(this, "Settings")
        card.addView(Switch(this).apply {
            text = "Copy the PC's clipboard to this phone automatically"
            isChecked = prefs.syncClipboard
            setOnCheckedChangeListener { _, on -> prefs.syncClipboard = on }
        })
        card.addView(Switch(this).apply {
            text = "Mirror notifications to the PC"
            isChecked = prefs.mirrorNotifications
            setOnCheckedChangeListener { _, on -> prefs.mirrorNotifications = on }
        })
        card.addView(Ui.button(this, "Reconnect") { LinkService.reconnect(this) })
        card.addView(Ui.button(this, "Unpair from ${prefs.pcName}") {
            confirm("Unpair from ${prefs.pcName}?") {
                startService(Intent(this, LinkService::class.java).setAction(LinkService.ACTION_STOP))
                prefs.forget()
                render()
            }
        })
        card.addView(Ui.text(this, "Version ${Updater.versionName(this)} (build ${Updater.currentBuild(this)}). Updates install automatically when you open the app.", 13f, muted = true))
        card.addView(Ui.button(this, "Check for updates") {
            val app = applicationContext
            Ui.toast(this, "Checking for updates…")
            Ui.background(app) { Updater.checkAndInstall(app, manual = true) }
        })
        card.addView(TextView(this).apply {
            text = "Files from the PC are saved in Download/PCLink."
            setTextColor(Ui.muted(this@MainActivity))
            textSize = 13f
        })
        root.addView(card)
    }

    // ------------------------------------------------------------- actions

    private fun toggleMic(view: Switch, on: Boolean) {
        if (on == Mic.isOn) return
        if (on && checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            view.isChecked = false
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), MIC_PERMISSION)
            return
        }
        Ui.background(this) {
            try {
                if (on) Mic.start(applicationContext) else { Mic.stop(); "Mic off" }
            } finally {
                runOnUiThread { render() }
            }
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == MIC_PERMISSION && grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) {
            Ui.background(this) {
                try { Mic.start(applicationContext) } finally { runOnUiThread { render() } }
            }
        } else if (requestCode == MIC_PERMISSION) {
            Ui.toast(this, "PCLink needs microphone access for this")
        }
    }

    private fun sys(cmd: String) = Ui.background(this) {
        LinkService.require().request("sys", jsonOf("cmd" to cmd))
        "Done"
    }

    private fun media(cmd: String) = Ui.background(this) {
        LinkService.require().request("media", jsonOf("cmd" to cmd))
        null
    }

    private fun confirm(question: String, onYes: () -> Unit) {
        android.app.AlertDialog.Builder(this)
            .setMessage(question)
            .setPositiveButton("Yes") { _, _ -> onYes() }
            .setNegativeButton("Cancel", null)
            .show()
    }

    /** Sends a Wake-on-LAN magic packet to the paired PC's MAC address. */
    private fun wakeOnLan() {
        val mac = prefs.mac.split(':', '-').mapNotNull { it.toIntOrNull(16)?.toByte() }
        if (mac.size != 6) {
            Ui.toast(this, "PC's network address unknown. Connect once first.")
            return
        }
        Ui.background(this) {
            val packet = ByteArray(6) { 0xFF.toByte() } + List(16) { mac }.flatten().toByteArray()
            DatagramSocket().use { s ->
                s.broadcast = true
                val targets = mutableListOf("255.255.255.255")
                prefs.hosts.firstOrNull()?.let { h ->
                    targets += h.substringBeforeLast('.') + ".255"
                }
                for (t in targets) {
                    for (port in listOf(9, 7)) s.send(DatagramPacket(packet, packet.size, InetAddress.getByName(t), port))
                }
            }
            "Wake signal sent. Wake-on-LAN must be enabled in the PC's BIOS and network adapter settings."
        }
    }

    private fun pickFiles() {
        val i = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
            putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
        }
        @Suppress("DEPRECATION")
        startActivityForResult(i, PICK_FILES)
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != PICK_FILES || resultCode != RESULT_OK || data == null) return
        val uris = mutableListOf<Uri>()
        data.clipData?.let { c -> for (i in 0 until c.itemCount) uris += c.getItemAt(i).uri }
        if (uris.isEmpty()) data.data?.let { uris += it }
        if (uris.isNotEmpty()) Ui.background(this) { ShareActivity.sendUris(applicationContext, uris) }
    }

    companion object {
        private const val PICK_FILES = 10
        private const val MIC_PERMISSION = 11
        @Volatile var visible = false
    }
}

@Suppress("unused")
private fun View.gone() {
    visibility = View.GONE
}
