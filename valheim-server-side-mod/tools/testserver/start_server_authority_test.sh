#!/bin/sh
# Local test server for the Server Authority mod. Not managed by Steam.
#
# Saves every 2 minutes rather than the default 30, because a crash loses
# everything since the last save and this build is expected to crash.
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll

export LD_LIBRARY_PATH="./doorstop_libs:$LD_LIBRARY_PATH"
export LD_PRELOAD="libdoorstop_x64.so:$LD_PRELOAD"

export LD_LIBRARY_PATH="./linux64:$LD_LIBRARY_PATH"
export XDG_CONFIG_HOME="$(pwd)/server_config"
export SteamAppId=892970

exec ./valheim_server.x86_64 \
  -name "ServerAuthority Test" \
  -port 2456 \
  -world "SrvAuthTest" \
  -public 0 \
  -saveinterval 120 \
  -backups 8
