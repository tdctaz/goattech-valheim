#!/bin/bash

SA_NAME="${SA_NAME:-ServerAuthority Test}"
SA_WORLD="${SA_WORLD:-SrvAuthTest}"
SA_PASSWORD="${SA_PASSWORD:-changeme123}"
SA_PORT="${SA_PORT:-2456}"
SA_PUBLIC="${SA_PUBLIC:-0}"
SA_SAVEINTERVAL="${SA_SAVEINTERVAL:-120}"

here="$(cd "$(dirname "$0")" && pwd)"
cd "$here" || exit 1

if [ ! -x ./valheim_server.x86_64 ]; then
  echo
  echo "ERROR: valheim_server.x86_64 is not in this folder."
  echo "  This folder: $here"
  echo
  echo "Copy everything from server-files into the folder that contains"
  echo "valheim_server.x86_64, then run it from there."
  exit 1
fi

logdir="$here/ServerAuthority-logs"
mkdir -p "$logdir"

export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll
export LD_LIBRARY_PATH="./doorstop_libs:./linux64:$LD_LIBRARY_PATH"
export LD_PRELOAD="libdoorstop_x64.so:$LD_PRELOAD"
export SteamAppId=892970

stopping=0
trap 'stopping=1' INT TERM

echo
echo "Server Authority watchdog"
echo "  world : $SA_WORLD"
echo "  port  : $SA_PORT"
echo "  saves : every $SA_SAVEINTERVAL seconds"
echo "  logs  : $logdir"
echo
echo "Press Ctrl+C to stop the server properly."
echo

run=0
fast_failures=0

while true; do
  run=$((run + 1))
  stamp=$(date +%Y%m%d-%H%M%S)
  log="$logdir/server-$stamp.log"
  started=$(date +%s)

  echo "[$(date +%H:%M:%S)] starting server (run $run) -> $(basename "$log")"

  ./valheim_server.x86_64 \
    -name "$SA_NAME" \
    -port "$SA_PORT" \
    -world "$SA_WORLD" \
    -password "$SA_PASSWORD" \
    -public "$SA_PUBLIC" \
    -saveinterval "$SA_SAVEINTERVAL" \
    -backups 8 2>&1 | tee -i "$log"
  code=${PIPESTATUS[0]}
  ran_for=$(( $(date +%s) - started ))

  if [ "$code" -eq 0 ] || [ "$stopping" -eq 1 ]; then
    echo "[$(date +%H:%M:%S)] server stopped after ${ran_for}s, exit code $code."
    exit 0
  fi

  echo
  echo "[$(date +%H:%M:%S)] SERVER DIED, exit code $code, after ${ran_for}s"

  crash="$logdir/CRASH-$stamp-exit$code.log"
  cp "$log" "$crash"
  {
    echo "=== Server Authority crash, exit code $code, ran ${ran_for}s, $(date) ==="
    echo
    echo "Interesting lines from this run:"
    grep -E 'Exception|Assertion|Caught fatal signal|stack frames|Server Authority' "$log" | tail -60
  } > "$logdir/CRASH-$stamp-summary.txt"
  if [ -f BepInEx/LogOutput.log ]; then
    cp BepInEx/LogOutput.log "$logdir/CRASH-$stamp-BepInEx.log"
  fi
  echo "                   evidence kept: $(basename "$crash")"

  if [ "$ran_for" -lt 15 ]; then
    fast_failures=$((fast_failures + 1))
    if [ "$fast_failures" -ge 3 ]; then
      echo
      echo "The server has died immediately three times in a row."
      echo "It is not crashing during play, it is failing to start. Usual causes:"
      echo "  - the world name or password at the top of this script is not valid"
      echo "    (the password must be at least 5 characters and cannot be in the server name)"
      echo "  - port $SA_PORT is already in use by another server"
      echo "  - these files are in the wrong folder"
      echo
      echo "Check the newest log in ServerAuthority-logs, then run this again."
      exit 1
    fi
  else
    fast_failures=0
  fi

  echo "[$(date +%H:%M:%S)] restarting in 5 seconds..."
  sleep 5
done
