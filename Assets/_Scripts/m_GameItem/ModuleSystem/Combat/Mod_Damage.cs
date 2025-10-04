// �˺�ģ��Ӧ�ù��������
using AYellowpaper.SerializedCollections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Damage : Module, IDamageSender
{
    #region �˺��������
    [Header("������Ч")]
    public List<GameEffect> AttackEffects = new List<GameEffect>();
    
    public SerializedDictionary<DamageTag, float> Weakness = new SerializedDictionary<DamageTag, float>();
    public GameValue_float Damage = new GameValue_float(10f);

    [Header("��ʱ�˺�����")]
    [Tooltip("�˺����ʱ�䣨�룩\n-1: ��Զ������\n0: ÿ֡����˺�\n>0: ÿ�����������˺�")]
    public float DamageInterval = -1f;
    [Tooltip("�Ƿ����ô���������ʱ���˺��߼���Ĭ��Ϊtrue��")]
    public bool EnableOnTriggerEnterDamage = true;

    [Header("������Ϣ")]
    [SerializeField] private bool showDebugWarnings = true;
    [SerializeField] private Collider2D damageCollider;
    
    // ��ʱ�˺����
    private float lastDamageTime = 0f;
    private List<DamageReceiver> insideReceivers = new List<DamageReceiver>();
    
    // ʵ��ModuleData����
    public override ModuleData _Data 
    { 
        get => MemoryPackableData; 
        set => MemoryPackableData = (Ex_ModData_MemoryPackable)value; 
    }
    public Ex_ModData_MemoryPackable MemoryPackableData;
    #endregion

    #region IDamageSender ʵ��
    Item IDamageSender.attacker { get => item; set => item = value; }
    SerializedDictionary<DamageTag, float> IDamageSender.Weakness { get => Weakness; set => Weakness = value; }
    GameValue_float IDamageSender.Damage { get => Damage; set => Damage = value; }
    #endregion

    #region Unity ��������
    public override void Load()
    {
        // ��ʼ��ʱ���Ի�ȡ��ײ�����
        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
        }
        
        
        // ��ʼ����ʱ�˺��������
        lastDamageTime = 0f;
        insideReceivers.Clear();
    }

    public override void Save()
    {
        // �����߼����Ժ���ʵ��
    }
    
    public override void ModUpdate(float deltaTime)
    {
        // ����ʱ�˺��߼�
        if (DamageInterval >= 0 && damageCollider != null && damageCollider.enabled)
        {
            // ����Ƿ�������˺���ʱ��
            if (DamageInterval == 0 || Time.time - lastDamageTime >= DamageInterval)
            {
                ApplyDamageToInsideReceivers();
                lastDamageTime = Time.time;
            }
        }
    }
    #endregion

    #region �˺�����
    public void OnTriggerEnter2D(Collider2D other)
    {
        // ��ײ�����˺������߼�
        if (damageCollider == null || !damageCollider.enabled) return;
        if (!other.TryGetComponent(out DamageReceiver receiver)) return;
        
        // �Ѿ����
        var beAttackTeam = other.GetComponentInParent<ITeam>();
        var belongItem = item?.Owner;
        var belongTeam = belongItem?.GetComponent<ITeam>();
        
        if (beAttackTeam != null && belongTeam != null && 
            belongTeam.CheckRelation(beAttackTeam.TeamID) == RelationType.Ally)
            return;
        
        // ��ӵ��ڲ��������б�
        if (!insideReceivers.Contains(receiver))
        {
            insideReceivers.Add(receiver);
        }
        
        // ��������˽���ʱ�˺�������������˺�
        if (EnableOnTriggerEnterDamage)
        {
            ApplyDamageToReceiver(receiver);
        }
    }
    
    public void OnTriggerExit2D(Collider2D other)
    {
        // ���ڲ��������б����Ƴ�
        if (other.TryGetComponent(out DamageReceiver receiver))
        {
            insideReceivers.Remove(receiver);
        }
    }

    private void ApplyDamageToInsideReceivers()
    {
        // ����������ײ���ڵĽ���������˺�
        for (int i = insideReceivers.Count - 1; i >= 0; i--)
        {
            if (insideReceivers[i] != null)
            {
                ApplyDamageToReceiver(insideReceivers[i]);
            }
            else
            {
                // �Ƴ������ٵĽ�����
                insideReceivers.RemoveAt(i);
            }
        }
    }
    
    private void ApplyDamageToReceiver(DamageReceiver receiver)
    {
        // ����˺�
        float acDamage = receiver.Hurt(this);
        
        // ���ɹ�����Ч
        if (AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = receiver.GetComponent<Collider2D>().ClosestPoint(transform.position);
            SpawnEffect(hitPoint, acDamage);
        }
    }

    private void SpawnEffect(Vector2 hitPoint, float damage)
    {
        // ��Ч�����߼�
        foreach (GameEffect effectPrefab in AttackEffects)
        {
            if (effectPrefab != null)
            {
                var effect = Instantiate(effectPrefab);
                effect.transform.position = new Vector3(hitPoint.x, hitPoint.y, 0f);
                effect.Effect(transform, damage);
            }
        }
    }
    #endregion

    #region ���������������˺�����/����
    /// <summary>
    /// �����˺���������״̬
    /// </summary>
    /// <param name="enabled">�Ƿ������˺����</param>
    public void SetDamageEnabled(bool enabled)
    {
        if (damageCollider != null)
        {
            damageCollider.enabled = enabled;
            damageCollider.isTrigger = true; // ȷ���Ǵ�����
            if (!enabled)
            {
                // ����ʱ����ڲ��������б�
                insideReceivers.Clear();
            }
        }
        else
        {
            // �����û�л�ȡ����ײ�壬���Ի�ȡ
            damageCollider = GetComponent<Collider2D>();
            if (damageCollider != null)
            {
                damageCollider.enabled = enabled;
                damageCollider.isTrigger = true; // ȷ���Ǵ�����
                if (!enabled)
                {
                    // ����ʱ����ڲ��������б�
                    insideReceivers.Clear();
                }
            }
            else if (showDebugWarnings)
            {
                Debug.LogWarning($"[{name}] δ�ҵ�Collider2D������޷������˺����״̬", this);
            }
        }
    }
    
    /// <summary>
    /// ��ȡ��ǰ�˺����״̬
    /// </summary>
    /// <returns>�˺�����Ƿ�����</returns>
    public bool IsDamageEnabled()
    {
        return damageCollider != null && damageCollider.enabled;
    }

    public void StartAttack()
    {
        SetDamageEnabled(true);
        lastDamageTime = Time.time; // �����˺���ʱ
    }
    public void StopAttack()
    {
        SetDamageEnabled(false);
    }

    #endregion
}