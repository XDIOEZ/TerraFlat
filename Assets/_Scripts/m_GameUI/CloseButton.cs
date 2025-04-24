using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CloseButton : MonoBehaviour
{
    public CanvasGroup _CanvasGroup;
    public Button _Button;
    public bool _IsDestroy = false;
    // Start is called before the first frame update
    void Start()
    {
        //获取父对象_CanvasGroup
        _CanvasGroup = GetComponentInParent<CanvasGroup>();
        //获取对象_Button
        _Button = GetComponent<Button>();
        //设置按钮的onClick事件
        _Button.onClick.AddListener(CloseMenu);
    }
    public void CloseMenu()
    {
        if (_IsDestroy)
        {
            Destroy(gameObject.transform.parent.gameObject);
        }
        else
        {
            _CanvasGroup.alpha = 0;
            _CanvasGroup.blocksRaycasts = false;
            _CanvasGroup.interactable = false;
        }
     
    }
}
