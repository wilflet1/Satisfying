package dev.pclink

import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService

/** Quick Settings tile: send the phone's clipboard to the PC. */
class ClipTile : TileService() {
    override fun onStartListening() {
        qsTile?.apply {
            state = if (LinkService.current != null) Tile.STATE_INACTIVE else Tile.STATE_UNAVAILABLE
            updateTile()
        }
    }

    @Suppress("DEPRECATION")
    override fun onClick() {
        // Reading the clipboard needs a focused activity, so hop through one.
        if (Build.VERSION.SDK_INT >= 34) {
            startActivityAndCollapse(ClipSendActivity.pending(this))
        } else {
            startActivityAndCollapse(ClipSendActivity.intent(this))
        }
    }
}

/** Quick Settings tile: one tap to play the PC's audio on the phone. */
class SpeakerTile : TileService() {
    override fun onStartListening() = refresh()

    private fun refresh() {
        qsTile?.apply {
            state = when {
                LinkService.current == null -> Tile.STATE_UNAVAILABLE
                Speaker.isPlaying -> Tile.STATE_ACTIVE
                else -> Tile.STATE_INACTIVE
            }
            updateTile()
        }
    }

    override fun onClick() {
        Thread {
            val msg = ActionReceiver.toggleSpeaker()
            android.os.Handler(mainLooper).post {
                refresh()
                Ui.toast(this, msg)
            }
        }.start()
    }
}
