package com.motionlinkvr.controller

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.WindowManager
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.TextView
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.URL
import java.security.KeyStore
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.Locale
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager

private const val DISCOVERY_PORT = 47822
private const val DISCOVERY_REQUEST = "MOTIONLINK_DISCOVER_V1"
private const val MAX_CERTIFICATE_BYTES = 16 * 1024

private data class DiscoveredPc(val address: String, val httpPort: Int, val httpsPort: Int) {
    override fun toString(): String = "$address  (MotionLink VR)"
}

class MainActivity : Activity(), SensorEventListener {
    private val worker: ExecutorService = Executors.newSingleThreadExecutor()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val sensorManager by lazy { getSystemService(Context.SENSOR_SERVICE) as SensorManager }
    private val rotationMatrix = FloatArray(9)
    private val orientation = FloatArray(3)

    private lateinit var statusText: TextView
    private lateinit var pcSpinner: Spinner
    private lateinit var pairingCodeInput: EditText
    private lateinit var scanButton: Button
    private lateinit var connectButton: Button
    private lateinit var motionButton: Button
    private lateinit var pauseButton: Button
    private lateinit var recenterButton: Button
    private lateinit var disconnectButton: Button
    private lateinit var sensitivityText: TextView
    private lateinit var smoothingText: TextView
    private lateinit var deadzoneText: TextView

    private var discoveredPcs: List<DiscoveredPc> = emptyList()
    private var webSocket: WebSocket? = null
    private var httpClient: OkHttpClient? = null
    private var isMotionRunning = false
    private var isPaused = true
    private var baseline: FloatArray? = null
    private var lastMotionSentAt = 0L
    private var sensitivity = 1f
    private var smoothing = 0.2f
    private var deadzone = 0.6f

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        buildScreen()
        scanForPcs()
    }

    override fun onSensorChanged(event: SensorEvent) {
        if ((event.sensor.type != Sensor.TYPE_ROTATION_VECTOR
                && event.sensor.type != Sensor.TYPE_GAME_ROTATION_VECTOR)
            || !isMotionRunning || isPaused) return

        SensorManager.getRotationMatrixFromVector(rotationMatrix, event.values)
        SensorManager.getOrientation(rotationMatrix, orientation)
        val current = FloatArray(3) { Math.toDegrees(orientation[it].toDouble()).toFloat() }
        val initial = baseline
        if (initial == null) {
            baseline = current
            send(MotionPacketEncoder.command("recenter"))
            return
        }

        val now = System.currentTimeMillis()
        if (now - lastMotionSentAt < 16) return
        lastMotionSentAt = now
        send(
            MotionPacketEncoder.motion(
                MotionPacketEncoder.wrapDegrees(current[0] - initial[0]),
                MotionPacketEncoder.wrapDegrees(current[1] - initial[1]),
                MotionPacketEncoder.wrapDegrees(current[2] - initial[2]),
                now
            )
        )
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    override fun onStop() {
        pauseMotion()
        super.onStop()
    }

    override fun onDestroy() {
        stopMotion()
        webSocket?.close(1000, "Controller closed")
        webSocket = null
        httpClient?.dispatcher?.executorService?.shutdown()
        worker.shutdownNow()
        super.onDestroy()
    }

    private fun buildScreen() {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(20), dp(20), dp(28))
        }
        val scrollView = ScrollView(this).apply {
            isFillViewport = true
            addView(content)
            setOnApplyWindowInsetsListener { view, insets ->
                val systemBars = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    val bars = insets.getInsets(WindowInsets.Type.systemBars())
                    intArrayOf(bars.left, bars.top, bars.right, bars.bottom)
                } else {
                    @Suppress("DEPRECATION")
                    intArrayOf(
                        insets.systemWindowInsetLeft,
                        insets.systemWindowInsetTop,
                        insets.systemWindowInsetRight,
                        insets.systemWindowInsetBottom
                    )
                }
                view.setPadding(systemBars[0], systemBars[1], systemBars[2], systemBars[3])
                insets
            }
        }
        setContentView(scrollView)
        scrollView.requestApplyInsets()

        content.addView(text("MotionLink VR", 28f, true))
        content.addView(text("Phone motion controller for your Windows PC", 15f))
        statusText = text("Searching for PCs on this Wi-Fi…", 16f, true).also {
            it.setPadding(0, dp(18), 0, dp(8))
            content.addView(it)
        }

        pcSpinner = Spinner(this)
        content.addView(label("Detected PC"))
        content.addView(pcSpinner)
        scanButton = button("Search for PCs") { scanForPcs() }
        content.addView(scanButton)

        content.addView(label("Pairing code shown in the Windows app"))
        pairingCodeInput = EditText(this).apply {
            hint = "8-character code"
            isSingleLine = true
            inputType = android.text.InputType.TYPE_CLASS_TEXT or
                android.text.InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS
            filters = arrayOf(android.text.InputFilter.LengthFilter(8))
        }
        content.addView(pairingCodeInput)
        connectButton = button("Connect securely") { connectToPc() }
        content.addView(connectButton)

        content.addView(label("Sensitivity"))
        sensitivityText = text("1.0x", 14f)
        content.addView(sensitivityText)
        addSlider(content, 380, 80) { progress ->
            sensitivity = 0.2f + progress * 0.01f
            sensitivityText.text = String.format(Locale.US, "%.1fx", sensitivity)
            sendSettings()
        }

        content.addView(label("Smoothing"))
        smoothingText = text("0.20", 14f)
        content.addView(smoothingText)
        addSlider(content, 100, 20) { progress ->
            smoothing = progress * 0.01f
            smoothingText.text = String.format(Locale.US, "%.2f", smoothing)
            sendSettings()
        }

        content.addView(label("Deadzone"))
        deadzoneText = text("0.6°", 14f)
        content.addView(deadzoneText)
        addSlider(content, 100, 6) { progress ->
            deadzone = progress * 0.1f
            deadzoneText.text = String.format(Locale.US, "%.1f°", deadzone)
            sendSettings()
        }

        val motionControls = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
        }
        motionButton = button("Start motion") { startMotion() }
        pauseButton = button("Pause") { togglePause() }
        for (control in listOf(motionButton, pauseButton)) {
            motionControls.addView(
                control,
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            )
        }
        content.addView(motionControls)
        val sessionControls = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
        }
        recenterButton = button("Recenter") { recenter() }
        disconnectButton = button("Disconnect") { disconnect() }
        for (control in listOf(recenterButton, disconnectButton)) {
            sessionControls.addView(
                control,
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            )
        }
        content.addView(sessionControls)

        content.addView(text(
            "Keep both devices on the same Wi-Fi. On first connection, compare the certificate " +
                "SHA-256 fingerprint with the one shown in the Windows app before trusting it.",
            13f
        ).apply { setPadding(0, dp(14), 0, 0) })
        pcSpinner.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) = Unit
            override fun onNothingSelected(parent: AdapterView<*>?) = Unit
        }
    }

    private fun addSlider(parent: LinearLayout, max: Int, initial: Int, onProgress: (Int) -> Unit) {
        parent.addView(SeekBar(this).apply {
            this.max = max
            progress = initial
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                    onProgress(progress)
                }

                override fun onStartTrackingTouch(seekBar: SeekBar?) = Unit
                override fun onStopTrackingTouch(seekBar: SeekBar?) = Unit
            })
        })
    }

    private fun scanForPcs() {
        scanButton.isEnabled = false
        statusText.text = "Searching for PCs on this Wi-Fi…"
        worker.execute {
            val result = runCatching { discoverPcs() }
            mainHandler.post {
                scanButton.isEnabled = true
                result.onSuccess { pcs ->
                    discoveredPcs = pcs
                    pcSpinner.adapter = ArrayAdapter(
                        this,
                        android.R.layout.simple_spinner_item,
                        pcs.ifEmpty { listOf(DiscoveredPc("No PC found", 0, 0)) }
                    ).apply { setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item) }
                    statusText.text = if (pcs.isEmpty()) {
                        "No PC found. Start LAN on Windows and allow MotionLink VR on your private network."
                    } else {
                        "Found ${pcs.size} PC${if (pcs.size == 1) "" else "s"} on the local network."
                    }
                }.onFailure { error ->
                    statusText.text = "PC search failed: ${error.message ?: "check Wi-Fi and retry"}"
                }
            }
        }
    }

    private fun discoverPcs(): List<DiscoveredPc> {
        val broadcastAddresses = linkedSetOf<InetAddress>()
        broadcastAddresses.add(InetAddress.getByName("255.255.255.255"))
        val interfaces = NetworkInterface.getNetworkInterfaces()
        while (interfaces.hasMoreElements()) {
            val networkInterface = interfaces.nextElement()
            if (!networkInterface.isUp || networkInterface.isLoopback) continue
            for (interfaceAddress in networkInterface.interfaceAddresses) {
                if (interfaceAddress.address is Inet4Address) {
                    interfaceAddress.broadcast?.let(broadcastAddresses::add)
                }
            }
        }

        val found = linkedMapOf<String, DiscoveredPc>()
        DatagramSocket().use { socket ->
            socket.broadcast = true
            val request = DISCOVERY_REQUEST.toByteArray(Charsets.UTF_8)
            repeat(3) {
                for (broadcast in broadcastAddresses) {
                    socket.send(DatagramPacket(request, request.size, broadcast, DISCOVERY_PORT))
                }
                Thread.sleep(150)
            }

            socket.soTimeout = 300
            val deadline = System.currentTimeMillis() + 2200
            val buffer = ByteArray(1024)
            while (System.currentTimeMillis() < deadline) {
                try {
                    val packet = DatagramPacket(buffer, buffer.size)
                    socket.receive(packet)
                    val response = JSONObject(String(packet.data, packet.offset, packet.length, Charsets.UTF_8))
                    if (response.optString("service") != "MotionLinkVR") continue

                    val address = response.optString("address")
                    val httpPort = response.optInt("httpPort")
                    val httpsPort = response.optInt("httpsPort")
                    if (!isValidIpv4(address) || httpPort !in 1..65535 || httpsPort !in 1..65535) continue
                    found[address] = DiscoveredPc(address, httpPort, httpsPort)
                } catch (_: java.net.SocketTimeoutException) {
                    continue
                } catch (_: org.json.JSONException) {
                    continue
            }
            }
        }
        return found.values.toList()
    }

    private fun isValidIpv4(value: String): Boolean {
        val parts = value.split('.')
        return parts.size == 4 && parts.all { part -> part.toIntOrNull()?.let { it in 0..255 } == true }
    }

    private fun connectToPc() {
        val pc = discoveredPcs.getOrNull(pcSpinner.selectedItemPosition)
        if (pc == null) {
            statusText.text = "Search for a PC before connecting."
            return
        }
        val code = pairingCodeInput.text.toString().trim().uppercase()
        if (!code.matches(Regex("[A-HJ-NP-Z2-9]{8}"))) {
            statusText.text = "Enter the 8-character pairing code shown in the Windows app."
            return
        }

        connectButton.isEnabled = false
        statusText.text = "Checking the PC certificate…"
        worker.execute {
            val result = runCatching {
                val certificate = downloadRootCertificate(pc)
                val fingerprint = MessageDigest.getInstance("SHA-256")
                    .digest(certificate.encoded)
                    .joinToString("") { "%02X".format(it) }
                certificate to fingerprint
            }
            mainHandler.post {
                connectButton.isEnabled = true
                result.onSuccess { (certificate, fingerprint) ->
                    AlertDialog.Builder(this)
                        .setTitle("Verify this PC")
                        .setMessage(
                            "Compare this SHA-256 fingerprint with the one displayed in the " +
                                "Windows app. Only trust it if they match.\n\n$fingerprint"
                        )
                        .setNegativeButton("Cancel", null)
                        .setPositiveButton("Matches — connect") { _, _ ->
                            openSecureSocket(pc, code, certificate)
                        }
                        .show()
                }.onFailure { error ->
                    statusText.text = "Could not read the PC certificate: ${error.message ?: "try again"}"
                }
            }
        }
    }

    private fun downloadRootCertificate(pc: DiscoveredPc): X509Certificate {
        val connection = URL("http://${pc.address}:${pc.httpPort}/root-ca.cer")
            .openConnection() as java.net.HttpURLConnection
        connection.connectTimeout = 4000
        connection.readTimeout = 4000
        try {
            if (connection.responseCode != HttpsURLConnection.HTTP_OK) {
                error("PC returned HTTP ${connection.responseCode}")
            }
            val bytes = connection.inputStream.use { input ->
                val output = ByteArrayOutputStream()
                val buffer = ByteArray(1024)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    if (output.size() + count > MAX_CERTIFICATE_BYTES) {
                        error("Certificate response is unexpectedly large")
                    }
                    output.write(buffer, 0, count)
                }
                output.toByteArray()
            }
            val certificate = CertificateFactory.getInstance("X.509")
                .generateCertificate(bytes.inputStream()) as X509Certificate
            require(certificate.basicConstraints >= 0) { "Downloaded certificate is not a CA" }
            return certificate
        } finally {
            connection.disconnect()
        }
    }

    private fun openSecureSocket(pc: DiscoveredPc, code: String, root: X509Certificate) {
        statusText.text = "Connecting securely to ${pc.address}…"
        worker.execute {
            val result = runCatching { createPinnedClient(root) }
            mainHandler.post {
                result.onSuccess { client ->
                    httpClient = client
                    val request = Request.Builder()
                        .url("wss://${pc.address}:${pc.httpsPort}/motion?code=$code")
                        .build()
                    webSocket = client.newWebSocket(request, socketListener)
                }.onFailure { error ->
                    statusText.text = "Could not create secure connection: ${error.message ?: "try again"}"
                }
            }
        }
    }

    private fun createPinnedClient(root: X509Certificate): OkHttpClient {
        val keyStore = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null, null)
            setCertificateEntry("motionlink-local-root", root)
        }
        val trustManagerFactory = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm())
            .apply { init(keyStore) }
        val trustManager = trustManagerFactory.trustManagers
            .filterIsInstance<X509TrustManager>()
            .single()
        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf(trustManager), SecureRandom())
        }

        return OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier { hostname, session ->
                HttpsURLConnection.getDefaultHostnameVerifier().verify(hostname, session)
            }
            .connectTimeout(5, TimeUnit.SECONDS)
            .readTimeout(0, TimeUnit.MILLISECONDS)
            .pingInterval(15, TimeUnit.SECONDS)
            .build()
    }

    private val socketListener = object : WebSocketListener() {
        override fun onOpen(webSocket: WebSocket, response: Response) {
            this@MainActivity.webSocket = webSocket
            mainHandler.post {
                statusText.text = "Securely connected to the PC."
                sendSettings()
            }
        }

        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
            if (this@MainActivity.webSocket === webSocket) this@MainActivity.webSocket = null
            mainHandler.post {
                stopMotion()
                statusText.text = "Disconnected from the PC."
            }
        }

        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
            if (this@MainActivity.webSocket === webSocket) this@MainActivity.webSocket = null
            mainHandler.post {
                stopMotion()
                statusText.text = "Connection failed: ${t.message ?: "check Wi-Fi, code, and firewall"}"
            }
        }
    }

    private fun startMotion() {
        if (webSocket == null) {
            statusText.text = "Connect to the PC before starting motion."
            return
        }
        val sensor = sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)
            ?: sensorManager.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)
        if (sensor == null) {
            statusText.text = "This phone does not provide a rotation-vector sensor."
            return
        }
        if (!isMotionRunning) {
            isMotionRunning = sensorManager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_GAME)
            if (!isMotionRunning) {
                statusText.text = "Could not start the phone motion sensor."
                return
            }
        }
        isPaused = false
        baseline = null
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        motionButton.text = "Motion active"
        pauseButton.text = "Pause"
        statusText.text = "Hold the phone still briefly to set the neutral position."
        send(MotionPacketEncoder.command("resume"))
    }

    private fun togglePause() {
        if (!isMotionRunning) {
            startMotion()
            return
        }
        isPaused = !isPaused
        if (!isPaused) baseline = null
        pauseButton.text = if (isPaused) "Resume" else "Pause"
        send(MotionPacketEncoder.command(if (isPaused) "pause" else "resume"))
        statusText.text = if (isPaused) "Motion paused." else "Motion resumed."
    }

    private fun recenter() {
        if (!isMotionRunning || isPaused) {
            statusText.text = "Start motion before recentering."
            return
        }
        baseline = null
        send(MotionPacketEncoder.command("recenter"))
        statusText.text = "Hold the phone still to set the new neutral position."
    }

    private fun pauseMotion() {
        if (!isMotionRunning || isPaused) return
        isPaused = true
        baseline = null
        send(MotionPacketEncoder.command("pause"))
        pauseButton.text = "Resume"
    }

    private fun stopMotion() {
        if (isMotionRunning) {
            send(MotionPacketEncoder.command("pause"))
            sensorManager.unregisterListener(this)
        }
        isMotionRunning = false
        isPaused = true
        baseline = null
        window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        if (::motionButton.isInitialized) {
            motionButton.text = "Start motion"
            pauseButton.text = "Pause"
        }
    }

    private fun disconnect() {
        stopMotion()
        webSocket?.close(1000, "Disconnected by user")
        webSocket = null
        httpClient?.dispatcher?.executorService?.shutdown()
        httpClient = null
        statusText.text = "Disconnected."
    }

    private fun sendSettings() {
        send(MotionPacketEncoder.settings(sensitivity, smoothing, deadzone))
    }

    private fun send(packet: String) {
        webSocket?.send(packet)
    }

    private fun button(title: String, action: () -> Unit): Button =
        Button(this).apply {
            text = title
            setOnClickListener { action() }
        }

    private fun label(value: String): TextView =
        text(value, 15f, true).apply { setPadding(0, dp(12), 0, dp(2)) }

    private fun text(value: String, size: Float, bold: Boolean = false): TextView =
        TextView(this).apply {
            text = value
            textSize = size
            if (bold) setTypeface(typeface, android.graphics.Typeface.BOLD)
        }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
}
