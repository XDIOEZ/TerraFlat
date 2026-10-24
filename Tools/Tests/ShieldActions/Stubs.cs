// 仅替代 Unity 外壳，测试链接正式动作时钟和临时防御代码，不重新实现被测规则。
public class CombatDefense { public float Physical; }
public partial class Mod_DamageReceiver
{
    private bool _deathHandled;
    public float Hp = 100;
    public bool IsRuntimeTransitioning;
    public CombatDefense Defense = new() { Physical = 3 };
    private void ClearTemporaryInvulnerability() { }
    public void EndLifetime() { _deathHandled = true; ClearTemporaryCombatEffects(); }
    public void BeginNewLifetime() { _deathHandled = false; Hp = 100; }
}
