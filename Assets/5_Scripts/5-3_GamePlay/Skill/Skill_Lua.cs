using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

public class Skill_Lua : Skill
{
    // Lua环境
    private LuaEnv luaEnv;
    // Lua脚本代码（直接在Inspector中输入）
    [TextArea(10, 20)] // 增加输入区域大小
    public string luaScriptCode;

    public override void Load()
    {
        // 输出runtimeSkill的全部数据
        if (runtimeSkill != null)
        {
            // 获取所有公共字段
            System.Reflection.FieldInfo[] fields = runtimeSkill.GetType().GetFields();
            foreach (var field in fields)
            {
                try
                {
                    object value = field.GetValue(runtimeSkill);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"获取字段 {field.Name} 的值时出错: {e.Message}");
                }
            }

            // 获取所有公共属性
            System.Reflection.PropertyInfo[] properties = runtimeSkill.GetType().GetProperties();
            foreach (var prop in properties)
            {
                try
                {
                    if (prop.CanRead)
                    {
                        object value = prop.GetValue(runtimeSkill);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"获取属性 {prop.Name} 的值时出错: {e.Message}");
                }
            }
        }
    }

    public override void SkillUpdate(float deltaTime)
    {
    }

    public override void Save()
    {
    }
}