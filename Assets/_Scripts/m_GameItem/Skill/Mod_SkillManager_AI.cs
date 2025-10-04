using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_SkillManager_AI : Mod_SkillManager
{
    public Mod_AnimatorReceiver animatorReceiver;

    public override void Load()
    {
        base.Load();
        animatorReceiver = item.itemMods.GetMod_ByID<Mod_AnimatorReceiver> (ModText.AnimatorReceiver);
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart += UseSkill;
            animatorReceiver.OnSkillStop += StopSkill;
        }
    }
    
    public override void Save()
    {
        // 清除事件挂接
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart -= UseSkill;
            animatorReceiver.OnSkillStop -= StopSkill;
        }
        
        base.Save();
    }

    public void UseSkill(int skillIndex)
    {
        CurrentSelectSkilIndex = skillIndex;
        Act();
    }
    
    public void StopSkill(int skillIndex)
    {
        StopSkillByIndex(skillIndex);
    }
    
    // 确保在对象销毁时清除事件挂接
    private void OnDestroy()
    {
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart -= UseSkill;
            animatorReceiver.OnSkillStop -= StopSkill;
        }
    }
}