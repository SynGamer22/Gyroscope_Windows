package com.motionlinkvr.controller

import java.util.Locale

internal object MotionPacketEncoder {
    fun wrapDegrees(value: Float): Float {
        var wrapped = value % 360f
        if (wrapped > 180f) wrapped -= 360f
        if (wrapped < -180f) wrapped += 360f
        return wrapped
    }

    fun motion(yaw: Float, pitch: Float, roll: Float, timestamp: Long): String =
        String.format(
            Locale.US,
            """{"type":"motion","yaw":%.4f,"pitch":%.4f,"roll":%.4f,"timestamp":%d}""",
            yaw,
            pitch,
            roll,
            timestamp
        )

    fun settings(sensitivity: Float, smoothing: Float, deadzone: Float): String =
        String.format(
            Locale.US,
            """{"type":"settings","sensitivity":%.2f,"smoothing":%.2f,"deadzone":%.2f}""",
            sensitivity,
            smoothing,
            deadzone
        )

    fun command(type: String): String {
        require(type in setOf("pause", "resume", "recenter"))
        return """{"type":"$type"}"""
    }
}
