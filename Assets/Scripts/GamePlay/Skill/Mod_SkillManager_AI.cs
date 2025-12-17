
public class Mod_SkillManager_AI : Mod_SkillManager
{
    public Mod_AnimatorController_Receiver animatorReceiver;

    public override void Load()
    {
        base.Load();
        animatorReceiver = item.itemMods.GetMod_ByID<Mod_AnimatorController_Receiver> (ModText.AnimatorReceiver);
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart += UseSkill;
            animatorReceiver.OnSkillStop += StopSkill;
        }
    }
    
    public override void Save()
    {
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
        // 清除事件挂接
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart -= UseSkill;
            animatorReceiver.OnSkillStop -= StopSkill;
        }
    }
}