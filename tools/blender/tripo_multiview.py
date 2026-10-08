"""Prepare, and only when told, run one Tripo multiview-to-model trial (plain CPython).

Without --submit nothing leaves the machine: the trial folder gets trial.json with the input
hashes, the exact request and the credit estimate, and the run stops. With --submit (and the key
in TRIPO_API_KEY, never on the command line) it checks the balance, uploads the views, creates
one task, polls it, downloads every output file unchanged and records their hashes. Nothing here
calls Tripo's rig, texture or convert endpoints.

Prices are the H-series rows of Tripo's pricing page as read on 2026-10-07 (100 credits = 1 USD);
the task's own credits_consumed is recorded and is the authority.
"""
import argparse, datetime, hashlib, json, mimetypes, os, sys, time, urllib.error, urllib.request, uuid

API = "https://openapi.tripo3d.ai/v3"   # overseas accounts; China-mainland accounts use openapi.tripo3d.com
VIEWS = ("front", "left", "back", "right")

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()

def estimate(params):
    """Credits for one H-series multiview task, from the pricing page's base rows and add-ons."""
    textured = params.get("texture", True) or params.get("pbr", True)
    credits = 30 if textured else 20
    if textured:
        credits += {"detailed": 10, "extreme": 20}.get(params.get("texture_quality", "standard"), 0)
    if params.get("geometry_quality") == "detailed":
        credits += 20
    if params.get("quad"):
        credits += 5
    if params.get("smart_low_poly"):
        credits += 10
    if params.get("generate_parts"):
        credits += 20
    return credits

def call(method, url, key, body=None, content_type="application/json"):
    data = body if isinstance(body, (bytes, type(None))) else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": f"Bearer {key}"})
    if data is not None:
        req.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            reply = json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {url}: HTTP {e.code}: {e.read()[:500]!r}")
    if reply.get("code") != 0:
        sys.exit(f"{method} {url}: {reply}")
    return reply["data"]

def upload(path, key):
    boundary = uuid.uuid4().hex
    mime = mimetypes.guess_type(path)[0] or "application/octet-stream"
    with open(path, "rb") as f:
        payload = f.read()
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{os.path.basename(path)}\"\r\n"
            f"Content-Type: {mime}\r\n\r\n").encode() + payload + f"\r\n--{boundary}--\r\n".encode()
    return call("POST", f"{API}/files", key, body, f"multipart/form-data; boundary={boundary}")["file_token"]

def main():
    global API
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for v in VIEWS:
        ap.add_argument(f"--{v}", help=f"{v} view image (PNG, JPEG or WebP)")
    ap.add_argument("--params", required=True, help="JSON file of request parameters (everything but inputs)")
    ap.add_argument("--out", required=True, help="trial folder; created, never reused")
    ap.add_argument("--submit", action="store_true", help="upload and spend credits (needs TRIPO_API_KEY)")
    ap.add_argument("--max-credits", type=float, help="with --submit: refuse if the estimate exceeds this")
    ap.add_argument("--poll", type=float, default=10.0, help="seconds between status checks")
    ap.add_argument("--api", default=API, help="API base (default: the overseas region)")
    ap.add_argument("--ignore-balance", action="store_true", help="submit even if the balance looks too low (Tripo refuses unfunded tasks)")
    a = ap.parse_args()
    API = a.api.rstrip("/")

    views = {v: os.path.abspath(getattr(a, v)) for v in VIEWS if getattr(a, v)}
    if "front" not in views or len(views) < 2:
        sys.exit("Tripo needs the front view and at least one other")
    with open(a.params) as f:
        params = json.load(f)
    if "inputs" in params:
        sys.exit("--params must not contain inputs; pass the views as arguments")
    out = os.path.abspath(a.out)
    trial_path = os.path.join(out, "trial.json")
    if os.path.exists(trial_path) and json.load(open(trial_path)).get("status") not in ("prepared", "refused"):
        sys.exit(f"{out} already holds a submitted trial; use a new folder")
    os.makedirs(out, exist_ok=True)

    trial = {
        "service": f"Tripo API (H series), POST {a.api.rstrip('/')}/generation/multiview-to-model",
        "prepared": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        "status": "prepared",
        "inputs": {v: {"file": p, "sha256": sha256(p), "bytes": os.path.getsize(p)} for v, p in views.items()},
        "params": params,
        "estimated_credits": estimate(params),
        "estimate_source": "developers.tripo3d.com pricing, read 2026-10-07; 100 credits = 1 USD",
    }
    if not a.submit:
        json.dump(trial, open(trial_path, "w"), indent=1)
        print(json.dumps(trial, indent=1))
        print(f"\nprepared only: nothing uploaded, no credits spent. {trial_path}")
        return

    key = os.environ.get("TRIPO_API_KEY")
    if not key:
        sys.exit("TRIPO_API_KEY is not set")
    if a.max_credits is None or trial["estimated_credits"] > a.max_credits:
        sys.exit(f"estimate {trial['estimated_credits']} credits exceeds --max-credits {a.max_credits}")
    trial["balance_before"] = call("GET", f"{API}/account/balance", key)
    if trial["balance_before"]["balance"] < trial["estimated_credits"] and not a.ignore_balance:
        sys.exit(f"balance {trial['balance_before']} below the estimate")
    trial["status"] = "uploading"
    json.dump(trial, open(trial_path, "w"), indent=1)
    tokens = {v: upload(p, key) for v, p in views.items()}
    trial["submitted"] = datetime.datetime.now().astimezone().isoformat(timespec="seconds")
    task = call("POST", f"{API}/generation/multiview-to-model", key,
                {"inputs": [{v: {"file_token": t}} for v, t in tokens.items()], **params})
    trial.update(status="submitted", task_id=task["task_id"])
    json.dump(trial, open(trial_path, "w"), indent=1)
    print("task", task["task_id"])

    while True:
        time.sleep(a.poll)
        d = call("GET", f"{API}/tasks/{task['task_id']}", key)
        print(d["status"], d.get("progress"))
        if d["status"] not in ("queued", "running"):
            break
    trial.update(status=d["status"], task=d)
    if d["status"] == "success":
        trial["outputs"] = {}
        for name, url in (d.get("output") or {}).items():
            if not isinstance(url, str) or not url.startswith("http"):
                continue
            ext = os.path.splitext(url.split("?")[0])[1] or ".bin"
            path = os.path.join(out, name + ext)
            urllib.request.urlretrieve(url, path)
            trial["outputs"][name] = {"file": path, "sha256": sha256(path), "bytes": os.path.getsize(path)}
    trial["balance_after"] = call("GET", f"{API}/account/balance", key)
    json.dump(trial, open(trial_path, "w"), indent=1)
    print(json.dumps({k: trial.get(k) for k in ("status", "task_id", "outputs")}, indent=1))

if __name__ == "__main__":
    main()
