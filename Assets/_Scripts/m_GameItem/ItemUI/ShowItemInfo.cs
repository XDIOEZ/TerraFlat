using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;


public class ShowItemInfo : MonoBehaviour
{
    public TextMeshProUGUI ItemDescription;
    
    public void ShowDescription(string description)
    {
        ItemDescription.text = description;
    }
    public void ShowInfo(ItemData itemData)
    {
        ShowDescription(itemData.ToString());
    }
}
