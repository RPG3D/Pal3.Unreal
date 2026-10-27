#!/bin/bash
# build-ios-mac.sh - iOS device packaging with editor runtime matrix (Mac)
#
# Companion of build-android-mac.sh. Target runtime is Mono only for now
# (iOS CoreCLR rides the dotnet-11 line and is handled separately), so the
# matrix here is: editor Mono or CoreCLR x iOS target Mono.
#
# Usage: ./build-ios-mac.sh <editorRuntime> [targetRuntime]
#   editorRuntime: Mono (default) | CoreCLR
#   targetRuntime: Mono (default) | CoreCLR  (IOSEngine.ini DotNetRuntime)
#
# Output lands in: Packaged/IOS-<editorRuntime>-<targetRuntime>/
set -euo pipefail

PROJ=/Users/admin/Documents/GitHub/SharpDemo
UPROJECT="$PROJ/SharpDemo.uproject"
ENGINE=/Users/admin/Documents/Code/UE5/Engine
UAT="$ENGINE/Build/BatchFiles/RunUAT.sh"
BUILD="$ENGINE/Build/BatchFiles/Mac/Build.sh"
SCRIPTS="$PROJ/Plugins/UnrealSharp/Build/Scripts"
LOGS=/tmp/ios-matrix
mkdir -p "$LOGS"

EDITOR_RT="${1:-Mono}"
TARGET_RT="${2:-Mono}"
case "$EDITOR_RT" in Mono|CoreCLR) ;; *) echo "Usage: $0 <Mono|CoreCLR> [Mono|CoreCLR]"; exit 1 ;; esac
case "$TARGET_RT" in Mono|CoreCLR) ;; *) echo "Usage: $0 <Mono|CoreCLR> [Mono|CoreCLR]"; exit 1 ;; esac

log() { echo "[$(date +%H:%M:%S)] $*"; }

# ---- Step 1: editor runtime ini ----------------------------------------------
log "iOS combo: editor=$EDITOR_RT target=$TARGET_RT"
sed -i '' -e "s/^DotNetRuntimeEditor=.*/DotNetRuntimeEditor=$EDITOR_RT/" \
    "$PROJ/Config/DefaultEngine.ini"
grep -n "DotNetRuntimeEditor" "$PROJ/Config/DefaultEngine.ini"

# Target runtime for iOS lives in the platform config layer.
IOSINI="$PROJ/Config/IOS/IOSEngine.ini"
if [ "$TARGET_RT" = "CoreCLR" ]; then
  printf '\n[UnrealSharp]\nDotNetRuntime=CoreCLR\n' >> "$IOSINI"
else
  python3 - "$IOSINI" <<'PY'
import sys, re
p = sys.argv[1]
s = open(p).read()
s = re.sub(r'\n\[UnrealSharp\]\nDotNetRuntime=[^\n]*\n', '\n', s)
open(p, 'w').write(s)
PY
fi
grep -A2 "^\[UnrealSharp\]" "$IOSINI" || echo "(IOSEngine.ini: default Mono)"

# UBT caches Build.cs evaluation per makefile; force re-evaluation.
touch "$PROJ/Plugins/UnrealSharp/Source/ThirdParty/MonoSDK/MonoSDK.Build.cs" \
      "$PROJ/Plugins/UnrealSharp/Source/ThirdParty/CoreClrSDK/CoreClrSDK.Build.cs"

# ---- Step 2: Game-target UBT build -------------------------------------------
log "Step 2: Game-target UBT build"
"$BUILD" SharpDemo Mac Development -project="$UPROJECT" \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-game.log" 2>&1 || { echo "FAIL step 2"; tail -20 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-game.log"; exit 1; }
log "Step 2 OK"

# ---- Step 3: PackageProjectMobile (IOS) ---------------------------------------
log "Step 3: PackageProjectMobile -TargetPlatform=IOS"
"$UAT" PackageProjectMobile -ScriptDir="$SCRIPTS" -Project="$UPROJECT" \
  -UEBuildConfig=Development -TargetPlatform=IOS -nocompileuat \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-package.log" 2>&1 || { echo "FAIL step 3"; tail -20 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-package.log"; exit 1; }
log "Step 3 OK: $(ls "$PROJ/Content/Managed/IOS" | wc -l | tr -d ' ') files in Content/Managed/IOS"

# ---- Step 4: Editor UBT build --------------------------------------------------
log "Step 4: Editor UBT build"
"$BUILD" SharpDemoEditor Mac Development -project="$UPROJECT" \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-editor.log" 2>&1 || { echo "FAIL step 4"; tail -20 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-editor.log"; exit 1; }
log "Step 4 OK"

# ---- Step 5: BuildCookRun -> .app ----------------------------------------------
ARCHIVE="$PROJ/Packaged/IOS-$EDITOR_RT-$TARGET_RT"
rm -rf "$ARCHIVE"; mkdir -p "$ARCHIVE"
log "Step 5: BuildCookRun -> $ARCHIVE"
"$UAT" BuildCookRun -project="$UPROJECT" -Build -Target=SharpDemo \
  -TargetPlatform=IOS -ClientConfig=Development -Cook -Pak -Stage \
  -Archive -package -nocompileuat -archivedirectory="$ARCHIVE" \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-cook.log" 2>&1 || { echo "FAIL step 5"; tail -30 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-cook.log"; exit 1; }
log "Step 5 OK"
ls -la "$ARCHIVE" | head -6
log "iOS combo editor=$EDITOR_RT target=$TARGET_RT COMPLETE"
