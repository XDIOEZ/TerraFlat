using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UltEvents;
using UnityEngine;

public class WorldUI_temp : MonoBehaviour
{
    TextMeshProUGUI text;
    public UltEvent onTextChange;
    // Start is called before the first frame update
    void Start()
    {
        //��ȡ�������ϵ�tmptext���
        if (text == null)
         text = GetComponentInChildren<TextMeshProUGUI>();
        
        //�����ı�����
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void SetText(float textFloat)
    {
        this.text.text = textFloat.ToString("0", CultureInfo.InvariantCulture);
    }
}
