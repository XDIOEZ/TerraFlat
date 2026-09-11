using UnityEngine;

/// <summary>
/// 通用物品投射模块：负责把带 Mod_Damage 的物品以刚体方式发射、按蓄力缩放速度与伤害，
/// 首次有效命中或飞行超时后停止，并按可配置概率保留为可拾取世界物品。
/// </summary>
public sealed class Mod_Projectile : Module, IItemModuleDependencyBinder
{
    public const string PersistedModuleId = "Mod_Projectile";

    #region 配置

    [Min(0f), Tooltip("最低蓄力时的飞行速度。")]
    public float MinSpeed = 5f;

    [Min(0f), Tooltip("满蓄力时的飞行速度。")]
    public float MaxSpeed = 12f;

    [Range(0f, 1f), Tooltip("最低蓄力时的伤害倍率。")]
    public float MinDamageMultiplier = 0.35f;

    [Min(0f), Tooltip("满蓄力时的伤害倍率。")]
    public float MaxDamageMultiplier = 1f;

    [Min(0.05f), Tooltip("没有命中目标时最多飞行多少秒。")]
    public float MaxFlightSeconds = 2.5f;

    [Range(0f, 1f), Tooltip("投射结束后保留为可拾取物品的概率；箭矢默认 50%。")]
    public float RecoveryChance = 0.5f;

    [Range(0f, 1f), Tooltip("投射物损坏后，从其普通合成配方中掉落一份原材料的概率；箭矢默认 30%。")]
    public float BrokenSalvageChance = 0.3f;

    [Tooltip("素材自身的朝向角度；当前箭矢素材从左下指向右上，因此为 45 度。")]
    public float SpriteForwardAngleDegrees = 45f;

    public Ex_ModData_MemoryPackable Data = new Ex_ModData_MemoryPackable();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => PersistedModuleId;

    #endregion

    #region 运行时状态

    private Mod_Damage _damage;
    private Rigidbody2D _body;
    private CombatDamage _baseDamage;
    private float _flightRemain;
    private bool _isFlying;
    private bool _endingFlight;

    #endregion

    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    /// <summary>确保模块数据拥有稳定 ID。</summary>
    public override void Awake()
    {
        Data ??= new Ex_ModData_MemoryPackable();
        Data.ID = PersistedModuleId;
        base.Awake();
    }

    /// <summary>在 Item 完成模块注册后显式解析伤害模块依赖。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        _damage = modules.RequireSingleModById<Mod_Damage>("Mod_Damage");
    }

    /// <summary>初始化待发射状态，并关闭伤害窗口。</summary>
    public override void Load()
    {
        if (_damage == null)
            throw new MissingComponentException($"{name} 缺少 Mod_Damage 依赖。");

        _damage.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        _damage.OnReceiverDamageResolved += HandleReceiverDamageResolved;
        _baseDamage = _damage.ResolveDamageValues().Scaled(1f);
        _damage.StopAttack();
        ConfigureBodyForRest();
        _isFlying = false;
        _endingFlight = false;
        _flightRemain = 0f;
    }

    /// <summary>投射物没有额外持久化运行态。</summary>
    public override void Save()
    {
    }

    /// <summary>飞行期间维护空间索引，并在超时后按落地规则处理回收。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!_isFlying)
            return;

        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        _flightRemain -= Mathf.Max(0f, deltaTime);
        if (_flightRemain <= 0f)
            FinishFlight();
    }

    /// <summary>解除伤害事件，防止对象池复用后重复订阅。</summary>
    public override void Unload()
    {
        if (_damage != null)
            _damage.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        _isFlying = false;
        _endingFlight = false;
    }

    #region 发射与停止

    /// <summary>按给定射手、方向、蓄力比例和来源武器倍率开始一次飞行。</summary>
    public void Launch(Item shooter, Vector2 direction, float charge01, float sourceDamageMultiplier = 1f)
    {
        if (item == null || _damage == null || direction.sqrMagnitude < 0.0001f)
            throw new System.InvalidOperationException($"{name} 无法发射：投射物尚未正确初始化或方向无效。");

        float normalizedCharge = Mathf.Clamp01(charge01);
        float speed = Mathf.Lerp(Mathf.Max(0f, MinSpeed), Mathf.Max(MinSpeed, MaxSpeed), normalizedCharge);
        float damageMultiplier = Mathf.Lerp(
            Mathf.Max(0f, MinDamageMultiplier),
            Mathf.Max(0f, MaxDamageMultiplier),
            normalizedCharge);
        damageMultiplier *= Mathf.Max(0f, sourceDamageMultiplier);
        Vector2 normalizedDirection = direction.normalized;

        item.Owner = shooter;
        item.SetInHand(false);
        item.itemData.Stack.Amount = 1f;
        item.itemData.Stack.CanBePickedUp = false;

        EnsureBody();
        _body.bodyType = RigidbodyType2D.Dynamic;
        _body.gravityScale = 0f;
        _body.constraints = RigidbodyConstraints2D.FreezeRotation;
        _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _body.velocity = normalizedDirection * speed;

        float angle = Mathf.Atan2(normalizedDirection.y, normalizedDirection.x) * Mathf.Rad2Deg;
        item.transform.rotation = Quaternion.Euler(0f, 0f, angle - SpriteForwardAngleDegrees);

        _damage.SetDamageValues(_baseDamage.Scaled(damageMultiplier));
        _damage.MaxAttackTargets = 1;
        _damage.StartAttack();

        _flightRemain = Mathf.Max(0.05f, MaxFlightSeconds);
        _endingFlight = false;
        _isFlying = true;
    }

    /// <summary>首次有效实体命中后立即结束飞行；无效结算不会吞掉箭矢。</summary>
    private void HandleReceiverDamageResolved(DamageReceiver receiver, float resolvedDamage)
    {
        if (_isFlying && resolvedDamage >= 0f)
            FinishFlight();
    }

    /// <summary>停止飞行，并按 RecoveryChance 决定留下可拾取物还是销毁。</summary>
    private void FinishFlight()
    {
        if (!_isFlying || _endingFlight)
            return;

        _endingFlight = true;
        _isFlying = false;
        _damage.StopAttack();
        _damage.SetDamageValues(_baseDamage.Scaled(1f));

        if (_body != null)
        {
            _body.velocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.bodyType = RigidbodyType2D.Kinematic;
        }

        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        bool recover = Random.value < Mathf.Clamp01(RecoveryChance);
        if (!recover)
        {
            TryDropBrokenSalvage();
            ItemMgr.Instance?.DespawnItem(item, saveData: false);
            return;
        }

        item.Owner = null;
        item.itemData.Stack.Amount = 1f;
        item.itemData.Stack.CanBePickedUp = true;
        _endingFlight = false;
    }

    /// <summary>投射物损坏时按配置概率掉落一份真实合成配方中的原材料。</summary>
    private void TryDropBrokenSalvage()
    {
        if (Random.value >= Mathf.Clamp01(BrokenSalvageChance) ||
            !TryResolveSalvageIngredient(out string ingredientItemId))
        {
            return;
        }

        GameRes gameRes = GameRes.ExistingInstance;
        ItemMgr itemManager = ItemMgr.Instance;
        if (gameRes == null || itemManager == null)
            return;

        ItemData salvageData = gameRes.CreateItemData(ingredientItemId);
        salvageData.Stack.Amount = 1f;
        salvageData.Stack.CanBePickedUp = true;
        itemManager.InstantiateItem(salvageData, item.transform.position);
    }

    /// <summary>从产出当前投射物的普通合成配方中，按材料用量随机选择一种可确定身份的原材料。</summary>
    private bool TryResolveSalvageIngredient(out string ingredientItemId)
    {
        ingredientItemId = null;
        GameRes gameRes = GameRes.ExistingInstance;
        string projectileItemId = item?.itemData?.IDName;
        if (gameRes == null || string.IsNullOrWhiteSpace(projectileItemId))
            return false;

        var recipes = gameRes.GetRecipes(RecipeType.Crafting);
        for (int recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
        {
            RuntimeRecipe recipe = recipes[recipeIndex];
            if (!RecipeProducesItem(recipe, projectileItemId))
                continue;

            var ingredients = recipe.inputs?.RowItems_List;
            if (ingredients == null)
                return false;

            int totalWeight = 0;
            for (int ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
            {
                RuntimeRecipeIngredient ingredient = ingredients[ingredientIndex];
                if (ingredient != null && ingredient.matchMode == MatchMode.ExactItem &&
                    ingredient.amount > 0 && !string.IsNullOrWhiteSpace(ingredient.ItemName))
                {
                    totalWeight += ingredient.amount;
                }
            }

            if (totalWeight <= 0)
                return false;

            int roll = Random.Range(0, totalWeight);
            for (int ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
            {
                RuntimeRecipeIngredient ingredient = ingredients[ingredientIndex];
                if (ingredient == null || ingredient.matchMode != MatchMode.ExactItem ||
                    ingredient.amount <= 0 || string.IsNullOrWhiteSpace(ingredient.ItemName))
                {
                    continue;
                }

                if (roll < ingredient.amount)
                {
                    ingredientItemId = ingredient.ItemName;
                    return true;
                }

                roll -= ingredient.amount;
            }

            return false;
        }

        return false;
    }

    /// <summary>判断配方是否会产出指定物品。</summary>
    private static bool RecipeProducesItem(RuntimeRecipe recipe, string itemId)
    {
        var results = recipe?.outputs?.results;
        if (results == null)
            return false;

        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            RuntimeRecipeResult result = results[resultIndex];
            if (result != null && result.amount > 0 &&
                string.Equals(result.ItemName, itemId, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>为投射物根节点创建或复用刚体。</summary>
    private void EnsureBody()
    {
        if (_body != null)
            return;

        _body = item.GetComponent<Rigidbody2D>();
        if (_body == null)
            _body = item.gameObject.AddComponent<Rigidbody2D>();
    }

    /// <summary>普通世界箭矢保持静止但继续参与拾取触发器。</summary>
    private void ConfigureBodyForRest()
    {
        EnsureBody();
        _body.bodyType = RigidbodyType2D.Kinematic;
        _body.gravityScale = 0f;
        _body.velocity = Vector2.zero;
        _body.angularVelocity = 0f;
        _body.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    #endregion
}
