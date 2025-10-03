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

    public override ModuleData _Data { get => MemoryPackableData; set => MemoryPackableData = (Ex_ModData_MemoryPackable)value; }
    public Ex_ModData_MemoryPackable MemoryPackableData;
    
    // ��Ӷ���ײ�������
    [SerializeField] private Collider2D damageCollider;
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
        
        // Ĭ�Ͻ�����ײ��
        if (damageCollider != null)
        {
            damageCollider.enabled = false;
        }
    }

    public override void Save()
    {
        // �����߼����Ժ���ʵ��
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
        
        // ����˺�
        float acDamage = receiver.Hurt(this);
        
        // ���ɹ�����Ч
        if (AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = other.ClosestPoint(transform.position);
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
        }
        else
        {
            // �����û�л�ȡ����ײ�壬���Ի�ȡ
            damageCollider = GetComponent<Collider2D>();
            if (damageCollider != null)
            {
                damageCollider.enabled = enabled;
            }
            else
            {
                Debug.LogWarning("[Mod_Damage] δ�ҵ�Collider2D������޷������˺����״̬");
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
    }
    public void StopAttack()
    {
        SetDamageEnabled(false);
    }

    #endregion
}