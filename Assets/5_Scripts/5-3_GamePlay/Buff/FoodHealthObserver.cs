using MemoryPack;

[System.Serializable]
public partial class FoodHealthObserver : ModuleObserverBase
{
    [MemoryPackIgnore]
    private DamageReceiver damageReceiver;
    public State state = new State();
    [MemoryPackIgnore]
    private Mod_Food food;

    [MemoryPackable]
    [System.Serializable]
    public partial class State
    {
        public bool Enabled = true;
        public float HealSpeed = 1f;
        public float WaterSelfHurt = 1f;
        public float ProteinSelfHurt = 1f;
        public float VitaminSelfHurt = 1f;
        public float HealNeedRatio = 0.6f;
    }

    public override void OnInit(Module mod)
    {
        if (mod is Mod_Food food)
        {
            this.food = food;
            if (damageReceiver == null)
            {
                food.item.itemMods.GetMod_ByID(ModText.Hp, out damageReceiver);
            }
        }
    }

    public override void OnUpdate(float timeDelta)
    {
        if (!state.Enabled || damageReceiver == null || food == null)
        {
            return;
        }

        var nutrition = food.Data.nutrition;
        float proteinHealNeed = nutrition.Max_Protein * state.HealNeedRatio;
        float waterHealNeed = nutrition.Max_Water * state.HealNeedRatio;

        if (nutrition.Protein <= 0)
        {
            damageReceiver.ForceHurt(state.ProteinSelfHurt * timeDelta);
        }
        else if (nutrition.Protein >= proteinHealNeed && nutrition.Water >= waterHealNeed)
        {
            damageReceiver.Heal(state.HealSpeed * timeDelta, food.item);
        }

        if (nutrition.Water <= 0)
        {
            damageReceiver.ForceHurt(state.WaterSelfHurt * timeDelta);
        }

        if (nutrition.Vitamins <= 0)
        {
            damageReceiver.ForceHurt(state.VitaminSelfHurt * timeDelta);
        }
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