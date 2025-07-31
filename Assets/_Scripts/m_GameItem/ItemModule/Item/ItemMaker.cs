using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public class ItemMaker
{
    [BoxGroup("��������")]
    [Tooltip("�����")]
    public Loot_List loots;

    [BoxGroup("��������")]
    [Tooltip("���䷶Χ����λ���ף�")]
    public float DropRange = 2f;

    [BoxGroup("�����߶�������")]
    [Tooltip("������������ʱ�䣨��λ���룩")]
    public float baseDuration = 0.5f;

    [BoxGroup("�����߶�������")]
    [Tooltip("����ʱ��Ծ�������ж�")]
    public float distanceSensitivity = 0.1f;

    [BoxGroup("�����߶�������")]
    [Tooltip("������ת�ٶ�")]
    public float baseRotationSpeed = 360f;

    [BoxGroup("�����߶�������")]
    [Tooltip("��ת�ٶ�ϵ��")]
    public float rotationSpeedFactor = 0.8f;

    [BoxGroup("�����߶�������")]
    [Tooltip("�������߶�")]
    public float maxHeight = 5f;

    [BoxGroup("��ض�������")]
    [Tooltip("��ص����߶���Сֵ")]
    public float bounceHeightMin = 0.05f;

    [BoxGroup("��ض�������")]
    [Tooltip("��ص����߶����ֵ")]
    public float bounceHeightMax = 0.5f;

    #region ����ӿ�

    [Button("Drop Item")]
    [Tooltip("����LootName������Ʒ")]
    public void DropItemByLootName(string LootName, float DropRange, Transform transform)
    {
        foreach (var item in loots.GetLoot(LootName).lootList)
        {
            DropItemByNameAndAmount(item.lootName, item.LootAmountMin, DropRange, transform);
        }
    }

    [Tooltip("����ItemName��Amount������Ʒ")]
    public void DropItemByNameAndAmount(string ItemName, float Amount, float DropRange,Transform transform)
    {
        Item item = GameItemManager.Instance.InstantiateItem(ItemName).GetComponent<Item>();
        item.Item_Data.Stack.Amount = Amount;

        Vector2 randomOffset = Random.insideUnitCircle * DropRange;
        Vector2 targetPos = (Vector2)transform.position + randomOffset;

        DropItem_cric(item, transform.position, 1);
    }

    [Tooltip("����Loot������Ʒ")]
    public void DropItemByLoot(Loot loot, float DropRange, Transform transform)
    {
        foreach (var item in loot.lootList)
        {
            DropItemByNameAndAmount(item.lootName, item.LootAmountMin, DropRange, transform);
        }
    }

    #endregion

    #region ��������

    /// <summary>
    /// ����Item������Ʒ�����������߶�����
    /// </summary>
    /// <param name="item">�������Ʒ</param>
    /// <param name="startPos">��ʼλ��</param>
    /// <param name="radius">���ɢ��뾶</param>
    [Tooltip("����Item������Ʒ ��������")]
    public void DropItem_cric(Item item, Vector3 startPos, float radius)
    {
        // ������Ʒ��ʱ���ɱ�ʰȡ
        item.Item_Data.Stack.CanBePickedUp = false;

        // �������Ŀ��λ��
        Vector2 endPos = startPos + new Vector3(Random.Range(-radius, radius), Random.Range(-radius, radius), 0f);

        // ���������߶���Э��
        item.GetComponent<MonoBehaviour>().StartCoroutine(
            ParabolaAnimation(
                item.transform,
                startPos,
                endPos,
                item,
                baseDuration,
                distanceSensitivity,
                baseRotationSpeed,
                rotationSpeedFactor,
                maxHeight
            )
        );
    }


    [Tooltip("����Item������Ʒ �����������򻯲�����")]
    public void DropItemWithAnimation(Transform itemTransform, Vector3 startPos, Vector3 endPos, Item item)
    {
        itemTransform.GetComponent<MonoBehaviour>().StartCoroutine(
            ParabolaAnimation(
                itemTransform,
                startPos,
                endPos,
                item,
                baseDuration,
                distanceSensitivity,
                baseRotationSpeed,
                rotationSpeedFactor,
                maxHeight
            )
        );
    }

    [Tooltip("�����߶���")]
    public IEnumerator ParabolaAnimation(
        Transform itemTransform,
        Vector3 startPos,
        Vector3 endPos,
        Item item,
        float baseDuration,
        float distanceSensitivity,
        float baseRotationSpeed,
        float rotationSpeedFactor,
        float maxHeight
    )
    {
        float timeElapsed = 0f;
        float distance = Vector3.Distance(startPos, endPos);
        float duration = baseDuration + distance * distanceSensitivity;

        Vector3 controlPoint = (startPos + endPos) / 2f + Vector3.up * Mathf.Clamp(
            Mathf.Lerp(0.5f, maxHeight, Mathf.InverseLerp(0f, 10f, distance)),
            0.5f,
            maxHeight
        );

        float rotationSpeed = distance * baseRotationSpeed / duration * rotationSpeedFactor;

        while (timeElapsed < duration)
        {
            float t = timeElapsed / duration;
            itemTransform.position = CalculateBezierPoint(t, startPos, controlPoint, endPos);
            itemTransform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);
            timeElapsed += Time.deltaTime;
            yield return null;
        }

        itemTransform.position = endPos;

        item.StartCoroutine(LandingSettleEffect(itemTransform, item, Random.Range(bounceHeightMin, bounceHeightMax)));
        item.Item_Data.Stack.CanBePickedUp = true;
    }

    [Tooltip("��ض���")]
    private static IEnumerator LandingSettleEffect(Transform itemTransform, Item item, float bounceHeight = 0.08f)
    {
        float duration = 0.2f;
        float elapsed = 0f;
        Vector3 originalPos = itemTransform.position;
        Quaternion originalRot = itemTransform.rotation;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float bounce = Mathf.Sin(t * Mathf.PI) * bounceHeight;
            itemTransform.position = originalPos + Vector3.up * bounce;
            itemTransform.rotation = Quaternion.Slerp(originalRot, Quaternion.identity, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        itemTransform.position = originalPos;
        itemTransform.rotation = Quaternion.identity;
    }

    [Tooltip("���������߼���")]
    private static Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return uu * p0 + 2 * u * t * p1 + tt * p2;
    }

    #endregion
}
