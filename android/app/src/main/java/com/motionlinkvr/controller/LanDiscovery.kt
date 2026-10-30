package com.motionlinkvr.controller

import java.net.Inet4Address

internal data class DiscoveredPc(val address: String, val httpPort: Int, val httpsPort: Int) {
    override fun toString(): String = "$address  (MotionLink VR)"
}

internal object LanDiscovery {
    fun isValidIpv4(value: String): Boolean {
        val octets = value.split('.')
        return octets.size == 4 && octets.all { octet ->
            octet.isNotEmpty()
                && octet.all(Char::isDigit)
                && octet.toIntOrNull()?.let { it in 0..255 } == true
        }
    }

    fun subnetHosts(address: Inet4Address, prefixLength: Int): List<String> {
        if (prefixLength !in 22..30) return emptyList()

        val addressBytes = address.address
        val addressValue = addressBytes.fold(0L) { value, byte ->
            (value shl 8) or (byte.toLong() and 0xff)
        }
        val mask = (0xffff_ffffL shl (32 - prefixLength)) and 0xffff_ffffL
        val network = addressValue and mask
        val broadcast = network or (mask xor 0xffff_ffffL)

        return (network + 1 until broadcast).asSequence()
            .filter { it != addressValue }
            .map { value ->
                listOf(24, 16, 8, 0).joinToString(".") { shift ->
                    ((value shr shift) and 0xff).toString()
                }
            }
            .toList()
    }

    fun isValidServerResponse(service: String, address: String, httpPort: Int, httpsPort: Int): Boolean =
        service == "MotionLinkVR"
            && isValidIpv4(address)
            && httpPort in 1..65535
            && httpsPort in 1..65535
}
