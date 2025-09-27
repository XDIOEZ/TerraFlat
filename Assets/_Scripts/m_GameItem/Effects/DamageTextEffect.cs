using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DamageTextEffect : GameEffect
{
    [Header("文本设置")]
    public TMP_Text TMP_text;
    public float moveSpeed = 1.0f;          // 上升速度
    public float fadeDuration = 1.0f;       // 淡出持续时间
    public float randomRange = 0.5f;        // 随机散开范围
    public Vector2 moveDirection = Vector2.up; // 基本移动方向
    
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
                Debug.LogError("DamageTextEffect: TMP_Text组件未找到！");
                Destroy(gameObject);
                return;
            }
        }
        
        // 设置伤害数值
        if (Data != null)
        {
            float data = System.Convert.ToSingle(Data);
            TMP_text.text = data.ToString("F0");
        }
        
        // 记录初始位置和颜色
        originalColor = TMP_text.color;
        
        // 计算随机偏移
        randomOffset = new Vector3(
            Random.Range(-randomRange, randomRange),
            Random.Range(-randomRange, randomRange),
            0f
        );
        
        // 计算目标位置
        targetPosition = Sender.position + (Vector3)moveDirection + randomOffset;
        
        // 启动动画协程
        StartCoroutine(AnimateText());
    }

    private IEnumerator AnimateText()
    {
        Vector3 startPosition = transform.position;
        
        while (lifetime < fadeDuration)
        {
            lifetime += Time.deltaTime;
            
            // 计算移动进度
            float moveProgress = lifetime * moveSpeed;
            
            // 更新位置（线性插值）
            transform.position = Vector3.Lerp(startPosition, targetPosition, moveProgress);
            
            // 计算透明度（线性淡出）
            float alpha = Mathf.Lerp(originalColor.a, 0f, lifetime / fadeDuration);
            TMP_text.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
            
            yield return null;
        }
        
        // 动画结束，销毁对象
        Destroy(gameObject);
    }
}