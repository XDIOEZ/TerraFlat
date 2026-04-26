using UnityEngine;

[System.Serializable]
public class FoodStatTableRow
{
    public GameObject Prefab; // 预制体引用
    public string PrefabPath; // 预制体路径
    public string PrefabName; // 预制体名称
    
    // 营养值
    public float Carbohydrates; // 碳水
    public float Fat; // 脂肪
    public float Protein; // 蛋白质
    public float Water; // 水
    public float Vitamins; // 维生素
    
    // 营养最大值
    public float Max_Carbohydrates;
    public float Max_Fat;
    public float Max_Protein;
    public float Max_Water;
    public float Max_Vitamins;
    
    // 食物特性
    public float Max_EatingProgress; // 最大进度（咀嚼次数）
    public float nutritionConsumeSpeed; // 营养消耗速度
    public float WaterConsumeSpeedRate; // 水份消耗倍率
    public float nutritionConsumeRate; // 营养消耗倍率
    
    // 腐败参数
    public bool EnableSpoilage; // 是否启用腐败
    public float SpoilageIntervalSeconds; // 腐败触发间隔（秒）
    public string SpoilageTargetItemID; // 腐败后替换目标物品ID
    
    // 状态标记
    public bool HasNutrition; // 是否存在营养数据
    public bool HasSpoilage; // 是否存在腐败配置
}
