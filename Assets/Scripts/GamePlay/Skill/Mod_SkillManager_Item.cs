using AYellowpaper.SerializedCollections;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class Mod_SkillManager_Item : Mod_SkillManager
{
    public override Item item => base.item.Owner;
}