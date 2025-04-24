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
        //��ȡ������_CanvasGroup
        _CanvasGroup = GetComponentInParent<CanvasGroup>();
        //��ȡ����_Button
        _Button = GetComponent<Button>();
        //���ð�ť��onClick�¼�
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
