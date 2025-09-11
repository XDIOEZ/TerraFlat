using UnityEngine;
using UnityEditor;
using System.Linq;

[ExecuteInEditMode]
[RequireComponent(typeof(SpriteRenderer))]
public class NoisePreview : MonoBehaviour
{
    [Tooltip("噪声设置资产")]
    public BaseNoise settings;

    [Header("预览设置")]
    [Tooltip("噪声预览图宽度")]
    public int width = 256;

    [Tooltip("噪声预览图高度")]
    public int height = 256;

    [Tooltip("噪声采样种子")]
    public int noiseSeed = 0;

    [Tooltip("编辑模式下是否允许自动更新")]
    public bool editModeRealtimeUpdate = true;

    [Tooltip("预览图缩放比例")]
    public float previewScale = 1f;

    [Header("像素风格设置")]
    [Tooltip("像素块大小，值越大棱角越分明")]
    [Range(1, 32)] public int pixelBlockSize = 4;

    [Tooltip("阈值过滤 - 小于此值的像素将被隐藏")]
    [Range(0f, 1f)] public float hideBelowThreshold = 0.3f;

    [Tooltip("阈值过滤 - 大于此值的像素将被隐藏")]
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

    // 订阅噪声设置的更新事件
    private void SubscribeToNoiseEvents()
    {
        UnsubscribeFromNoiseEvents(); // 先取消旧订阅

        if (settings != null)
        {
           settings.UpdateEvent.Clear();
           settings.UpdateEvent +=(OnNoiseSettingsUpdated);
        }
    }

    // 取消订阅噪声设置的更新事件
    private void UnsubscribeFromNoiseEvents()
    {
        if (settings != null)
        {
            settings.UpdateEvent += (OnNoiseSettingsUpdated);
        }
    }

    // 当噪声设置更新时触发
    private void OnNoiseSettingsUpdated()
    {
        if (CanUpdate())
        {
            GenerateNoiseTexture();
        }
    }

    // 检查是否可以更新预览
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
            Debug.LogWarning("自动添加了SpriteRenderer组件", this);
        }

        SetupTransparentMaterial();
        CreateTextureIfNeeded();

        isInitialized = true;
    }

    // 修改SetupTransparentMaterial方法，替换material为sharedMaterial
    private void SetupTransparentMaterial()
    {
        if (transparentMaterial == null)
        {
            Shader defaultShader = Shader.Find("Sprites/Default");
            if (defaultShader != null)
            {
                // 关键修复：使用sharedMaterial避免创建实例
                if (targetRenderer.sharedMaterial == null || targetRenderer.sharedMaterial.shader != defaultShader)
                {
                    // 尝试查找已存在的共享材质，避免重复创建
                    transparentMaterial = Resources.FindObjectsOfTypeAll<Material>()
                        .FirstOrDefault(m => m.shader == defaultShader && m.hideFlags == HideFlags.None);

                    // 如果没有找到则创建新材质
                    if (transparentMaterial == null)
                    {
                        transparentMaterial = new Material(defaultShader);
                        transparentMaterial.name = "NoisePreview_SharedMaterial";
                    }

                    // 编辑模式下标记为不保存，避免污染资源
                    if (!Application.isPlaying)
                    {
                        transparentMaterial.hideFlags = HideFlags.DontSaveInEditor;
                    }

                    // 使用sharedMaterial赋值
                    targetRenderer.sharedMaterial = transparentMaterial;
                }
                else
                {
                    transparentMaterial = targetRenderer.sharedMaterial;
                }
            }
            else
            {
                Debug.LogWarning("找不到默认shader，请确保存在'Sprites/Default' shader", this);
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

    // 同时修改CleanupResources中的材质清理逻辑
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

        // 编辑模式下不销毁共享材质，避免影响其他实例
        if (transparentMaterial != null && !Application.isPlaying)
        {
            transparentMaterial = null; // 仅解除引用，不销毁
        }
        // 运行时仍需要销毁
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

    [ContextMenu("刷新噪声预览")]
    public void RefreshPreview()
    {
        GenerateNoiseTexture();
    }

    private void OnValidate()
    {
        // 基础参数范围限制不变
        width = Mathf.Clamp(width, 16, 2048);
        height = Mathf.Clamp(height, 16, 2048);
        previewScale = Mathf.Max(0.1f, previewScale);
        pixelBlockSize = Mathf.Clamp(pixelBlockSize, 1, 32);

        // 确保阈值逻辑正确（下阈值不大于上阈值）
        if (hideBelowThreshold > hideAboveThreshold)
        {
            (hideBelowThreshold, hideAboveThreshold) = (hideAboveThreshold, hideBelowThreshold);
        }

        // 关键修改：去掉延迟，直接即时更新（加安全检查避免报错）
        if (!Application.isPlaying && editModeRealtimeUpdate && isInitialized && noiseTexture != null)
        {
            // 直接调用生成逻辑，无需延迟
            GenerateNoiseTexture();
        }
        // 若未初始化，先初始化再更新（避免首次参数变化时无响应）
        else if (!Application.isPlaying && editModeRealtimeUpdate && !isInitialized)
        {
            InitializeComponents();
            if (noiseTexture != null)
            {
                GenerateNoiseTexture();
            }
        }
    }

    // 当噪声设置资产更换时重新订阅事件
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
    // 在类的公共变量区域添加：
    [Header("辅助线设置")]
    [Tooltip("场景中预览范围框的颜色")]
    public Color gizmosColor = new Color(1, 1, 1, 0.3f); // 青色半透明默认值

    // 修改OnDrawGizmos方法：
    private void OnDrawGizmos()
    {
        Gizmos.color = gizmosColor; // 使用公开可配置的颜色
        Vector3 size = new Vector3(width / 100f * previewScale, height / 100f * previewScale, 0.1f);
        Gizmos.DrawCube(transform.position, size);
    }

}
