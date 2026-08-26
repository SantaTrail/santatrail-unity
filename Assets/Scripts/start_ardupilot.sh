#!/bin/bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARAM_FILE="${SCRIPT_DIR}/sitl_unity_safe.parm"
HOME_LAT="${1:-52.155719}"
HOME_LON="${2:-4.964212}"
HOME_ALT="${3:-10}"
HOME_YAW="${4:-0}"

find_ardupilot_binary() {
  if [ -n "${SANTATRAIL_ARDUPILOT_BIN:-}" ] && [ -f "$SANTATRAIL_ARDUPILOT_BIN" ] && [ -x "$SANTATRAIL_ARDUPILOT_BIN" ]; then
    printf '%s\n' "$SANTATRAIL_ARDUPILOT_BIN"
    return 0
  fi

  search_dir="$SCRIPT_DIR"
  while [ "$search_dir" != "/" ]; do
    candidate="$search_dir/ArduPilot/arducopter"
    if [ -f "$candidate" ] && [ -x "$candidate" ]; then
      printf '%s\n' "$candidate"
      return 0
    fi
    search_dir="$(dirname "$search_dir")"
  done

  candidate="$HOME/ardupilot/build/sitl/bin/arducopter"
  if [ -f "$candidate" ] && [ -x "$candidate" ]; then
    printf '%s\n' "$candidate"
    return 0
  fi

  return 1
}

ARDUCOPTER_BIN="$(find_ardupilot_binary || true)"

pkill -f "ArduPilot/arducopter" >/dev/null 2>&1 || true
pkill -f "/build/sitl/bin/arducopter" >/dev/null 2>&1 || true

# QGroundControl receives MAVLink on UDP 14550 and Unity on UDP 14551.
# Never kill processes only because they own a port; the targeted commands above
# remove only stale SantaTrail/ArduPilot SITL processes.

sleep 1

if [ ! -f "$PARAM_FILE" ]; then
  echo "Parameter file not found: $PARAM_FILE"
  exit 1
fi

if [ -z "$ARDUCOPTER_BIN" ]; then
  echo "ArduCopter SITL binary not found. Package ArduPilot/arducopter beside the game."
  exit 1
fi

SITL_STATE_DIR="${SANTATRAIL_SITL_STATE_DIR:-${HOME}/Library/Application Support/SantaTrail/SITL}"
mkdir -p "$SITL_STATE_DIR"
cd "$SITL_STATE_DIR"

exec "$ARDUCOPTER_BIN" \
  --wipe \
  --model quad \
  --home="${HOME_LAT},${HOME_LON},${HOME_ALT},${HOME_YAW}" \
  --defaults "$PARAM_FILE" \
  --serial0=udpclient:127.0.0.1:14550 \
  --serial1=udpclient:127.0.0.1:14551
