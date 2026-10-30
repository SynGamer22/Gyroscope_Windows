import { createMotionState, recenterMotion, updateMotionState } from './motion.js';
import { createPeerConnection, createDataChannel, safeJsonParse } from './webrtc.js';
import { createSupabaseClient, fetchSessionInfo, insertSignal } from './supabase.js';

const appState = {
  motion: createMotionState(),
  peerConnection: null,
  channel: null,
  isRunning: false,
  isPaused: false,
  sensorSupported: false,
  pairingCode: 'MOTION',
  latestTimestamp: 0,
  status: {
    connection: 'Disconnected',
    sensor: 'Waiting',
    latency: '—'
  }
};

const connectionBadge = document.getElementById('connectionBadge');
const connectionStatus = document.getElementById('connectionStatus');
const sensorStatus = document.getElementById('sensorStatus');
const latencyStatus = document.getElementById('latencyStatus');
const pairingCodeNode = document.getElementById('pairingCode');
const pitchValue = document.getElementById('pitchValue');
const yawValue = document.getElementById('yawValue');
const rollValue = document.getElementById('rollValue');
const motionMarker = document.getElementById('motionMarker');
const startMotionButton = document.getElementById('startMotionButton');
const pauseButton = document.getElementById('pauseButton');
const disconnectButton = document.getElementById('disconnectButton');
const recenterButton = document.getElementById('recenterButton');
const sensitivityRange = document.getElementById('sensitivityRange');
const smoothingRange = document.getElementById('smoothingRange');
const deadzoneRange = document.getElementById('deadzoneRange');
const sensitivityValue = document.getElementById('sensitivityValue');
const smoothingValue = document.getElementById('smoothingValue');
const deadzoneValue = document.getElementById('deadzoneValue');

function setConnection(state) {
  appState.status.connection = state;
  connectionBadge.textContent = state;
  connectionBadge.className = `badge ${state === 'Connected' ? 'connected' : 'disconnected'}`;
  connectionStatus.textContent = state;
}

function updateSensorStatus(label) {
  appState.status.sensor = label;
  sensorStatus.textContent = label;
}

function updateReadouts(yaw, pitch, roll) {
  pitchValue.textContent = `${pitch.toFixed(2)}°`;
  yawValue.textContent = `${yaw.toFixed(2)}°`;
  rollValue.textContent = `${roll.toFixed(2)}°`;

  const x = 50 + yaw * 1.5;
  const y = 50 + pitch * 1.5;
  motionMarker.style.transform = `translate(${x - 50}%, ${y - 50}%)`;
}

function handleOrientationEvent(event) {
  if (!appState.isRunning || appState.isPaused) return;

  const pitch = Number.isFinite(event.beta) ? event.beta : 0;
  const yaw = Number.isFinite(event.gamma) ? event.gamma : 0;
  const roll = Number.isFinite(event.alpha) ? event.alpha : 0;

  if (!Number.isFinite(pitch) || !Number.isFinite(yaw) || !Number.isFinite(roll)) {
    updateSensorStatus('Invalid');
    return;
  }

  updateSensorStatus('Active');
  const timestamp = Date.now();
  const updated = updateMotionState(appState.motion, pitch, yaw, roll, timestamp);
  if (!updated) {
    return;
  }

  updateReadouts(appState.motion.yaw, appState.motion.pitch, appState.motion.roll);

  const payload = {
    yaw: Number(appState.motion.yaw.toFixed(3)),
    pitch: Number(appState.motion.pitch.toFixed(3)),
    roll: Number(appState.motion.roll.toFixed(3)),
    timestamp
  };

  if (appState.channel && appState.channel.readyState === 'open') {
    appState.channel.send(JSON.stringify(payload));
  }
}

async function requestSensorPermission() {
  if (!('DeviceOrientationEvent' in window)) {
    updateSensorStatus('Unsupported');
    throw new Error('Your browser does not support the Device Orientation API.');
  }

  try {
    if (typeof DeviceOrientationEvent.requestPermission === 'function') {
      const response = await DeviceOrientationEvent.requestPermission();
      if (response !== 'granted') {
        throw new Error('Motion permission was denied.');
      }
    }

    appState.sensorSupported = true;
    updateSensorStatus('Ready');
    return true;
  } catch (error) {
    updateSensorStatus('Denied');
    throw error;
  }
}

function bindControls() {
  sensitivityRange.addEventListener('input', (event) => {
    const value = Number(event.target.value);
    appState.motion.sensitivity = value;
    sensitivityValue.textContent = `${value.toFixed(1)}x`;
  });

  smoothingRange.addEventListener('input', (event) => {
    const value = Number(event.target.value);
    appState.motion.smoothing = value;
    smoothingValue.textContent = value.toFixed(2);
  });

  deadzoneRange.addEventListener('input', (event) => {
    const value = Number(event.target.value);
    appState.motion.deadzone = value;
    deadzoneValue.textContent = `${value.toFixed(1)}°`;
  });

  startMotionButton.addEventListener('click', async () => {
    try {
      await requestSensorPermission();
      appState.isRunning = true;
      appState.isPaused = false;
      window.addEventListener('deviceorientation', handleOrientationEvent, true);
      setConnection('Connected');
      startMotionButton.textContent = 'Motion Live';
    } catch (error) {
      console.error(error);
      updateSensorStatus('Error');
    }
  });

  pauseButton.addEventListener('click', () => {
    appState.isPaused = !appState.isPaused;
    pauseButton.textContent = appState.isPaused ? 'Resume' : 'Pause';
    setConnection(appState.isPaused ? 'Paused' : 'Connected');
  });

  disconnectButton.addEventListener('click', () => {
    appState.isRunning = false;
    appState.isPaused = true;
    window.removeEventListener('deviceorientation', handleOrientationEvent, true);
    if (appState.peerConnection) {
      appState.peerConnection.close();
      appState.peerConnection = null;
    }
    if (appState.channel) {
      appState.channel.close();
      appState.channel = null;
    }
    setConnection('Disconnected');
    updateSensorStatus('Offline');
  });

  recenterButton.addEventListener('click', () => {
    recenterMotion(appState.motion, appState.motion.pitch, appState.motion.yaw, appState.motion.roll);
    updateReadouts(0, 0, 0);
  });
}

async function initializeWebRtc() {
  const url = window.location.origin;
  if (!url || url.includes('localhost') || url.includes('127.0.0.1')) {
    console.warn('Development mode is active; static WebRTC pair flow is limited to local network testing.');
  }

  const code = appState.pairingCode;
  pairingCodeNode.textContent = code;

  const supabaseUrl = (window.__SUPABASE_URL__ || '').trim();
  const supabaseKey = (window.__SUPABASE_PUBLISHABLE_KEY__ || '').trim();

  if (supabaseUrl && supabaseKey) {
    try {
      const supabase = await createSupabaseClient({ url: supabaseUrl, anonKey: supabaseKey });
      const session = await fetchSessionInfo(supabase, code);
      if (session) {
        const peerConnection = createPeerConnection({ iceServers: [] });
        const channel = createDataChannel(peerConnection, 'motion');
        appState.peerConnection = peerConnection;
        appState.channel = channel;
        setConnection('Connected');
        channel.onopen = () => setConnection('Connected');
        channel.onclose = () => setConnection('Disconnected');
      }
    } catch (error) {
      console.warn('Supabase setup is incomplete; running in offline mode.', error);
    }
  }
}

bindControls();
initializeWebRtc();

window.addEventListener('beforeunload', () => {
  if (appState.peerConnection) appState.peerConnection.close();
});
