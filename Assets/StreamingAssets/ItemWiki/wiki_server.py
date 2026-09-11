"""FlatWorld Item Wiki 本地服务。

核心职责：
1. 以项目根目录提供静态文件，使 Wiki 可以继续使用相对路径读取 GameConfig 与 Sprite。
2. 仅允许写入 Item Manifest 中已启用的 JSON 分包。
3. 写入前校验文件指纹、Item 继承、分包 shellPrefab、战利品引用与重复 ID。
4. 写入前创建备份，并使用临时文件替换，避免浏览器编辑破坏权威 JSON。
"""

from __future__ import annotations

import argparse
import copy
from datetime import datetime, timezone
import hashlib
import json
import os
import socket
import shutil
import sys
import threading
import time
import webbrowser
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import unquote, urlparse


WIKI_DIRECTORY = Path(__file__).resolve().parent
PROJECT_ROOT = WIKI_DIRECTORY.parents[2]
ITEM_ROOT = PROJECT_ROOT / "Assets" / "StreamingAssets" / "GameConfig" / "Items"
ITEM_MANIFEST = ITEM_ROOT / "item-manifest.json"
LOOT_TABLE_FILE = PROJECT_ROOT / "Assets" / "StreamingAssets" / "GameConfig" / "LootTables" / "loot-tables.json"
BACKUP_ROOT = PROJECT_ROOT / "Library" / "FlatWorldItemWiki" / "Backups"
ITEM_METADATA_FILE = WIKI_DIRECTORY / "item-metadata.json"
MAX_REQUEST_BYTES = 2 * 1024 * 1024
SAVE_LOCK = threading.Lock()
WIKI_URL_PATH = "/Assets/StreamingAssets/ItemWiki/"
PUBLIC_WIKI_FILES = {
    "app.js",
    "index.html",
    "item-metadata.json",
    "module-glossary.json",
    "styles.css",
}
PUBLIC_IMAGE_SUFFIXES = (".png", ".jpg", ".jpeg", ".webp")


class WikiValidationError(Exception):
    """表示浏览器提交的数据不满足当前项目 Item JSON 契约。"""


class WikiConflictError(Exception):
    """表示目标 JSON 已被其他工具修改，当前编辑必须先重新加载。"""


class ExclusiveThreadingHTTPServer(ThreadingHTTPServer):
    """独占监听端口，避免旧静态服务器与 Wiki 写入服务同时占用同一地址。"""

    allow_reuse_address = False

    def server_bind(self) -> None:
        """Windows 下启用独占地址，其它平台使用普通绑定。"""
        if os.name == "nt" and hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()


def read_json(path: Path) -> dict[str, Any]:
    """读取 UTF-8 JSON 根对象。"""
    with path.open("r", encoding="utf-8-sig") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise WikiValidationError(f"JSON 根节点不是对象：{path.name}")
    return value


def file_sha256(path: Path) -> str:
    """返回文件原始字节 SHA-256，用于阻止覆盖外部修改。"""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_item_metadata() -> dict[str, Any]:
    """读取 Wiki 独立 Item 元数据字典；该文件不参与游戏运行时配置。"""
    if not ITEM_METADATA_FILE.exists():
        return {"schemaVersion": 1, "items": {}}
    metadata = read_json(ITEM_METADATA_FILE)
    if metadata.get("schemaVersion") != 1 or not isinstance(metadata.get("items"), dict):
        raise WikiValidationError("item-metadata.json 结构无效")
    return metadata


def write_item_modified_time(item_id: str) -> str:
    """记录指定 Item 最近一次经 Wiki 成功修改的 UTC 时间。"""
    metadata = load_item_metadata()
    modified_at = datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
    metadata["items"][item_id] = modified_at
    temporary_path = ITEM_METADATA_FILE.with_suffix(ITEM_METADATA_FILE.suffix + ".tmp")
    serialized = json.dumps(metadata, ensure_ascii=False, indent=2) + "\n"
    try:
        with temporary_path.open("w", encoding="utf-8", newline="") as handle:
            handle.write(serialized)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary_path, ITEM_METADATA_FILE)
    finally:
        if temporary_path.exists():
            temporary_path.unlink()
    return modified_at


def normalize_package_path(value: str) -> str:
    """只接受 Manifest 内部的普通相对路径，禁止目录穿越和盘符。"""
    raw = str(value or "").replace("\\", "/").strip()
    path = PurePosixPath(raw)
    if not raw or path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise WikiValidationError(f"分包路径无效：{value}")
    if ":" in raw:
        raise WikiValidationError(f"分包路径包含非法冒号：{value}")
    return path.as_posix()


def load_manifest_packages() -> list[dict[str, Any]]:
    """读取并校验 Item Manifest 的启用分包。"""
    manifest = read_json(ITEM_MANIFEST)
    if manifest.get("schemaVersion") != 1:
        raise WikiValidationError("item-manifest.json 的 schemaVersion 不受支持")
    packages = manifest.get("packages")
    if not isinstance(packages, list) or not packages:
        raise WikiValidationError("Item Manifest 没有 packages")

    result: list[dict[str, Any]] = []
    ids: set[str] = set()
    paths: set[str] = set()
    for package in packages:
        if not isinstance(package, dict):
            raise WikiValidationError("Item Manifest 包含空或非法分包")
        package_id = str(package.get("id") or "").strip()
        package_path = normalize_package_path(package.get("path") or "")
        if not package_id:
            raise WikiValidationError("Item Manifest 包含空分包 ID")
        if package_id.lower() in ids:
            raise WikiValidationError(f"Item Manifest 包含重复分包 ID：{package_id}")
        if package_path.lower() in paths:
            raise WikiValidationError(f"Item Manifest 包含重复分包路径：{package_path}")
        ids.add(package_id.lower())
        paths.add(package_path.lower())
        if package.get("enabled", True) is not False:
            result.append({**package, "id": package_id, "path": package_path})
    if not result:
        raise WikiValidationError("Item Manifest 没有启用的分包")
    return result


def merge_like_json_net(base: Any, override: Any) -> Any:
    """模拟当前 ItemDefinitionCatalogLoader 的 JObject.Merge 行为。"""
    if isinstance(override, list):
        return copy.deepcopy(override)
    if not isinstance(override, dict):
        return copy.deepcopy(override)
    output = copy.deepcopy(base) if isinstance(base, dict) else {}
    for key, value in override.items():
        if isinstance(value, list):
            output[key] = copy.deepcopy(value)
        elif isinstance(value, dict) and isinstance(output.get(key), dict):
            output[key] = merge_like_json_net(output[key], value)
        elif isinstance(value, dict):
            output[key] = merge_like_json_net({}, value)
        else:
            output[key] = copy.deepcopy(value)
    return output


def remove_replaced_module_bodies(inherited: dict[str, Any], source: dict[str, Any]) -> None:
    """子定义更换模块 Prefab 时清理父模块身体，保持与运行时解析规则一致。"""
    inherited_modules = inherited.get("modules")
    source_modules = source.get("modules")
    if not isinstance(inherited_modules, dict) or not isinstance(source_modules, dict):
        return
    for module_name, child_body in source_modules.items():
        parent_body = inherited_modules.get(module_name)
        if not isinstance(child_body, dict) or not isinstance(parent_body, dict):
            continue
        parent_prefab = str(parent_body.get("prefab") or "").strip().lower()
        child_prefab = str(child_body.get("prefab") or "").strip().lower()
        if child_prefab and child_prefab != parent_prefab:
            inherited_modules.pop(module_name, None)


def resolve_items(sources: dict[str, dict[str, Any]]) -> dict[str, dict[str, Any]]:
    """解析全部 parent 继承并拒绝缺失父项或循环。"""
    resolved: dict[str, dict[str, Any]] = {}
    resolving: set[str] = set()

    def resolve_one(key: str) -> dict[str, Any]:
        if key in resolved:
            return resolved[key]
        if key in resolving:
            raise WikiValidationError(f"物品定义继承存在循环：{sources[key].get('id', key)}")
        source = sources.get(key)
        if source is None:
            raise WikiValidationError(f"找不到物品定义：{key}")
        resolving.add(key)
        result: dict[str, Any] = {}
        parent_id = str(source.get("parent") or "").strip()
        if parent_id:
            parent_key = parent_id.lower()
            if parent_key not in sources:
                raise WikiValidationError(f"物品 {source.get('id')} 找不到 parent：{parent_id}")
            result = copy.deepcopy(resolve_one(parent_key))
            result.pop("gameName", None)
            result.pop("labelKey", None)
            result.pop("descriptionKey", None)
        remove_replaced_module_bodies(result, source)
        result = merge_like_json_net(result, source)
        result["id"] = source["id"]
        result["abstract"] = bool(source.get("abstract", False))
        result.pop("parent", None)
        resolving.remove(key)
        resolved[key] = result
        return result

    for source_key in sources:
        resolve_one(source_key)
    return resolved


def walk_named_values(value: Any, name: str) -> list[Any]:
    """递归收集指定字段，供模块内嵌 LootPrefabName 校验。"""
    result: list[Any] = []
    if isinstance(value, dict):
        for key, child in value.items():
            if key.lower() == name.lower():
                result.append(child)
            result.extend(walk_named_values(child, name))
    elif isinstance(value, list):
        for child in value:
            result.extend(walk_named_values(child, name))
    return result


def load_loot_tables() -> dict[str, dict[str, Any]]:
    """读取全局战利品目录，并按稳定 ID 建立索引。"""
    root = read_json(LOOT_TABLE_FILE)
    if root.get("schemaVersion") != 1 or not isinstance(root.get("lootTables"), list):
        raise WikiValidationError("loot-tables.json 结构无效")
    tables: dict[str, dict[str, Any]] = {}
    for table in root["lootTables"]:
        if not isinstance(table, dict):
            raise WikiValidationError("loot-tables.json 包含非法表定义")
        table_id = str(table.get("id") or "").strip()
        if not table_id or table_id.lower() in tables:
            raise WikiValidationError(f"战利品表 ID 为空或重复：{table_id or '<空>'}")
        tables[table_id.lower()] = table
    return tables


def validate_all_items(roots_by_package: dict[str, dict[str, Any]], packages: list[dict[str, Any]]) -> None:
    """对提议中的所有 Item 分包执行当前 Wiki 写入所需的完整静态校验。"""
    sources: dict[str, dict[str, Any]] = {}
    package_sources: dict[str, list[dict[str, Any]]] = {}
    for package in packages:
        root = roots_by_package.get(package["id"])
        if not isinstance(root, dict) or root.get("schemaVersion") != 1 or not isinstance(root.get("items"), list):
            raise WikiValidationError(f"分包 {package['id']} 结构无效")
        package_sources[package["id"]] = []
        for source in root["items"]:
            if not isinstance(source, dict):
                raise WikiValidationError(f"分包 {package['id']} 包含非对象 Item")
            item_id = str(source.get("id") or "").strip()
            if not item_id:
                raise WikiValidationError(f"分包 {package['id']} 包含空 Item ID")
            key = item_id.lower()
            if key in sources:
                raise WikiValidationError(f"跨分包存在重复 Item ID：{item_id}")
            normalized = copy.deepcopy(source)
            normalized["id"] = item_id
            sources[key] = normalized
            package_sources[package["id"]].append(normalized)

    resolved = resolve_items(sources)
    concrete_ids = {item["id"].lower() for item in resolved.values() if not item.get("abstract", False)}

    for item in resolved.values():
        if not item.get("abstract", False):
            if not str(item.get("shellPrefab") or "").strip():
                raise WikiValidationError(f"物品 {item['id']} 缺少 shellPrefab")
            if not str(item.get("gameName") or "").strip():
                raise WikiValidationError(f"物品 {item['id']} 缺少默认显示名 gameName")

    for package in packages:
        expected_shell = str(package.get("shellPrefab") or "").strip()
        if not expected_shell:
            continue
        for source in package_sources[package["id"]]:
            actual = str(resolved[source["id"].lower()].get("shellPrefab") or "").strip()
            if actual.lower() != expected_shell.lower():
                raise WikiValidationError(
                    f"物品 {source['id']} 解析出的 shellPrefab 与分包 {package['id']} 不一致；"
                    f"expected={expected_shell}, actual={actual}"
                )

    loot_tables = load_loot_tables()
    for item in resolved.values():
        table_id = str(item.get("lootTableId") or "").strip()
        if table_id:
            table = loot_tables.get(table_id.lower())
            if table is None:
                raise WikiValidationError(f"物品 {item['id']} 引用了不存在的战利品表：{table_id}")
            entries = table.get("entries") or []
            if not isinstance(entries, list):
                raise WikiValidationError(f"战利品表 {table_id} 的 entries 无效")
            for entry in entries:
                entry_id = str(entry.get("itemId") or "").strip() if isinstance(entry, dict) else ""
                if not entry_id or entry_id.lower() not in concrete_ids:
                    raise WikiValidationError(f"战利品表 {table_id} 引用了不存在或抽象的 ItemDefinition：{entry_id}")

        modules = item.get("modules")
        if isinstance(modules, dict):
            for embedded_id in walk_named_values(modules, "LootPrefabName"):
                target = str(embedded_id or "").strip()
                if not target or target.lower() not in concrete_ids:
                    raise WikiValidationError(
                        f"物品 {item['id']} 模块引用了不存在或抽象的 ItemDefinition：{target}"
                    )


def save_item(payload: dict[str, Any]) -> dict[str, Any]:
    """校验并原子写回单个 Item 原始定义。"""
    item_id = str(payload.get("itemId") or "").strip()
    package_path = normalize_package_path(payload.get("packagePath") or "")
    expected_hash = str(payload.get("expectedHash") or "").strip().lower()
    source = payload.get("source")
    if not item_id or not isinstance(source, dict):
        raise WikiValidationError("保存请求缺少 itemId 或 source")
    source_id = str(source.get("id") or "").strip()
    if source_id.lower() != item_id.lower():
        raise WikiValidationError("稳定 ID 不允许在 Wiki 编辑器中修改")
    if not expected_hash:
        raise WikiValidationError("保存请求缺少文件指纹，请重新读取 JSON")

    packages = load_manifest_packages()
    package = next((entry for entry in packages if entry["path"].lower() == package_path.lower()), None)
    if package is None:
        raise WikiValidationError(f"目标文件不是启用的 Item 分包：{package_path}")

    target_path = (ITEM_ROOT / Path(package_path)).resolve()
    item_root_resolved = ITEM_ROOT.resolve()
    if target_path.parent != item_root_resolved and item_root_resolved not in target_path.parents:
        raise WikiValidationError("目标文件越出 Item 配置目录")
    if not target_path.is_file():
        raise WikiValidationError(f"目标分包不存在：{package_path}")

    current_hash = file_sha256(target_path)
    if current_hash.lower() != expected_hash:
        raise WikiConflictError("目标 JSON 已被其他工具修改，请先点击“重新读取 JSON”后再编辑。")

    roots_by_package: dict[str, dict[str, Any]] = {}
    target_root: dict[str, Any] | None = None
    target_index = -1
    for current_package in packages:
        current_path = ITEM_ROOT / current_package["path"]
        root = read_json(current_path)
        roots_by_package[current_package["id"]] = root
        if current_package["path"].lower() == package_path.lower():
            target_root = root
            for index, current_source in enumerate(root.get("items") or []):
                if isinstance(current_source, dict) and str(current_source.get("id") or "").strip().lower() == item_id.lower():
                    if target_index >= 0:
                        raise WikiValidationError(f"分包内存在重复 Item ID：{item_id}")
                    target_index = index

    if target_root is None or target_index < 0:
        raise WikiConflictError(f"原 Item {item_id} 已被移动或删除，请重新读取 JSON。")

    target_root["items"][target_index] = copy.deepcopy(source)
    validate_all_items(roots_by_package, packages)

    original_bytes = target_path.read_bytes()
    newline = "\r\n" if b"\r\n" in original_bytes else "\n"
    has_utf8_bom = original_bytes.startswith(b"\xef\xbb\xbf")
    serialized = json.dumps(target_root, ensure_ascii=False, indent=2).replace("\n", newline) + newline
    timestamp = time.strftime("%Y%m%d-%H%M%S")
    backup_directory = BACKUP_ROOT / f"{timestamp}-{int((time.time() % 1) * 1000):03d}"
    backup_directory.mkdir(parents=True, exist_ok=True)
    backup_path = backup_directory / target_path.name
    shutil.copy2(target_path, backup_path)

    temporary_path = target_path.with_suffix(target_path.suffix + ".itemwiki.tmp")
    try:
        encoding = "utf-8-sig" if has_utf8_bom else "utf-8"
        with temporary_path.open("w", encoding=encoding, newline="") as handle:
            handle.write(serialized)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary_path, target_path)
    finally:
        if temporary_path.exists():
            temporary_path.unlink()

    modified_at = None
    try:
        modified_at = write_item_modified_time(item_id)
    except Exception as error:
        print(f"[Item Wiki] metadata update failed for {item_id}: {error}", file=sys.stderr)

    return {
        "ok": True,
        "itemId": item_id,
        "packagePath": package_path,
        "hash": file_sha256(target_path),
        "backup": str(backup_path.relative_to(PROJECT_ROOT)).replace("\\", "/"),
        "modifiedAt": modified_at,
    }


class WikiRequestHandler(SimpleHTTPRequestHandler):
    """同时提供静态 Wiki 与受限 JSON 写入 API。"""

    public_readonly = False

    def __init__(self, *args: Any, **kwargs: Any) -> None:
        super().__init__(*args, directory=str(PROJECT_ROOT), **kwargs)

    def send_json(self, status: int, payload: dict[str, Any]) -> None:
        """输出统一 JSON 响应。"""
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def end_headers(self) -> None:
        """公开模式补充基础浏览器安全响应头，避免页面被第三方站点嵌入。"""
        request_path = urlparse(self.path).path
        if request_path == WIKI_URL_PATH.rstrip("/") or request_path.startswith(WIKI_URL_PATH):
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
            self.send_header("Pragma", "no-cache")
            self.send_header("Expires", "0")
        if self.public_readonly:
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("X-Frame-Options", "DENY")
            self.send_header("Referrer-Policy", "no-referrer")
        super().end_headers()

    def is_public_static_path_allowed(self, request_path: str) -> bool:
        """公开模式只放行 Wiki 页面、Item 配置、战利品表和 Sprite 所需文件。"""
        decoded = unquote(request_path)
        if "\\" in decoded or "\x00" in decoded:
            return False
        raw_parts = decoded.split("/")
        if any(part in {".", ".."} for part in raw_parts):
            return False

        normalized = PurePosixPath(decoded.lstrip("/")).as_posix()
        lower = normalized.lower()
        wiki_prefix = "assets/streamingassets/itemwiki/"
        if lower == wiki_prefix.rstrip("/"):
            return True
        if lower.startswith(wiki_prefix):
            relative = normalized[len(wiki_prefix):]
            return "/" not in relative and relative.lower() in PUBLIC_WIKI_FILES

        item_prefix = "assets/streamingassets/gameconfig/items/"
        if lower.startswith(item_prefix) and lower.endswith(".json"):
            return True
        if lower == "assets/streamingassets/gameconfig/loottables/loot-tables.json":
            return True

        if lower.startswith("assets/"):
            if lower.endswith(PUBLIC_IMAGE_SUFFIXES):
                return True
            if any(lower.endswith(f"{suffix}.meta") for suffix in PUBLIC_IMAGE_SUFFIXES):
                return True
        return False

    def send_wiki_redirect(self) -> None:
        """把公开根地址直接重定向到 Wiki 首页，避免展示项目目录。"""
        self.send_response(302)
        self.send_header("Location", WIKI_URL_PATH)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self) -> None:
        """返回服务能力或普通静态资源。"""
        request_path = urlparse(self.path).path
        if request_path == "/api/wiki/status":
            self.send_json(
                200,
                {
                    "writable": not self.public_readonly,
                    "publicReadOnly": self.public_readonly,
                    "service": "FlatWorldItemWiki",
                    "version": 1,
                },
            )
            return
        if self.public_readonly:
            if request_path == "/":
                self.send_wiki_redirect()
                return
            if not self.is_public_static_path_allowed(request_path):
                self.send_error(404, "Not Found")
                return
        super().do_GET()

    def do_HEAD(self) -> None:
        """公开模式对 HEAD 使用与 GET 相同的静态文件白名单。"""
        request_path = urlparse(self.path).path
        if self.public_readonly:
            if request_path == "/":
                self.send_wiki_redirect()
                return
            if request_path != "/api/wiki/status" and not self.is_public_static_path_allowed(request_path):
                self.send_error(404, "Not Found")
                return
        super().do_HEAD()

    def do_POST(self) -> None:
        """处理 Item 写入 API，其他 POST 一律拒绝。"""
        if self.public_readonly:
            self.send_json(403, {"ok": False, "error": "公开 Wiki 为只读模式，禁止写入项目数据。"})
            return
        if urlparse(self.path).path != "/api/items/save":
            self.send_json(404, {"ok": False, "error": "未知 API"})
            return
        try:
            length = int(self.headers.get("Content-Length") or "0")
            if length <= 0 or length > MAX_REQUEST_BYTES:
                raise WikiValidationError("请求体为空或超过 2 MB 上限")
            payload = json.loads(self.rfile.read(length).decode("utf-8"))
            if not isinstance(payload, dict):
                raise WikiValidationError("请求根节点必须是对象")
            with SAVE_LOCK:
                result = save_item(payload)
            self.send_json(200, result)
        except WikiConflictError as error:
            self.send_json(409, {"ok": False, "error": str(error)})
        except (WikiValidationError, json.JSONDecodeError, UnicodeDecodeError) as error:
            self.send_json(400, {"ok": False, "error": str(error)})
        except Exception as error:  # 防止服务进程因单次保存异常退出。
            print(f"[Item Wiki] save failed: {error}", file=sys.stderr)
            self.send_json(500, {"ok": False, "error": f"保存失败：{error}"})


def main() -> None:
    """启动本机 Wiki 服务；公开分享时可切换为严格只读白名单模式。"""
    parser = argparse.ArgumentParser(description="FlatWorld Item Wiki local server")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--no-browser", action="store_true", help="只启动服务，不自动打开浏览器")
    parser.add_argument("--public-readonly", action="store_true", help="启用公开分享只读白名单模式")
    parser.add_argument("--strict-port", action="store_true", help="端口被占用时直接失败，不自动顺延")
    args = parser.parse_args()

    WikiRequestHandler.public_readonly = args.public_readonly

    server = None
    selected_port = args.port
    candidates = (args.port,) if args.strict_port else range(args.port, args.port + 20)
    for candidate in candidates:
        try:
            server = ExclusiveThreadingHTTPServer(("127.0.0.1", candidate), WikiRequestHandler)
            selected_port = candidate
            break
        except OSError:
            continue
    if server is None:
        port_range = str(args.port) if args.strict_port else f"{args.port}..{args.port + 19}"
        raise RuntimeError(f"端口 {port_range} 被占用，无法启动 Item Wiki。")

    wiki_url = f"http://127.0.0.1:{selected_port}{WIKI_URL_PATH}"
    print(f"[Item Wiki] {wiki_url}")
    if args.public_readonly:
        print("[Item Wiki] Public read-only allowlist enabled. Press Ctrl+C to stop.")
    else:
        print("[Item Wiki] JSON edit API enabled for localhost only. Press Ctrl+C to stop.")
    if not args.no_browser:
        webbrowser.open(wiki_url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
