using UnityEngine;

[CreateAssetMenu(fileName = "LandNoise", menuName = "Noise/LandNoise")]
public class LandNoise : BaseNoise
{
    [Header("�������ӵ�����")]
    [Tooltip("���ӵ�ǿ�ȣ�ֵԽ�ߺӵ�Խ����")]
    [Range(0.1f, 5f)] public float mainRiverStrength = 2f;

    [Tooltip("���ӵ�ƽ���ȣ�ֵԽ�ߺӵ�Խ��������")]
    [Range(1, 8)] public int mainSmoothness = 3;

    [Header("֧������")]
    [Tooltip("�Ƿ�����֧��")]
    public bool enableTributaries = true;

    [Tooltip("֧��ǿ�ȣ�ֵԽ��֧��Խ����")]
    [Range(0.1f, 2f)] public float tributaryStrength = 0.8f;

    [Tooltip("֧��������ֵԽ��֧��Խ��")]
    [Range(1, 4)] public int tributaryCount = 2;

    [Header("�ӵ���ȿ���")]
    [Tooltip("�����ӵ����")]
    [Range(0.5f, 5f)] public float baseWidth = 1.5f;

    [Tooltip("��ȱ仯�̶ȣ�ֵԽ�߿�ȱ仯Խ����")]
    [Range(0f, 1f)] public float widthVariation = 0.3f;

    /// <summary>
    /// �����ʺϺ���������ֵ����ֵ�����ʾ����
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // ��֤ seed �� 0~9999

        // ����λ�ü��㣨�������ƫ�ƣ�
        float baseX = x * frequency + (seed + seedOffset) * 0.123f;
        float baseY = y * frequency + (seed + seedOffset) * 0.321f;

        // �������ӵ�������ʹ�ö���˶ȵ���ʵ������Ч����
        float mainRiverNoise = 0;
        float mainAmplitude = 1;
        float mainFrequency = 1;

        for (int i = 0; i < mainSmoothness; i++)
        {
            float sampleX = baseX * mainFrequency;
            float sampleY = baseY * mainFrequency;

            // ʹ��Perlin�������ɻ����ӵ���״
            mainRiverNoise += Mathf.PerlinNoise(sampleX, sampleY) * mainAmplitude;

            mainAmplitude *= 0.5f;
            mainFrequency *= 2f;
        }

        // ��һ�����ӵ�����
        mainRiverNoise = mainRiverNoise * 0.666f; // �򵥹�һ������˶ȵ��ܺ�
        mainRiverNoise = 1 - (mainRiverNoise * mainRiverStrength); // ��ת��Ӧ��ǿ��

        // ����֧������
        float tributaryNoise = 0;
        if (enableTributaries)
        {
            for (int i = 0; i < tributaryCount; i++)
            {
                // Ϊÿ��֧��ʹ�ò�ͬ��ƫ�ƣ�������ͬ��·��
                float offsetX = i * 123.45f;
                float offsetY = i * 543.21f;

                float tribX = baseX * (2 + i) + offsetX;
                float tribY = baseY * (2 + i) + offsetY;

                tributaryNoise += Mathf.PerlinNoise(tribX, tribY) * tributaryStrength;
            }

            // ��һ��֧������
            tributaryNoise /= tributaryCount;
            tributaryNoise = 1 - tributaryNoise; // ��תʹ��ֵ��ʾ����
        }

        // ������ӵ���֧����ȡ���ֵ������ܳ�Ϊ����������
        float combinedNoise = Mathf.Min(mainRiverNoise, 1 - tributaryNoise);

        // Ӧ�ÿ�ȱ仯
        float widthNoise = Mathf.PerlinNoise(baseX * 3, baseY * 3) * widthVariation;
        float widthFactor = baseWidth - widthNoise;

        // ��������ֵ��ʹ�ӵ�������ָ��͵�ֵ
        float riverNoise = combinedNoise * (1 + widthFactor);

        // ȷ�������[0,1]��Χ��
        return Mathf.Clamp01(riverNoise);
    }
}
