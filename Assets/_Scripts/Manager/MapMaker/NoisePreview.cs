using UnityEngine;
using UnityEditor;
using System.Linq;

[ExecuteInEditMode]
[RequireComponent(typeof(SpriteRenderer))]
public class NoisePreview : MonoBehaviour
{
    [Tooltip("���������ʲ�")]
    public BaseNoise settings;

    [Header("Ԥ������")]
    [Tooltip("����Ԥ��ͼ���")]
    public int width = 256;

    [Tooltip("����Ԥ��ͼ�߶�")]
    public int height = 256;

    [Tooltip("������������")]
    public int noiseSeed = 0;

    [Tooltip("�༭ģʽ���Ƿ������Զ�����")]
    public bool editModeRealtimeUpdate = true;

    [Tooltip("Ԥ��ͼ���ű���")]
    public float previewScale = 1f;

    [Header("���ط������")]
    [Tooltip("���ؿ��С��ֵԽ�����Խ����")]
    [Range(1, 32)] public int pixelBlockSize = 4;

    [Tooltip("��ֵ���� - С�ڴ�ֵ�����ؽ�������")]
    [Range(0f, 1f)] public float hideBelowThreshold = 0.3f;

    [Tooltip("��ֵ���� - ���ڴ�ֵ�����ؽ�������")]
    [Range(0f, 1f)] public float hideAboveThreshold = 0.7f;

    private Texture2D noiseTexture;
    private SpriteRenderer targetRenderer;
    private Sprite noiseSprite;
    private bool isInitialized = false;
    private Material transparentMaterial;
    public Color color = Color.black;

    private void OnEnable()
    {
        InitializeComponents();
        SubscribeToNoiseEvents();

        if (!Application.isPlaying)
        {
            GenerateNoiseTexture();
        }
    }

    private void Start()
    {
        GenerateNoiseTexture();
    }

    private void OnDisable()
    {
        UnsubscribeFromNoiseEvents();
    }

    private void Update()
    {
        transform.localScale = Vector3.one * previewScale;
    }

    // �����������õĸ����¼�
    private void SubscribeToNoiseEvents()
    {
        UnsubscribeFromNoiseEvents(); // ��ȡ���ɶ���

        if (settings != null)
        {
           settings.UpdateEvent.Clear();
           settings.UpdateEvent +=(OnNoiseSettingsUpdated);
        }
    }

    // ȡ�������������õĸ����¼�
    private void UnsubscribeFromNoiseEvents()
    {
        if (settings != null)
        {
            settings.UpdateEvent += (OnNoiseSettingsUpdated);
        }
    }

    // ���������ø���ʱ����
    private void OnNoiseSettingsUpdated()
    {
        if (CanUpdate())
        {
            GenerateNoiseTexture();
        }
    }

    // ����Ƿ���Ը���Ԥ��
    private bool CanUpdate()
    {
        return isInitialized &&
               (Application.isPlaying ||
                (editModeRealtimeUpdate && !Application.isPlaying));
    }

    private void InitializeComponents()
    {
        if (isInitialized) return;

        targetRenderer = GetComponent<SpriteRenderer>();

        if (targetRenderer == null)
        {
            targetRenderer = gameObject.AddComponent<SpriteRenderer>();
            Debug.LogWarning("�Զ������SpriteRenderer���", this);
        }

        SetupTransparentMaterial();
        CreateTextureIfNeeded();

        isInitialized = true;
    }

    // �޸�SetupTransparentMaterial�������滻materialΪsharedMaterial
    private void SetupTransparentMaterial()
    {
        if (transparentMaterial == null)
        {
            Shader defaultShader = Shader.Find("Sprites/Default");
            if (defaultShader != null)
            {
                // �ؼ��޸���ʹ��sharedMaterial���ⴴ��ʵ��
                if (targetRenderer.sharedMaterial == null || targetRenderer.sharedMaterial.shader != defaultShader)
                {
                    // ���Բ����Ѵ��ڵĹ�����ʣ������ظ�����
                    transparentMaterial = Resources.FindObjectsOfTypeAll<Material>()
                        .FirstOrDefault(m => m.shader == defaultShader && m.hideFlags == HideFlags.None);

                    // ���û���ҵ��򴴽��²���
                    if (transparentMaterial == null)
                    {
                        transparentMaterial = new Material(defaultShader);
                        transparentMaterial.name = "NoisePreview_SharedMaterial";
                    }

                    // �༭ģʽ�±��Ϊ�����棬������Ⱦ��Դ
                    if (!Application.isPlaying)
                    {
                        transparentMaterial.hideFlags = HideFlags.DontSaveInEditor;
                    }

                    // ʹ��sharedMaterial��ֵ
                    targetRenderer.sharedMaterial = transparentMaterial;
                }
                else
                {
                    transparentMaterial = targetRenderer.sharedMaterial;
                }
            }
            else
            {
                Debug.LogWarning("�Ҳ���Ĭ��shader����ȷ������'Sprites/Default' shader", this);
            }
        }
    }

    private void CreateTextureIfNeeded()
    {
        if (noiseTexture == null || noiseTexture.width != width || noiseTexture.height != height)
        {
            CleanupResources();

            noiseTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            noiseTexture.wrapMode = TextureWrapMode.Clamp;
            Color[] transparentPixels = new Color[width * height];
            for (int i = 0; i < transparentPixels.Length; i++)
            {
                transparentPixels[i] = new Color(0, 0, 0, 0);
            }
            noiseTexture.SetPixels(transparentPixels);
            noiseTexture.Apply();

            if (!Application.isPlaying)
            {
                noiseTexture.hideFlags = HideFlags.DontSave;
            }
        }
    }

    public void GenerateNoiseTexture()
    {
        if (!isInitialized)
        {
            InitializeComponents();
        }

        CreateTextureIfNeeded();

        if (settings == null)
        {
            FillTextureWithColor(color);
            return;
        }

        for (int y = 0; y < height; y += pixelBlockSize)
        {
            for (int x = 0; x < width; x += pixelBlockSize)
            {
                float noiseValue = settings.Sample(x, y, noiseSeed);
                bool shouldHide = noiseValue < hideBelowThreshold || noiseValue > hideAboveThreshold;
                Color blockColor = shouldHide
                    ? new Color(0, 0, 0, 0)
                    : new Color(color.r, color.g, color.b, color.a);

                int blockWidth = Mathf.Min(pixelBlockSize, width - x);
                int blockHeight = Mathf.Min(pixelBlockSize, height - y);

                for (int dy = 0; dy < blockHeight; dy++)
                {
                    for (int dx = 0; dx < blockWidth; dx++)
                    {
                        noiseTexture.SetPixel(x + dx, y + dy, blockColor);
                    }
                }
            }
        }

        noiseTexture.Apply();
        UpdateSprite();

        if (!Application.isPlaying)
        {
            SceneView.RepaintAll();
        }
    }

    private void UpdateSprite()
    {
        CleanupSprite();

        noiseSprite = Sprite.Create(
            noiseTexture,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f
        );

        if (!Application.isPlaying)
        {
            noiseSprite.hideFlags = HideFlags.DontSave;
        }

        targetRenderer.sprite = noiseSprite;
    }

    // ͬʱ�޸�CleanupResources�еĲ��������߼�
    private void CleanupResources()
    {
        CleanupSprite();

        if (noiseTexture != null)
        {
            if (Application.isPlaying)
            {
                Destroy(noiseTexture);
            }
            else
            {
                DestroyImmediate(noiseTexture);
            }
            noiseTexture = null;
        }

        // �༭ģʽ�²����ٹ�����ʣ�����Ӱ������ʵ��
        if (transparentMaterial != null && !Application.isPlaying)
        {
            transparentMaterial = null; // ��������ã�������
        }
        // ����ʱ����Ҫ����
        else if (Application.isPlaying && transparentMaterial != null)
        {
            Destroy(transparentMaterial);
            transparentMaterial = null;
        }
    }

    private void CleanupSprite()
    {
        if (noiseSprite != null)
        {
            if (Application.isPlaying)
            {
                Destroy(noiseSprite);
            }
            else
            {
                DestroyImmediate(noiseSprite);
            }
            noiseSprite = null;
        }
    }

    private void FillTextureWithColor(Color color)
    {
        if (noiseTexture == null) return;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                noiseTexture.SetPixel(x, y, color);
            }
        }
        noiseTexture.Apply();
        UpdateSprite();
    }

    [ContextMenu("ˢ������Ԥ��")]
    public void RefreshPreview()
    {
        GenerateNoiseTexture();
    }

    private void OnValidate()
    {
        // ����������Χ���Ʋ���
        width = Mathf.Clamp(width, 16, 2048);
        height = Mathf.Clamp(height, 16, 2048);
        previewScale = Mathf.Max(0.1f, previewScale);
        pixelBlockSize = Mathf.Clamp(pixelBlockSize, 1, 32);

        // ȷ����ֵ�߼���ȷ������ֵ����������ֵ��
        if (hideBelowThreshold > hideAboveThreshold)
        {
            (hideBelowThreshold, hideAboveThreshold) = (hideAboveThreshold, hideBelowThreshold);
        }

        // �ؼ��޸ģ�ȥ���ӳ٣�ֱ�Ӽ�ʱ���£��Ӱ�ȫ�����ⱨ��
        if (!Application.isPlaying && editModeRealtimeUpdate && isInitialized && noiseTexture != null)
        {
            // ֱ�ӵ��������߼��������ӳ�
            GenerateNoiseTexture();
        }
        // ��δ��ʼ�����ȳ�ʼ���ٸ��£������״β����仯ʱ����Ӧ��
        else if (!Application.isPlaying && editModeRealtimeUpdate && !isInitialized)
        {
            InitializeComponents();
            if (noiseTexture != null)
            {
                GenerateNoiseTexture();
            }
        }
    }

    // �����������ʲ�����ʱ���¶����¼�
    private void OnDidApplyAnimationProperties()
    {
        SubscribeToNoiseEvents();
        if (isInitialized)
        {
            GenerateNoiseTexture();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromNoiseEvents();
        CleanupResources();
        isInitialized = false;
    }
    // ����Ĺ�������������ӣ�
    [Header("����������")]
    [Tooltip("������Ԥ����Χ�����ɫ")]
    public Color gizmosColor = new Color(1, 1, 1, 0.3f); // ��ɫ��͸��Ĭ��ֵ

    // �޸�OnDrawGizmos������
    private void OnDrawGizmos()
    {
        Gizmos.color = gizmosColor; // ʹ�ù��������õ���ɫ
        Vector3 size = new Vector3(width / 100f * previewScale, height / 100f * previewScale, 0.1f);
        Gizmos.DrawCube(transform.position, size);
    }

}
