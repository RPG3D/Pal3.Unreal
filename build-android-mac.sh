#!/bin/bash
# build-android-mac.sh - Android packaging with editor×target runtime matrix (Mac)
#
# Verifies host(dotnet runtime in editor) and target(dotnet runtime in APK)
# independence across all four combinations, mirroring the Win11 matrix:
#   A: editor Mono    x target Mono     (current defaults)
#   B: editor Mono    x target CoreCLR  (AndroidEngine.ini DotNetRuntime=CoreCLR)
#   C: editor CoreCLR x target Mono     (DefaultEngine.ini DotNetRuntimeEditor=CoreCLR)
#   D: editor CoreCLR x target CoreCLR
#
# Usage: ./build-android-mac.sh <A|B|C|D>
#
# Per-combo flow (mirrors BuildAndroid.bat):
#   1. write combo ini state (DotNetRuntimeEditor / Android DotNetRuntime)
#   2. Game-target UBT build (Mac host; generates UHT/Game glue)
#   3. RunUAT PackageProjectMobile (publish managed -> Content/Managed/Android)
#   4. Editor UBT build (cook prerequisite; backfills flat Binaries/Managed)
#   5. RunUAT BuildCookRun (cook+pak+stage+archive -> APK)
#
# APK lands in: Packaged/Android-<combo>/
set -euo pipefail

PROJ=/Users/admin/Documents/GitHub/SharpDemo
UPROJECT="$PROJ/SharpDemo.uproject"
ENGINE=/Users/admin/Documents/Code/UE5/Engine
UAT="$ENGINE/Build/BatchFiles/RunUAT.sh"
BUILD="$ENGINE/Build/BatchFiles/Mac/Build.sh"
SCRIPTS="$PROJ/Plugins/UnrealSharp/Build/Scripts"
LOGS=/tmp/android-matrix
mkdir -p "$LOGS"

COMBO="${1:-A}"
case "$COMBO" in
  A) EDITOR_RT=Mono;    TARGET_RT=Mono    ;;
  B) EDITOR_RT=Mono;    TARGET_RT=CoreCLR ;;
  C) EDITOR_RT=CoreCLR; TARGET_RT=Mono    ;;
  D) EDITOR_RT=CoreCLR; TARGET_RT=CoreCLR ;;
  *) echo "Usage: $0 <A|B|C|D>"; exit 1 ;;
esac

export ANDROID_HOME="$HOME/Library/Android/sdk"
export NDKROOT="$ANDROID_HOME/ndk/27.2.12479018"
[ -d "$NDKROOT" ] || { echo "NDKROOT missing: $NDKROOT"; exit 1; }

log() { echo "[$(date +%H:%M:%S)] $*"; }

# ---- Step 1: combo ini state -------------------------------------------------
log "Combo $COMBO: editor=$EDITOR_RT target=$TARGET_RT"
sed -i '' -e "s/^DotNetRuntimeEditor=.*/DotNetRuntimeEditor=$EDITOR_RT/" \
    "$PROJ/Config/DefaultEngine.ini"
mkdir -p "$PROJ/Config/Android"
if [ "$TARGET_RT" = "CoreCLR" ]; then
  printf '[UnrealSharp]\nDotNetRuntime=CoreCLR\n' > "$PROJ/Config/Android/AndroidEngine.ini"
else
  # restore to empty (platform default Mono)
  : > "$PROJ/Config/Android/AndroidEngine.ini"
fi
grep -n "DotNetRuntimeEditor" "$PROJ/Config/DefaultEngine.ini"
[ -s "$PROJ/Config/Android/AndroidEngine.ini" ] && cat "$PROJ/Config/Android/AndroidEngine.ini" || echo "(AndroidEngine.ini: default Mono)"

# UBT caches Build.cs evaluation results per makefile; ini-only changes don't
# invalidate them. Touch both SDK Build.cs files so the runtime-selection gate
# (ShouldUseMono) is re-evaluated with the new ini state.
touch "$PROJ/Plugins/UnrealSharp/Source/ThirdParty/MonoSDK/MonoSDK.Build.cs" \
      "$PROJ/Plugins/UnrealSharp/Source/ThirdParty/CoreClrSDK/CoreClrSDK.Build.cs"

# ---- Step 1.5: ABI gate -------------------------------------------------------
# CoreCLR's interpreter rejects calli that need P/Invoke marshalling (string/ref/
# out/bool), and the constraint is identical on 10 and 11. Fail before packaging
# instead of discovering it on device.
if [ -x "$PROJ/Plugins/UnrealSharp/Tools/verify-calli-abi.py" ]; then
  log "Step 1.5: calli ABI gate"
  "$PROJ/Plugins/UnrealSharp/Tools/verify-calli-abi.py" > "$LOGS/calli-gate.log" 2>&1 \
    || { echo "FAIL calli ABI gate (see $LOGS/calli-gate.log)"; tail -20 "$LOGS/calli-gate.log"; exit 1; }
  log "Step 1.5 OK"
fi

# ---- Step 2: Game-target UBT build (UHT/Game glue) ---------------------------
log "Step 2: Game-target UBT build"
"$BUILD" SharpDemo Mac Development -project="$UPROJECT" \
  > "$LOGS/combo$COMBO-game.log" 2>&1 || { echo "FAIL step 2"; tail -20 "$LOGS/combo$COMBO-game.log"; exit 1; }
log "Step 2 OK"

# ---- Step 3: PackageProjectMobile --------------------------------------------
log "Step 3: PackageProjectMobile"
"$UAT" PackageProjectMobile -ScriptDir="$SCRIPTS" -Project="$UPROJECT" \
  -UEBuildConfig=Development -TargetPlatform=Android -nocompileuat \
  > "$LOGS/combo$COMBO-package.log" 2>&1 || { echo "FAIL step 3"; tail -20 "$LOGS/combo$COMBO-package.log"; exit 1; }
log "Step 3 OK: $(ls "$PROJ/Content/Managed/Android" | wc -l | tr -d ' ') files in Content/Managed/Android"

# ---- Step 4: Editor UBT build (cook prerequisite) ----------------------------
log "Step 4: Editor UBT build"
"$BUILD" SharpDemoEditor Mac Development -project="$UPROJECT" \
  > "$LOGS/combo$COMBO-editor.log" 2>&1 || { echo "FAIL step 4"; tail -20 "$LOGS/combo$COMBO-editor.log"; exit 1; }
log "Step 4 OK"

# ---- Step 5: BuildCookRun -> APK ---------------------------------------------
# UBT never prunes stale staged/native outputs; wipe them so a package never
# inherits the previous combo's binaries.
rm -rf "$PROJ/Binaries/Android" "$PROJ/Saved/StagedBuilds/Android"

ARCHIVE="$PROJ/Packaged/Android-$COMBO"
rm -rf "$ARCHIVE"; mkdir -p "$ARCHIVE"
log "Step 5: BuildCookRun -> $ARCHIVE"
"$UAT" BuildCookRun -project="$UPROJECT" -Build -Target=SharpDemo \
  -TargetPlatform=Android -ClientConfig=Development -Cook -Pak -Stage \
  -Archive -package -nocompileuat -archivedirectory="$ARCHIVE" \
  > "$LOGS/combo$COMBO-cook.log" 2>&1 || { echo "FAIL step 5"; tail -30 "$LOGS/combo$COMBO-cook.log"; exit 1; }
log "Step 5 OK"
ls -la "$ARCHIVE"
log "Combo $COMBO COMPLETE"
