using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ModAction :ScriptableObject
{
    public string Name;
    [TextArea(3, 10)] // ������ʾ��С�������������
    public string Description;
    public abstract void Action(Item ModOwner ,Module module, Item targetItem=null);
}
