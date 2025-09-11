using UltEvents;
using UnityEngine;

public abstract class BaseNoise : ScriptableObject
{
    [Tooltip("��������Ƶ��")]
    public float frequency = 0.01f;

    [Tooltip("�������ƫ��")]
    public int seedOffset = 0;

    public UltEvent UpdateEvent = new UltEvent();

    public BiomeData biomeData;

    /// <summary>
    /// ����������������������� [0,1] ֵ
    /// </summary>
    public abstract float Sample(float x, float y, int seed);

    // ��SO������Inspector�б仯ʱ����
    [ContextMenu("Force Update")]
    private void Update()
    {
         UpdateEvent.Invoke();
    }

    // ��SO������ʱ����
    protected virtual void OnEnable()
    {
        // ��ֹδ��ʼ�������
        if (UpdateEvent == null)
            UpdateEvent = new UltEvent();
    }
}
