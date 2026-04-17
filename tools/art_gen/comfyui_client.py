# tools/art_gen/comfyui_client.py
# ─────────────────────────────────────────────────────────────────────────────
# Stdlib-only HTTP client for the ComfyUI API (localhost:8188).
#
# Three functions cover the full generation lifecycle:
#   queue_prompt()     — submit a workflow, get a prompt_id back
#   poll_until_done()  — wait for ComfyUI to finish processing that prompt
#   download_image()   — fetch the resulting PNG bytes
#
# Zero third-party dependencies: uses only urllib.request, urllib.parse,
# json, and time so this module runs in any plain Python 3.8+ environment
# without a virtualenv.
#
# Usage (from art_gen.py):
#   from comfyui_client import queue_prompt, poll_until_done, download_image
#
#   prompt_id    = queue_prompt(workflow)
#   png_filename = poll_until_done(prompt_id, timeout=180.0)
#   png_bytes    = download_image(png_filename)
# ─────────────────────────────────────────────────────────────────────────────

import json
import time
import urllib.parse
import urllib.request

# ── Base URL ──────────────────────────────────────────────────────────────────
# Change this if ComfyUI is running on a different host or port.
COMFYUI_URL = "http://127.0.0.1:8188"


# ── queue_prompt ──────────────────────────────────────────────────────────────

def queue_prompt(workflow: dict) -> str:
    """Submit a ComfyUI workflow to the generation queue.

    POSTs the workflow to /prompt and returns the server-assigned prompt_id
    that can be passed to poll_until_done() to track completion.

    Args:
        workflow: A ComfyUI workflow dict in API format (the node graph loaded
            from workflow_api.json, patched with per-asset prompt/seed/size
            values).

    Returns:
        The prompt_id string assigned by ComfyUI (e.g.
        "3b2a1f0e-dead-beef-cafe-000000000001").

    Raises:
        RuntimeError: If the HTTP response status is not 200, with the
            message "queue_prompt failed: HTTP {status}".
    """
    payload = json.dumps({"prompt": workflow}).encode("utf-8")
    #print(payload)
    req = urllib.request.Request(
        url=f"{COMFYUI_URL}/prompt",
        data=payload,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req) as resp:
        status = resp.status
        if status != 200:
            raise RuntimeError(f"queue_prompt failed: HTTP {status}")
        body = json.loads(resp.read().decode("utf-8"))

    return body["prompt_id"]


# ── poll_until_done ───────────────────────────────────────────────────────────

def poll_until_done(prompt_id: str, timeout: float = 120.0) -> str:
    """Wait for a queued ComfyUI job to finish and return the output filename.

    Polls GET /history/{prompt_id} every 0.5 seconds.  ComfyUI returns an
    empty dict ``{}`` while the job is still queued or running; the job is
    complete when ``prompt_id`` appears as a key in the response.

    The output filename is extracted by scanning the ``outputs`` dict for the
    first node that contains an ``"images"`` list, then returning
    ``images[0]["filename"]``.

    Args:
        prompt_id: The prompt_id string returned by queue_prompt().
        timeout: Maximum seconds to wait before giving up. Defaults to 120.0.
            Increase for large/high-resolution assets (terrain, boss character)
            that take longer on the GPU.

    Returns:
        The filename string of the generated image as stored in ComfyUI's
        output directory (e.g. "raptor_00001_.png"). Pass this to
        download_image() to retrieve the PNG bytes.

    Raises:
        TimeoutError: If the job has not completed within ``timeout`` seconds,
            with the message "Timed out after {timeout}s waiting for
            {prompt_id}".
    """
    url = f"{COMFYUI_URL}/history/{prompt_id}"
    deadline = time.monotonic() + timeout

    while time.monotonic() < deadline:
        with urllib.request.urlopen(url) as resp:
            body = json.loads(resp.read().decode("utf-8"))

        if prompt_id in body:
            # Job complete — find the first node that has an images list.
            outputs = body[prompt_id]["outputs"]
            for node_id, node_output in outputs.items():
                if "images" in node_output and node_output["images"]:
                    return node_output["images"][0]["filename"]

            # outputs present but no images key — shouldn't happen with a
            # SaveImage node, but guard against it gracefully.
            raise RuntimeError(
                f"poll_until_done: job {prompt_id} finished but no images "
                f"found in outputs: {list(outputs.keys())}"
            )

        time.sleep(0.5)

    raise TimeoutError(
        f"Timed out after {timeout}s waiting for {prompt_id}"
    )


# ── download_image ────────────────────────────────────────────────────────────

def download_image(
    filename: str,
    subfolder: str = "",
    image_type: str = "output",
) -> bytes:
    """Download a generated image from ComfyUI's /view endpoint.

    ComfyUI stores generated images in its own output directory.  This
    function fetches the raw PNG bytes over HTTP so art_gen.py can write them
    to the repo's asset tree without needing filesystem access to the ComfyUI
    installation.

    Args:
        filename: The image filename returned by poll_until_done() (e.g.
            "raptor_00001_.png").
        subfolder: Optional subfolder within ComfyUI's output directory.
            Defaults to "" (root output dir).
        image_type: The ComfyUI image type parameter. Defaults to "output".
            Other valid values are "input" and "temp".

    Returns:
        Raw PNG file content as bytes.  Write directly to disk with
        ``pathlib.Path.write_bytes()``.

    Raises:
        RuntimeError: If the HTTP response status is not 200, with the
            message "download_image failed: HTTP {status}".
    """
    params = urllib.parse.urlencode({
        "filename": filename,
        "subfolder": subfolder,
        "type": image_type,
    })
    url = f"{COMFYUI_URL}/view?{params}"

    with urllib.request.urlopen(url) as resp:
        status = resp.status
        if status != 200:
            raise RuntimeError(f"download_image failed: HTTP {status}")
        return resp.read()


# ── Smoke test ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print("OK — comfyui_client loaded")
