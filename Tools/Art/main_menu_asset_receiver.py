"""Local-only PNG upload receiver for FlatWorld menu backgrounds.

Run: python Tools/Art/main_menu_asset_receiver.py
Upload: curl -X POST -H "X-Upload-Token: <printed token>" -H "Content-Type: image/png" --data-binary @image.png http://127.0.0.1:8765/upload/01_morning_grassland.png
"""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import os
import secrets
import struct


ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "Assets" / "6_Art" / "Generated" / "MainMenuBackgrounds"
TOKEN = os.environ.get("FLATWORLD_ASSET_UPLOAD_TOKEN") or secrets.token_urlsafe(32)
MAX_BYTES = 30 * 1024 * 1024
ALLOWED = {
    "01_morning_grassland.png", "02_orbital_world.png",
    "03_space_launch.png", "04_night_base.png",
    "05_rain_forest.png", "06_volcanic_land.png",
}


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        name = self.path.removeprefix("/upload/")
        if self.path != "/upload/" + name or name not in ALLOWED:
            return self.reply(404, "Unknown asset name")
        if not secrets.compare_digest(self.headers.get("X-Upload-Token", ""), TOKEN):
            return self.reply(403, "Invalid token")
        if self.headers.get("Content-Type", "").split(";")[0] != "image/png":
            return self.reply(415, "PNG only")
        try:
            size = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return self.reply(400, "Invalid size")
        if not 24 <= size <= MAX_BYTES:
            return self.reply(413, "Invalid size")
        data = self.rfile.read(size)
        if len(data) != size or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
            return self.reply(400, "Invalid PNG")
        width, height = struct.unpack(">II", data[16:24])
        if not (1280 <= width <= 8192 and 720 <= height <= 8192):
            return self.reply(400, "Unexpected dimensions")
        DEST.mkdir(parents=True, exist_ok=True)
        target = DEST / name
        temp = DEST / (name + ".uploading")
        temp.write_bytes(data)
        temp.replace(target)
        self.reply(200, f"Saved {name} ({width}x{height})")

    def reply(self, code, message):
        self.send_response(code)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.end_headers()
        self.wfile.write(message.encode())


if __name__ == "__main__":
    print(f"Destination: {DEST}", flush=True)
    print(f"Upload token: {TOKEN}", flush=True)
    print("Listening on http://127.0.0.1:8765", flush=True)
    ThreadingHTTPServer(("127.0.0.1", 8765), Handler).serve_forever()
