using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DamageTextEffect : GameEffect
{
    [Header("�ı�����")]
    public TMP_Text TMP_text;
    public float moveSpeed = 1.0f;          // �����ٶ�
    public float fadeDuration = 1.0f;       // ��������ʱ��
    public float randomRange = 0.5f;        // ���ɢ����Χ
    public Vector2 moveDirection = Vector2.up; // �����ƶ�����
    
    private float lifetime = 0f;
    private Color originalColor;
    private Vector3 targetPosition;
    private Vector3 randomOffset;

    public override void Effect(Transform Sender, object Data = null)
    {
        if (TMP_text == null)
        {
            TMP_text = GetComponentInChildren<TMP_Text>();
            if (TMP_text == null)
            {
                Debug.LogError("DamageTextEffect: TMP_Text���δ�ҵ���");
                Destroy(gameObject);
                return;
            }
        }
        
        // �����˺���ֵ
        if (Data != null)
        {
            float data = System.Convert.ToSingle(Data);
            TMP_text.text = data.ToString("F0");
        }
        
        // ��¼��ʼλ�ú���ɫ
        originalColor = TMP_text.color;
        
        // �������ƫ��
        randomOffset = new Vector3(
            Random.Range(-randomRange, randomRange),
            Random.Range(-randomRange, randomRange),
            0f
        );
        
        // ����Ŀ��λ��
        targetPosition = Sender.position + (Vector3)moveDirection + randomOffset;
        
        // ��������Э��
        StartCoroutine(AnimateText());
    }

    private IEnumerator AnimateText()
    {
        Vector3 startPosition = transform.position;
        
        while (lifetime < fadeDuration)
        {
            lifetime += Time.deltaTime;
            
            // �����ƶ�����
            float moveProgress = lifetime * moveSpeed;
            
            // ����λ�ã����Բ�ֵ��
            transform.position = Vector3.Lerp(startPosition, targetPosition, moveProgress);
            
            // ����͸���ȣ����Ե�����
            float alpha = Mathf.Lerp(originalColor.a, 0f, lifetime / fadeDuration);
            TMP_text.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            
            yield return null;
        }
        
        // �������������ٶ���
        Destroy(gameObject);
    }
}