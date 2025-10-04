using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Laser_Skill : MonoBehaviour
{
    [Header("����ʱ����")]
    public RuntimeSkill runtimeSkill;
    
    [Header("�������")]
    public LineRenderer lineRenderer;
    public Transform laserEffect;
    public BoxCollider2D laserCollider; // 2D������ײ��
    public List<Module> mods = new List<Module>();
    
    [Header("Ԥ��������")]
    public GameObject laserEffectPrefab;
    public Vector3 startPoint;
    
    // Start is called before the first frame update
    void Start()
    {
     
        if (runtimeSkill != null)
        {
            startPoint = runtimeSkill.skillManager.transform.position + (Vector3)runtimeSkill.skillManager.castingPointOffset["Laser"];      // ��ʼ��������
            if (lineRenderer != null)
            {
                lineRenderer.SetPosition(0, startPoint);
                lineRenderer.SetPosition(1, new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, runtimeSkill.skillSender.transform.position.z));
            }
            
            // ʵ������������Чλ��
            if (laserEffectPrefab != null)
            {
                laserEffect = Instantiate(laserEffectPrefab).transform;
                laserEffect.position = new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, transform.position.z);
            }
            
            // ��ȡ�Ӷ����ϵ�BoxCollider2D���
            laserCollider = GetComponentInChildren<BoxCollider2D>();
            
            // ��ȡ�����Ӷ����ϵ�Module���
            mods = new List<Module>(GetComponentsInChildren<Module>());
            
            // ��������ģ��
            foreach (var mod in mods)
            {
                mod.Load();
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        // ���skillSender�Ƿ���ڣ���������������ټ���
        if (runtimeSkill == null || runtimeSkill.skillSender == null)
        {
            Destroy(gameObject);
            return;
        }
        
        // ��������ģ��
        foreach (var mod in mods)
        {
            mod.ModUpdate(Time.deltaTime);
        }
        
        // ��targetPoint�����ø�Ϊ runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point
        if (runtimeSkill != null)
        {
            // ��ȡʵʱ��Ŀ���
            Vector2 currentTargetPoint = runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point;
        startPoint = runtimeSkill.skillManager.transform.position + (Vector3)runtimeSkill.skillManager.castingPointOffset["Laser"];      // ��ʼ��������

            // ���¼����������յ�
            if (lineRenderer != null)
            {
                lineRenderer.SetPosition(0, startPoint);
                lineRenderer.SetPosition(1, new Vector3(currentTargetPoint.x, currentTargetPoint.y, runtimeSkill.skillSender.transform.position.z));
            }
            
            // ������Чλ��
            if (laserEffect != null)
            {
                laserEffect.position = new Vector3(currentTargetPoint.x, currentTargetPoint.y, laserEffect.position.z);
            }
            
            // ������ײ���Ĵ�С����ת��ƥ�伤����
            if (laserCollider != null)
            {
                UpdateLaserCollider(currentTargetPoint);
            }
            
            // ���½���
            runtimeSkill.progress += Time.deltaTime;
        }
    }
    
    // ���¼�����ײ���Ĵ�С����ת
    private void UpdateLaserCollider(Vector2 targetPoint)
    {
        Vector2 startPoint = runtimeSkill.skillSender.transform.position;
        Vector2 endPoint = targetPoint;
        
        // ���㼤���ߵ����ĵ�
        Vector2 center = (startPoint + endPoint) * 0.5f;
        
        // ���㼤���ߵĳ���
        float length = Vector2.Distance(startPoint, endPoint);
        
        // ������ײ����λ�õ����ĵ�
        laserCollider.transform.position = new Vector3(center.x, center.y, laserCollider.transform.position.z);
        
        // ������ײ���Ĵ�С�����輤������һ����ȣ�
        float lineWidth = lineRenderer != null ? lineRenderer.startWidth : 0.1f;
        laserCollider.size = new Vector2(length, lineWidth);
        
        // ���㲢������ײ������ת
        Vector2 direction = endPoint - startPoint;
        if (direction != Vector2.zero)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            laserCollider.transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }
    
    // ����ʱ����
    private void OnDestroy()
    {
        // ���skillSender�Ƿ���ڣ���������򱣴�ģ��
        if (runtimeSkill != null && runtimeSkill.skillSender != null)
        {
            if (laserEffect != null)
            {
                // ��������ģ��
                foreach (var mod in mods)
                {
                    mod.Save();
                }
                Destroy(laserEffect.gameObject);
            }
        }
        else
        {
            // ���skillSender�����ڣ�ֱ��������Ч
            if (laserEffect != null)
            {
                Destroy(laserEffect.gameObject);
            }
        }
    }
}