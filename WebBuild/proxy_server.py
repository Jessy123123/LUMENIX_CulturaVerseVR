import http.server
import os
import mimetypes
import json
import urllib.request
import urllib.error

# ── MIME types for Unity gz bundles ────────────────────────────────────────
mimetypes.add_type("application/javascript",   ".js.gz")
mimetypes.add_type("application/wasm",         ".wasm.gz")
mimetypes.add_type("application/octet-stream", ".data.gz")

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

# ── Reassemble split large files ────────────────────────────────────────────
def _reassemble_parts(target):
    part0, part1 = target + ".part0", target + ".part1"
    if os.path.exists(target) and os.path.getsize(target) > 1_000_000:
        return  # already assembled and looks real
    if os.path.exists(part0) and os.path.exists(part1):
        print(f"Assembling {os.path.basename(target)} from parts...")
        with open(target, "wb") as out:
            for p in [part0, part1]:
                with open(p, "rb") as f:
                    out.write(f.read())
        print(f"  Done: {os.path.getsize(target):,} bytes")

_reassemble_parts(os.path.join(BASE_DIR, "Build", "WebBuild.data.gz"))

# ── Load .env ───────────────────────────────────────────────────────────────
def _load_env(path):
    env = {}
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if "=" in line and not line.startswith("#"):
                    k, v = line.split("=", 1)
                    env[k.strip()] = v.strip()
    except FileNotFoundError:
        pass
    return env

# ── Load config.json ────────────────────────────────────────────────────────
def _load_config(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}

_env    = _load_env(os.path.join(BASE_DIR, "StreamingAssets", ".env"))
_config = _load_config(os.path.join(BASE_DIR, "StreamingAssets", "config.json"))

# Prefer Railway env vars, fall back to .env file
GROQ_KEY   = os.environ.get("GROQ_API_KEY",        _env.get("GROQ_API_KEY", ""))
GOOGLE_KEY = os.environ.get("GOOGLE_API_KEY",       _env.get("GOOGLE_API_KEY", ""))
HF_KEY     = os.environ.get("HUGGINGFACE_API_KEY",  _env.get("HUGGINGFACE_API_KEY", ""))
OLLAMA_URL = os.environ.get("OLLAMA_URL",           _config.get("ollamaUrl", "http://localhost:11434"))
PORT       = int(os.environ.get("PORT", 8080))


# ── Low-level proxy helper ──────────────────────────────────────────────────
def _forward(url, body, extra_headers=None, timeout=30):
    headers = {"Content-Type": "application/json"}
    if extra_headers:
        headers.update(extra_headers)
    req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()
    except Exception as e:
        return 502, json.dumps({"error": str(e)}).encode()


# ── Per-route handlers ──────────────────────────────────────────────────────
def _route_stt(body):
    url = f"https://speech.googleapis.com/v1/speech:recognize?key={GOOGLE_KEY}"
    return _forward(url, body)

def _route_groq(body):
    url = "https://api.groq.com/openai/v1/chat/completions"
    return _forward(url, body, {"Authorization": f"Bearer {GROQ_KEY}"})

def _route_ollama(body):
    url = f"{OLLAMA_URL}/api/generate"
    return _forward(url, body, timeout=60)

def _route_tts(body):
    url = f"https://texttospeech.googleapis.com/v1/text:synthesize?key={GOOGLE_KEY}"
    return _forward(url, body)

def _route_emotion(body):
    url = f"https://language.googleapis.com/v1/documents:analyzeSentiment?key={GOOGLE_KEY}"
    return _forward(url, body)

def _route_huggingface(body):
    url = "https://router.huggingface.co/hf-inference/models/j-hartmann/emotion-english-distilroberta-base"
    return _forward(url, body, {
        "Authorization": f"Bearer {HF_KEY}",
        "Content-Type": "application/json; charset=utf-8",
    })


PROXY_ROUTES = {
    "/proxy/stt":         _route_stt,
    "/proxy/groq":        _route_groq,
    "/proxy/ollama":      _route_ollama,
    "/proxy/tts":         _route_tts,
    "/proxy/emotion":     _route_emotion,
    "/proxy/huggingface": _route_huggingface,
}


# ── Request handler ─────────────────────────────────────────────────────────
class Handler(http.server.SimpleHTTPRequestHandler):

    def do_OPTIONS(self):
        self.send_response(204)
        self._cors()
        self.end_headers()

    def do_POST(self):
        path = self.path.split("?")[0]
        handler = PROXY_ROUTES.get(path)
        if handler is None:
            self.send_error(404, "Unknown proxy route")
            return

        length = int(self.headers.get("Content-Length", 0))
        body   = self.rfile.read(length) if length else b""

        status, data = handler(body)

        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self._cors()
        self.end_headers()
        self.wfile.write(data)

    def send_head(self):
        url_path = self.path.split("?")[0]
        if url_path.endswith(".gz"):
            fs_path = self.translate_path(url_path)
            if not os.path.isfile(fs_path):
                self.send_error(404, "File not found")
                return None

            ctype = (
                "application/wasm"         if ".wasm" in url_path else
                "application/javascript"   if ".js"   in url_path else
                "application/octet-stream"
            )
            size = os.path.getsize(fs_path)
            self.send_response(200)
            self.send_header("Content-Type",     ctype)
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Content-Length",   str(size))
            self._coep()
            self.end_headers()
            return open(fs_path, "rb")

        return super().send_head()

    def end_headers(self):
        if not self.path.split("?")[0].endswith(".gz"):
            self._coep()
        super().end_headers()

    def _cors(self):
        self.send_header("Access-Control-Allow-Origin",  "*")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")

    def _coep(self):
        self.send_header("Cross-Origin-Opener-Policy",   "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")

    def log_message(self, fmt, *args):
        print(f"  {self.address_string()} {fmt % args}")


if __name__ == "__main__":
    os.chdir(BASE_DIR)
    server = http.server.ThreadingHTTPServer(("", PORT), Handler)
    print(f"Proxy + static server on port {PORT}")
    print(f"  Groq key   : {'ok' if GROQ_KEY   else 'MISSING'}")
    print(f"  Google key : {'ok' if GOOGLE_KEY else 'MISSING'}")
    print(f"  HF key     : {'ok' if HF_KEY     else 'MISSING'}")
    print(f"  Ollama URL : {OLLAMA_URL}")
    server.serve_forever()
