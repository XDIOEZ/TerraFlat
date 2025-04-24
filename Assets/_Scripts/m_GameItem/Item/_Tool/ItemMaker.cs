using Org.BouncyCastle.Utilities.IO.Pem;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemMaker : MonoBehaviour
{
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

    [Button("Drop Item")]
    public void DropItem(string TableName, float DropRange)
    {
        foreach (var item in loots.GetLoot(TableName).lootList)
        {
            ItemData _data = GameRes.Instance.GetPrefab(item.lootName).GetComponent<Item>().Item_Data;
            _data.Stack.Amount = item.lootAmount;
            DropItemWithAnimation(item.lootName, _data, transform.position, (Vector2)transform.position + (Random.insideUnitCircle * DropRange));
        }
    }

    public void DropItemByLoot(Loot loot, float DropRange)
    {
        foreach (var item in loot.lootList)
        {
            ItemData _data = GameRes.Instance.GetPrefab(item.lootName).GetComponent<Item>().Item_Data;
            _data.Stack.Amount = item.lootAmount;
            DropItemWithAnimation(item.lootName, _data, transform.position, (Vector2)transform.position + (Random.insideUnitCircle * DropRange));
        }
    }

    public void DropItemWithAnimation(
        string PrefabName,
        ItemData itemData,
        Vector3 startPos,
        Vector2 endPos,
        float baseDuration = 0.5f,
        float distanceSensitivity = 0.1f
    )
    {
        GameObject newObject = GameRes.Instance.InstantiatePrefab(PrefabName, startPos);
        if (newObject == null)
        {
            Debug.LogError("\u5b9e\u4f8b\u5316\u7269\u4f53\u5931\u8d25\uff01");
            return;
        }

        Item item = newObject.GetComponent<Item>();
        if (item == null)
        {
            Debug.LogError("\u672a\u627e\u5230 Item \u7ec4\u4ef6\uff01");
            GameObject.Destroy(newObject);
            return;
        }

        item.Item_Data = itemData;
        item.Item_Data.Stack.CanBePickedUp = false;

        newObject.GetComponent<MonoBehaviour>().StartCoroutine(
            ParabolaAnimation(
                newObject.transform,
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

    public void DropItemWithAnimation(
        Transform itemTransform,
        Vector3 startPos,
        Vector3 endPos,
        Item item
    )
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

    public IEnumerator ParabolaAnimation(
        Transform itemTransform,
        Vector3 startPos,
        Vector3 endPos,
        Item item,
        float baseDuration,
        float distanceSensitivity,
        float baseRotationSpeed = 360f,
        float rotationSpeedFactor = 0.8f,
        float maxHeight = 5f
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

    private static Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return uu * p0 + 2 * u * t * p1 + tt * p2;
    }
}
