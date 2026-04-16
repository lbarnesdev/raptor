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
#   pip install Pillow   (required for terrain center-crop post-processing)
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

# ── Same-directory import resolution ─────────────────────────────────────────
# Ensures `from comfyui_client import …` works whether the script is invoked
# as `python art_gen.py` from any working directory or imported as a module.
_SCRIPT_DIR = pathlib.Path(__file__).resolve().parent
if str(_SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPT_DIR))

from comfyui_client import download_image, poll_until_done, queue_prompt  # noqa: E402

# ── Node ID constants ─────────────────────────────────────────────────────────
# These match the reference workflow_api.json shipped with this repo.
# Update to match your own exported workflow if your node IDs differ.
NODE_POSITIVE     = "6"
NODE_NEGATIVE     = "7"
NODE_KSAMPLER     = "3"
NODE_EMPTY_LATENT = "5"
NODE_LORA_LOADER  = "10"

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR    = _SCRIPT_DIR
REPO_ROOT     = SCRIPT_DIR.parent.parent
PROMPTS_FILE  = SCRIPT_DIR / "prompts.json"
WORKFLOW_FILE = SCRIPT_DIR / "workflow_api.json"


# ── Post-processing helpers ───────────────────────────────────────────────────

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

        # ── Patch image dimensions ────────────────────────────────────────────
        workflow[NODE_EMPTY_LATENT]["inputs"]["width"]  = asset["size"][0]
        workflow[NODE_EMPTY_LATENT]["inputs"]["height"] = asset["size"][1]

        # ── CHANGE 1: Terrain wide-gen patch ──────────────────────────────────
        # Wide assets (terrain strips, full-screen UI/screens) are generated at
        # 2048 px so the centre 1920 px can be cropped free of SD edge falloff.
        # The seed is offset by +10 (plus frame index) to keep it distinct from
        # any non-wide run that might use the same base seed.
        if asset["size"][0] >= 1920:
            workflow[NODE_EMPTY_LATENT]["inputs"]["width"] = 2048
            workflow[NODE_KSAMPLER]["inputs"]["seed"] = asset["seed"] + 10 + i

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

            # ── CHANGE 2: Centre-crop wide assets ─────────────────────────────
            # Generated at 2048 px (CHANGE 1); crop to target width in-place.
            if asset["size"][0] >= 1920:
                crop_center(out_path, target_width=asset["size"][0])

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
