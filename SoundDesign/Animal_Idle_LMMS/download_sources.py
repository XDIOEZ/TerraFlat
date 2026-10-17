"""下载有明确许可的真实录音，保留来源页面和原始文件供重建。"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent


# region 真实录音与许可存证
def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (FlatWorld audio asset preparation)"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def download():
    manifest_path = ROOT / "sources.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for name, recording in manifest["recordings"].items():
        destination = ROOT / recording["sourceFile"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        proof = ROOT / "Licenses" / (name + ".html")
        proof.parent.mkdir(exist_ok=True)
        if not proof.is_file():
            page = fetch(recording["sourcePage"]).decode("utf-8")
            proof.write_text("\n".join(line.rstrip() for line in page.splitlines()) + "\n", encoding="utf-8")
        page = proof.read_text(encoding="utf-8")
        assert recording["licenseEvidence"] in page, f"License statement missing: {name}"
        url = recording.get("downloadUrl")
        if not url:
            urls = re.findall(r"https://cdn\.freesound\.org/previews/[\w/.-]+-hq\.mp3", page)
            assert urls, f"Public high-quality preview missing: {name}"
            url = urls[0]
        if not destination.is_file():
            destination.write_bytes(fetch(url))
        digest = hashlib.sha256(destination.read_bytes()).hexdigest()
        expected = recording.get("sha256")
        assert not expected or expected == digest, f"Recording changed: {name}"
        recording.update(downloadUrl=url, sha256=digest, licenseProof=proof.relative_to(ROOT).as_posix())
        print(f"{name}: {destination.stat().st_size} bytes / {digest}")
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.parse_args()
    download()
