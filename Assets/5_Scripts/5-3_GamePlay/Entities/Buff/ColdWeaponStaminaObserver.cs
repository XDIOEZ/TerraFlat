using MemoryPack;
using UnityEngine;
/// <summary>
/// 处理与精力相关的逻辑与字段，由 Mod_Food 驱动（观察者模式中的观察者）。
/// </summary>
[System.Serializable]
public partial class ColdWeaponStaminaObserver : ModuleObserverBase
{
    [Header("状态参数")]
    public State state = new State();
    [Header("关联模块")]
    [MemoryPackIgnore]
    private Mod_Stamina stamina;
    [MemoryPackIgnore]
    private Mod_ColdWeapon coldWeapon;

    private bool isAttacking = false;

    [MemoryPackable]
    [System.Serializable]
    public partial class State
    {
        public float StaminaConsumeSpeed = 1f;
        public float StaminaConsumeSpeedRate = 1f;
        public float StaminaLeast = 20f;
    }

    public override void OnInit(Module mod)
    {
        if (mod.item.Owner != null)
        {
            mod.item.Owner.itemMods.GetMod_ByID(ModText.Stamina, out stamina);
        }
        coldWeapon = mod as Mod_ColdWeapon;
        if (coldWeapon != null)
        {
            coldWeapon.OnAttackStart+= StartConsumingStamina;
            coldWeapon.OnAttackStop += StopConsumingStamina;
        }
    }
    public override void OnUpdate(float timeDelta)
    {
        if (isAttacking && stamina != null && coldWeapon != null)
        {
            stamina.AddStamina(-state.StaminaConsumeSpeed * state.StaminaConsumeSpeedRate * timeDelta);

            bool staminaOK = stamina.CurrentValue > state.StaminaLeast;

            if (!staminaOK)
            {
                coldWeapon.StopAttack();
            }

            coldWeapon.CanAttack &= staminaOK;
        }
    }

    public void OnDestroy()
    {
        if (coldWeapon != null)
        {
            coldWeapon.OnAttackStart -= StartConsumingStamina;
            coldWeapon.OnAttackStop -= StopConsumingStamina;
        }
    }

    private void StartConsumingStamina()
    {
        isAttacking = true;
    }

    private void StopConsumingStamina()
    {
        isAttacking = false;
    }

    public override byte[] OnSave(Module mod)
    {
        return MemoryPack.MemoryPackSerializer.Serialize(state);
    }

    public override void OnLoad(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return;
        }

        var restored = MemoryPack.MemoryPackSerializer.Deserialize<State>(payload);
        if (restored != null)
        {
            state = restored;
        }
    }
}