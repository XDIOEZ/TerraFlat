using MemoryPack;
/// <summary>
/// 处理与精力相关的逻辑与字段，由 Mod_Food 驱动（观察者模式中的观察者）。
/// </summary>
[System.Serializable]
public partial class FoodStaminaObserver : ModuleObserverBase
{
    [MemoryPackIgnore]
    private Mod_Stamina stamina;

    public State state = new State();
    [MemoryPackIgnore]
    private Mod_Food food;



    [MemoryPackable]
    [System.Serializable]
    public partial class State
    {
        public float StaminaRecoverSpeed = 1f;
        public float StaminaConsumeSpeed = 0.5f;
    }

    public override void OnInit(Module mod)
    {
        if (mod is Mod_Food food)
        {
            food.item.itemMods.GetMod_ByID(ModText.Stamina, out Mod_Stamina currentStamina);
            this.food = food;
            stamina = currentStamina;
        }
    }
    public override void OnUpdate(float timeDelta)
    {
        if (stamina == null)
        {
            return;
        }

        if (stamina.Data.CurrentStamina < stamina.Data.MaxStamina)
        {
            food.ConsumeNutrition(timeDelta * state.StaminaConsumeSpeed);
            stamina.AddStamina(state.StaminaRecoverSpeed * timeDelta);
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