# if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

[CustomEditor(typeof(SeasonConfig))]
public class SeasonConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); // 绘制默认属性
        if (GUILayout.Button("自动生成参数"))
        {
            AutoGenerateParameters();
        }
    }

    private void AutoGenerateParameters()
    {
        var targetConfig = target as SeasonConfig;
        Random.InitState((int)targetConfig.season); // 确保同一季节的随机性一致
        targetConfig.days.Clear();

        // 获取当前 SeasonConfig 的路径和目录
        string configPath = AssetDatabase.GetAssetPath(targetConfig);
        string configFolder = Path.GetDirectoryName(configPath);

        // 获取用户自定义的 seasonName（用于生成 DayConfig 的名称）
        string seasonName = targetConfig.seasonName;

        // 生成 DayConfig 的基础参数
        float gameDayDuration = targetConfig.gameDayDuration;
        int count = 0;
        float sunriseStartBase = 0;
        float sunriseEndBase = 0;
        float sunsetStartBase = 0;
        float sunsetEndBase = 0;

        // 根据季节类型设置基础参数
        switch (targetConfig.season)
        {
            case SeasonType.Spring:
                count = 5;
                sunriseStartBase = 0.2f;
                sunriseEndBase = 0.3f;
                sunsetStartBase = 0.7f;
                sunsetEndBase = 0.8f;
                break;
            case SeasonType.Summer:
                count = 10;
                sunriseStartBase = 0.15f;
                sunriseEndBase = 0.25f;
                sunsetStartBase = 0.75f;
                sunsetEndBase = 0.85f;
                break;
            case SeasonType.Autumn:
                count = 5;
                sunriseStartBase = 0.25f;
                sunriseEndBase = 0.35f;
                sunsetStartBase = 0.65f;
                sunsetEndBase = 0.75f;
                break;
            case SeasonType.Winter:
                count = 10;
                sunriseStartBase = 0.3f;
                sunriseEndBase = 0.4f;
                sunsetStartBase = 0.6f;
                sunsetEndBase = 0.7f;
                break;
        }

        for (int i = 0; i < count; i++)
        {
            // 创建 DayConfig 对象
            var dayConfig = ScriptableObject.CreateInstance<DayConfig>();
            dayConfig.gameDayDuration = gameDayDuration;

            // 随机偏移（±5%）
            float randomFactor = Random.Range(-0.05f, 0.05f);
            dayConfig.sunriseStartTime = sunriseStartBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunriseEndTime = sunriseEndBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunsetStartTime = sunsetStartBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunsetEndTime = sunsetEndBase * gameDayDuration * (1 + randomFactor);

            // 确保时间顺序正确
            dayConfig.sunriseEndTime = Mathf.Max(dayConfig.sunriseStartTime + 0.05f * gameDayDuration, dayConfig.sunriseEndTime);
            dayConfig.sunsetStartTime = Mathf.Max(dayConfig.sunriseEndTime + 0.05f * gameDayDuration, dayConfig.sunsetStartTime);
            dayConfig.sunsetEndTime = Mathf.Min(dayConfig.sunsetStartTime + 0.1f * gameDayDuration, gameDayDuration);

            // 设置 dayName（使用 seasonName 字段）
            dayConfig.dayName = $"{seasonName}_{i}";

            // 保存到 SeasonConfig 同一目录
            string dayAssetPath = Path.Combine(configFolder, $"{dayConfig.dayName}.asset");
            AssetDatabase.CreateAsset(dayConfig, dayAssetPath);

            // 添加到 SeasonConfig 的 days 列表
            targetConfig.days.Add(dayConfig);
        }

        // 更新并保存配置
        EditorUtility.SetDirty(targetConfig);
        AssetDatabase.SaveAssets();
    }
}
# endif