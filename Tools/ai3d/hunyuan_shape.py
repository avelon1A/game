"""Free image->3D shape via the public Hunyuan3D-2.1 demo (tencent/Hunyuan3D-2.1 on Hugging Face)."""
import shutil, sys
from pathlib import Path
from gradio_client import Client, handle_file

def path_of(r):
    if isinstance(r, dict): r = r.get("value") or r.get("path") or r.get("name")
    return r

names = sys.argv[1:] or ["vanguard", "pixie", "shade", "nova", "bolt"]
out = Path("output/hunyuan"); out.mkdir(parents=True, exist_ok=True)
import os
tok = os.environ.get("HF_TOKEN") or next((l.split("=",1)[1].strip() for l in open(".env") if l.startswith("HF_TOKEN=")), None) if os.path.exists(".env") or os.environ.get("HF_TOKEN") else None
c = Client("tencent/Hunyuan3D-2.1", verbose=False, hf_token=tok)
for n in names:
    dest = out / f"{n}.glb"
    if dest.exists(): print(n, "exists"); continue
    try:
        res = c.predict(handle_file(f"input/{n}_clean.png"), None, None, None, None, 40, 5.5, 1234, 320, True, 8000, False, api_name="/shape_generation")
        src = path_of(res[0])
        shutil.copy(src, dest)
        print(n, "->", dest, Path(src).suffix, dest.stat().st_size // 1024, "KB", res[2] if len(res) > 2 else "")
    except Exception as e:
        print(n, "FAILED", str(e)[:300])
