"""离线回归：python Tools/Tests/RainWetness/verify.py；需要 .NET 8 SDK 与 Node，不操作 Unity。"""
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys


def main():
    root = Path(__file__).resolve().parents[3]
    config = root / "Assets/StreamingAssets/GameConfig/Buffs/attribute_modifiers.json"
    wiki = root / "Assets/StreamingAssets/ItemWiki"
    spec = importlib.util.spec_from_file_location("buff_validation", wiki / "buff_validation.py")
    validation = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validation)
    wet = next(b for b in json.loads(config.read_text(encoding="utf-8-sig"))["buffs"] if b["id"] == "潮湿")
    count = 0
    for package in (root / "Assets/StreamingAssets/GameConfig/Buffs").glob("*.json"):
        for buff in json.loads(package.read_text(encoding="utf-8-sig")).get("buffs", []):
            validation.validate_definition(buff)
            count += 1
    for field, value in [
        ("rainStackIntervalSeconds", -1), ("rainStackIntervalSeconds", float("inf")),
        ("rainStackIntervalSeconds", 0), ("rainMaxStacks", -1), ("rainMaxStacks", 11),
        ("rainMaxStacks", 1.5), ("rainMaxStacks", True), ("rainReferenceIntensity", 0),
        ("rainReferenceIntensity", 1.1), ("rainReferenceIntensity", float("nan")),
    ]:
        invalid = deepcopy(wet)
        invalid[field] = value
        try:
            validation.validate_definition(invalid)
        except ValueError:
            pass
        else:
            raise AssertionError(f"Wiki accepted invalid {field}={value!r}")
    disabled = deepcopy(wet)
    for field in ("rainStackIntervalSeconds", "rainMaxStacks", "rainReferenceIntensity"):
        disabled[field] = 0
    validation.validate_definition(disabled)
    print(f"PASS: {count} production Buff definitions and 11 rain-schema cases in Wiki validation.", flush=True)
    js = """
const assert = require('node:assert/strict');
const fs = require('node:fs');
const wiki = require(process.argv[1]);
const wet = JSON.parse(fs.readFileSync(process.argv[2], 'utf8').replace(/^\\uFEFF/, '')).buffs.find(b => b.id === '潮湿');
for (const field of ['rainStackIntervalSeconds', 'rainMaxStacks', 'rainReferenceIntensity'])
    assert(wiki.stats(wet).some(row => row[2].path === field));
const text = wiki.describe(wet).join(' ');
assert(text.includes('60') && text.includes('5') && text.includes('0.65'));
console.log('PASS: Wiki exposes all three editable rain fields and derived rules.');
"""
    subprocess.run(["node", "-e", js, str(wiki / "buffs.js"), str(config)], check=True, cwd=root)
    subprocess.run(["dotnet", "run", "--project", str(Path(__file__).with_name("RainWetness.Tests.csproj")),
                    "--", str(config)], check=True, cwd=root)


if __name__ == "__main__":
    try:
        main()
    except (OSError, AssertionError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        sys.exit(1)
