using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using Force.DeepCloner;
using NUnit.Framework.Interfaces;

public class BuildingShadow : MonoBehaviour
{
    public List<GameObject> AroundObjects = new List<GameObject>();
    public SpriteRenderer ShadowRenderer;
    public Color ShadowColor = new Color(1f, 1f, 1f, 0.7f);
    public Color WarringColor = Color.red;

    private Tween moveTween;
    private Tween colorTween;

    // �����ֶμ�¼tween 
    private Tween alphaTween;

    public bool AroundHaveGameObject => AroundObjects.Count > 0;
    public void OnTriggerEnter2D(Collider2D collision)
    {
        // ֻ�����Trigger����
        if (!collision.isTrigger)
        {
            AroundObjects.Add(collision.gameObject);
        }
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.isTrigger)
        {
            AroundObjects.Remove(collision.gameObject);
        }
    }

    public void OnDestroy()
    {
        AroundObjects.Clear();
        // ��������DOTween��tween 
        DOTween.Kill(transform);
        DOTween.Kill(ShadowRenderer);
        // ��ȫ���ټ�¼��tween 
        moveTween?.Kill();
        colorTween?.Kill();
        alphaTween?.Kill();
    }

    // ��װ��ɫ����߼� 
    public void UpdateColor(bool hasOverlap)
    {
        Color targetColor = hasOverlap ? WarringColor : ShadowColor;
        if (ShadowRenderer != null)
        {
            colorTween?.Kill();
            colorTween = DOTween.To(() => ShadowRenderer.color, c => ShadowRenderer.color = c, targetColor, 0.2f);
        }
    }

    // ���� BoxCollider2D ������ϵ��
    public Vector2 BoxColliderScale = Vector2.one;

    public void InitShadow(SpriteRenderer newRenderer)
    {
        if (newRenderer == null || ShadowRenderer == null) return;

        ShadowRenderer.sprite = newRenderer.sprite;
        ShadowRenderer.sortingOrder = newRenderer.sortingOrder - 1;
        ShadowRenderer.color = ShadowColor;

        Bounds bounds = newRenderer.sprite.bounds;

        BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider != null)
        {
            boxCollider.size = Vector2.Scale(bounds.size, BoxColliderScale);
            boxCollider.offset = bounds.center;
        }
    
}



// ��װ͸���ȱ仯 
public void UpdateAlpha(float alpha)
    {
        if (ShadowRenderer != null)
        {
            Color color = ShadowRenderer.color;
            color.a = alpha * 0.5f; // ��� 0.5 
            ShadowRenderer.color = color;
            ShadowRenderer.enabled = alpha > 0f;
        }
    }

    // ��װ�ƶ��߼� 
    public void SmoothMove(Vector3 targetPosition)
    {
        moveTween?.Kill();
        moveTween = transform.DOMove(targetPosition, 0.1f).SetEase(Ease.OutQuad);
    }
}

[System.Serializable]
public class Building_InstallAndUninstall
{
    private Transform hostTransform;
    private SpriteRenderer hostRenderer;
    private BoxCollider2D boxCollider2D;
    private Item item;
    private Hp Hp;
    private ItemData Item_Data;
    private BuildingShadow GhostShadow;


    [Header("���ü�����")]

    [Tooltip("������ҷ��ý������������")]
    public float MaxDistance = 2.0f;

    [Tooltip("���ڼ���͸���ȵ���С�ɼ����루�������ʱ͸����Ϊ���")]
    [SerializeField] private float minVisibleDistance = 0.3f;

    [Tooltip("���ڼ���͸���ȵ����ɼ����루���볬��ʱ�������ɼ���")]
    [SerializeField] private float maxVisibleDistance = 3.5f;

    public void Update()
    {
        if (hostTransform.parent == null || item == null || item.Item_Data.Stack.CanBePickedUp)
            return;

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;

        if (GhostShadow == null)
        {
            GhostShadow = GameRes.Instance.InstantiatePrefab("BuildingShadow").GetComponent<BuildingShadow>();
            GhostShadow.InitShadow(hostRenderer);
        }

        float distance = Vector2.Distance(hostTransform.position, mouseWorldPos);
        float alpha = Mathf.InverseLerp(maxVisibleDistance, minVisibleDistance, distance);
        alpha = Mathf.Clamp01(alpha);

        GhostShadow.UpdateAlpha(alpha);

        if (GhostShadow.ShadowRenderer.enabled)
        {
            GhostShadow.SmoothMove(mouseWorldPos);
        }

        GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
    }

    public void Init(Transform owner)
    {
        hostTransform = owner.transform;
        hostRenderer = owner.GetComponentInChildren<SpriteRenderer>();
        boxCollider2D = owner.GetComponent<BoxCollider2D>();
        item = owner.GetComponent<Item>();
        Hp = owner.GetComponent<IHealth>().Hp;
        Item_Data = item?.Item_Data;
    }
    public void Install()
    {
        if (GhostShadow == null || GhostShadow.AroundHaveGameObject)
        {
            Debug.Log("��������Ʒ���޷���װ��");
            return;
        }

        float distance = Vector2.Distance(hostTransform.position, GhostShadow.transform.position);
        if (distance > MaxDistance)
        {
            Debug.Log("���������þ��룬�޷���װ��");
            return;
        }

        if (item == null) return;

        item.Item_Data.Stack.Amount--;
        item.UpdatedUI_Event?.Invoke();
        Hp.Value = Hp.maxValue;

        //ʵ�����µ�
        GameObject Installed = GameRes.Instance.InstantiatePrefab(Item_Data.Name, GhostShadow.transform.position);
        Installed.transform.localScale *= 1 / 0.7f;

        Item Installed_cloneItem = Installed.GetComponent<Item>();

        Installed_cloneItem.GetComponent<BoxCollider2D>().isTrigger = false;
        if (Installed_cloneItem != null)
        {
            Installed_cloneItem.Item_Data = item.Item_Data.DeepClone();
            Installed_cloneItem.Item_Data.Stack.Amount = 1;
        }

        if (item.Item_Data.Stack.Amount <= 0)
        {
            CleanupGhost();
            GameObject.Destroy(hostTransform.gameObject);
        }
    }

    public void UnInstall()
    {
        hostTransform.localScale *= 0.7f;
        boxCollider2D.isTrigger = true;
        Item_Data.Stack.CanBePickedUp = true;
        Hp.Value = -1;
        Item_Data.Durability -= 1;

        Vector2 pos = (Vector2)hostTransform.position;
        ItemMaker itemMaker = hostTransform.GetComponent<ItemMaker>();

        itemMaker.DropItemWithAnimation(
            hostTransform,
            hostTransform.position,
            pos + (Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();
    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            GameObject.Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }
}
