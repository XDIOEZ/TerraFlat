using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class Defense
{
    public float defenseStrength = 0;
    public float defenseToughness = 0;
    public float defenseMagic = 0;

    public float maxDefenseStrength = 0;  
    public float maxDefenseToughness = 0;
    public float maxDefenseMagic = 0;

    //��д+�����
    public static Defense operator +(Defense a, Defense b)
    {
        Defense result = new Defense(0, 0);
        result.defenseStrength = a.defenseStrength + b.defenseStrength;
        result.defenseToughness = a.defenseToughness + b.defenseToughness;
        return result;
    }
    //��д-�����
    public static Defense operator -(Defense a, Defense b)
    {
        Defense result = new Defense(0, 0);
        result.defenseStrength = a.defenseStrength - b.defenseStrength;
        result.defenseToughness = a.defenseToughness - b.defenseToughness;
        return result;
    }

    //��дTOSTRING����
    public override string ToString()
    {
        return "����ǿ��: " + defenseStrength + " ����: " + defenseToughness;
    }

    public Defense(float defenseStrength, float defenseToughness)
    {
        this.defenseStrength = defenseStrength;
        this.defenseToughness = defenseToughness;
    }
}