export function createPeerConnection({ iceServers = [] } = {}) {
  const config = {
    iceServers: iceServers.length ? iceServers : [{ urls: 'stun:stun.l.google.com:19302' }],
    iceCandidatePoolSize: 0
  };

  return new RTCPeerConnection(config);
}

export function createDataChannel(pc, label = 'motion') {
  return pc.createDataChannel(label, {
    ordered: false,
    maxRetransmits: 0,
    negotiated: false
  });
}

export function safeJsonParse(value) {
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}
