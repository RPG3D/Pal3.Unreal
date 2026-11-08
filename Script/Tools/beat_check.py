#!/usr/bin/env python3
"""beat_check.py - per-dialogue-line verification of actor positions & camera
coordinates against script-derived expectations.

Parses a run log (PalScriptRunner command stream + Pal3Driver heartbeats),
tracks expected Unity-space state through each command, and diffs it against
the observed heartbeat values at every DialogueRenderText beat.
"""
import re
import sys

LOG = sys.argv[1] if len(sys.argv) > 1 else "Saved/Logs/pal3_tex12.log"

NAV_TILE = 12.0
NAV_MIN = (-93.16289, -117.490524)   # layer0 GB min X/Z (from Pal3DataProbe)

def gb_to_unity(x, y, z):
    return (-x / 20.0, y / 20.0, z / 20.0)

def tile_to_unity(tx, ty):
    gx = NAV_MIN[0] + (tx + 0.5) * NAV_TILE
    gz = NAV_MIN[1] + (ty + 0.5) * NAV_TILE
    return gb_to_unity(gx, 0.1238, gz)

state = {
    "jingtian": None, "xuejian": None,
    "cam": None, "cam_rot": None,
    "obs_jt": None, "obs_xj": None, "obs_cam": None,
    "pending_walk": None,   # (actor_key, target) — actor walks toward tile
}

events = []
for ln in open(LOG, "r", encoding="utf-8", errors="ignore"):
    ln = ln.rstrip("\n")
    if "DialogueRenderText" in ln and "[1001" in ln:
        m = re.search(r'DialogueRenderText \[62\] \{"DialogueText":"([^"]*)"\}', ln)
        off_m = re.search(r"Scene Script \[1001 [^\[]*\[(\d+)\]", ln)
        if m and off_m:
            text = m.group(1).replace("\\n", " ")
            events.append(("dialogue", int(off_m.group(1)), text[:26]))
        continue
    m = re.search(r"Scene Script \[1001 [^\[]*\[(\d+)\]: (\w+) \[\d+\] (\{.*\})", ln)
    if m:
        try:
            payload = eval(m.group(3))
        except Exception:
            payload = {}
        events.append(("cmd", int(m.group(1)), m.group(2), payload))
        continue
    m = re.search(r"\[Pal3Driver\] actors.*?cam=\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\).*?"
                  r"jingtian=\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\|ue.*?\) xuejian=\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\|", ln)
    if m:
        g = [float(v) for v in m.groups()]
        # heartbeat cam= is the raw UE actor location (cm, X fwd/Y right/Z up):
        # convert to Unity meters via U.x=UE.Y/100, U.y=UE.Z/100, U.z=UE.X/100.
        cam_unity = (g[1] / 100.0, g[2] / 100.0, g[0] / 100.0)
        events.append(("hb", cam_unity, (g[3], g[4], g[5]), (g[6], g[7], g[8])))

def fmt(v):
    return f"({v[0]:6.2f},{v[1]:5.2f},{v[2]:6.2f})" if v else " " * 26

def close(a, b, tol=0.35):
    if a is None or b is None:
        return "  ?"
    d = max(abs(a[i] - b[i]) for i in range(3))
    return "  OK   " if d <= tol else f"DIFF {d:.2f}"

print(f"{'off':>6} {'beat':<28} | {'jt expect':>26} {'jt observed':>26} {'cmp':>9} | {'cam expect':>26} {'cam observed':>26} {'cmp':>9}")
print("-" * 185)

for ev in events:
    kind = ev[0]
    if kind == "cmd":
        off, name, p = ev[1], ev[2], ev[3]
        if name == "ActorSetTilePosition":
            aid = p.get("ActorId")
            pos = tile_to_unity(p.get("TileXPosition", 0), p.get("TileYPosition", 0))
            if aid in (-1, 0):
                state["jingtian"] = pos
            elif aid == 1:
                state["xuejian"] = pos
        elif name in ("ActorPathTo", "ActorMoveTo"):
            aid = p.get("ActorId")
            pos = tile_to_unity(p.get("TileXPosition", 0), p.get("TileYPosition", 0))
            if aid in (-1, 0):
                state["pending_walk"] = ("jingtian", pos)
            elif aid == 1:
                state["pending_walk"] = ("xuejian", pos)
        elif name == "CameraSetTransform":
            state["cam"] = gb_to_unity(p.get("GameBoxXPosition", 0), p.get("GameBoxYPosition", 0), p.get("GameBoxZPosition", 0))
            state["cam_rot"] = (-p.get("Pitch", 0), 180.0 + p.get("Yaw", 0))
    elif kind == "hb":
        state["obs_cam"], state["obs_jt"], state["obs_xj"] = ev[1], ev[2], ev[3]
        # a finished walk updates the actor position
        if state.get("pending_walk"):
            who, target = state["pending_walk"]
            obs = state["obs_jt"] if who == "jingtian" else state["obs_xj"]
            if close(target, obs, 0.6).startswith("  OK"):
                state[who] = target
                state["pending_walk"] = None
    elif kind == "dialogue":
        off, text = ev[1], ev[2]
        jt_ok = close(state["jingtian"], state["obs_jt"])
        cam_ok = close(state["cam"], state["obs_cam"])
        print(f"{off:>6} {text:<28} | {fmt(state['jingtian']):>26} {fmt(state['obs_jt']):>26} {jt_ok:>9} | "
              f"{fmt(state['cam']):>26} {fmt(state['obs_cam']):>26} {cam_ok:>9}")
