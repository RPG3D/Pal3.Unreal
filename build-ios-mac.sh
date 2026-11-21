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
ENGINE="${UE_ENGINE_ROOT:-/Users/admin/Documents/Code/UE5.9/Engine}"
if [ -d "$ENGINE/Engine/Build" ]; then ENGINE="$ENGINE/Engine"; fi
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

# Target runtime for iOS lives in the platform config layer. Rewrite the section
# idempotently: appending on every CoreCLR run used to leave dozens of duplicates.
IOSINI="$PROJ/Config/IOS/IOSEngine.ini"
python3 - "$IOSINI" "$TARGET_RT" <<'PY'
import sys, re
path, target = sys.argv[1], sys.argv[2]
text = open(path).read()
text = re.sub(r'\n\[UnrealSharp\]\nDotNetRuntime=[^\n]*\n', '\n', text).rstrip('\n') + '\n'
if target == 'CoreCLR':
    text += '\n[UnrealSharp]\nDotNetRuntime=CoreCLR\n'
open(path, 'w').write(text)
PY
grep -A2 "^\[UnrealSharp\]" "$IOSINI" || echo "(IOSEngine.ini: default Mono)"

# UBT caches Build.cs evaluation per makefile; force re-evaluation.
touch "$PROJ/Plugins/UnrealSharp/Source/ThirdParty/DotNetRuntimeSDK/DotNetRuntimeSDK.Build.cs"

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

# ---- Step 2: Game-target UBT build -------------------------------------------
log "Step 2: Game-target UBT build"
"$BUILD" SharpDemo Mac Development -project="$UPROJECT" \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-game.log" 2>&1 || { echo "FAIL step 2"; tail -20 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-game.log"; exit 1; }
log "Step 2 OK"

# ---- Step 3: PackageProjectMobile (IOS) ---------------------------------------
log "Step 3: PackageProjectMobile -TargetPlatform=IOS"
dotnet run --project "$PROJ/tools/ManagedModules/ModulePublisher.csproj" -- IOS all \
  --publish-host --engine-root "$ENGINE" --configuration Development --architecture arm64 \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-package.log" 2>&1 || { echo "FAIL step 3"; tail -20 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-package.log"; exit 1; }
log "Step 3 OK: $(ls "$PROJ/Content/Managed/IOS" | wc -l | tr -d ' ') files in Content/Managed/IOS"


# ---- Step 4: Editor UBT build --------------------------------------------------
log "Step 4: Editor UBT build"
"$BUILD" SharpDemoEditor Mac Development -project="$UPROJECT" \
  > "$LOGS/ios-$EDITOR_RT-$TARGET_RT-editor.log" 2>&1 || { echo "FAIL step 4"; tail -20 "$LOGS/ios-$EDITOR_RT-$TARGET_RT-editor.log"; exit 1; }
log "Step 4 OK"

# ---- Step 5: BuildCookRun -> .app ----------------------------------------------
# UBT never prunes staged frameworks, so a previous run's Mono.framework (or
# CoreCLR.framework) survives into the next bundle. Wipe the staging tree so the
# packaged .app only contains the framework its backend actually needs.
rm -rf "$PROJ/Binaries/IOS" "$PROJ/Saved/StagedBuilds/IOS"

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
