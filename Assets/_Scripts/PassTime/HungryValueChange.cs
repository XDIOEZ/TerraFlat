using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

public class HungryValueChange : MonoBehaviour, IFunction_ChangeHungryValue
{
    public float CurrentHungryValue = 100;
    public float MaxHungryValue = 100;
    public float GetMaxHungryValue()
    {
        return MaxHungryValue;
    }
    public float BaseHungryRate; // ��������������
    private float adjustedHungryRate; // ������ļ���������
    public float OnMoveHungryRate; // �ƶ�ʱ���������ʵļӳ�

    public void ChangeHungryValue(float deltaTime)
    {
        if (CanMoveItem != null)
        {
            
            // ���ݵ�����ļ��������ʼ��ټ���ֵ
            CurrentHungryValue -= deltaTime * adjustedHungryRate;

            // ȷ������ֵ�������0
            if (CurrentHungryValue < 0)
            {
                CurrentHungryValue = 0;
            }
        }
    }

    public void Update()
    {
        // ��������������
        AdjustHungryRateBasedOnSpeed();

        // ��������������ʸı伢��ֵ
        ChangeHungryValue(Time.deltaTime);
    }

    private void AdjustHungryRateBasedOnSpeed()
    {
        Rigidbody2D rb = GetComponentInParent<Rigidbody2D>();
        Debug.Log(rb);
        if (rb != null)
        {
            Vector2 velocity = rb.linearVelocity;
            float speed = velocity.magnitude;

            // �����ٶ�ÿ����1��λ����������������0.1��λ
            adjustedHungryRate = BaseHungryRate + (speed * OnMoveHungryRate);
        }
        else
        {
            adjustedHungryRate = BaseHungryRate;
        }
    }

    private ICan_ChangeHungryValue canChangeHungryValue;

    public ICan_ChangeHungryValue CanMoveItem
    {
        get
        {
            if (canChangeHungryValue == null)
            {
                canChangeHungryValue = GetComponentInParent<ICan_ChangeHungryValue>();
            }
            return canChangeHungryValue;
        }
        set => canChangeHungryValue = value;
    }

    void OnGUI()
    {
        // ����Ѫ���ı������Ⱥ͸߶�
        float healthBarHeight = 20f; // Ѫ���ĸ߶�
        Rect healthBarBackground = new Rect(0, Screen.height - healthBarHeight, Screen.width, healthBarHeight);

        // ����Ѫ���ı���������ʹ��Ĭ�ϵĻ�ɫ
        GUI.Box(healthBarBackground, "", GUI.skin.box);

        // ���ݵ�ǰ����ֵ����Ѫ���Ŀ���
        float healthBarWidth = healthBarBackground.width * (CurrentHungryValue / GetMaxHungryValue());

        // ����Ѫ���ľ�������
        Rect healthBar = new Rect(healthBarBackground.x, healthBarBackground.y, healthBarWidth, healthBarBackground.height);

        // ����GUI��ɫΪ��ɫ
        GUI.color = Color.yellow;

        // ����Ѫ��
        GUI.Box(healthBar, "");

        // ������ɫ������Ӱ�������GUI����
        GUI.color = Color.white;
    }

}
public interface ICan_ChangeHungryValue
{
    float GetHungryValue(); // ����ֵ�仯
    void SetHungryValue(float value); // ����ֵ�仯
}

public interface IFunction_ChangeHungryValue
{
    void ChangeHungryValue(float value); // ����ֵ�仯
}
