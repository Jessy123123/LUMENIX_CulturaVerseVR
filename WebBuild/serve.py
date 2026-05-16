import http.server
import os
import mimetypes

# Teach Python's mimetypes the correct MIME for Unity's gz files
mimetypes.add_type("application/javascript",   ".js.gz")
mimetypes.add_type("application/wasm",         ".wasm.gz")
mimetypes.add_type("application/octet-stream", ".data.gz")

class UnityWebGLHandler(http.server.SimpleHTTPRequestHandler):

    def send_head(self):
        # Intercept .gz files before SimpleHTTPRequestHandler sets wrong headers
        url_path = self.path.split("?")[0]
        if url_path.endswith(".gz"):
            fs_path = self.translate_path(url_path)
            if not os.path.isfile(fs_path):
                self.send_error(404, "File not found")
                return None

            # Pick the right Content-Type for the decompressed content
            if ".wasm" in url_path:
                ctype = "application/wasm"
            elif ".js" in url_path:
                ctype = "application/javascript"
            else:
                ctype = "application/octet-stream"

            size = os.path.getsize(fs_path)
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Content-Length", str(size))
            self.send_header("Cross-Origin-Opener-Policy", "same-origin")
            self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
            self.end_headers()
            return open(fs_path, "rb")

        return super().send_head()

    def end_headers(self):
        url_path = self.path.split("?")[0]
        if not url_path.endswith(".gz"):
            self.send_header("Cross-Origin-Opener-Policy", "same-origin")
            self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        super().end_headers()

    def log_message(self, format, *args):
        pass

if __name__ == "__main__":
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    port = 8080
    server = http.server.HTTPServer(("", port), UnityWebGLHandler)
    print(f"Serving Unity WebGL at http://localhost:{port}")
    server.serve_forever()
