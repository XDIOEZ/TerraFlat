using UnityEngine;

// ����Unity�༭���д����������ʲ�
[CreateAssetMenu(fileName = "PerlinNoise", menuName = "Noise/Perlin Noise")]
public class PerlinNoise : BaseNoise
{
    [Tooltip("���� octave ������Ӱ��ϸ�ڷḻ��")]
    public int octaves = 4;

    [Tooltip(" octave ���Ƶ�ʱ���")]
    public float lacunarity = 2.0f;

    [Tooltip("octave ������˥��ϵ��")]
    public float persistence = 0.5f;

    /// <summary>
    /// ʵ��Perlin����������֧�ֶ�octave����
    /// </summary>
    public override float Sample(float x, float y, int seed)
    {
        seed = seed % 10000; // ��֤ seed �� 0~9999

        float total = 0;
        float amplitude = 1;
        float frequency = this.frequency;
        float maxValue = 0; // ���ڹ�һ�������[0,1]

        // ���Ӷ��octave��ǿϸ��
        for (int i = 0; i < octaves; i++)
        {
            // �������ƫ�ƺ�������ӣ�ʵ�ֲ�ͬ���ӵ������仯
            float sampleX = x * frequency + (seed + seedOffset) * 0.1f;
            float sampleY = y * frequency + (seed + seedOffset) * 0.1f;

            // Perlin����ԭ������ֵ��Χ��[0,1]
            float noiseValue = Mathf.PerlinNoise(sampleX, sampleY);
            total += noiseValue * amplitude;

            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        // ��һ�������[0,1]
        return total / maxValue;
    }
}