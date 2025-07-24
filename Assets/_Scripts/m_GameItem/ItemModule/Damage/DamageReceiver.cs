using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using UnityEngine;

public class DamageReceiver : Module
{
    public Ex_ModData Data;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }

    // 新增的SaveData字段，添加序列化特性以便在检查器中可见
    [SerializeField]
    public DamageReceiver_SaveData saveData = new DamageReceiver_SaveData();

    // 将血量和最大血量改为属性，引用saveData中的字段
    public GameValue_float MaxHp
    {
        get => saveData.MaxHp;
        set => saveData.MaxHp = value;
    }

    public float Hp
    {
        get => saveData.Hp;
        set => saveData.Hp = value;
    }

    [Header("受击动画设置")]
    public int flashCount = 1;

    [Min(0.01f)]
    public float flashDuration = 0.2f;

    public Color flashColor = new Color(1f, 0f, 0f, 1f);
    public float shakeDuration = 0.15f;
    public float shakeMagnitude = 0.1f;

    private bool isFlashing = false;
    private SpriteRenderer cachedSpriteRenderer; // ✅ 缓存 SpriteRenderer

    [System.Serializable]  // 添加序列化特性
    public class DamageReceiver_SaveData
    {
        [Header("生命值设置")]
        public float Hp = 100;
        public GameValue_float MaxHp = new GameValue_float(100);

        // 可根据需要添加更多字段，比如是否死亡等
    }

    public override void Awake()
    {
        if (_Data.Name == "")
        {
            _Data.Name = ModText.Hp;
        }
    }

    public virtual void TakeDamage(float damage)
    {
        // 如果已经死亡（血量 <= 0），则不再处理伤害
        if (Hp <= 0) return;

        Hp -= damage;
        OnAction.Invoke(Hp);

        // 触发动画和震动效果（仅在存活时）
        if (cachedSpriteRenderer != null && !isFlashing)
        {
            Hit_Flash(cachedSpriteRenderer);
            StartCoroutine(ShakeSprite(cachedSpriteRenderer.transform));
        }

        // 可选：如果血量降到0以下，可以做一些死亡处理
        if (Hp <= 0)
        {
            // 比如触发死亡事件、播放死亡动画等
            // Die();
        }
    }

    #region 动画效果

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

    public override void Load()
    {
        Data.ReadData(ref saveData);
        // ✅ 只查找一次并缓存
        cachedSpriteRenderer = item.Sprite;
        if (cachedSpriteRenderer == null)
        {
            Debug.LogWarning("[DamageReceiver] 未找到 SpriteRenderer。");
        }
        // 可根据需要填充
    }

    public override void Save()
    {
        // 可根据需要填充
        Data.WriteData(saveData);
    }
}