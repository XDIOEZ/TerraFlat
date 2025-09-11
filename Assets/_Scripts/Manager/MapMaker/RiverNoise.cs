using UltEvents;
using UnityEngine;

[CreateAssetMenu(fileName = "TriFractalRiverNoise", menuName = "Noise/TriFractalRiverNoise")]
public class TriFractalRiverNoise : BaseNoise
{
    [Header("���β���")]
    [Tooltip("����������Խ��ϸ��Խ�࣬�����ܸ��")]
    [Range(1, 12)]
    public int octaves = 5;

    [Tooltip("ÿ�� octave Ƶ�ʱ���")]
    public float lacunarity = 2f;

    [Tooltip("ÿ�� octave ����˥�������� 0.3 - 0.8��")]
    [Range(0.0f, 1.0f)]
    public float gain = 0.5f;

    [Header("������������")]
    [Tooltip("������ֵ������ֵ������ֵ��������γɺӴ���0..1��")]
    [Range(0f, 1f)]
    public float threshold = 0.45f;

    [Tooltip("�ӵ���ȣ���Χ�ڵķ���ֵ�ὥ��Ϊ��������ԽСԽϸխ")]
    [Range(0.01f, 0.5f)]
    public float riverWidth = 0.08f;

    [Tooltip("�Ƿ�ת����Ϊ true ʱ������ֵԶ����ֵ�ĵط�Ϊ�ӵ�")]
    public bool invertBand = false;

    [Header("��½��������������ѡ��")]
    [Tooltip("��ѡ���������½������ ScriptableObject�����ڸ���½���������ں������ɣ���������ԣ�")]
    public BaseNoise landNoise = null;

    [Tooltip("½������Ӱ��ǿ�ȣ�>0 ������ֵ��<0 ������ֵ��")]
    [Range(-1f, 1f)]
    public float landInfluence = 0.25f;

    [Tooltip("��ʹ�� landNoise ʱ�����ô�ƫ�Ʋ���½�������������뱾������ȫ�غ�")]
    public Vector2 landNoiseOffset = new Vector2(1000f, 1000f);

    /// <summary>
    /// ����������������������� [0,1] ֵ
    /// �����߼���
    /// 1. �� ridged / triangle ���� fBm��ÿ octave ȡ Perlin -> ridge transform��
    /// 2. ��һ�� fBm �� [0,1]
    /// 3. �� smoothstep �� threshold �� riverWidth/2 ���乹����������֧�ַ�ת��
    /// 4. ���ṩ landNoise������ landNoise ���� threshold���Ӷ��ڲ�ͬ�����ϸı�����ܶ�/����
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // ��֤ seed �� 0~9999

        // �� seed ת��ƫ�ƣ�����ֱ�Ӱ� seed �ӵ�Ƶ���ϵ����ظ�ģʽ������Ȼ��
        float seedX = seed * 100.13f + seedOffset * 10.7f;
        float seedY = seed * 73.21f - seedOffset * 7.33f;

        // 1) tri / ridged fBm
        float amplitude = 1f;
        float frequencyLocal = frequency;
        float sum = 0f;
        float maxPossible = 0f; // ���ڹ�һ��
        for (int i = 0; i < Mathf.Max(1, octaves); i++)
        {
            float sx = (x + seedX) * frequencyLocal;
            float sy = (y + seedY) * frequencyLocal;
            float n = Mathf.PerlinNoise(sx, sy); // [0,1]

            // ridge transform (��������ɼ⼹/������״)
            // �Ȱ� [0,1] ӳ��Ϊ [-1,1]����ȡ����ֵ����ת��ʹ�á���/����������
            float ridge = 1f - Mathf.Abs(2f * n - 1f); // [0,1], �����γɷ�
            // ��ѡ���⴦��̧��Сֵ�Ա�����ȫΪ0�����ﲻ����
            sum += ridge * amplitude;

            maxPossible += amplitude;
            amplitude *= gain;
            frequencyLocal *= lacunarity;
        }

        // 2) ��һ�� fBm �� [0,1]
        float fbm = (maxPossible > 0f) ? (sum / maxPossible) : 0f;
        fbm = Mathf.Clamp01(fbm);

        // 3) �� landNoise ������ֵ����ѡ��
        float appliedThreshold = threshold;
        if (landNoise != null)
        {
            // ����½�����������Լ��� seed + offset��
            float landSample = landNoise.Sample(x + landNoiseOffset.x, y + landNoiseOffset.y, seed);
            // landSample �� [0,1]�����Ǹ��������ƶ���ֵ���������Խ��/Խ½�أ����ܸ������ٵĺ���
            // ��ʽ��appliedThreshold = threshold + (1 - landSample) * landInfluence
            // ����Ը�����Ҫ��ת landSample ��Ӱ��
            appliedThreshold = threshold + (1f - landSample) * landInfluence;
            appliedThreshold = Mathf.Clamp01(appliedThreshold);
        }

        // 4) ����������fbm ������ֵԽ�������Խ��
        float halfWidth = Mathf.Max(0.0001f, riverWidth * 0.5f);
        float lower = appliedThreshold - halfWidth;
        float upper = appliedThreshold + halfWidth;

        // ʹ�� smoothstep ʵ��ƽ����״��Unity û������ smoothstep����д��
        float t;
        if (!invertBand)
        {
            // �߶��� [lower,upper] ʱΪ 1��������ƽ�����ɵ� 0
            t = SmoothStep(lower, appliedThreshold, fbm) * (1f - SmoothStep(appliedThreshold, upper, fbm));
            // ������ʽ�������ֵ ~1 �� appliedThreshold ��������˥������Ϊ�˸��򵥺��ȶ����Ǹ��þ��뷨��
            float dist = Mathf.Abs(fbm - appliedThreshold);
            // dist 0 -> 0, ��� -> 1 -> ������Ҫ���������� dist small -> 1
            float normalizedDist = Mathf.InverseLerp(halfWidth, 0f, dist); // halfWidth->0 maps to 0->1
            t = Mathf.Clamp01(normalizedDist);
        }
        else
        {
            // ��ת��Զ����ֵ��Ϊ�ӵ�
            float dist = Mathf.Abs(fbm - appliedThreshold);
            float normalized = Mathf.InverseLerp(0f, halfWidth, dist); // 0->halfWidth maps to 0->1
            t = Mathf.Clamp01(normalized);
        }

        // ������ clamp ������
        return Mathf.Clamp01(t);
    }

    /// <summary>
    /// �� GLSL/Shader �� smoothstep ͬ����Ϊ��
    /// smoothstep(edge0, edge1, x) ƽ����ֵ��x<=edge0 ����0��x>=edge1 ����1���м�ƽ��
    /// </summary>
    private float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge0 >= edge1) return (x < edge0) ? 0f : 1f;
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        // Hermite interpolation
        return t * t * (3f - 2f * t);
    }
}
