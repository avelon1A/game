#!/usr/bin/env python3
"""
VEIL character pipeline (Meshy API):  concept image -> textured 3D model -> auto-rig -> animations.

  .venv/bin/python meshy_pipeline.py --dry-run          # no credits: checks key, lists chosen animations + cost
  .venv/bin/python meshy_pipeline.py                    # all 5 characters
  .venv/bin/python meshy_pipeline.py --only vanguard    # one character (good first test)

Reads MESHY_API_KEY from Tools/ai3d/.env (never printed). Progress is saved to state.json after
every step, so re-running resumes and never pays twice for the same step. Results are written to
Client/Assets/Veil/Characters/<name>/ for Unity to import.
"""
import argparse, base64, json, os, sys, time
from pathlib import Path
import requests

ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent.parent / "Client" / "Assets" / "Veil" / "Characters"
STATE = ROOT / "state.json"
API = "https://api.meshy.ai/openapi/v1"
CHARACTERS = ["vanguard", "pixie", "shade", "nova", "bolt"]

# gameplay action -> library search keywords (first match wins). walk/run come free with rigging.
ACTIONS = {
    "idle":    ["idle", "breathing", "stand"],
    "sprint":  ["sprint", "fast run", "run fast"],
    "jump":    ["jump"],
    "fall":    ["fall"],
    "dash":    ["dodge", "roll", "dash"],
    "shoot":   ["shoot", "gun", "pistol", "throw", "cast"],
    "hit":     ["hit reaction", "hit", "damage", "impact"],
    "death":   ["death", "dying", "die", "fall down"],
    "victory": ["victory", "cheer", "celebrat", "win"],
}

COST = {"image": 30, "rig": 5, "anim": 3}


def load_key():
    env = ROOT / ".env"
    key = os.environ.get("MESHY_API_KEY", "")
    if not key and env.exists():
        for line in env.read_text().splitlines():
            if line.strip().startswith("MESHY_API_KEY="):
                key = line.split("=", 1)[1].strip().strip('"').strip("'")
    if not key or "your_key" in key:
        sys.exit("No MESHY_API_KEY found. Create Tools/ai3d/.env with MESHY_API_KEY=msy_...")
    return key


class Meshy:
    def __init__(self, key):
        self.s = requests.Session()
        self.s.headers["Authorization"] = f"Bearer {key}"

    def get(self, path, **params):
        r = self.s.get(API + path, params=params, timeout=60)
        self._check(r)
        return r.json()

    def post(self, path, body):
        r = self.s.post(API + path, json=body, timeout=120)
        self._check(r)
        return r.json()

    @staticmethod
    def _check(r):
        if r.status_code >= 400:
            msg = r.text[:400]
            if r.status_code == 401: msg = "invalid API key"
            if r.status_code == 402: msg = "not enough credits (402 Payment Required)"
            raise RuntimeError(f"Meshy API {r.status_code}: {msg}")

    def wait(self, path, label):
        last = -1
        while True:
            t = self.get(path)
            st, pr = t.get("status"), t.get("progress", 0)
            if pr != last:
                print(f"    {label}: {st} {pr}%", flush=True)
                last = pr
            if st == "SUCCEEDED":
                return t
            if st in ("FAILED", "CANCELED", "EXPIRED"):
                raise RuntimeError(f"{label} {st}: {(t.get('task_error') or {}).get('message', '')}")
            time.sleep(6)


def load_state():
    return json.loads(STATE.read_text()) if STATE.exists() else {}


def save_state(st):
    STATE.write_text(json.dumps(st, indent=2))


def download(url, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    with requests.get(url, stream=True, timeout=300) as r:
        r.raise_for_status()
        with open(dest, "wb") as f:
            for chunk in r.iter_content(1 << 16):
                f.write(chunk)
    print(f"    saved {dest.relative_to(ROOT.parent.parent)} ({dest.stat().st_size // 1024} KB)")


def pick_actions(library):
    chosen = {}
    items = library if isinstance(library, list) else library.get("result") or library.get("items") or library.get("data") or []
    for action, words in ACTIONS.items():
        for w in words:
            hit = next((a for a in items if w in (a.get("name", "") + " " + a.get("key", "")).lower()), None)
            if hit:
                chosen[action] = {"id": hit["action_id"], "name": hit.get("name")}
                break
    return chosen, items


def data_uri(path):
    return "data:image/png;base64," + base64.b64encode(path.read_bytes()).decode()


def run_character(m, st, name, actions):
    cs = st.setdefault(name, {})
    out = OUT / name
    img = ROOT / "input" / f"{name}.png"
    print(f"\n=== {name} ===")

    # 1) image -> textured 3D (A-pose helps auto-rigging)
    if "image_task" not in cs:
        body = {"image_url": data_uri(img), "should_texture": True, "enable_pbr": False, "pose_mode": "a-pose",
                "topology": "triangle", "target_polycount": 25000}
        cs["image_task"] = m.post("/image-to-3d", body)["result"]
        save_state(st)
    if "model_done" not in cs:
        t = m.wait(f"/image-to-3d/{cs['image_task']}", "model")
        raw = ROOT / "output" / name
        if t.get("thumbnail_url"): download(t["thumbnail_url"], raw / "preview.png")
        download(t["model_urls"]["glb"], raw / f"{name}_model.glb")
        cs["model_done"] = True
        save_state(st)

    # 2) auto-rig (returns rigged character + walking/running)
    if "rig_task" not in cs:
        cs["rig_task"] = m.post("/rigging", {"input_task_id": cs["image_task"], "height_meters": 1.8})["result"]
        save_state(st)
    if "rig_done" not in cs:
        t = m.wait(f"/rigging/{cs['rig_task']}", "rig")
        r = t["result"]
        download(r["rigged_character_fbx_url"], out / f"{name}_rigged.fbx")
        ba = r.get("basic_animations", {})
        if ba.get("walking_fbx_url"): download(ba["walking_fbx_url"], out / f"{name}@walk.fbx")
        if ba.get("running_fbx_url"): download(ba["running_fbx_url"], out / f"{name}@run.fbx")
        cs["rig_done"] = True
        save_state(st)

    # 3) extra animations
    anims = cs.setdefault("anims", {})
    for action, info in actions.items():
        a = anims.setdefault(action, {})
        if a.get("done"):
            continue
        if "task" not in a:
            a["task"] = m.post("/animations", {"rig_task_id": cs["rig_task"], "action_id": info["id"]})["result"]
            save_state(st)
        t = m.wait(f"/animations/{a['task']}", f"anim {action} ({info['name']})")
        download(t["result"]["animation_fbx_url"], out / f"{name}@{action}.fbx")
        a["done"] = True
        save_state(st)
    print(f"  {name} complete")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", choices=CHARACTERS)
    args = ap.parse_args()

    m = Meshy(load_key())
    lib = m.get("/animations/library")
    actions, items = pick_actions(lib)
    (ROOT / "animation_library.json").write_text(json.dumps(items, indent=2))
    print(f"API key OK. Animation library: {len(items)} actions (saved to animation_library.json)")
    for k, v in actions.items():
        print(f"  {k:8s} -> #{v['id']} {v['name']}")
    missing = [k for k in ACTIONS if k not in actions]
    if missing: print("  (not found in library:", ", ".join(missing), ")")

    names = [args.only] if args.only else CHARACTERS
    per = COST["image"] + COST["rig"] + COST["anim"] * len(actions)
    print(f"\nEstimated cost: {per} credits per character x {len(names)} = {per * len(names)} credits")
    if args.dry_run:
        return
    st = load_state()
    for n in names:
        run_character(m, st, n, actions)
    print("\nAll done. Next: open Unity (or run the batch build) — the importer turns these into playable characters.")


if __name__ == "__main__":
    main()
