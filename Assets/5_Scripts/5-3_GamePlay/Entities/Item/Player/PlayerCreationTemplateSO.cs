using System;
using UnityEngine;

/// <summary>新玩家实例化前应用的移动、营养、体力和基础属性模板；已有存档不会重新覆盖。</summary>
[CreateAssetMenu(fileName = "PlayerCreationTemplate_Default", menuName = "FlatWorld/Player/Creation Template")]
public sealed class PlayerCreationTemplateSO : ScriptableObject
{
    #region 配置模型

    [Serializable]
    public sealed class CoreSettings
    {
        [Min(0f)] public float dataSpeed = 8f;
        [Min(0f)] public float playerPov = 10f;
        [Min(0f)] public float perceptionRadiusMultiplier = 1f;
        [Min(0f)] public float initialStamina = 100f;
        [Min(0f)] public float maxStamina = 100f;
        [Min(0f)] public float staminaRecoverySpeed = 10f;
    }

    [Serializable]
    public sealed class MovementSettings
    {
        [Min(0f)] public float speed = 5f;
        [Min(0f)] public float slowDownSpeed = 5f;
        [Min(0f)] public float endSpeed = 0.1f;
        [Min(0f)] public float moveStaminaConsume = 0f;
        [Min(0f)] public float runStaminaConsume = 2f;
        [Min(0.01f)] public float runSpeedRate = 1.5f;
        [Min(0f)] public float runStaminaThreshold = 2f;
        [Min(0.01f)] public float speedTransitionDuration = 0.24f;
        [Min(0.01f)] public float stopTransitionDuration = 0.07f;
        public bool hungerActionEnabled = true;
        [Min(0f)] public float moveNutritionConsumeMultiplier = 1.6f;
        [Min(0f)] public float runNutritionConsumeMultiplier = 2f;
        [Min(0f)] public float runWaterConsumeMultiplier = 0.25f;
    }

    [Serializable]
    public sealed class FoodSettings
    {
        public Nutrition nutrition = new Nutrition
        {
            Carbohydrates = 50f,
            Max_Carbohydrates = 100f,
            Fat = 50f,
            Max_Fat = 100f,
            Protein = 50f,
            Max_Protein = 100f,
            Water = 150f,
            Max_Water = 150f,
            Vitamins = 50f,
            Max_Vitamins = 100f
        };
        [Min(0f)] public float nutritionConsumeSpeed = 0.05f;
        [Min(0f)] public float waterConsumeSpeedRate = 0.1f;
        [Min(0f)] public float nutritionConsumeRate = 1f;
        [Min(0f)] public float staminaRecoverSpeed = 1f;
        [Min(0f)] public float staminaConsumeSpeed = 0.5f;
        public bool healthEnabled = true;
        [Min(0f)] public float healSpeed = 0.01f;
        [Min(0f)] public float waterSelfHurt = 1f;
        [Min(0f)] public float proteinSelfHurt = 1f;
        [Min(0f)] public float vitaminSelfHurt = 1f;
        [Range(0f, 1f)] public float healNeedRatio = 0.6f;
        [Min(0f)] public float proteinHealThreshold = 60f;
    }

    [Serializable]
    public sealed class StaminaSettings
    {
        [Min(0f)] public float currentStamina = 100f;
        [Min(0f)] public float maxStamina = 100f;
    }

    #endregion

    #region 配置

    [Header("基础玩家参数")]
    public CoreSettings core = new();

    [Header("移动与跑步参数")]
    public MovementSettings movement = new();

    [Header("饥饿与口渴参数")]
    public FoodSettings food = new();

    [Header("体力模块参数")]
    public StaminaSettings stamina = new();

    #endregion

    #region 公共方法

    public void ApplyTo(Player player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));
        if (player.Data == null)
            throw new InvalidOperationException("玩家创建模板无法应用：Player.Data 为空。");
        if (core == null || movement == null || food == null || stamina == null)
            throw new InvalidOperationException("玩家创建模板配置不完整。");

        ApplyCore(player.Data);
        ApplyMovement(player.GetComponentInChildren<Mover>(true));
        ApplyFood(player.GetComponentInChildren<Mod_Food>(true));
        ApplyStamina(player.GetComponentInChildren<Mod_Stamina>(true));

        ItemData templateData = player.Get_NewItemData();
        player.Data.ModuleDataDic = templateData.ModuleDataDic;
    }

    #endregion

    #region 私有方法

    private void ApplyCore(Data_Player data)
    {
        data.Speed = new GameValue_float(core.dataSpeed);
        data.PlayerPov = core.playerPov;
        data.PerceptionRadiusMultiplier = core.perceptionRadiusMultiplier;
        data.stamina = core.initialStamina;
        data.staminaMax = core.maxStamina;
        data.staminaRecoverySpeed = core.staminaRecoverySpeed;
    }

    private void ApplyMovement(Mover mover)
    {
        if (mover == null)
            return;

        mover.Data = new Mover.Mover_SaveData
        {
            Speed = new GameValue_float(movement.speed),
            slowDownSpeed = movement.slowDownSpeed,
            endSpeed = movement.endSpeed,
            moveStaminaConsume = movement.moveStaminaConsume,
            runStaminaConsume = movement.runStaminaConsume,
            runSpeedRate = movement.runSpeedRate,
            isRunning = false,
            RunStaminaThreshold = movement.runStaminaThreshold
        };
        mover.speedTransitionDuration = movement.speedTransitionDuration;
        mover.stopTransitionDuration = movement.stopTransitionDuration;
        mover.hungerAction = new MovementHungerActionDefinition
        {
            enabled = movement.hungerActionEnabled,
            moveNutritionConsumeMultiplier = movement.moveNutritionConsumeMultiplier,
            runNutritionConsumeMultiplier = movement.runNutritionConsumeMultiplier,
            runWaterConsumeMultiplier = movement.runWaterConsumeMultiplier
        };
        mover.ModDataMemoryPack ??= new Ex_ModData_MemoryPackable();
        mover.ModDataMemoryPack.WriteData(mover.Data);
    }

    private void ApplyFood(Mod_Food foodModule)
    {
        if (foodModule == null)
            return;

        Food foodData = foodModule.Data;
        foodData.nutrition = CloneNutrition(food.nutrition);
        foodData.nutritionConsumeSpeed = new GameValue_float(food.nutritionConsumeSpeed);
        foodData.WaterConsumeSpeedRate = food.waterConsumeSpeedRate;
        foodData.nutritionConsumeRate = food.nutritionConsumeRate;
        foodModule.Data = foodData;
        foodModule.StaminaState = new Mod_Food.FoodStaminaState
        {
            StaminaRecoverSpeed = food.staminaRecoverSpeed,
            StaminaConsumeSpeed = food.staminaConsumeSpeed
        };
        foodModule.HealthState = new Mod_Food.FoodHealthState
        {
            Enabled = food.healthEnabled,
            HealSpeed = food.healSpeed,
            WaterSelfHurt = food.waterSelfHurt,
            ProteinSelfHurt = food.proteinSelfHurt,
            VitaminSelfHurt = food.vitaminSelfHurt,
            HealNeedRatio = food.healNeedRatio,
            PlayerProteinHealThreshold = food.proteinHealThreshold
        };
    }

    private void ApplyStamina(Mod_Stamina staminaModule)
    {
        if (staminaModule == null)
            return;

        staminaModule.Data = new Mod_Stamina.StaminaData
        {
            CurrentStamina = Mathf.Clamp(stamina.currentStamina, 0f, Mathf.Max(0f, stamina.maxStamina)),
            MaxStamina = Mathf.Max(0f, stamina.maxStamina)
        };
        staminaModule.modData ??= new Ex_ModData_MemoryPackable();
        staminaModule.modData.WriteData(staminaModule.Data);
    }

    private static Nutrition CloneNutrition(Nutrition source)
    {
        if (source == null)
            throw new InvalidOperationException("玩家创建模板的营养配置为空。");

        return new Nutrition
        {
            Carbohydrates = source.Carbohydrates,
            Max_Carbohydrates = source.Max_Carbohydrates,
            Fat = source.Fat,
            Max_Fat = source.Max_Fat,
            Protein = source.Protein,
            Max_Protein = source.Max_Protein,
            Water = source.Water,
            Max_Water = source.Max_Water,
            Vitamins = source.Vitamins,
            Max_Vitamins = source.Max_Vitamins
        };
    }

    #endregion
}
