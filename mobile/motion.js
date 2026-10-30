export function normalizeAngle(value) {
  let normalized = value % 360;
  if (normalized > 180) normalized -= 360;
  if (normalized < -180) normalized += 360;
  return normalized;
}

export function clamp(value, min, max) {
  if (!Number.isFinite(value)) return min;
  return Math.min(Math.max(value, min), max);
}

export function createMotionState() {
  return {
    pitch: 0,
    yaw: 0,
    roll: 0,
    offsetPitch: 0,
    offsetYaw: 0,
    offsetRoll: 0,
    lastTimestamp: 0,
    isActive: false,
    smoothing: 0.2,
    sensitivity: 1.0,
    deadzone: 0.6,
    isPaused: false
  };
}

export function recenterMotion(state, pitch, yaw, roll) {
  state.offsetPitch = pitch;
  state.offsetYaw = yaw;
  state.offsetRoll = roll;
  state.pitch = 0;
  state.yaw = 0;
  state.roll = 0;
}

export function updateMotionState(state, pitch, yaw, roll, timestamp) {
  if (!Number.isFinite(pitch) || !Number.isFinite(yaw) || !Number.isFinite(roll)) {
    return false;
  }

  const normalizedPitch = normalizeAngle(pitch);
  const normalizedYaw = normalizeAngle(yaw);
  const normalizedRoll = normalizeAngle(roll);

  const relativePitch = normalizeAngle(normalizedPitch - state.offsetPitch);
  const relativeYaw = normalizeAngle(normalizedYaw - state.offsetYaw);
  const relativeRoll = normalizeAngle(normalizedRoll - state.offsetRoll);

  if (Math.abs(relativePitch) <= state.deadzone) {
    state.pitch = 0;
  } else {
    const target = relativePitch * state.sensitivity;
    state.pitch = state.pitch + (target - state.pitch) * (1 - state.smoothing);
  }

  if (Math.abs(relativeYaw) <= state.deadzone) {
    state.yaw = 0;
  } else {
    const target = relativeYaw * state.sensitivity;
    state.yaw = state.yaw + (target - state.yaw) * (1 - state.smoothing);
  }

  state.roll = relativeRoll;
  state.lastTimestamp = timestamp;
  return true;
}
