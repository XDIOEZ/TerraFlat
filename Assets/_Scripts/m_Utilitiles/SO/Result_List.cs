using System;

[Serializable]
public class Result_List
{
    public string resultItem = "";
    public int resultAmount = 1;

    public override string ToString()
    {
        return $"{resultAmount}x{resultItem}";
    }
}