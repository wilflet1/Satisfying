package dev.pclink

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.net.wifi.WifiManager
import android.os.PowerManager
import java.net.DatagramPacket
import java.net.DatagramSocket
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Plays the PC's audio. Packets are `u64 seq | AES-256-GCM(pcm16) | tag`,
 * nonce = 4 zero bytes + seq. Late or out-of-order packets are dropped and
 * the queue is kept short so latency stays low.
 */
object Speaker {
    @Volatile var isPlaying = false
        private set
    private var socket: DatagramSocket? = null
    private var thread: Thread? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var wakeLock: PowerManager.WakeLock? = null

    /** Max audio queued in the phone before packets are dropped. */
    private const val MAX_LATENCY_MS = 90

    /**
     * When the phone mic is on, PC audio plays as voice-communication audio:
     * it follows the headset the mic uses and echo cancellation can hear it.
     */
    @Volatile var voiceMode = false

    /** For the on-screen readout: packets received and the last problem. */
    @Volatile var packets = 0L
        private set
    @Volatile var lastError: String? = null
        private set

    @Synchronized
    fun start(context: Context, key: ByteArray, rate: Int, channels: Int): Int {
        stop()
        val sock = DatagramSocket(0).apply {
            receiveBufferSize = 512 * 1024
            soTimeout = 1000
        }
        val app = context.applicationContext
        wifiLock = app.getSystemService(WifiManager::class.java)
            .createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "pclink-speaker")
            .apply { setReferenceCounted(false); acquire() }
        wakeLock = app.getSystemService(PowerManager::class.java)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "pclink:speaker")
            .apply { setReferenceCounted(false); acquire(6 * 60 * 60 * 1000L) }

        socket = sock
        isPlaying = true
        thread = Thread({ play(sock, key, rate, channels) }, "pclink-speaker").apply {
            priority = Thread.MAX_PRIORITY
            start()
        }
        return sock.localPort
    }

    private fun makeTrack(rate: Int, channels: Int, voice: Boolean): AudioTrack {
        val mask = if (channels == 1) AudioFormat.CHANNEL_OUT_MONO else AudioFormat.CHANNEL_OUT_STEREO
        val minBuf = AudioTrack.getMinBufferSize(rate, mask, AudioFormat.ENCODING_PCM_16BIT)
        val attrs = if (voice) {
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION)
                .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                .build()
        } else {
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                .build()
        }
        return AudioTrack.Builder()
            .setAudioAttributes(attrs)
            .setAudioFormat(
                AudioFormat.Builder()
                    .setSampleRate(rate)
                    .setChannelMask(mask)
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .build(),
            )
            .setBufferSizeInBytes(maxOf(minBuf * 2, rate * 2 * channels * 150 / 1000))
            .setTransferMode(AudioTrack.MODE_STREAM)
            .setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
            .build()
    }

    /** Makes the track, falling back to plain media audio if the voice path fails. */
    private fun safeTrack(rate: Int, channels: Int, voice: Boolean): AudioTrack = try {
        makeTrack(rate, channels, voice)
    } catch (e: Exception) {
        lastError = "Voice audio path failed (${e.message}); using media audio"
        makeTrack(rate, channels, false)
    }

    private fun play(sock: DatagramSocket, key: ByteArray, rate: Int, channels: Int) {
        try {
            playLoop(sock, key, rate, channels)
        } catch (e: Throwable) {
            // Never die silently: show the problem and tell the PC.
            lastError = "Playback stopped: ${e.message ?: e.javaClass.simpleName}"
            LinkService.changed()
        }
    }

    private fun playLoop(sock: DatagramSocket, key: ByteArray, rate: Int, channels: Int) {
        val frameBytes = 2 * channels
        var voice = voiceMode
        var track = safeTrack(rate, channels, voice)
        packets = 0
        lastError = null
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val keySpec = SecretKeySpec(key, "AES")
        val nonce = ByteArray(12)
        val buf = ByteArray(4096)
        val packet = DatagramPacket(buf, buf.size)
        var lastSeq = -1L
        var framesWritten = 0L
        val maxQueued = rate.toLong() * MAX_LATENCY_MS / 1000
        track.play()
        try {
            while (isPlaying && sock === socket) {
                if (voiceMode != voice) {
                    // The mic was turned on or off: switch the audio path.
                    voice = voiceMode
                    track.stop()
                    track.release()
                    track = safeTrack(rate, channels, voice)
                    track.play()
                    framesWritten = 0
                }
                try {
                    sock.receive(packet)
                } catch (_: java.net.SocketTimeoutException) {
                    continue
                }
                val len = packet.length
                if (len < 8 + 16) continue
                var seq = 0L
                for (i in 0 until 8) seq = (seq shl 8) or (buf[i].toLong() and 0xff)
                if (seq <= lastSeq) continue // late or duplicate
                System.arraycopy(buf, 0, nonce, 4, 8)
                val pcm = try {
                    cipher.init(Cipher.DECRYPT_MODE, keySpec, GCMParameterSpec(128, nonce))
                    cipher.doFinal(buf, 8, len - 8)
                } catch (_: Exception) {
                    continue // not ours / corrupted
                }
                lastSeq = seq
                packets++
                val played = track.playbackHeadPosition.toLong() and 0xffffffffL
                val queued = framesWritten - played
                if (queued < 0) framesWritten = played // underrun reset
                if (queued > maxQueued) continue // keep latency bounded
                val n = track.write(pcm, 0, pcm.size, AudioTrack.WRITE_NON_BLOCKING)
                if (n > 0) framesWritten += n / frameBytes
            }
        } finally {
            try {
                track.stop()
            } catch (_: IllegalStateException) {
            }
            track.release()
        }
    }

    @Synchronized
    fun stop() {
        val wasPlaying = isPlaying
        isPlaying = false
        socket?.close()
        socket = null
        thread = null
        wifiLock?.release()
        wifiLock = null
        wakeLock?.let { if (it.isHeld) it.release() }
        wakeLock = null
        if (wasPlaying) LinkService.current?.event("speaker.stopped", org.json.JSONObject())
    }
}
