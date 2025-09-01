using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

// 🍎 红苹果，作为食物的 Item 实现
public class Apple_Red : Item
{
    // 数据引用
    public Data_Creature data;
    public Mod_Food mod_food;

    // 当前吃掉的数值进度
    public float EatingProgress = 0f;
    //长大后
    public string TreeName; 
    // 实现 Item 抽象属性
    public override ItemData itemData
    {
        get => data;
        set => data = (Data_Creature)value;
    }

    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic { get => data.BuffRunTimeData_Dic; set => data.BuffRunTimeData_Dic = value; }

    public new void Start()
    {
        base.Start();
       // mod_food = Mods[ModText.Food] as Mod_Food;
        GetComponentInChildren<Mod_PlantGrow>().OnAction += BeToAppleTree;
        GetComponentInChildren<TileEffectReceiver>().OnTileEnterEvent += OnTileEnter;
    }
/*    /// <summary>
    /// 调用吃的行为
    /// </summary>
    public override void Act()
    {
        var Food = BelongItem.Mods[ModText.Food] as Mod_Food;
        if (Food == null) return;
        mod_food.BeEat(Eater:Food);
    }*/

    void OnTileEnter(TileData data)
    {
     //   Debug.Log("OnTileEnter");

        if (data == null)
        {
            Debug.LogError("TileData 为空，无法处理！");
            Mods["生长模块"]._Data.isRunning = false;
            return;
        }

        if (data is not TileData_Grass tileData)
        {
            Debug.LogWarning($"TileData 类型错误，当前类型是 {data.GetType().Name}，期望类型是 TileData_Grass");
            Mods["生长模块"]._Data.isRunning = false;
            return;
        }

        if (tileData.FertileValue.Value > 0)
        {
       //     Debug.Log("当前格子适合生长，启动生长模块");
            Mods["生长模块"]._Data.isRunning = true;
        }
        else
        {
            Debug.LogWarning($"当前格子的肥沃度值为 {tileData.FertileValue.Value}，不适合生长");
            Mods["生长模块"]._Data.isRunning = false;
        }

    }


    /// <summary>
    /// 被吃掉逻辑，返回营养值（如果吃完）
    /// </summary>


    // 实现苹果变为苹果树
    // 实现苹果变为苹果树
    public void BeToAppleTree(float Index)
    {
       // Debug.Log(Index);
        if (Index == 1)
        {
            if (CheckNearbyObjects())
            {
            //    Debug.Log("周围物体过多，无法生成苹果树。");
                return; // 阻止生成苹果树
            }

            GameItemManager.Instance.InstantiateItem(TreeName, transform.position, transform.rotation, scale: Vector3.one * 0.25f);
            Destroy(gameObject);
        }
    }

    // 检测周围物体数量是否超过限制
    private bool CheckNearbyObjects()
    {
        float checkRadius = 2f; // 检测半径
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, checkRadius);

        int count = 0;

        foreach (var collider in colliders)
        {
            if (collider.gameObject != this.gameObject)
            {
                count++;
                if (count > 3)
                {
                    return true; // 超过3个，返回阻止
                }
            }
        }

        return false; // 没超过，允许生成
    }

    // 可选：调试用，画检测范围
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 2f);
    }
}
