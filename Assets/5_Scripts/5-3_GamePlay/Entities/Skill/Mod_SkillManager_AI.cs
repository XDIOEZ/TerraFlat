
public class Mod_SkillManager_AI : Mod_SkillManager
{
    public Mod_AnimatorController_Receiver animatorReceiver;

    protected override void OnLoad()
    {
        base.OnLoad();
        animatorReceiver = item.itemMods.GetMod_ByID<Mod_AnimatorController_Receiver> (ModText.AnimatorReceiver);
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart += UseSkill;
            animatorReceiver.OnSkillStop += StopSkill;
        }
    }
    
    protected override void OnSave()
    {
        base.OnSave();
    }

    protected override void OnUnload()
    {
        if (animatorReceiver != null)
        {
            animatorReceiver.OnSkillStart -= UseSkill;
            animatorReceiver.OnSkillStop -= StopSkill;
            animatorReceiver = null;
        }

        base.OnUnload();
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
        Unload();
    }
}
