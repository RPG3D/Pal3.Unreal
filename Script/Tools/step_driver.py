#!/usr/bin/env python3
"""step_driver.py - RC-driven stepwise story walker for the port verification.

Advances dialogue one pulse at a time through Remote Control, screenshots the UE
window at milestone script offsets, and records the actor/camera heartbeat lines.

Usage: python step_driver.py <log-path>
"""
import json
import re
import subprocess
import sys
import time
import urllib.request

BASE = "http://127.0.0.1:30010"
OVERLAY = "/Script/SharpDemo.Default__Pal3DebugOverlay"
LOG = sys.argv[1] if len(sys.argv) > 1 else "Saved/Logs/pal3_night.log"
SHOT_DIR = "Saved/Screenshots"
CAP_PS1 = r"C:\Users\imxql\AppData\Local\Temp\cap_ue.ps1"

MILESTONES = {
    412: "action_z19_lie",
    630: "action_j02_situp",
    962: "action_j05_fall",
    1150: "action_j06",
    2789: "getup_talk",
    5200: "teleport_pair",
    9010: "keyframe9006",
    9080: "orbit_mid",
    9260: "after_orbit",
}

def rc_call(func, params=None):
    payload = {"objectPath": OVERLAY, "functionName": func,
               "parameters": params or {}, "generateTransaction": False}
    req = urllib.request.Request(BASE + "/remote/object/call",
                                 data=json.dumps(payload).encode(), method="PUT",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            return r.status
    except Exception as e:
        print(f"rc_call {func} failed: {e}")
        return None

def screenshot(tag):
    out = f"{SHOT_DIR}/step_{tag}.png"
    subprocess.run(["powershell", "-ExecutionPolicy", "Bypass", "-File", CAP_PS1,
                    "-OutPath", out.replace("/", "\\")],
                   capture_output=True, timeout=30)
    print(f"  [shot] {out}")

def last_offset():
    try:
        with open(LOG, "r", encoding="utf-8", errors="ignore") as f:
            text = f.read()
    except OSError:
        return -1
    offs = re.findall(r"Scene Script \[1001 [^\[]*\[(\d+)\]", text)
    return int(offs[-1]) if offs else -1

def heartbeat():
    try:
        with open(LOG, "r", encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()
    except OSError:
        return ""
    for line in reversed(lines):
        if "[Pal3Driver] actors" in line:
            m = re.search(r"cam=\([^)]*\)|jingtian=\([^)]*\)|xuejian=\([^)]*\)", line)
            return " ".join(re.findall(r"(cam=\([^)]*\)|jingtian=\([^)]*\)|xuejian=\([^)]*\))", line))
    return ""

def main():
    done = set()
    last = last_offset()
    print(f"start offset: {last}")
    deadline = time.time() + 600
    while time.time() < deadline and last < 9260:
        rc_call("AdvanceDialogue")
        time.sleep(0.6)
        now = last_offset()
        if now != last:
            print(f"offset {now}  {heartbeat()}")
            last = now
        for target, tag in MILESTONES.items():
            if target not in done and now >= target:
                done.add(target)
                time.sleep(1.0)
                screenshot(tag)
                print(f"  [milestone] {tag} @ {now}")
    print(f"final offset: {last}")
    print(f"final state: {heartbeat()}")

if __name__ == "__main__":
    main()
