using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class DamageReceiver_New : MonoBehaviour, IDamageReceiver
{
    [Header("Damage Panel Settings")]
    public string DamagePanelName = "Panel_DamageText"; // �����˺������������
    public float damagePanelSpeed = 3f; // �˺������ƶ��ٶ�
    public float damagePanelDuration = 1.5f; // ����ʱ��
                                             // ���������Ž׶�ʱ��ռ�ȣ�0~1��Ĭ��1:1��
    [Range(0f, 1f)] // ʹ��Unity��Range������ʾΪ������
    public float scalePhaseRatio = 0.5f;

    [Header("Scale Settings")]
    public float startingScale = 1.0f; // ��ʼ���ű�����ʵ����ʱ�Ĵ�С��
    public float maximumScale = 0.8f; // ������ű���������Ŀ��ֵ��

    public Color damagePanelColor = Color.white; // ��ʼ��ɫ

    private IHealth health;
    private SpriteRenderer spriteRenderer;

    public string HitParticlesName = "Particle_Hit";


    private bool isFlashing = false;

    [Min(1)]
    public int flashCount = 1;

    [Min(0.01f)]
    public float flashDuration = 0.2f;

    public Color flashColor = new Color(1f, 0f, 0f,1f);

    private Vector3 hitPoint; // ��¼�����е�λ��

    public BoxCollider2D BoxCollider2D;

    public bool CanBeInjured
    {
        get => canBeInjured;
        set
        {
            if (value == false)
            {
                BoxCollider2D.enabled = false;
            }
            else
            {
                BoxCollider2D.enabled = true;
            }
            canBeInjured = value;
        }
    } // �Ƿ���Ա�

    [Header("Shake Settings")]
    public float shakeDuration = 0.15f;
    public float shakeMagnitude = 0.1f;
    private bool canBeInjured;

    void Start()
    {
        HitParticlesName = "Particle_Hit";
        DamagePanelName = "Panel_DamageText";
        BoxCollider2D = GetComponent<BoxCollider2D>();
        CanBeInjured = true;
    }
    public SpriteRenderer SpriteRenderer
    {
        get
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = transform.parent.GetComponentInChildren<SpriteRenderer>();
            }
            return spriteRenderer;
        }

        set
        {
            spriteRenderer = value;
        }
    }

    public void TakeDamage(Damage damage , Vector2 hitPoint)
    {
        health = GetComponentInParent<IHealth>();
        if (health == null) return;

        if (health.Hp.value< 0) return;

        float i = health.GetDamage(damage);

        if (!isFlashing)
        {
            Hit_Flash(SpriteRenderer);
            StartCoroutine(ShakeSprite(SpriteRenderer.transform));
        }

        if (HitParticlesName != string.Empty)
        {
          //  Instantiate(GameResManager.Instance.GetPrefab(HitParticlesName), hitPoint, Quaternion.identity);
            CreateMainColorParticle(HitParticlesName, hitPoint);
        }

        if (!string.IsNullOrEmpty(DamagePanelName))
        {
            // �����˺��������
            GameObject damagePanel = Instantiate(
                GameRes.Instance.GetPrefab(DamagePanelName),
                hitPoint,
                Quaternion.identity
            );

            // ��ȡ�ı��������Э�����ȡһ�Σ�
            TextMeshProUGUI textMeshPro = damagePanel.GetComponentInChildren<TextMeshProUGUI>();
            textMeshPro.text = Mathf.FloorToInt(i).ToString();
            textMeshPro.color = damagePanelColor;

            // ֱ�Ӵ����������
           ShowDamagePanel(damagePanel, textMeshPro, hitPoint, i);
        }




        Debug.Log("��ɵ��˺�Ϊ: " + i + " damage");
    }

    #region ������嶯��
    private void ShowDamagePanel(GameObject damagePanel, TextMeshProUGUI textMeshPro, Vector3 startPos, float damageAmount)
    {
        Transform panelTransform = damagePanel.transform;
        Vector3 randomDirection = Random.insideUnitCircle.normalized;

        float duration = damagePanelDuration;

        // ��������˺�ֵ��̬�仯�ĳ�ʼ����
        float minScale = 1f;
        float maxScale = 2f;

        float clampedDamage = Mathf.Clamp(damageAmount, 10f, 100f);
        float scaleFactor = Mathf.Lerp(minScale, maxScale, (clampedDamage - 10f) / (100f - 10f));

        float growDuration = duration * scalePhaseRatio;    // ���׶�ʱ��
        float shrinkDuration = duration * (1 - scalePhaseRatio); // ��С�׶�ʱ��

        growDuration = Mathf.Clamp(growDuration, 0.1f, duration);
        shrinkDuration = Mathf.Max(duration - growDuration, 0.1f);

        // ��ʼ��������
        panelTransform.localScale = Vector3.one * scaleFactor;

        // �������Ŷ������У��Ŵ�һ������С��ԭ���ţ�
        Sequence scaleSequence = DOTween.Sequence();
        scaleSequence.Append(panelTransform.DOScale(scaleFactor * maximumScale, growDuration));
        scaleSequence.Append(panelTransform.DOScale(scaleFactor, shrinkDuration));

        Sequence mainSequence = DOTween.Sequence();
        mainSequence.Append(
            panelTransform.DOMove(
                startPos + randomDirection * damagePanelSpeed * duration,
                duration
            )
        )
        .Join(scaleSequence)
        .Join(
            DOTween.To(
                () => textMeshPro.color,
                x => textMeshPro.color = x,
                new Color(textMeshPro.color.r, textMeshPro.color.g, textMeshPro.color.b, 0f),
                duration
            )
        )
        .OnComplete(() => Destroy(damagePanel));
    }


    #endregion



    #region ����Ч��
    #region ������˸����

    private IEnumerator ShakeSprite(Transform spriteTransform)
    {
        Vector3 originalPos = spriteTransform.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float x = Random.Range(-1f, 1f) * shakeMagnitude;
            float y = Random.Range(-1f, 1f) * shakeMagnitude;

            spriteTransform.localPosition = new Vector3(originalPos.x + x, originalPos.y + y, originalPos.z);

            elapsed += Time.deltaTime;
            yield return null;
        }

        spriteTransform.localPosition = originalPos;
    }
    #endregion
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
            // ���뵽��˸��ɫ
            yield return StartCoroutine(LerpColor(spriteRenderer, originalColor, flashColor, flashDuration * 0.5f));
            // ������ԭɫ
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

    private GameObject CreateMainColorParticle(string prefabName, Vector2 position)
    {
        SpriteRenderer sr = transform.parent.GetComponentInChildren<SpriteRenderer>();

        if (sr != null && sr.sprite != null)
        {
            var colorThief = new ColorThief.ColorThief();
            List<ColorThief.QuantizedColor> palette = colorThief.GetPalette(sr.sprite.texture, 5); // ��ȡǰ5����ɫ

            GameObject particle = GameRes.Instance.InstantiatePrefab(prefabName, position);
            ParticleSystem ps = particle.GetComponent<ParticleSystem>();

            if (ps != null && palette != null && palette.Count > 0)
            {
                var colorModule = ps.colorOverLifetime;
                colorModule.enabled = true;

                Gradient gradient = new Gradient();
                GradientColorKey[] colorKeys = new GradientColorKey[palette.Count];
                GradientAlphaKey[] alphaKeys = new GradientAlphaKey[1];

                float step = 1f / (palette.Count - 1);

                for (int i = 0; i < palette.Count; i++)
                {
                    UnityEngine.Color color = palette[i].UnityColor;
                    colorKeys[i] = new GradientColorKey(color, i * step);
                }

                alphaKeys[0] = new GradientAlphaKey(1f, 0f); // ���ֲ�͸����Ϊ1

                gradient.SetKeys(colorKeys, alphaKeys);
                colorModule.color = new ParticleSystem.MinMaxGradient(gradient);
            }

            return particle;
        }

        return null;
    }
    #endregion


    private void OnTriggerEnter2D(Collider2D other)
    {
        // ��ȡ������λ��
        hitPoint = other.ClosestPoint(transform.position);
    }
}

public interface IDamageReceiver
{
    void TakeDamage(Damage damage, Vector2 hitPoint);
    public bool CanBeInjured { get; set; }
}
   
