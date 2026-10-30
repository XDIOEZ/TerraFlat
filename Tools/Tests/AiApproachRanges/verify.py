"""离线测试正式鱼觅食方法和共用距离规则；不启动、暂停或刷新 Unity。"""
from pathlib import Path
import subprocess
import sys


def extract_method(source: str, signature: str) -> str:
    """按唯一签名取生产方法，不在测试中复写追击/进食算法。"""
    if source.count(signature) != 1:
        raise ValueError(f"生产方法签名不唯一或已变化：{signature}")
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    cursor = opening + 1
    while depth and cursor < len(source):
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    if depth:
        raise ValueError(f"生产方法缺少闭括号：{signature}")
    return source[start:cursor]


def main() -> None:
    root = Path(__file__).resolve().parents[3]
    output = root / ".codex-tmp/ai-approach-tests"
    output.mkdir(parents=True, exist_ok=True)
    source = (root / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/Mod_AquaticMovement.cs").read_text(encoding="utf-8-sig")
    methods = [extract_method(source, signature) for signature in (
        "private void TickForage(float deltaTime)",
        "private bool SwimTowards(Vector2 position, float deltaTime,",
        "private void MovePosition(Vector2 position)",
    )]
    # 营养门槛也直接取正式表达式，替身只提供输入和外部世界服务。
    for name in ("WillEatAvailableFood", "ShouldActivelyForage"):
        start = source.index(f"private bool {name} =>")
        methods.append(source[start:source.index(";", start) + 1])
    generated = "using FlatWorld.Navigation;\nusing UnityEngine;\npublic sealed partial class FishHarness\n{\n"
    generated += "\n".join(methods) + "\n}\n"
    (output / "ProductionFish.cs").write_text(generated, encoding="utf-8")
    config = root / "Assets/StreamingAssets/GameConfig/Actors/definitions/fish-actors.json"
    subprocess.run(["dotnet", "run", "--project", str(Path(__file__).with_name("AiApproachRanges.Tests.csproj")),
                    "--", str(config)], cwd=root, check=True)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
