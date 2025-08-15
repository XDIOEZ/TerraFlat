using System;

[Serializable]
public class Result_List
{
    public string item = "";
    public int amount = 1;

    public override string ToString()
    {
        return $"{amount}x{item}";
    }
}