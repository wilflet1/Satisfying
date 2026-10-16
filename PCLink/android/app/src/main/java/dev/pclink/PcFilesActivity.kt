package dev.pclink

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.LinearLayout
import org.json.JSONObject
import java.text.DateFormat
import java.util.Date

/** Browse the PC's shared folders; download files or upload into a folder. */
class PcFilesActivity : Activity() {
    private lateinit var root: LinearLayout
    private var path: String? = null
    private var parent: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        root = Ui.page(this)
        load(null)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        val p = parent
        when {
            p != null -> load(p)
            path != null -> load(null)
            else -> @Suppress("DEPRECATION") super.onBackPressed()
        }
    }

    private fun load(target: String?) {
        Ui.background(this) {
            val link = LinkService.require()
            if (target == null) {
                val roots = link.requestArray("fs.roots")
                runOnUiThread { showRoots(roots) }
            } else {
                val listing = link.request("fs.list", jsonOf("path" to target))
                runOnUiThread { showListing(listing) }
            }
            null
        }
    }

    private fun showRoots(roots: org.json.JSONArray) {
        path = null
        parent = null
        root.removeAllViews()
        root.addView(Ui.text(this, "Shared folders on ${Prefs(this).pcName}", 20f, bold = true))
        root.addView(Ui.text(this, "Change these in the PC's config.toml (shared_folders).", 13f, muted = true))
        val card = Ui.card(this)
        for (i in 0 until roots.length()) {
            val r = roots.getJSONObject(i)
            card.addView(Ui.button(this, "📁 ${r.getString("name")}") { load(r.getString("path")) })
        }
        root.addView(card)
    }

    private fun showListing(listing: JSONObject) {
        path = listing.getString("path")
        parent = listing.str("parent")
        root.removeAllViews()
        root.addView(Ui.text(this, path!!, 14f, muted = true))
        val bar = Ui.row(this)
        bar.addView(Ui.button(this, "↑ Up") { onBackPressedCompat() })
        bar.addView(Ui.button(this, "⬆ Upload here") { pickUpload() })
        root.addView(bar)
        val card = Ui.card(this)
        val entries = listing.getJSONArray("entries")
        if (entries.length() == 0) card.addView(Ui.text(this, "Empty folder", muted = true))
        val fmt = DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT)
        for (i in 0 until entries.length()) {
            val e = entries.getJSONObject(i)
            val isDir = e.getBoolean("dir")
            val label = if (isDir) "📁 ${e.getString("name")}" else
                "📄 ${e.getString("name")}\n${Ui.size(e.getLong("size"))} · ${fmt.format(Date(e.getLong("mtime")))}"
            card.addView(Ui.button(this, label) {
                if (isDir) load(e.getString("path")) else download(e.getString("path"), e.getString("name"), e.getLong("size"))
            }.apply { gravity = android.view.Gravity.START or android.view.Gravity.CENTER_VERTICAL })
        }
        root.addView(card)
    }

    private fun onBackPressedCompat() {
        val p = parent
        if (p != null) load(p) else load(null)
    }

    private fun download(pcPath: String, name: String, size: Long) {
        Ui.toast(this, "Downloading $name…")
        val ctx = applicationContext
        Ui.background(ctx) {
            val link = LinkService.require()
            val dir = PhoneOps(ctx).inbox()
            val file = Transfers.track(ctx, "Downloading $name", size) { progress ->
                link.pull(pcPath, dir) { done, _ -> progress(done) }
            }
            android.media.MediaScannerConnection.scanFile(ctx, arrayOf(file.path), null, null)
            "Saved to Download/PCLink/${file.name}"
        }
    }

    private fun pickUpload() {
        val i = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
            putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
        }
        @Suppress("DEPRECATION")
        startActivityForResult(i, 1)
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        @Suppress("DEPRECATION")
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK || data == null) return
        val uris = mutableListOf<Uri>()
        data.clipData?.let { c -> for (i in 0 until c.itemCount) uris += c.getItemAt(i).uri }
        if (uris.isEmpty()) data.data?.let { uris += it }
        val dir = path ?: return
        val ctx = applicationContext
        Ui.background(ctx) {
            val link = LinkService.require()
            for (uri in uris) {
                var name = "file"
                var size: Long? = null
                ctx.contentResolver.query(uri, null, null, null, null)?.use {
                    if (it.moveToFirst()) {
                        val n = it.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                        val s = it.getColumnIndex(android.provider.OpenableColumns.SIZE)
                        if (n >= 0) name = it.getString(n) ?: name
                        if (s >= 0 && !it.isNull(s)) size = it.getLong(s)
                    }
                }
                val input = ctx.contentResolver.openInputStream(uri) ?: continue
                Transfers.track(ctx, "Uploading $name", size) { progress -> link.push(input, name, size, dir, "file", progress) }
            }
            runOnUiThread { load(dir) }
            "Uploaded ${uris.size} file(s)"
        }
    }
}
