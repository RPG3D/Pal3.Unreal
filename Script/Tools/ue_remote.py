#!/usr/bin/env python3
"""ue_remote.py - minimal client for UE's Remote Control HTTP API.

Acts as the runtime-side counterpart of an editor MCP: it talks to the *running
game* (-game, not PIE) over http://127.0.0.1:30010 and exposes the same kind of
queries an MCP tool would (list actors, find by name, read/write properties,
execute console commands, call any UFUNCTION).

Setup on the UE side (already committed in this project):
  * plugin "RemoteControl" enabled in SharpDemo.uproject
  * Config/DefaultRemoteControl.ini: bAutoStartWebServer=True, port 30010,
    bAllowAnyRemoteFunctionCall=True (local dev only)
  * Source/SharpDemo/Pal3DebugOverlay.h exposes DumpActors/FindActors/
    DumpActorPositions as UFUNCTIONs for a readable actor dump.

Usage:
  python ue_remote.py info
  python ue_remote.py actors [max]
  python ue_remote.py find <substring>
  python ue_remote.py props <objectPath> <property>
  python ue_remote.py set <objectPath> <property> <json>
  python ue_remote.py call <objectPath> <function> [json-params]
  python ue_remote.py console <command>

Object paths look like:
  /Script/SharpDemo.Default__Pal3DebugOverlay      (our debug library CDO)
  /Game/PAL3/Maps/LV_Pal3.LV_Pal3:PersistentLevel.Actor_12
"""
import json
import sys
import urllib.request

BASE = "http://127.0.0.1:30010"
OVERLAY = "/Script/SharpDemo.Default__Pal3DebugOverlay"


def _post(path, payload):
    data = json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=data, method="PUT",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            return json.loads(response.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        body = e.read().decode(errors="ignore")
        raise SystemExit(f"HTTP {e.code}: {body[:400]}")


def _get(path):
    with urllib.request.urlopen(BASE + path, timeout=15) as response:
        return json.loads(response.read().decode() or "{}")


def call(object_path, function, params=None):
    return _post("/remote/object/call", {
        "objectPath": object_path,
        "functionName": function,
        "parameters": params or {},
        "generateTransaction": False,
    })


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1

    command = argv[1]

    if command == "info":
        print(json.dumps(_get("/remote/info"), indent=2))
        return 0

    if command == "actors":
        limit = int(argv[2]) if len(argv) > 2 else 120
        result = call(OVERLAY, "DumpActors", {"MaxCount": limit})
        print(result.get("ReturnValue", result))
        return 0

    if command == "find":
        result = call(OVERLAY, "FindActors", {"NameContains": argv[2], "MaxCount": 60})
        print(result.get("ReturnValue", result))
        return 0

    if command == "positions":
        klass = argv[2] if len(argv) > 2 else "Actor"
        result = call(OVERLAY, "DumpActorPositions", {"ClassContains": klass, "MaxCount": 200})
        print(result.get("ReturnValue", result))
        return 0

    if command == "props":
        query = f"/remote/object/property?objectPath={urllib.parse.quote(argv[2])}&propertyName={argv[3]}"
        print(json.dumps(_get(query), indent=2))
        return 0

    if command == "set":
        value = json.loads(argv[4])
        print(json.dumps(_post("/remote/object/property", {
            "objectPath": argv[2], "propertyName": argv[3], "propertyValue": value,
        }), indent=2))
        return 0

    if command == "call":
        params = json.loads(argv[4]) if len(argv) > 4 else {}
        print(json.dumps(call(argv[2], argv[3], params), indent=2))
        return 0

    if command == "console":
        # Console commands run through the engine's URemoteControlFunctionLibrary.
        print(json.dumps(call("/Script/RemoteControl.RemoteControlFunctionLibrary", "ExecuteConsoleCommand",
                              {"Command": argv[2]}), indent=2))
        return 0

    print(f"unknown command: {command}")
    return 1


if __name__ == "__main__":
    import urllib.parse  # noqa: E402  (used by props)
    sys.exit(main(sys.argv))
