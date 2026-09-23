#!/bin/bash
# Keeps the test server up and preserves the evidence when it dies.
#
# A Mono abort kills the process rather than raising an exception, so without
# this a crash looks like silence. Each run gets its own log; any run that ends
# abnormally has its log kept aside with the fatal signal and the frames before
# it, which is the part that identifies the fault.
here="$(cd "$(dirname "$0")" && pwd)"
logdir="${1:-$here/authority_logs}"
mkdir -p "$logdir"

while true; do
  stamp=$(date +%Y%m%d-%H%M%S)
  log="$logdir/server-$stamp.log"
  echo "[watchdog] starting, logging to $log"
  ( cd "$here" && ./start_server_authority_test.sh ) > "$log" 2>&1
  code=$?

  if [ $code -eq 0 ]; then
    echo "[watchdog] server exited cleanly"
    exit 0
  fi

  crash="$logdir/CRASH-$stamp-exit$code.log"
  cp "$log" "$crash"
  {
    echo "=== exit code $code at $(date) ==="
    grep -nE 'Exception|Assertion|Caught fatal signal|Obtained [0-9]+ stack frames' "$log" | tail -40
  } > "$logdir/CRASH-$stamp-summary.txt"
  echo "[watchdog] server died with $code, evidence in $crash"
  sleep 5
done
