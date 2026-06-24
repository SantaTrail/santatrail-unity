#!/bin/bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARAM_FILE="${SCRIPT_DIR}/sitl_unity_safe.parm"
HOME_LAT="${1:-13.8455}"
HOME_LON="${2:-100.5688}"
HOME_ALT="${3:-10}"
HOME_YAW="${4:-0}"

pkill -f "sim_vehicle.py.*ArduCopter" >/dev/null 2>&1 || true
pkill -f "/build/sitl/bin/arducopter" >/dev/null 2>&1 || true
pkill -f "mavproxy.py.*tcp:127.0.0.1:5760" >/dev/null 2>&1 || true

for port in 5760 14550 14551; do
  pids=$(lsof -ti tcp:$port 2>/dev/null || true)
  if [ -n "$pids" ]; then
    kill -9 $pids >/dev/null 2>&1 || true
  fi

  pids=$(lsof -ti udp:$port 2>/dev/null || true)
  if [ -n "$pids" ]; then
    kill -9 $pids >/dev/null 2>&1 || true
  fi
done

sleep 1

cd ~/ardupilot/ArduCopter || exit

if [ ! -f "$PARAM_FILE" ]; then
  echo "Parameter file not found: $PARAM_FILE"
  exit 1
fi

python3 ../Tools/autotest/sim_vehicle.py \
-v ArduCopter \
--no-rebuild \
-w \
--console \
--custom-location="${HOME_LAT},${HOME_LON},${HOME_ALT},${HOME_YAW}" \
--add-param-file="$PARAM_FILE" \
--mavproxy-args='--cmd="set mavfwd true;param set AVOID_ENABLE 0;param set OA_TYPE 0;param set FENCE_ENABLE 0;param set FS_THR_ENABLE 0;param set FS_DR_ENABLE 0;param set FS_EKF_ACTION 0;param set FS_OPTIONS 0;param set GUID_OPTIONS 0;param set GUID_TIMEOUT 0;param set FLTMODE_CH 0;param set RC7_OPTION 0;param set WPNAV_SPEED 700;param set WPNAV_ACCEL 250;param set WPNAV_ACCEL_Z 150;param set WPNAV_RADIUS 200;param set EK3_IMU_MASK 1;param set EK3_PRIMARY 0"' \
--out=udp:127.0.0.1:14550 \
--out=udp:127.0.0.1:14551
