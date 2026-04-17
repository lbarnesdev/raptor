# tools/art_gen/art_gen.py
# ─────────────────────────────────────────────────────────────────────────────
# RAPTOR art generation driver — submits assets to a local ComfyUI instance
# and writes the resulting PNGs into the repo's asset tree.
#
# Usage:
#   python art_gen.py                        # generate all assets, skip existing
#   python art_gen.py --asset-id player_idle # generate one asset by id
#   python art_gen.py --force                # regenerate even if file exists
#   python art_gen.py --lora-weight 1.0      # override LoRA weight for this run
#
# Prerequisites:
#   ComfyUI running at localhost:8188
#   prompts.json and workflow_api.json in the same directory as this script
#   comfyui_client.py in the same directory as this script
#   pip install Pillow   (required for sprite downscale + terrain center-crop)
#   pip install rembg    (recommended — automatic background removal for sprites)
#   pip install onnxruntime  (required by rembg)
#
# Background removal:
#   rembg is used to strip the background from all sprite assets and save them
#   as RGBA PNGs.  Terrain strips and screen backgrounds are NOT processed.
#   If rembg is not installed, background removal is skipped with a warning.
#   Install with: pip install rembg onnxruntime
#
# Node ID constants (Section 7 of the arch doc):
#   If your exported workflow_api.json uses different node IDs, update the
#   NODE_* constants below to match before running.
# ─────────────────────────────────────────────────────────────────────────────

import argparse
import copy
import datetime
import json
import pathlib
import sys

from PIL import Image

# ── Optional rembg import ─────────────────────────────────────────────────────
# rembg removes backgrounds from generated sprites and saves transparent PNGs.
# Gracefully degrades if not installed — sprites are kept as RGB in that case.
try:
    from rembg import remove as _rembg_remove
    _REMBG_AVAILABLE = True
except ImportError:
    _REMBG_AVAILABLE = False

# ── Same-directory import resolution ─────────────────────────────────────────
# Ensures `from comfyui_client import …` works whether the script is invoked
# as `python art_gen.py` from any working directory or imported as a module.
_SCRIPT_DIR = pathlib.Path(__file__).resolve().parent
if str(_SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPT_DIR))

from comfyui_client import download_image, poll_until_done, queue_prompt  # noqa: E402

# ── Generation resolution floor ──────────────────────────────────────────────
# SD 1.5-based models (DreamShaper 8) produce coherent output only at or near
# their training resolution of 512 px.  Assets whose shortest dimension is
# smaller than GEN_MIN are scaled up proportionally so that the shorter side
# equals GEN_MIN; the image is then downscaled to the true target size in a
# post-processing step.  This is the same strategy used for terrain (generate
# wide → crop to target), generalised to all sprite sizes.
GEN_MIN = 512

# SD coherence degrades on very wide canvases (aspect ratio > ~1.5:1) because
# it treats the left and right halves as separate compositional units, causing
# objects like aircraft to split at the midpoint.  GEN_MAX_LONG caps the longer
# generation dimension so the canvas stays within the ratio SD handles cleanly.
# The downscale step corrects the final size regardless of what this is set to.
GEN_MAX_LONG = 768

# ── Node ID constants ─────────────────────────────────────────────────────────
# These match the reference workflow_api.json shipped with this repo.
# Update to match your own exported workflow if your node IDs differ.
NODE_POSITIVE     = "12"
NODE_NEGATIVE     = "13"
NODE_KSAMPLER     = "16"
NODE_EMPTY_LATENT = "14"
NODE_LORA_LOADER  = "11"

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR    = _SCRIPT_DIR
REPO_ROOT     = SCRIPT_DIR.parent.parent
PROMPTS_FILE  = SCRIPT_DIR / "prompts.json"
WORKFLOW_FILE = SCRIPT_DIR / "workflow_api.json"


# ── Post-processing helpers ───────────────────────────────────────────────────

def remove_background(img_path: pathlib.Path) -> None:
    """Remove the background from a sprite PNG using rembg, saving as RGBA.

    SD 1.5 cannot output true transparency — it always generates an RGB image
    with some background colour.  rembg uses a neural segmentation model (U2Net)
    to predict the foreground mask and writes the result back as a 32-bit RGBA
    PNG, giving Godot a sprite with a clean alpha channel.

    Only called for sprite assets (output_dir contains "sprites/").  Terrain
    strips and screen backgrounds are left as-is because they ARE the background.

    If rembg is not installed this function is a no-op and prints a one-time
    warning so the user knows background removal was skipped.

    Args:
        img_path: Path to the PNG file.  Modified in-place (RGBA output).
    """
    if not _REMBG_AVAILABLE:
        print(
            "[WARN] rembg not installed — background NOT removed from sprites.\n"
            "       Run: pip install rembg onnxruntime",
            file=sys.stderr,
        )
        return
    img_bytes = img_path.read_bytes()
    result    = _rembg_remove(img_bytes)   # returns RGBA PNG bytes
    img_path.write_bytes(result)


def scale_gen_size(target_w: int, target_h: int) -> tuple[int, int]:
    """Return the generation size for a target sprite resolution.

    SD 1.5 models produce coherent output only near their training resolution
    of 512 px.  If the shorter side of the target is below GEN_MIN (512), both
    dimensions are scaled up proportionally so the shorter side equals GEN_MIN.
    The result is rounded to the nearest multiple of 8 (latent space
    requirement).

    Examples:
        128 × 64  → 768 × 384   (short=64 → ×8 = 1024×512, long capped to 768 → 768×384)
        112 × 56  → 768 × 384   (same cap applies to enemy aircraft)
        512 × 512 → 512 × 512   (already at floor, no change)
        256 × 192 → 680 × 512   (no cap needed, long side is 680 < 768)
        1920 × 400 → unchanged  (handled separately by terrain wide-gen logic)

    Args:
        target_w: True output width in pixels (from prompts.json "size").
        target_h: True output height in pixels.

    Returns:
        (gen_w, gen_h) — the resolution to request from ComfyUI.
    """
    short = min(target_w, target_h)
    if short >= GEN_MIN:
        return target_w, target_h
    scale = GEN_MIN / short
    gen_w = round(target_w * scale / 8) * 8
    gen_h = round(target_h * scale / 8) * 8

    # Cap the longer dimension to GEN_MAX_LONG to avoid extreme aspect ratios
    # that cause SD to treat each half of the canvas as a separate composition.
    long = max(gen_w, gen_h)
    if long > GEN_MAX_LONG:
        cap_scale = GEN_MAX_LONG / long
        gen_w = round(gen_w * cap_scale / 8) * 8
        gen_h = round(gen_h * cap_scale / 8) * 8

    return gen_w, gen_h


def downscale_to_target(img_path: pathlib.Path, target_w: int, target_h: int) -> None:
    """Downscale a PNG in-place to (target_w, target_h) using high-quality Lanczos.

    Called after generation when scale_gen_size returned a size larger than the
    true target.  The image is resized to fit exactly within the target box and
    then saved back over the original file.

    Args:
        img_path: Path to the PNG.  Modified in-place.
        target_w: Final output width in pixels.
        target_h: Final output height in pixels.
    """
    img = Image.open(img_path)
    if img.size != (target_w, target_h):
        img.resize((target_w, target_h), Image.LANCZOS).save(img_path)


def flip_horizontal(img_path: pathlib.Path) -> None:
    """Mirror a PNG left-to-right in-place.

    SD has a strong training-data bias toward right-facing aircraft.
    Prompting against this bias causes the "Janus" artifact (two noses).
    The fix: generate right-facing (letting SD do what it wants naturally),
    then flip the image here to produce a clean left-facing sprite.

    Used for all enemy aircraft that must face left (Wraith, Specter).

    Args:
        img_path: Path to the PNG file.  Modified in-place.
    """
    img = Image.open(img_path)
    img.transpose(Image.FLIP_LEFT_RIGHT).save(img_path)


def crop_center(img_path: pathlib.Path, target_width: int) -> None:
    """Crop a PNG in-place to target_width pixels wide, taking from the centre.

    Stable Diffusion produces gradient colour falloff near image edges, so wide
    terrain and screen assets are generated at 2048 px and then cropped to
    their target width (typically 1920 px) to discard the degraded margins.
    The crop is centred horizontally; height is unchanged.

    Args:
        img_path: Path to the PNG file.  Modified in-place.
        target_width: Desired output width in pixels.

    Raises:
        ValueError: If the image is narrower than target_width.
    """
    img = Image.open(img_path)
    if img.width < target_width:
        raise ValueError(
            f"crop_center: image width {img.width} < target {target_width}"
        )
    left = (img.width - target_width) // 2
    img.crop((left, 0, left + target_width, img.height)).save(img_path)


def update_manifest(manifest_path: pathlib.Path, record: dict) -> None:
    """Upsert a generation record into art_manifest.json.

    The manifest is a JSON array of records keyed by (id, frame).  If a record
    with the same (id, frame) pair already exists it is replaced in-place;
    otherwise the new record is appended.  The file is written after every call
    so a mid-run crash does not lose the record of completed work.

    Args:
        manifest_path: Path to the manifest JSON file.  Created if absent.
        record: Dict with keys ``id``, ``filename``, ``seed``, ``frame``,
            ``timestamp``, ``status`` ("OK" or "ERROR"), ``error`` (str or
            None).
    """
    if manifest_path.exists():
        records: list[dict] = json.loads(
            manifest_path.read_text(encoding="utf-8")
        )
    else:
        records = []

    # Upsert by (id, frame) — replace existing entry or append new one.
    key = (record["id"], record["frame"])
    for idx, r in enumerate(records):
        if (r["id"], r["frame"]) == key:
            records[idx] = record
            break
    else:
        records.append(record)

    manifest_path.write_text(
        json.dumps(records, indent=2, ensure_ascii=False), encoding="utf-8"
    )


# ── Per-asset generation ──────────────────────────────────────────────────────

def generate_asset(
    asset: dict,
    workflow_template: dict,
    config: dict,
    args: argparse.Namespace,
) -> tuple[int, int, int]:
    """Generate all frames for a single asset entry from prompts.json.

    Workflow template is never mutated — a deep copy is made for each frame
    so that concurrent or sequential calls cannot bleed settings across assets.

    Skip logic: ALL frame output files must exist for the asset to be skipped.
    A partial run (some frames missing) is retried in full so the asset ends
    up complete.

    Args:
        asset: A single asset dict from prompts.json["assets"].
        workflow_template: The unmodified workflow loaded from workflow_api.json.
        config: The top-level prompts.json dict (provides style_prefix and
            negative_prompt).
        args: Parsed argparse.Namespace (provides .force and .lora_weight).

    Returns:
        A (ok_count, skip_count, err_count) tuple where each counter reflects
        the number of frames in each outcome category.
    """
    asset_id = asset["id"]
    n_frames = asset["frames"]

    # ── Resolve all output paths up front ────────────────────────────────────
    out_paths: list[pathlib.Path] = []
    for i in range(n_frames):
        filename = asset["filename_pattern"].replace("{frame}", f"{i:02d}")
        out_paths.append(REPO_ROOT / asset["output_dir"] / filename)

    # ── Skip check (all frames must exist; partial runs are retried in full) ──
    if not args.force and all(p.exists() for p in out_paths):
        print(f"[SKIP] {asset_id}")
        return (0, n_frames, 0)

    # ── Per-frame generation ──────────────────────────────────────────────────
    n_ok = n_err = 0

    for i, out_path in enumerate(out_paths):
        # Deep-copy the template so patching this frame cannot affect others.
        workflow = copy.deepcopy(workflow_template)

        # ── Patch prompts ─────────────────────────────────────────────────────
        full_positive = config["style_prefix"] + ", " + asset["prompt"]
        workflow[NODE_POSITIVE]["inputs"]["text"] = full_positive
        workflow[NODE_NEGATIVE]["inputs"]["text"] = config["negative_prompt"]

        # ── Patch seed (increments per frame so each frame is unique) ─────────
        workflow[NODE_KSAMPLER]["inputs"]["seed"] = asset["seed"] + i

        # ── Patch image dimensions (two mutually exclusive paths) ────────────
        target_w, target_h = asset["size"][0], asset["size"][1]

        if target_w >= 1920:
            # ── CHANGE 1: Terrain / full-screen wide-gen path ─────────────────
            # Wide assets are generated at 2048 px wide so the centre 1920 px
            # can be cropped free of SD edge falloff.  Height is kept as-is
            # (terrain strips are already tall enough for SD to handle cleanly).
            # Seed is offset by +10 to stay distinct from any non-wide run.
            gen_w, gen_h = 2048, target_h
            workflow[NODE_KSAMPLER]["inputs"]["seed"] = asset["seed"] + 10 + i
        else:
            # ── Sprite upscale path ───────────────────────────────────────────
            # scale_gen_size raises the shorter dimension to GEN_MIN (512 px)
            # so SD 1.5 produces coherent output.  The generated image is
            # downscaled to the true target size after writing to disk.
            gen_w, gen_h = scale_gen_size(target_w, target_h)

        workflow[NODE_EMPTY_LATENT]["inputs"]["width"]  = gen_w
        workflow[NODE_EMPTY_LATENT]["inputs"]["height"] = gen_h

        # ── LoRA weight override ──────────────────────────────────────────────
        # Per-asset "lora_weight" field takes priority over the --lora-weight
        # CLI flag; both override the template default of 0.85.
        lora_w = asset.get("lora_weight") or getattr(args, "lora_weight", None)
        if lora_w is not None:
            workflow[NODE_LORA_LOADER]["inputs"]["strength_model"] = float(lora_w)
            workflow[NODE_LORA_LOADER]["inputs"]["strength_clip"]  = float(lora_w)

        # ── Submit → poll → download → write ──────────────────────────────────
        try:
            prompt_id      = queue_prompt(workflow)
            comfy_filename = poll_until_done(prompt_id, timeout=180.0)
            png_bytes      = download_image(comfy_filename)

            out_path.parent.mkdir(parents=True, exist_ok=True)
            out_path.write_bytes(png_bytes)

            # ── CHANGE 2a: Centre-crop wide assets ────────────────────────────
            # Generated at 2048 px (CHANGE 1); crop to target width in-place.
            if target_w >= 1920:
                crop_center(out_path, target_width=target_w)

            # ── CHANGE 2b: Downscale small sprites to true target size ─────────
            # Generated at GEN_MIN floor (512 px short side); resize to the
            # true sprite dimensions so Godot imports at the correct size.
            elif (gen_w, gen_h) != (target_w, target_h):
                downscale_to_target(out_path, target_w, target_h)

            # ── CHANGE 2c: Flip left-facing sprites ───────────────────────────
            # SD has a strong right-facing bias for aircraft. Prompting against
            # it causes the "Janus" artifact (two noses / doubled subject).
            # Assets with "flip_horizontal": true are generated right-facing
            # (natural for SD) and then mirrored here to face left.
            if asset.get("flip_horizontal"):
                flip_horizontal(out_path)

            # ── CHANGE 2d: Remove background from sprite assets ───────────────
            # SD always generates an RGB background even when prompted not to.
            # rembg strips it and saves an RGBA PNG for use in Godot.
            # Terrain strips and screens are deliberately excluded — they ARE
            # the background.  The check uses the asset's output_dir field.
            _is_sprite = "sprites" in asset["output_dir"]
            if _is_sprite:
                remove_background(out_path)

            rel = out_path.relative_to(REPO_ROOT)
            print(f"[OK] {asset_id} frame {i} → {rel}")
            n_ok += 1

            # ── CHANGE 3: Manifest update on success ──────────────────────────
            update_manifest(
                pathlib.Path(args.manifest_path),
                {
                    "id": asset["id"],
                    "filename": str(out_path.relative_to(REPO_ROOT)),
                    "seed": workflow[NODE_KSAMPLER]["inputs"]["seed"],
                    "frame": i,
                    "timestamp": datetime.datetime.utcnow().strftime(
                        "%Y-%m-%dT%H:%M:%SZ"
                    ),
                    "status": "OK",
                    "error": None,
                },
            )

        except Exception as e:  # noqa: BLE001
            print(f"[ERROR] {asset_id} frame {i}: {e}")
            n_err += 1

            # ── CHANGE 4: Manifest update on exception ────────────────────────
            _err_filename = str(
                (
                    REPO_ROOT
                    / asset["output_dir"]
                    / asset["filename_pattern"].replace("{frame}", f"{i:02d}")
                ).relative_to(REPO_ROOT)
            )
            update_manifest(
                pathlib.Path(args.manifest_path),
                {
                    "id": asset["id"],
                    "filename": _err_filename,
                    "seed": asset["seed"] + i,
                    "frame": i,
                    "timestamp": datetime.datetime.utcnow().strftime(
                        "%Y-%m-%dT%H:%M:%SZ"
                    ),
                    "status": "ERROR",
                    "error": str(e),
                },
            )

    return (n_ok, 0, n_err)


# ── Entry point ───────────────────────────────────────────────────────────────

def main() -> None:
    """Parse CLI args, load data files, and run the generation loop."""

    # ── CLI ───────────────────────────────────────────────────────────────────
    parser = argparse.ArgumentParser(
        description="Generate RAPTOR game sprites via ComfyUI (localhost:8188).",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Examples:\n"
            "  python art_gen.py                        # full batch, skip existing\n"
            "  python art_gen.py --asset-id player_idle # single asset\n"
            "  python art_gen.py --force                # regenerate all\n"
            "  python art_gen.py --lora-weight 1.0      # stronger comic style\n"
        ),
    )
    parser.add_argument(
        "--asset-id",
        metavar="ID",
        help="Generate only the asset with this id (e.g. player_idle).",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        default=False,
        help="Regenerate assets even if the output file already exists.",
    )
    parser.add_argument(
        "--lora-weight",
        metavar="FLOAT",
        type=float,
        default=None,
        help=(
            "Override LoRA strength_model and strength_clip for every asset "
            "in this run (default: use per-asset value or template default 0.85)."
        ),
    )
    parser.add_argument(
        "--manifest-path",
        metavar="PATH",
        default=str(SCRIPT_DIR / "art_manifest.json"),
        help=(
            "Path to the art_manifest.json output file "
            "(default: tools/art_gen/art_manifest.json)."
        ),
    )
    args = parser.parse_args()

    # ── Load data files ───────────────────────────────────────────────────────
    if not PROMPTS_FILE.exists():
        print(f"[FATAL] prompts.json not found at {PROMPTS_FILE}", file=sys.stderr)
        sys.exit(1)
    if not WORKFLOW_FILE.exists():
        print(f"[FATAL] workflow_api.json not found at {WORKFLOW_FILE}", file=sys.stderr)
        sys.exit(1)

    config            = json.loads(PROMPTS_FILE.read_text(encoding="utf-8"))
    workflow_template = json.loads(WORKFLOW_FILE.read_text(encoding="utf-8"))
    all_assets: list[dict] = config["assets"]

    # ── Build asset list ──────────────────────────────────────────────────────
    if args.asset_id:
        asset_index = {a["id"]: a for a in all_assets}
        if args.asset_id not in asset_index:
            available = ", ".join(a["id"] for a in all_assets)
            print(
                f"[FATAL] Unknown asset id: '{args.asset_id}'.\n"
                f"Available ids: {available}",
                file=sys.stderr,
            )
            sys.exit(1)
        assets_to_run = [asset_index[args.asset_id]]
    else:
        assets_to_run = all_assets

    total = len(assets_to_run)
    print(
        f"RAPTOR art_gen — {total} asset(s) queued"
        f"{' [FORCE]' if args.force else ''}"
        f"{f' [lora-weight={args.lora_weight}]' if args.lora_weight else ''}"
    )

    # ── Generation loop ───────────────────────────────────────────────────────
    n_ok = n_skip = n_err = 0

    for asset in assets_to_run:
        ok, skip, err = generate_asset(asset, workflow_template, config, args)
        n_ok   += ok
        n_skip += skip
        n_err  += err

    # ── Summary ───────────────────────────────────────────────────────────────
    print(f"Done. {n_ok} generated, {n_skip} skipped, {n_err} errors.")
    if n_err:
        sys.exit(1)  # non-zero exit so CI can detect partial failures


if __name__ == "__main__":
    main()