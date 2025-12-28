using UnityEngine;

/// <summary>
/// 修改食物模块中饥饿消耗速度（nutritionConsumeSpeed）的 Buff 行为。
/// 通过乘算修正来放大或缩小饥饿消耗系数。
/// </summary>
[CreateAssetMenu(fileName = "新建Buff行为_修改饥饿消耗速度", menuName = "Buff/FoodConsumeSpeedChange")]
public class BuffAction_FoodConsumeSpeedChange : BuffAction
{
    [Header("饥饿消耗乘算倍率（>1加快，<1减慢）")]
    public float ConsumeSpeedMultiplier = 1f;

    private Mod_Food mod;

    public override void Apply(BuffRunTime data)
    {
        if (mod == null)
        {
            if (data == null || data.buff_Receiver == null)
            {
                Debug.LogWarning("BuffAction_FoodConsumeSpeedChange: buff_Receiver 为空，取消 Apply");
                return;
            }

            data.buff_Receiver.itemMods.GetMod_ByID(ModText.Food, out mod);
            if (mod == null)
            {
                // 接收者没有食物模块时，直接忽略该 Buff 行为
                Debug.Log("BuffAction_FoodConsumeSpeedChange: 接收者没有 Food 模块，取消 Apply");
                return;
            }
        }

        if (mod.Data == null)
        {
            Debug.LogWarning("BuffAction_FoodConsumeSpeedChange: Food.Data 为空");
            return;
        }

        // 使用乘算修正来调整饥饿消耗速度，方便与其他 Buff 叠加
        mod.Data.nutritionConsumeSpeed.MultiplicativeModifier *= ConsumeSpeedMultiplier;
    }

    public override BuffAction Clone()
    {
        var newBuff = Instantiate(this);
        newBuff.mod = null; // 防止在克隆后沿用旧的 Food 引用
        return newBuff;
    }
}
