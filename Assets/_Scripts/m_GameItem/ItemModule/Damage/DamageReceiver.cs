using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 处理模块伤害接收与反馈动画
/// </summary>
public class DamageReceiver : Module
{
    #region 数据引用

    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    [SerializeField]
    public DamageReceiver_SaveData Data = new DamageReceiver_SaveData();

    public GameValue_float MaxHp
    {
        get => Data.MaxHp;
        set => Data.MaxHp = value;
    }

    public float Hp
    {
        get => Data.Hp;
        set => Data.Hp = value;
    }

    [System.Serializable]
    public class DamageReceiver_SaveData
    {
        [Header("生命值设置")]
        public float Hp = 100;
        public GameValue_float MaxHp = new GameValue_float(100);
        [Header("伤害者的UID列表")]
        public List<int> AttackersUIDs = new List<int>();
    }

    #endregion

    #region 动画相关参数

    [Header("受击动画设置")]
    public int flashCount = 1;

    [Min(0.01f)]
    public float flashDuration = 0.2f;

    public Color flashColor = new Color(1f, 0f, 0f, 1f);
    public float shakeDuration = 0.15f;
    public float shakeMagnitude = 0.1f;

    private bool isFlashing = false;
    private SpriteRenderer cachedSpriteRenderer;

    #endregion

    #region 生命周期函数

    public override void Awake()
    {
        if (_Data.Name == "")
            _Data.Name = ModText.Hp;
    }

    public override void Load()
    {
        modData.ReadData(ref Data);

        cachedSpriteRenderer = item.Sprite;
        if (cachedSpriteRenderer == null)
            Debug.LogWarning("[DamageReceiver] 未找到 SpriteRenderer。");
    }

    public override void Save()
    {
        modData.WriteData(Data);
    }

    #endregion

    #region 受击处理

    public virtual void TakeDamage(float damage,Item attacker)
    {
        if (Hp <= 0) return;

        Hp -= damage;

        Data.AttackersUIDs.Add(attacker.Item_Data.Guid);
        //检测列表是否超过3个 超过则移除第一个
        if (Data.AttackersUIDs.Count > 3)
        {
            Data.AttackersUIDs.RemoveAt(0);
        }

        OnAction.Invoke(Hp);

        if (cachedSpriteRenderer != null && !isFlashing)
        {
            Hit_Flash(cachedSpriteRenderer);
            StartCoroutine(ShakeSprite(cachedSpriteRenderer.transform));
        }

        if (Hp <= 0)
        {
            // 可在此处扩展死亡处理逻辑
            // Die();
        }
    }

    #endregion

    #region 动画效果实现

    public void Hit_Flash(SpriteRenderer spriteRenderer)
    {
        StartCoroutine(FlashCoroutine(spriteRenderer));
    }

    private IEnumerator FlashCoroutine(SpriteRenderer spriteRenderer)
    {
        isFlashing = true;
        Color originalColor = spriteRenderer.material.color;

        for (int i = 0; i < flashCount; i++)
        {
            yield return StartCoroutine(LerpColor(spriteRenderer, originalColor, flashColor, flashDuration * 0.5f));
            yield return StartCoroutine(LerpColor(spriteRenderer, flashColor, originalColor, flashDuration * 0.5f));
        }

        isFlashing = false;
    }

    private IEnumerator LerpColor(SpriteRenderer spriteRenderer, Color fromColor, Color toColor, float duration)
    {
        float time = 0f;
        while (time < duration)
        {
            spriteRenderer.material.color = Color.Lerp(fromColor, toColor, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        spriteRenderer.material.color = toColor;
    }

    private IEnumerator ShakeSprite(Transform spriteTransform)
    {
        Vector3 originalPos = spriteTransform.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float x = Random.Range(-1f, 1f) * shakeMagnitude;
            float y = Random.Range(-1f, 1f) * shakeMagnitude;

            spriteTransform.localPosition = originalPos + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        spriteTransform.localPosition = originalPos;
    }

    #endregion
}
