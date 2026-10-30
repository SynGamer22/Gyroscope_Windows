import { createMotionState, normalizeAngle, recenterMotion, updateMotionState } from './motion.js';

const pairingCode = new URLSearchParams(location.search).get('code') || '';
const socketUrl = pairingCode ? `wss://${location.host}/motion?code=${encodeURIComponent(pairingCode)}` : '';
const appState = {
  motion: createMotionState(),
  socket: null,
  socketRetries: 0,
  allowReconnect: true,
  isRunning: false,
  isPaused: false,
  firstSensorEvent: true,
  latestRaw: null
};

const connectionBadge = document.getElementById('connectionBadge');
const connectionStatus = document.getElementById('connectionStatus');
const sensorStatus = document.getElementById('sensorStatus');
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
const errorMessage = document.getElementById('errorMessage');

function setConnection(state, connected = false) {
  connectionBadge.textContent = state;
  connectionBadge.className = `badge ${connected ? 'connected' : 'disconnected'}`;
  connectionStatus.textContent = state;
}

function showError(message) {
  errorMessage.textContent = message;
  errorMessage.hidden = !message;
}

function sendPacket(packet) {
  if (!appState.socket || appState.socket.readyState !== WebSocket.OPEN) return false;
  if (appState.socket.bufferedAmount > 8192) return false;
  appState.socket.send(JSON.stringify(packet));
  return true;
}

function sendSettings() {
  sendPacket({
    type: 'settings',
    sensitivity: Number(sensitivityRange.value),
    smoothing: Number(smoothingRange.value),
    deadzone: Number(deadzoneRange.value)
  });
}

function connectSocket() {
  if (!socketUrl || !appState.allowReconnect) {
    setConnection('No pairing code');
    return;
  }
  if (appState.socket && [WebSocket.OPEN, WebSocket.CONNECTING].includes(appState.socket.readyState)) return;

  setConnection('Connecting');
  const socket = new WebSocket(socketUrl);
  appState.socket = socket;
  socket.addEventListener('open', () => {
    appState.socketRetries = 0;
    setConnection('PC connected', true);
    sendSettings();
    if (appState.isRunning && !appState.isPaused) sendPacket({ type: 'resume' });
  });
  socket.addEventListener('close', () => {
    if (appState.socket === socket) appState.socket = null;
    setConnection('PC disconnected');
    if (appState.allowReconnect) {
      const delay = Math.min(5000, 500 * (2 ** appState.socketRetries));
      appState.socketRetries += 1;
      window.setTimeout(connectSocket, delay);
    }
  });
  socket.addEventListener('error', () => setConnection('Cannot reach PC'));
}

function updateReadouts(yaw, pitch, roll) {
  pitchValue.textContent = `${pitch.toFixed(2)}°`;
  yawValue.textContent = `${yaw.toFixed(2)}°`;
  rollValue.textContent = `${roll.toFixed(2)}°`;
  motionMarker.style.left = `${Math.max(12, Math.min(88, 50 + yaw * 1.2))}%`;
  motionMarker.style.top = `${Math.max(12, Math.min(88, 50 + pitch * 1.2))}%`;
}

function handleOrientationEvent(event) {
  if (!appState.isRunning || appState.isPaused) return;
  if (!Number.isFinite(event.beta) || !Number.isFinite(event.gamma) || !Number.isFinite(event.alpha)) {
    sensorStatus.textContent = 'Unreliable data';
    return;
  }

  const raw = {
    pitch: normalizeAngle(event.beta),
    yaw: normalizeAngle(event.alpha),
    roll: normalizeAngle(event.gamma)
  };
  appState.latestRaw = raw;
  if (appState.firstSensorEvent) {
    recenterMotion(appState.motion, raw.pitch, raw.yaw, raw.roll);
    appState.firstSensorEvent = false;
  }

  if (!updateMotionState(appState.motion, raw.pitch, raw.yaw, raw.roll, Date.now())) return;
  sensorStatus.textContent = 'Active';
  updateReadouts(appState.motion.yaw, appState.motion.pitch, appState.motion.roll);
  sendPacket({
    type: 'motion',
    yaw: appState.motion.yaw,
    pitch: appState.motion.pitch,
    roll: appState.motion.roll,
    timestamp: Date.now()
  });
}

async function requestSensorPermission() {
  if (!('DeviceOrientationEvent' in window)) {
    throw new Error('This browser does not support orientation sensors. Use Android Chrome.');
  }
  if (typeof DeviceOrientationEvent.requestPermission === 'function') {
    const response = await DeviceOrientationEvent.requestPermission();
    if (response !== 'granted') throw new Error('Motion permission was denied. Allow motion access in the browser settings.');
  }
}

function bindControls() {
  for (const control of [sensitivityRange, smoothingRange, deadzoneRange]) {
    control.addEventListener('input', () => {
      sensitivityValue.textContent = `${Number(sensitivityRange.value).toFixed(1)}x`;
      smoothingValue.textContent = Number(smoothingRange.value).toFixed(2);
      deadzoneValue.textContent = `${Number(deadzoneRange.value).toFixed(1)}°`;
      sendSettings();
    });
  }

  startMotionButton.addEventListener('click', async () => {
    showError('');
    try {
      await requestSensorPermission();
      appState.isRunning = true;
      appState.isPaused = false;
      appState.firstSensorEvent = true;
      window.addEventListener('deviceorientation', handleOrientationEvent, true);
      sensorStatus.textContent = 'Waiting for sensor';
      startMotionButton.textContent = 'Motion Live';
      pauseButton.textContent = 'Pause';
      sendPacket({ type: 'resume' });
    } catch (error) {
      sensorStatus.textContent = 'Unavailable';
      showError(error instanceof Error ? error.message : 'Could not start motion sensors.');
    }
  });

  pauseButton.addEventListener('click', () => {
    appState.isPaused = !appState.isPaused;
    pauseButton.textContent = appState.isPaused ? 'Resume' : 'Pause';
    sendPacket({ type: appState.isPaused ? 'pause' : 'resume' });
    const open = appState.socket?.readyState === WebSocket.OPEN;
    setConnection(appState.isPaused ? 'Paused' : open ? 'PC connected' : 'PC disconnected', open && !appState.isPaused);
  });

  disconnectButton.addEventListener('click', () => {
    appState.allowReconnect = false;
    appState.isRunning = false;
    appState.isPaused = true;
    window.removeEventListener('deviceorientation', handleOrientationEvent, true);
    sendPacket({ type: 'pause' });
    appState.socket?.close();
    appState.socket = null;
    sensorStatus.textContent = 'Offline';
    setConnection('Disconnected');
  });

  recenterButton.addEventListener('click', () => {
    if (!appState.latestRaw) return;
    recenterMotion(appState.motion, appState.latestRaw.pitch, appState.latestRaw.yaw, appState.latestRaw.roll);
    updateReadouts(0, 0, 0);
    sendPacket({ type: 'recenter' });
  });
}

pairingCodeNode.textContent = pairingCode || 'Open the PC-provided URL';
if (!window.isSecureContext && location.hostname !== 'localhost') {
  showError('This page is not a trusted HTTPS origin. Install the local certificate using the setup link from the PC app, then reload.');
}
bindControls();
connectSocket();

window.addEventListener('pagehide', () => {
  sendPacket({ type: 'pause' });
  appState.socket?.close();
});
