"""静态检查物理、伤害与表现的依赖方向，不刷新或控制Unity。"""
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[3]
GAMEPLAY = ROOT / "Assets/5_Scripts/5-3_GamePlay"


def main() -> None:
    forbidden = re.compile(
        r"\b(?:Mod_Damage|Mod_DamageReceiver|IGameplayPhysicsDamageTarget|DamageTextEffect|CombatPhysicsChannels)\b"
        r"|\.(?:SendMessage|BroadcastMessage)\s*\("
    )
    physics_files = list((GAMEPLAY / "Physics2D").rglob("*.cs"))
    violations = []
    for path in physics_files:
        for line, source in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if forbidden.search(source):
                violations.append(f"{path.relative_to(ROOT)}:{line}: {source.strip()}")

    obsolete = "GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>"
    for path in (ROOT / "Assets/5_Scripts").rglob("*.cs"):
        if obsolete in path.read_text(encoding="utf-8-sig"):
            violations.append(f"{path.relative_to(ROOT)}: 生命解析仍走通用物理组件查找")

    for name in ("Mod_ContactDamage.cs", "Mod_Projectile.cs"):
        source = (GAMEPLAY / "Entities/Combat" / name).read_text(encoding="utf-8-sig")
        if "GameplayCombatBridge.ResolvePhysicsBodyDamageReceiver(" not in source:
            violations.append(f"{name}: 身体接触缺少明确的主生命适配入口")

    core = (ROOT / "Assets/5_Scripts/Shared/Combat/CombatHitWindow.cs").read_text(encoding="utf-8-sig")
    if "using UnityEngine" in core or "UnityEngine." in core:
        violations.append("CombatHitWindow: 纯逻辑窗口不得依赖Unity物理对象")

    if violations:
        raise SystemExit("FAIL:\n" + "\n".join(violations))
    print(f"PASS: {len(physics_files)} physics sources and gameplay call sites respect combat boundaries.")
    print("Static dependency checks only; Unity collisions and displayed damage numbers were not executed.")


if __name__ == "__main__":
    main()
