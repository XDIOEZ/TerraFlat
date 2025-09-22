using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ModAction :ScriptableObject
{
    public string Name;
    [TextArea(3, 10)] // 参数表示最小行数和最大行数
    public string Description;
    public abstract void Action(Item ModOwner ,Module module, Item targetItem=null);
}
