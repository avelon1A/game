"""2D joints from the concept art (MediaPipe Pose, classic API). Writes joints/<name>.json (image pixels)."""
import json, sys, os
import numpy as np
import mediapipe as mp
from PIL import Image, ImageDraw
NAMES = {11: "shoulderL_img", 12: "shoulderR_img", 13: "elbowL_img", 14: "elbowR_img", 15: "wristL_img", 16: "wristR_img",
         23: "hipL_img", 24: "hipR_img", 25: "kneeL_img", 26: "kneeR_img", 27: "ankleL_img", 28: "ankleR_img", 0: "nose"}
os.makedirs("joints", exist_ok=True)
with mp.solutions.pose.Pose(static_image_mode=True, model_complexity=2, min_detection_confidence=0.1) as pose:
    for name in sys.argv[1:] or ["vanguard", "pixie", "shade", "nova", "bolt"]:
        im = Image.open(f"input/{name}_clean.png").convert("RGB")
        W, H = im.size
        res, scale = None, 1.0
        for size in (W, 1024, 768, 512):
            test = im if size == W else im.resize((size, size))
            r = pose.process(np.array(test))
            if r.pose_landmarks:
                res, scale = r, W / size
                break
        if res is None:
            if os.path.exists(f"joints/{name}.json"): os.remove(f"joints/{name}.json")
            print(name, "NO POSE"); continue
        lm = res.pose_landmarks.landmark
        out = {v: [lm[k].x * W, lm[k].y * H, lm[k].visibility] for k, v in NAMES.items()}  # normalised coords -> full-res pixels
        json.dump({"size": [W, H], "joints": out}, open(f"joints/{name}.json", "w"), indent=1)
        d = ImageDraw.Draw(im)
        for k, (x, y, vis) in out.items():
            d.ellipse([x - 9, y - 9, x + 9, y + 9], fill=(255, 0, 180) if "L" in k else (0, 200, 255))
        im.resize((384, 384)).save(f"joints/{name}_pose.png")
        print(name, "ok", {k.replace("_img", ""): (round(v[0]), round(v[1])) for k, v in out.items() if k != "nose"})
