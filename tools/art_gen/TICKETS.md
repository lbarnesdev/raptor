# Phase 4 Art Pipeline — Sprint Tickets
**Project:** RAPTOR  
**Directory:** `tools/art_gen/`  
**Total estimated effort:** 7.5 hours  
**Execution order:** ART-001 → ART-002 → ART-003 → ART-004

---

## TICKET-ART-001 — Data files: prompts.json + workflow_api.json

**Effort:** 0.5 h  
**Dependencies:** None  

### Description
Create the two static data files that `art_gen.py` reads at startup. Both files are fully specified in the Phase 4 architecture doc — this ticket is transcription, not design. `prompts.json` is the source of truth for all 62 named asset definitions (expanding to ~74 output files once multi-frame explosion sets are counted). `workflow_api.json` is the reference ComfyUI node graph; the user will replace it with their own exported copy after building the workflow in the ComfyUI UI, but the reference copy allows `art_gen.py` to be developed and tested against a known structure.

### Acceptance criteria
- `tools/art_gen/prompts.json` exists and is valid JSON
- Contains top-level keys: `style_prefix`, `negative_prompt`, `model`, `assets`
- `assets` array contains exactly 62 entries; each entry has `id`, `prompt`, `size` (2-element array), `frames`, `seed`, `output_dir`, `filename_pattern`
- All seeds are unique integers
- Multi-frame assets (`explosion_small_0`, `explosion_medium_0`, `explosion_large_0`) have `frames: 6` and `filename_pattern` containing `{frame}`
- Terrain assets have `size[0] == 1920`
- `tools/art_gen/workflow_api.json` exists and is valid JSON
- Contains nodes: `"4"` (CheckpointLoaderSimple), `"10"` (LoraLoader), `"6"` (CLIPTextEncode positive), `"7"` (CLIPTextEncode negative), `"5"` (EmptyLatentImage), `"3"` (KSampler), `"8"` (VAEDecode), `"9"` (SaveImage)
- KSampler inputs: `steps: 25`, `cfg: 7.0`, `sampler_name: "dpmpp_2m"`, `scheduler: "karras"`

### Claude prompt
```
Write two JSON files for the RAPTOR ComfyUI art generation pipeline.

FILE 1 — tools/art_gen/prompts.json
Top-level structure:
{
  "style_prefix": "comic book art style, bold ink outlines, flat cel shading, high contrast, science fiction military aesthetic, vivid colors, clean edges, game sprite on transparent background, isolated object, no background",
  "negative_prompt": "photorealistic, photograph, 3d render, watercolor, sketch, pencil, blurry, soft focus, gradient background, text, watermark, multiple objects, cluttered, noisy, grainy, low contrast, realistic skin texture, hyperdetailed, subsurface scattering",
  "model": "dreamshaper_8",
  "assets": [ ... ]
}

Each asset object has these fields:
  id             — unique snake_case string
  prompt         — asset-specific description only (style_prefix is prepended by art_gen.py)
  size           — [width, height] in pixels
  frames         — integer, 1 for stills; 6 for explosion animation sets
  seed           — unique integer (do not reuse any seed)
  output_dir     — relative path from repo root, e.g. "assets/sprites/player"
  filename_pattern — filename; use {frame} placeholder if frames > 1

Include ALL of the following assets in this exact order:

PLAYER (output_dir: assets/sprites/player):
  player_idle       [128,64]  seed:1001  "F-22 Raptor fighter jet, side view facing right, near-future sci-fi variant, crystalline blue thruster glow, jagged alien-tech hull plating, spectral purple afterburner trail, sleek silhouette, game sprite"
  player_thrust     [128,64]  seed:1002  "F-22 Raptor fighter jet, side view facing right, near-future sci-fi variant, bright crystalline blue thrusters at full burn, intense afterburner flame, alien-tech hull plating, game sprite, thrust animation frame"
  player_hit        [128,64]  seed:1003  "F-22 Raptor fighter jet, side view facing right, near-future sci-fi variant, hit flash, crackling energy damage effect, orange impact sparks, shield disruption glow, game sprite"
  player_death      [128,64]  seed:1004  "F-22 Raptor fighter jet, breaking apart, side view, sci-fi variant, explosion beginning, fragments separating, orange fire, game sprite, destruction animation"
  shield_bubble     [160,96]  seed:2001  "energy shield bubble, hexagonal cell pattern, translucent blue glow, sci-fi force field, oval shape, game sprite, isolated"
  shield_break      [192,128] seed:2002  "energy shield shattering, hexagonal fragments flying outward, blue electricity sparks, sci-fi force field destruction, game sprite, isolated"
  plasma_muzzle_flash [48,32] seed:2003  "plasma cannon muzzle flash, bright cyan-white burst, circular energy discharge, sci-fi weapon effect, game sprite, isolated"

PROJECTILES (output_dir: assets/sprites/projectiles):
  plasma_bolt_player   [32,12]  seed:3001  "plasma energy bolt, elongated horizontal capsule shape, bright cyan core, white hot center, glowing edges, sci-fi projectile, game sprite"
  plasma_blob_wraith   [20,20]  seed:3002  "alien bioluminescent plasma blob, organic green energy orb, pulsing alien projectile, tendrils of green light, sci-fi bullet, game sprite"
  crystal_spine_specter [24,10] seed:3003  "red crystalline spine projectile, sharp angular shape, alien red crystal shard, glowing red edges, sci-fi enemy bullet, game sprite"
  organic_spore_turret  [18,18] seed:3004  "alien organic spore projectile, round with trailing tendrils, bioluminescent purple-green, hazardous cloud effect, sci-fi turret bullet, game sprite"
  missile_body     [40,12]  seed:3005  "air-to-air missile, sleek aerodynamic body, white and gray military finish, silver warhead, sci-fi near-future variant, side view, game sprite"
  missile_trail    [48,10]  seed:3006  "rocket exhaust trail, white smoke and orange flame streak, missile thrust plume, side view trailing effect, game sprite"

FX (output_dir: assets/sprites/fx):
  explosion_small_0   [48,48]   frames:6  seed:4001  filename:explosion_small_{frame}.png   "small explosion frame 1, bright orange and yellow fireball burst, comic book style, radial blast, game sprite fx"
  explosion_medium_0  [96,96]   frames:6  seed:4010  filename:explosion_medium_{frame}.png  "medium explosion frame 1, orange fireball with black smoke tendrils, comic book style, expanding ring shockwave, game sprite fx"
  explosion_large_0   [192,192] frames:6  seed:4020  filename:explosion_large_{frame}.png   "large explosion frame 1, massive orange and white fireball, comic book style, debris fragments, thick black smoke, dramatic scale, game sprite fx"

ENEMIES (output_dir: assets/sprites/enemies):
  wraith_fighter_idle   [112,56] seed:5001  "Chinese J-20 stealth fighter jet, side view facing left as enemy, alien possession: bioluminescent green tendrils along fuselage, cracked cockpit with single glowing alien eye, swept-wing delta silhouette, game sprite"
  wraith_fighter_death  [112,56] seed:5002  "Chinese J-20 stealth fighter jet breaking apart, alien possession, green bioluminescent alien tendrils disintegrating, side view, orange explosion beginning, game sprite death frame"
  specter_fighter_idle  [112,56] seed:5010  "Russian Su-57 stealth fighter jet, side view facing left as enemy, alien possession: red crystalline growths along fuselage, twin-tail silhouette, trailing alien spore cloud behind engines, game sprite"
  specter_fighter_death [112,56] seed:5011  "Russian Su-57 stealth fighter jet breaking apart, red crystalline alien growths shattering, twin-tail silhouette, side view, crimson explosion, game sprite death frame"
  specter_spore_trail   [80,32]  seed:5012  "hazardous alien spore cloud trail, lingering purple-red particle cloud, toxic haze effect, sci-fi environmental hazard, elongated horizontal shape, game sprite fx"
  harbinger_turret_idle   [72,72] seed:5020  "military SAM anti-air turret, ground-mounted, side view, alien possession: organic coral-like alien growth consuming the hardware, bioluminescent purple veins, turret barrel pointing upward-right, game sprite"
  harbinger_turret_firing [72,72] seed:5021  "military SAM anti-air turret, ground-mounted, side view, alien possession: organic coral-like growth, muzzle flash at barrel, firing state, turret rotated to fire angle, game sprite"
  harbinger_turret_death  [72,72] seed:5022  "military SAM anti-air turret destroyed, alien organic coral growth burning and disintegrating, explosion fragments, ground-mounted wreckage, side view, game sprite"

BOSS PROPS (output_dir: assets/sprites/boss):
  boss_constitution_intact    [128,160]  seed:6010
  boss_constitution_glowing   [128,160]  seed:6011
  boss_constitution_destroyed [160,192]  seed:6012
  boss_fox_logo_intact        [160,128]  seed:6020
  boss_fox_logo_glowing       [160,128]  seed:6021
  boss_fox_logo_destroyed     [192,160]  seed:6022
  boss_toupee_closed          [256,128]  seed:6030
  boss_toupee_open            [256,192]  seed:6031
  boss_statue_liberty_tentacled [256,512] seed:6040
  boss_statue_liberty_free      [256,512] seed:6041
  boss_tentacle_cluster_idle    [128,128] seed:6050
  boss_tentacle_cluster_hit     [128,128] seed:6051
  boss_tentacle_cluster_destroyed [160,160] seed:6052
  boss_hate_shuriken     [40,40]  seed:6060
  boss_hate_shuriken_alt [40,40]  seed:6061
(Write appropriate prompts for each boss prop based on their names and the RAPTOR visual style: comic book, sci-fi military, alien possession aesthetic.)

BOSS CHARACTER + ALIEN PASSENGER (output_dir: assets/sprites/boss):
  alien_passenger_idle      [96,128]  seed:6070
  alien_passenger_agitated  [96,128]  seed:6071
  alien_passenger_detached  [96,128]  seed:6072
  alien_passenger_flee      [112,96]  seed:6073
  boss_demagogue_phase1     [512,768] seed:6001
  boss_demagogue_phase2     [512,768] seed:6002
  boss_demagogue_phase3     [512,768] seed:6003
  boss_demagogue_stagger    [512,768] seed:6004
(Write appropriate prompts: alien passenger is a small creature with spindly legs, enormous oval eyes, bulbous skull, PROPAGANDA t-shirt, riding near a giant ear. Demagogue is a giant political caricature boss with enormous head, tiny body, navy suit, red tie, comically tiny hands.)

TERRAIN (output_dir: assets/sprites/terrain):
  terrain_sky_bg      [1920,400] seed:7001  filename:sky_bg.png
  terrain_mid_land    [1920,200] seed:7002  filename:mid_land.png
  terrain_mid_water   [1920,200] seed:7003  filename:mid_water.png
  terrain_ground_land [1920,160] seed:7004  filename:ground_land.png
  terrain_ground_water [1920,160] seed:7005  filename:ground_water.png
(Write prompts: sci-fi military war zone, comic book style, horizontal strips. Sky: orange-purple dusk with battle smoke. Mid: rolling hills or ocean with warship silhouettes. Ground: earthen terrain or ocean surface with alien growth.)

UI (output_dir: assets/sprites/ui):
  ui_hud_frame       [1920,1080] seed:8001  filename:hud_frame.png
  ui_health_pip      [32,32]    seed:8002  filename:health_pip.png
  ui_shield_indicator [128,24]  seed:8003  filename:shield_bar.png
  ui_missile_icon    [32,16]    seed:8004  filename:missile_icon.png
  ui_boss_hp_bar     [512,32]   seed:8005  filename:boss_hp_bar.png

SCREENS (output_dir: assets/screens):
  screen_title_card      [1920,1080] seed:9001  filename:title_card.png
  screen_game_over       [1920,1080] seed:9002  filename:game_over.png
  screen_win_good_ending [1920,1080] seed:9003  filename:win_good_ending.png
  screen_win_bad_ending  [1920,1080] seed:9004  filename:win_bad_ending.png

Rules:
- Every asset has frames:1 unless specified otherwise
- filename_pattern matches the filename column above (default: {id}.png if not listed)
- No two assets share a seed value
- Output the complete, valid JSON only — no commentary

---

FILE 2 — tools/art_gen/workflow_api.json
Write the reference ComfyUI workflow in API format with these exact node IDs and class types:

{
  "4":  CheckpointLoaderSimple  — ckpt_name: "dreamshaper_8.safetensors"
  "10": LoraLoader              — lora_name: "comicbook_v2.safetensors", strength_model: 0.85, strength_clip: 0.85, model: ["4",0], clip: ["4",1]
  "6":  CLIPTextEncode          — text: "POSITIVE PROMPT HERE", clip: ["10",1]
  "7":  CLIPTextEncode          — text: "NEGATIVE PROMPT HERE", clip: ["10",1]
  "5":  EmptyLatentImage        — width: 512, height: 512, batch_size: 1
  "3":  KSampler                — model:["10",0], positive:["6",0], negative:["7",0], latent_image:["5",0], seed:42, steps:25, cfg:7.0, sampler_name:"dpmpp_2m", scheduler:"karras", denoise:1.0
  "8":  VAEDecode               — samples:["3",0], vae:["4",2]
  "9":  SaveImage               — images:["8",0], filename_prefix:"raptor_"
}

Output complete valid JSON. This is the template; art_gen.py patches node text/seed/size values at runtime.
```

---

## TICKET-ART-002 — ComfyUI HTTP client: comfyui_client.py

**Effort:** 2 h  
**Dependencies:** ART-001 (workflow_api.json structure drives the polling contract)

### Description
A standalone Python module containing the three functions `art_gen.py` calls to communicate with ComfyUI's HTTP API. Written in pure stdlib so there are no pip dependencies. The module is separated from `art_gen.py` so it can be tested in isolation against a live ComfyUI instance without running the full generation loop.

The three operations map to three API endpoints:
- `queue_prompt()` → `POST /prompt` — submits a workflow and returns the `prompt_id`
- `poll_until_done()` → `GET /history/{prompt_id}` on a 0.5s loop — waits for ComfyUI to finish and returns the output filename
- `download_image()` → `GET /view` — fetches the PNG bytes for a completed image

### Acceptance criteria
- File is at `tools/art_gen/comfyui_client.py`
- No third-party imports — stdlib only (`urllib.request`, `urllib.parse`, `json`, `time`)
- Module-level constant `COMFYUI_URL = "http://127.0.0.1:8188"` 
- `queue_prompt(workflow: dict) -> str` — POSTs `{"prompt": workflow}` as JSON, returns `prompt_id` string, raises `RuntimeError` if HTTP status is not 200
- `poll_until_done(prompt_id: str, timeout: float = 120.0) -> str` — polls `GET /history/{prompt_id}` every 0.5 s; returns the output filename (the last PNG key under `outputs → node_id → images → [0] → filename`); raises `TimeoutError` if `timeout` seconds elapse without a result
- `download_image(filename: str, subfolder: str = "", image_type: str = "output") -> bytes` — GETs `/view?filename=…&subfolder=…&type=…`, returns raw bytes, raises `RuntimeError` on non-200
- All three functions have docstrings describing parameters and return values
- Running `python comfyui_client.py` prints `"OK — comfyui_client loaded"` (smoke-test main guard)

### Claude prompt
```
Write tools/art_gen/comfyui_client.py — a Python stdlib-only module for the RAPTOR
ComfyUI art generation pipeline. ComfyUI runs at localhost:8188.

Module-level constant:
  COMFYUI_URL = "http://127.0.0.1:8188"

Implement these three functions using only urllib.request, urllib.parse, json, and time:

1. queue_prompt(workflow: dict) -> str
   POST http://127.0.0.1:8188/prompt
   Body: json.dumps({"prompt": workflow}).encode("utf-8")
   Headers: Content-Type: application/json
   Returns: response JSON["prompt_id"] as a string
   Raises: RuntimeError("queue_prompt failed: HTTP {status}") if status != 200

2. poll_until_done(prompt_id: str, timeout: float = 120.0) -> str
   Loop: GET http://127.0.0.1:8188/history/{prompt_id} every 0.5 seconds
   The response is a dict; the job is done when prompt_id is a key in it.
   When done: navigate response[prompt_id]["outputs"] to find the first node
   that has an "images" list; return images[0]["filename"].
   Raises: TimeoutError(f"Timed out after {timeout}s waiting for {prompt_id}")
   if timeout expires.
   Note: /history returns {} (empty dict) while the job is queued/running.

3. download_image(filename: str, subfolder: str = "", image_type: str = "output") -> bytes
   GET http://127.0.0.1:8188/view?filename={filename}&subfolder={subfolder}&type={image_type}
   Returns raw response bytes (the PNG file content).
   Raises: RuntimeError("download_image failed: HTTP {status}") if status != 200

Requirements:
- Pure stdlib — no requests, no httpx, no third-party packages
- Every function has a Google-style docstring (Args / Returns / Raises)
- At the bottom of the file:
    if __name__ == "__main__":
        print("OK — comfyui_client loaded")
```

---

## TICKET-ART-003 — Core script: art_gen.py

**Effort:** 3 h  
**Dependencies:** ART-001 (reads both JSON files), ART-002 (imports comfyui_client)

### Description
The main generation driver. Loads `prompts.json` and `workflow_api.json`, accepts CLI arguments to control which assets to generate, patches the workflow for each asset, calls the ComfyUI client, and writes output PNGs to their target directories. Idempotency is built in: assets that already exist on disk are skipped unless `--force` is passed.

Key design decisions to preserve:
- **Workflow is deep-copied per asset** — never mutate the loaded template in place, so re-running a patched workflow doesn't bleed settings across assets
- **Multi-frame seed increment** — for assets with `frames > 1`, frame N uses `seed + N` so each frame is a distinct image rather than a duplicate
- **Per-asset lora_weight** — if an asset definition contains `"lora_weight"`, it overrides the template's default 0.85 for that asset only (mitigation for the "too realistic boss" edge case from Section 6)
- **Repo root resolution** — `art_gen.py` is at `tools/art_gen/`; output paths in `prompts.json` are relative to repo root (two levels up)

### Acceptance criteria
- File is at `tools/art_gen/art_gen.py`
- CLI: `python art_gen.py` runs all assets; `--asset-id <id>` runs one; `--force` skips existence check; `--lora-weight <float>` overrides LoRA weight for the whole run
- Node ID constants at top of file: `NODE_POSITIVE`, `NODE_NEGATIVE`, `NODE_KSAMPLER`, `NODE_EMPTY_LATENT`, `NODE_LORA_LOADER` — default to `"6"`, `"7"`, `"3"`, `"5"`, `"10"` matching the reference workflow; user updates these if their exported IDs differ
- `generate_asset(asset, workflow_template, config, args)` function handles one asset (all frames)
- For each frame: deep-copies workflow, patches prompts/seed/size/lora, calls `queue_prompt` → `poll_until_done` → `download_image`, writes bytes to `{repo_root}/{output_dir}/{resolved_filename}`
- Skip logic: for single-frame assets, skip if output file exists. For multi-frame, skip if **all** frame files exist. Print `[SKIP] {id}` when skipping
- Progress output: `[OK] {id} → {relative_path}` on success, `[ERROR] {id} frame {n}: {message}` on exception (continue to next asset)
- Final summary line: `Done. {n_ok} generated, {n_skip} skipped, {n_err} errors.`
- Importing the module does not trigger generation (all logic is inside `if __name__ == "__main__":` or called functions)

### Claude prompt
```
Write tools/art_gen/art_gen.py — the main generation script for the RAPTOR
ComfyUI art pipeline.

The script lives at tools/art_gen/art_gen.py. Output paths in prompts.json are
relative to repo root (two directories up from this file).

── Imports ──────────────────────────────────────────────────────────────────
import argparse, copy, json, os, pathlib, sys
from comfyui_client import queue_prompt, poll_until_done, download_image

── Node ID constants (user updates to match their exported workflow) ─────────
NODE_POSITIVE    = "6"
NODE_NEGATIVE    = "7"
NODE_KSAMPLER    = "3"
NODE_EMPTY_LATENT = "5"
NODE_LORA_LOADER = "10"

── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR   = pathlib.Path(__file__).parent
REPO_ROOT    = SCRIPT_DIR.parent.parent
PROMPTS_FILE = SCRIPT_DIR / "prompts.json"
WORKFLOW_FILE = SCRIPT_DIR / "workflow_api.json"

── CLI ───────────────────────────────────────────────────────────────────────
argparse with these flags:
  --asset-id   str   generate only this asset id (optional)
  --force      bool  regenerate even if output file exists (default False)
  --lora-weight float  override LoRA strength_model and strength_clip for all assets

── generate_asset(asset, workflow_template, config, args) ───────────────────
Handles one asset entry from prompts.json. Iterates over range(asset["frames"]).
For each frame index i:

  1. Determine output path:
       filename = asset["filename_pattern"].replace("{frame}", f"{i:02d}")
       out_path = REPO_ROOT / asset["output_dir"] / filename
  
  2. Skip check (unless args.force):
       If all frame files exist → print "[SKIP] {id}" and return
       (Check all frames before starting any, so partial runs get retried)

  3. Deep-copy the workflow template:
       workflow = copy.deepcopy(workflow_template)

  4. Patch the workflow copy:
       workflow[NODE_POSITIVE]["inputs"]["text"] = config["style_prefix"] + ", " + asset["prompt"]
       workflow[NODE_NEGATIVE]["inputs"]["text"] = config["negative_prompt"]
       workflow[NODE_KSAMPLER]["inputs"]["seed"]  = asset["seed"] + i
       workflow[NODE_EMPTY_LATENT]["inputs"]["width"]  = asset["size"][0]
       workflow[NODE_EMPTY_LATENT]["inputs"]["height"] = asset["size"][1]
       # LoRA override: per-asset field takes priority, then --lora-weight CLI arg
       lora_w = asset.get("lora_weight") or getattr(args, "lora_weight", None)
       if lora_w is not None:
           workflow[NODE_LORA_LOADER]["inputs"]["strength_model"] = lora_w
           workflow[NODE_LORA_LOADER]["inputs"]["strength_clip"]  = lora_w

  5. Submit and retrieve:
       prompt_id = queue_prompt(workflow)
       comfy_filename = poll_until_done(prompt_id, timeout=180.0)
       png_bytes = download_image(comfy_filename)

  6. Write to disk:
       out_path.parent.mkdir(parents=True, exist_ok=True)
       out_path.write_bytes(png_bytes)
       print(f"[OK] {asset['id']} frame {i} → {out_path.relative_to(REPO_ROOT)}")

  On any exception from steps 5–6: print "[ERROR] {id} frame {i}: {e}", do not
  re-raise; the caller counts errors.

── main() ────────────────────────────────────────────────────────────────────
  Load prompts.json → config dict
  Load workflow_api.json → workflow_template dict
  Build asset list: all assets, or just the one matching --asset-id (KeyError
  if id not found → print helpful message and sys.exit(1))
  
  Counters: n_ok = 0, n_skip = 0, n_err = 0
  
  For each asset, call generate_asset(); use return value to update counters:
    generate_asset returns a tuple (ok_count, skip_count, err_count) per-frame.
  
  Print final summary:
    f"Done. {n_ok} generated, {n_skip} skipped, {n_err} errors."

if __name__ == "__main__":
    main()

Requirements:
- Never mutate workflow_template — always deep-copy inside generate_asset
- Skip check covers all frames before starting any — don't generate frame 0 then
  skip frames 1-5
- Imports from comfyui_client must work when both files are in the same directory
  (use sys.path.insert or rely on same-dir import resolution)
- No third-party imports other than comfyui_client (which is also stdlib-only)
```

---

## TICKET-ART-004 — Post-processing + manifest: terrain crop and art_manifest.json

**Effort:** 2 h  
**Dependencies:** ART-003 (modifies art_gen.py in-place; manifest writer hooks into the same generation loop)

### Description
Two additive features bolted onto the generation loop from ART-003. Neither changes the core generate/skip/error logic — they are post-processing hooks and a bookkeeping side-channel.

**Terrain center-crop:** Stable Diffusion produces gradient falloff at image edges, so 1920px-wide terrain strips never tile cleanly. The fix is to generate at 2048px, then crop to the centre 1920px in Python with Pillow. The crop is applied in-place after the PNG is written, transparent to the rest of the pipeline. The detection trigger is `asset["size"][0] >= 1920` — this catches all five terrain strips and the four 1920×1080 UI/screen assets, all of which benefit from the same crop.

**art_manifest.json:** A JSON array of records, one per generated file, written to `tools/art_gen/art_manifest.json`. It is upserted after every successful or failed generation so a mid-run crash doesn't lose the record of completed work. Each record holds `id`, `filename`, `seed`, `frame`, `timestamp` (ISO 8601 UTC), `status` (`"OK"` or `"ERROR"`), and `error` (null or the exception message). The manifest is the audit trail for the full 74-asset run — the Section 9 checklist step "Review art_manifest.json for any ERROR lines" depends on it.

### Acceptance criteria
- `tools/art_gen/art_gen.py` is modified (not replaced) with both features integrated
- `pip install Pillow` is the only new dependency; it is documented in a comment at the top of the file
- `crop_center(img_path: pathlib.Path, target_width: int) -> None` function: opens image, computes `left = (img.width - target_width) // 2`, saves crop in-place; raises `ValueError` if `img.width < target_width`
- Crop is applied after writing to disk, only when `asset["size"][0] >= 1920`; for terrain assets the generation width is patched to `2048` and the seed to `asset["seed"] + 10` before submitting to ComfyUI (per Section 6 of the arch doc)
- `update_manifest(manifest_path, record: dict) -> None` function: loads existing manifest JSON (empty list if file missing), upserts the record by `(id, frame)` key, writes the file atomically
- Each manifest record: `{"id": str, "filename": str, "seed": int, "frame": int, "timestamp": str, "status": "OK"|"ERROR", "error": str|null}`
- `update_manifest` is called inside `generate_asset` after every frame — on success with `status: "OK"` and on exception with `status: "ERROR"` and the exception message
- `--manifest-path` CLI argument added (default: `str(SCRIPT_DIR / "art_manifest.json")`)
- Running the full batch twice produces a manifest with no duplicate `(id, frame)` pairs — the upsert overwrites stale records

### Claude prompt
```
Modify tools/art_gen/art_gen.py (already written) to add two features.
Do not rewrite the file from scratch — make surgical additions.

── New dependency (add comment near top of file) ────────────────────────────
# pip install Pillow   (required for terrain center-crop post-processing)
from PIL import Image
import datetime

── New CLI flag (add to argparse block) ─────────────────────────────────────
--manifest-path   str   path to art_manifest.json
                        default: str(SCRIPT_DIR / "art_manifest.json")

── New function: crop_center ────────────────────────────────────────────────
def crop_center(img_path: pathlib.Path, target_width: int) -> None:
    """
    Crop a PNG in-place to target_width pixels wide, taking from the horizontal
    centre. Used to remove gradient falloff on wide terrain/screen assets.

    Args:
        img_path: Path to the PNG file (modified in-place).
        target_width: Desired output width in pixels.

    Raises:
        ValueError: if the image is narrower than target_width.
    """
    img = Image.open(img_path)
    if img.width < target_width:
        raise ValueError(
            f"crop_center: image width {img.width} < target {target_width}")
    left = (img.width - target_width) // 2
    img.crop((left, 0, left + target_width, img.height)).save(img_path)

── New function: update_manifest ────────────────────────────────────────────
def update_manifest(manifest_path: pathlib.Path, record: dict) -> None:
    """
    Upsert a generation record into art_manifest.json.

    The manifest is a JSON array. Records are keyed by (id, frame). If a
    record with the same (id, frame) exists it is replaced; otherwise the new
    record is appended. The file is written after every call so a mid-run
    crash does not lose completed work.

    Args:
        manifest_path: Path to the manifest JSON file.
        record: Dict with keys id, filename, seed, frame, timestamp,
                status ("OK" or "ERROR"), error (str or None).
    """
    if manifest_path.exists():
        records = json.loads(manifest_path.read_text(encoding="utf-8"))
    else:
        records = []
    # Upsert by (id, frame)
    key = (record["id"], record["frame"])
    for i, r in enumerate(records):
        if (r["id"], r["frame"]) == key:
            records[i] = record
            break
    else:
        records.append(record)
    manifest_path.write_text(
        json.dumps(records, indent=2, ensure_ascii=False), encoding="utf-8")

── Modifications to generate_asset ─────────────────────────────────────────
Inside the per-frame loop, make these two changes:

CHANGE 1 — Terrain wide-gen patch (apply before submitting to ComfyUI):
  If asset["size"][0] >= 1920:
    # Generate 2048px wide so we have margin to crop; offset seed to avoid
    # reusing the exact same latent as a non-terrain run.
    workflow[NODE_EMPTY_LATENT]["inputs"]["width"] = 2048
    workflow[NODE_KSAMPLER]["inputs"]["seed"] = asset["seed"] + 10 + i

CHANGE 2 — After writing png_bytes to disk:
  If asset["size"][0] >= 1920:
    crop_center(out_path, target_width=asset["size"][0])

CHANGE 3 — Manifest update on success (after crop step):
  update_manifest(
      pathlib.Path(args.manifest_path),
      {
          "id": asset["id"],
          "filename": str(out_path.relative_to(REPO_ROOT)),
          "seed": workflow[NODE_KSAMPLER]["inputs"]["seed"],
          "frame": i,
          "timestamp": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
          "status": "OK",
          "error": None,
      }
  )

CHANGE 4 — Manifest update on exception (in the except block):
  update_manifest(
      pathlib.Path(args.manifest_path),
      {
          "id": asset["id"],
          "filename": str((REPO_ROOT / asset["output_dir"] /
                           asset["filename_pattern"].replace("{frame}", f"{i:02d}"))
                          .relative_to(REPO_ROOT)),
          "seed": asset["seed"] + i,
          "frame": i,
          "timestamp": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
          "status": "ERROR",
          "error": str(e),
      }
  )

Output the complete modified art_gen.py with all four changes integrated.
Preserve all existing logic from ART-003 unchanged.
```

---

## Ticket summary

| ID | Title | File(s) produced | Effort | Depends on |
|----|-------|-----------------|--------|------------|
| ART-001 | Data files | `prompts.json`, `workflow_api.json` | 0.5 h | — |
| ART-002 | ComfyUI HTTP client | `comfyui_client.py` | 2.0 h | ART-001 |
| ART-003 | Core generation script | `art_gen.py` | 3.0 h | ART-001, ART-002 |
| ART-004 | Post-processing + manifest | `art_gen.py` (modified) | 2.0 h | ART-003 |
| | **Total** | | **7.5 h** | |

## Usage after all tickets are complete

```
tools/art_gen/
├── prompts.json          ← ART-001
├── workflow_api.json     ← ART-001 (replace with your exported copy)
├── comfyui_client.py     ← ART-002
├── art_gen.py            ← ART-003 + ART-004
└── art_manifest.json     ← generated at runtime by ART-004
```

Run sequence per Section 5 of the arch doc:
```bash
# 1. Validate style (Group 1)
python art_gen.py --asset-id player_idle
python art_gen.py --asset-id wraith_fighter_idle
python art_gen.py --asset-id alien_passenger_idle

# 2. Full batch once style is approved
python art_gen.py

# 3. Review for errors
python -c "import json; [print(r) for r in json.load(open('art_manifest.json')) if r['status']=='ERROR']"
```
