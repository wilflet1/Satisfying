package dev.pclink

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.media.AudioDeviceInfo
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioRecord
import android.media.MediaRecorder
import android.media.audiofx.AcousticEchoCanceler
import android.media.audiofx.NoiseSuppressor
import android.os.Build
import android.util.Base64
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Phone mic → PC microphone. Records in voice-communication mode (headset
 * mic when one is connected, echo cancellation and noise suppression) and
 * streams 10 ms packets: `u64 seq | AES-256-GCM(pcm16 mono) | tag`, the
 * same format as the speaker stream, to the port the PC opened.
 */
object Mic {
    @Volatile var isOn = false
        private set
    /** Which mic is in use, for the UI. */
    @Volatile var source = ""
        private set
    private var thread: Thread? = null
    private var socket: DatagramSocket? = null

    /** Starts the mic; returns a message for the user. Call off the main thread. */
    @Synchronized
    fun start(ctx: Context): String {
        if (isOn) return "Mic is already on"
        if (ctx.checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            throw IOException("Allow PCLink to use the microphone first (open the app and turn the mic on there)")
        }
        val link = LinkService.require()
        // Android only lets an app use the mic in the background if it was
        // started while the app was open.
        LinkService.setMicForeground(true)
        val params = try {
            link.request("mic.open", org.json.JSONObject())
        } catch (e: Exception) {
            LinkService.setMicForeground(false)
            throw e
        }
        val key = Base64.decode(params.getString("key"), Base64.DEFAULT)
        val rate = params.optInt("rate", 48_000)
        val port = params.getInt("port")
        val host = InetAddress.getByName(link.host)

        val am = ctx.getSystemService(AudioManager::class.java)
        am.mode = AudioManager.MODE_IN_COMMUNICATION
        source = routeToHeadset(am)

        val minBuf = AudioRecord.getMinBufferSize(rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        val record = try {
            AudioRecord.Builder()
                .setAudioSource(MediaRecorder.AudioSource.VOICE_COMMUNICATION)
                .setAudioFormat(
                    AudioFormat.Builder()
                        .setSampleRate(rate)
                        .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                        .build(),
                )
                .setBufferSizeInBytes(maxOf(minBuf, rate / 10 * 2))
                .build()
        } catch (e: SecurityException) {
            resetAudio(am)
            LinkService.setMicForeground(false)
            throw IOException("Microphone permission denied")
        }
        if (AcousticEchoCanceler.isAvailable()) AcousticEchoCanceler.create(record.audioSessionId)?.enabled = true
        if (NoiseSuppressor.isAvailable()) NoiseSuppressor.create(record.audioSessionId)?.enabled = true

        val sock = DatagramSocket()
        socket = sock
        isOn = true
        // Route the PC's audio through the same voice path, so a Bluetooth
        // headset carries both directions and echo cancellation hears it.
        Speaker.voiceMode = true
        thread = Thread({ stream(record, sock, host, port, key, rate, am) }, "pclink-mic").apply {
            priority = Thread.MAX_PRIORITY
            start()
        }
        LinkService.changed()
        return "Mic on: $source"
    }

    private fun stream(record: AudioRecord, sock: DatagramSocket, host: InetAddress, port: Int, key: ByteArray, rate: Int, am: AudioManager) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val keySpec = SecretKeySpec(key, "AES")
        val nonce = ByteArray(12)
        val frame = ShortArray(rate / 100) // 10 ms
        val bytes = ByteArray(frame.size * 2)
        var seq = 0L
        try {
            record.startRecording()
            while (isOn && sock === socket) {
                var got = 0
                while (got < frame.size) {
                    val n = record.read(frame, got, frame.size - got)
                    if (n <= 0) break
                    got += n
                }
                if (got < frame.size) continue
                for (i in frame.indices) {
                    bytes[i * 2] = frame[i].toInt().toByte()
                    bytes[i * 2 + 1] = (frame[i].toInt() shr 8).toByte()
                }
                val packet = seal(cipher, keySpec, nonce, seq, bytes)
                try {
                    sock.send(DatagramPacket(packet, packet.size, host, port))
                } catch (_: IOException) {
                }
                seq++
            }
        } finally {
            try {
                record.stop()
            } catch (_: IllegalStateException) {
            }
            record.release()
            resetAudio(am)
        }
    }

    /** `u64 big-endian seq | AES-256-GCM(pcm) | tag`, nonce = 4 zero bytes + seq. */
    fun seal(cipher: Cipher, key: SecretKeySpec, nonce: ByteArray, seq: Long, pcm: ByteArray): ByteArray {
        for (i in 0 until 8) nonce[4 + i] = (seq ushr (56 - 8 * i)).toByte()
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(128, nonce))
        val sealed = cipher.doFinal(pcm)
        val packet = ByteArray(8 + sealed.size)
        System.arraycopy(nonce, 4, packet, 0, 8)
        System.arraycopy(sealed, 0, packet, 8, sealed.size)
        return packet
    }

    /** Uses a headset mic when one is connected. Returns a label for the UI. */
    private fun routeToHeadset(am: AudioManager): String {
        if (Build.VERSION.SDK_INT >= 31) {
            val preferred = listOf(
                AudioDeviceInfo.TYPE_BLE_HEADSET,
                AudioDeviceInfo.TYPE_BLUETOOTH_SCO,
                AudioDeviceInfo.TYPE_WIRED_HEADSET,
                AudioDeviceInfo.TYPE_USB_HEADSET,
            )
            val devices = am.availableCommunicationDevices
            for (type in preferred) {
                val d = devices.firstOrNull { it.type == type } ?: continue
                if (am.setCommunicationDevice(d)) return "${d.productName} (${label(type)})"
            }
            // No headset: call audio would otherwise go to the earpiece and
            // the PC's sound would seem to vanish. Use the loudspeaker.
            devices.firstOrNull { it.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER }?.let { am.setCommunicationDevice(it) }
            return "phone mic (loudspeaker)"
        }
        @Suppress("DEPRECATION")
        if (am.isBluetoothScoAvailableOffCall) {
            am.startBluetoothSco()
            am.isBluetoothScoOn = true
            return "Bluetooth headset mic"
        }
        @Suppress("DEPRECATION")
        if (am.isWiredHeadsetOn) return "wired headset mic"
        @Suppress("DEPRECATION")
        am.isSpeakerphoneOn = true
        return "phone mic (loudspeaker)"
    }

    private fun label(type: Int) = when (type) {
        AudioDeviceInfo.TYPE_BLE_HEADSET, AudioDeviceInfo.TYPE_BLUETOOTH_SCO -> "Bluetooth"
        AudioDeviceInfo.TYPE_USB_HEADSET -> "USB"
        else -> "wired"
    }

    @Suppress("DEPRECATION")
    private fun resetAudio(am: AudioManager) {
        if (Build.VERSION.SDK_INT >= 31) {
            am.clearCommunicationDevice()
        } else {
            if (am.isBluetoothScoOn) {
                am.isBluetoothScoOn = false
                am.stopBluetoothSco()
            }
            am.isSpeakerphoneOn = false
        }
        am.mode = AudioManager.MODE_NORMAL
    }

    @Synchronized
    fun stop(tellPc: Boolean = true) {
        if (!isOn) return
        isOn = false
        socket?.close()
        socket = null
        thread = null
        source = ""
        Speaker.voiceMode = false
        LinkService.setMicForeground(false)
        if (tellPc) {
            LinkService.current?.let { link -> Thread { try { link.request("mic.close", org.json.JSONObject(), 5000) } catch (_: Exception) {} }.start() }
        }
        LinkService.changed()
    }
}
