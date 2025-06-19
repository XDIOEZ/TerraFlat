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
        DrawDefaultInspector(); // ����Ĭ������
        if (GUILayout.Button("�Զ����ɲ���"))
        {
            AutoGenerateParameters();
        }
    }

    private void AutoGenerateParameters()
    {
        var targetConfig = target as SeasonConfig;
        Random.InitState((int)targetConfig.season); // ȷ��ͬһ���ڵ������һ��
        targetConfig.days.Clear();

        // ��ȡ��ǰ SeasonConfig ��·����Ŀ¼
        string configPath = AssetDatabase.GetAssetPath(targetConfig);
        string configFolder = Path.GetDirectoryName(configPath);

        // ��ȡ�û��Զ���� seasonName���������� DayConfig �����ƣ�
        string seasonName = targetConfig.seasonName;

        // ���� DayConfig �Ļ�������
        float gameDayDuration = targetConfig.gameDayDuration;
        int count = 0;
        float sunriseStartBase = 0;
        float sunriseEndBase = 0;
        float sunsetStartBase = 0;
        float sunsetEndBase = 0;

        // ���ݼ����������û�������
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
            // ���� DayConfig ����
            var dayConfig = ScriptableObject.CreateInstance<DayConfig>();
            dayConfig.gameDayDuration = gameDayDuration;

            // ���ƫ�ƣ���5%��
            float randomFactor = Random.Range(-0.05f, 0.05f);
            dayConfig.sunriseStartTime = sunriseStartBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunriseEndTime = sunriseEndBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunsetStartTime = sunsetStartBase * gameDayDuration * (1 + randomFactor);
            dayConfig.sunsetEndTime = sunsetEndBase * gameDayDuration * (1 + randomFactor);

            // ȷ��ʱ��˳����ȷ
            dayConfig.sunriseEndTime = Mathf.Max(dayConfig.sunriseStartTime + 0.05f * gameDayDuration, dayConfig.sunriseEndTime);
            dayConfig.sunsetStartTime = Mathf.Max(dayConfig.sunriseEndTime + 0.05f * gameDayDuration, dayConfig.sunsetStartTime);
            dayConfig.sunsetEndTime = Mathf.Min(dayConfig.sunsetStartTime + 0.1f * gameDayDuration, gameDayDuration);

            // ���� dayName��ʹ�� seasonName �ֶΣ�
            dayConfig.dayName = $"{seasonName}_{i}";

            // ���浽 SeasonConfig ͬһĿ¼
            string dayAssetPath = Path.Combine(configFolder, $"{dayConfig.dayName}.asset");
            AssetDatabase.CreateAsset(dayConfig, dayAssetPath);

            // ��ӵ� SeasonConfig �� days �б�
            targetConfig.days.Add(dayConfig);
        }

        // ���²���������
        EditorUtility.SetDirty(targetConfig);
        AssetDatabase.SaveAssets();
    }
}
# endif