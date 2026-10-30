using FlatWorld.Networking;
using UnityEngine;

public partial class Mod_DamageReceiver
{
    #region 非生物的燃烧销毁
    public void ApplyWorldFireExposure(float exposureSeconds, float burnDurationSeconds)
    {
        if (!GameNetwork.HasStateAuthority || item == null || item.DestructionHandled || Hp <= 0f || exposureSeconds <= 0f) return;
        Hp = Mathf.Max(0f, Hp - MaxHp * exposureSeconds / burnDurationSeconds);
        if (Hp > 0f) { Save(); return; }
        // 烧毁不触发采集、拆除返还和普通死亡战利品。
        Data.DeathCause = new DeathCauseRecord { CauseId = "burning" };
        ConsumeCurrentDeath();
        ItemMgr.Instance.DespawnItem(item, saveData: false);
    }
    #endregion
}
