using UnityEngine;


public class m_ShadowManager : MonoBehaviour
{
    // 使用自定义属性
    public string sortingLayerName = "Default";

    void Start()
    {
        var spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sortingLayerName = sortingLayerName;

        if (transform.parent?.parent != null&&transform.parent.parent.GetComponent<AttackTrigger>()!= null)
        {
            //Debug.Log("透明度设置为 0");
            spriteRenderer.color = new Color(1, 1, 1, 0);
        }
        else
        {
            spriteRenderer.color = new Color(1, 1, 1, 0.5f);
        }
    }
}
