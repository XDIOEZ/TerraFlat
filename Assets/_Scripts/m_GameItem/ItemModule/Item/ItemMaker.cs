using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public class ItemMaker
{
    [BoxGroup("掉落配置")]
    [Tooltip("掉落表")]
    public List_Loot loots;

    [BoxGroup("掉落配置")]
    [Tooltip("掉落范围（单位：米）")]
    public float DropRange = 2f;

    [BoxGroup("抛物线动画参数")]
    [Tooltip("基础动画持续时间（单位：秒）")]
    public float baseDuration = 0.5f;

    [BoxGroup("抛物线动画参数")]
    [Tooltip("动画时间对距离的敏感度")]
    public float distanceSensitivity = 0.1f;

    [BoxGroup("抛物线动画参数")]
    [Tooltip("基础旋转速度")]
    public float baseRotationSpeed = 360f;

    [BoxGroup("抛物线动画参数")]
    [Tooltip("旋转速度系数")]
    public float rotationSpeedFactor = 0.8f;

    [BoxGroup("抛物线动画参数")]
    [Tooltip("最大抛物高度")]
    public float maxHeight = 5f;

    [BoxGroup("落地动画参数")]
    [Tooltip("落地弹动高度最小值")]
    public float bounceHeightMin = 0.05f;

    [BoxGroup("落地动画参数")]
    [Tooltip("落地弹动高度最大值")]
    public float bounceHeightMax = 0.5f;

    #region 掉落接口

    [Button("Drop Item")]
    [Tooltip("根据LootName掉落物品")]
    public void DropItemByLootName(string LootName, float DropRange, Transform transform)
    {
        foreach (var item in loots.GetLoot(LootName).lootList)
        {
            DropItemByNameAndAmount(item.lootName, item.lootAmount, DropRange, transform);
        }
    }

    [Tooltip("根据ItemName和Amount掉落物品")]
    public void DropItemByNameAndAmount(string ItemName, float Amount, float DropRange,Transform transform)
    {
        Item item = RunTimeItemManager.Instance.InstantiateItem(ItemName).GetComponent<Item>();
        item.Item_Data.Stack.Amount = Amount;

        Vector2 randomOffset = Random.insideUnitCircle * DropRange;
        Vector2 targetPos = (Vector2)transform.position + randomOffset;

        DropItemWithAnimation(item, transform.position, targetPos);
    }

    [Tooltip("根据Loot掉落物品")]
    public void DropItemByLoot(Loot loot, float DropRange, Transform transform)
    {
        foreach (var item in loot.lootList)
        {
            DropItemByNameAndAmount(item.lootName, item.lootAmount, DropRange, transform);
        }
    }

    #endregion

    #region 动画核心

    [Tooltip("根据Item掉落物品 附带动画")]
    public void DropItemWithAnimation(Item item, Vector3 startPos, Vector2 endPos)
    {
        item.Item_Data.Stack.CanBePickedUp = false;

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

    [Tooltip("根据Item掉落物品 附带动画（简化参数）")]
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

    [Tooltip("抛物线动画")]
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

    [Tooltip("落地动画")]
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

    [Tooltip("贝塞尔曲线计算")]
    private static Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return uu * p0 + 2 * u * t * p1 + tt * p2;
    }

    #endregion
}
