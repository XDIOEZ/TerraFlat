public class Mod_SkillManager_Item : Mod_SkillManager
{

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.SkillManager_Item;
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        base.ModUpdate(deltaTime);
    }
    public override void Load()
    {
        base.Load();
    }
    public override void Save()
    {
        base.Save();
    }

    public override void Act()
    {
        base.Act();
    }

    #endregion

}
