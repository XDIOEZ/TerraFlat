using MemoryPack;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RuntimeSkill
{
    // 运行时参数：数据大部分来自技能模板，并在技能运行期间动态变化。
    [Tooltip("技能数据")]
    public BaseSkill skillData;
    [Tooltip("技能持续时间")]
    public float duration = 1;
    [Tooltip("技能进度")]
    public float progress = 0;
    [Tooltip("技能发送者")]
    public Item skillSender;
    [Tooltip("技能接收者")]
    public Item skillReceiver;
    [Tooltip("技能起点")]
    public Vector2 startPoint;
    [Tooltip("技能终点")]
    public Vector2 targetPoint;
    [Tooltip("技能管理模块")]
    public Mod_SkillManager skillManager;
    [Tooltip("技能实例字典")]
    public Dictionary<string, Skill> skillInstanceDict = new Dictionary<string, Skill>();

    public void Start()
    {
        foreach (var action in skillData.Actions)
        {
            if (action == null)
            {
                Debug.LogWarning("RuntimeSkill: 技能行为为空,已跳过");
                continue;
            }

            Transform castingPoint = skillManager.GetCastingPoint(action.GetCastingPointIndex());
            if (castingPoint == null)
            {
                Debug.LogWarning($"RuntimeSkill: 施法点为空,已跳过技能行为 '{action.name}'");
                continue;
            }

            GameObject staging = new GameObject("Skill Physics Preparation") { hideFlags = HideFlags.DontSave };
            staging.SetActive(false);
            Skill actionInstance = null;
            bool added = false;
            try
            {
                Scene scene = skillSender != null ? skillSender.gameObject.scene : castingPoint.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(staging, scene);
                // 停用外壳先进入施放者场景，避免物理体在错误世界中生效。
                actionInstance = GameObject.Instantiate(action, castingPoint.position, Quaternion.identity, staging.transform);
                actionInstance.gameObject.SetActive(false);
                actionInstance.transform.SetParent(null, true);
                actionInstance.runtimeSkill = this;
                skillInstanceDict.Add(action.name, actionInstance);
                added = true;
                string worldKey = GameplayWorldRegistry.GetItemWorldKey(skillSender);
                using (GameplayWorldRegistry.TryGet(worldKey, out _) ? GameplayWorldRegistry.UseWorld(worldKey) : default)
                    actionInstance.Load();
                actionInstance.gameObject.SetActive(action.gameObject.activeSelf);
            }
            catch (System.Exception failure)
            {
                if (added) skillInstanceDict.Remove(action.name);
                if (actionInstance != null)
                {
                    try { actionInstance.Unload(); }
                    catch (System.Exception cleanup)
                    {
                        GameObject.Destroy(actionInstance.gameObject);
                        throw new System.AggregateException("技能准备失败且物理清理失败。", failure, cleanup);
                    }
                    GameObject.Destroy(actionInstance.gameObject);
                }
                throw;
            }
            finally { GameObject.Destroy(staging); }
        }
    }

    public void Stay(float deltaTime)
    {
        progress += deltaTime;
        
            foreach (var skill in skillInstanceDict.Values)
            {
                if (skill != null)
                {
                    skill.SkillUpdate(deltaTime);
                }
            }
    }

    public void Stop()
    {
            foreach (var skill in skillInstanceDict.Values)
            {
                if (skill != null)
                {
                    skill.Save();
                    skill.Unload();
                    GameObject.Destroy(skill.gameObject);
                }
            }
        // 清空技能实例字典。
        skillInstanceDict.Clear();
    }

    public bool IsFinished()
    {
        return progress >= duration;
    }
}
