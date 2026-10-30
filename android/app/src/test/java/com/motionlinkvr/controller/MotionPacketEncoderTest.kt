package com.motionlinkvr.controller

import org.junit.Assert.assertEquals
import org.junit.Test

class MotionPacketEncoderTest {
    @Test
    fun wrapsAnglesAcrossTheHeadingBoundary() {
        assertEquals(-179f, MotionPacketEncoder.wrapDegrees(181f), 0.001f)
        assertEquals(179f, MotionPacketEncoder.wrapDegrees(-181f), 0.001f)
        assertEquals(45f, MotionPacketEncoder.wrapDegrees(405f), 0.001f)
    }

    @Test
    fun encodesMotionWithServerCompatibleShape() {
        assertEquals(
            """{"type":"motion","yaw":12.5000,"pitch":-4.2500,"roll":1.5000,"timestamp":1234}""",
            MotionPacketEncoder.motion(12.5f, -4.25f, 1.5f, 1234)
        )
    }

    @Test
    fun encodesSettingsAndOnlySupportedCommands() {
        assertEquals(
            """{"type":"settings","sensitivity":1.20,"smoothing":0.20,"deadzone":0.60}""",
            MotionPacketEncoder.settings(1.2f, 0.2f, 0.6f)
        )
        assertEquals("""{"type":"pause"}""", MotionPacketEncoder.command("pause"))
    }
}
