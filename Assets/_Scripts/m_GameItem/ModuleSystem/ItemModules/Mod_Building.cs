using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using static Mod_Building;

public class Mod_Building : Module
{
    #region ���ݶ���
    [Serializable]
    public class Building_Data
    {
        public float Hp = -1f;
        public GameValue_float MaxHp = new(100f);
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;
    }
    #endregion

    #region �����ֶ�
    public Building_Data Data = new Building_Data();
    public Ex_ModData BuildingData;
    public BuildingShadow GhostShadow;
    public BoxCollider2D boxCollider2D;
    public DamageReceiver damageReceiver;
    #endregion

    #region ����
    public override ModuleData _Data
    {
        get => BuildingData;
        set => BuildingData = (Ex_ModData)value;
    }

    public bool IsItemInInventory => item.BelongItem != null;
    #endregion

    #region ��������
    public override void Load()
    {
        BuildingData.ReadData(ref Data);
        boxCollider2D = item.GetComponentInChildren<BoxCollider2D>();
        damageReceiver = (DamageReceiver)item.itemMods.GetMod_ByID(ModText.Hp);
        damageReceiver.OnAction += OnHit;
        item.OnAct += Install;
    }

    public override void Save()
    {
        BuildingData.WriteData( Data);
        item.itemData.ModuleDataDic[_Data.Name] = BuildingData;
    }

    public override void Action(float deltaTime)
    {
        if (item == null)
        {
            Debug.LogWarning("item ��δ��ʼ����");
            return;
        }
        // ֻ�����������ʱ����ʾ����ͶӰ
        if (IsItemInInventory)
        {
            // ����Ѿ���װ��ɣ���������ͶӰ
            if (IsInstalled())
            {
                CleanupGhost();
                return;
            }

            // �����δ��װ��������ʾ����ͶӰ
            HandleGhostShadow();
        }
        else
        {
            // �����������ʱ��������ͶӰ
            CleanupGhost();
        }
    }

    public void OnDestroy()
    {
        CleanupGhost();
        Debug.Log($"[BaseBuilding] ��������٣�����GhostShadow");

        if (item != null)
            item.OnAct -= Install;
    }
    #endregion

    #region �˺�����
    private void OnHit(float hp)
    {
        Data.Hp = hp;
        if (hp <= 0)
        {
            UnInstall();
        }
        Debug.Log("�˺���" + hp);
    }
    #endregion

    #region ������װ/ж��
    [Button]
    public virtual void Install()
    {
        // ֻ�����������ʱ���ܰ�װ
        if (!IsItemInInventory)
        {
            Debug.LogWarning($"[������װ] ��װʧ��: ��Ʒ�����������");
            return;
        }

        if (!CanInstall())
            return;

        ExecuteInstallation();
    }

    [Button]
    public virtual void UnInstall()
    {
        item.transform.localScale *= 0.5f;

        if (boxCollider2D != null)
            boxCollider2D.isTrigger = true;

        if (item.itemData != null)
        {
            item.itemData.Stack.CanBePickedUp = true;
        }

        Data.Hp = 0;

        Vector2 pos = (Vector2)item.transform.position;
        ItemMaker itemMaker = new ItemMaker();
        itemMaker.DropItemWithAnimation(
            item.transform,
            item.transform.position,
            pos + (UnityEngine.Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();
    }
    #endregion

    #region ��װ��֤
    private bool CanInstall()
    {
        // 1. �������ͶӰ
        if (GhostShadow == null)
        {
            Debug.LogError($"[������װ] ��װʧ��: ����ͶӰ���󲻴��� (����λ��: {item.transform.position})");
            return false;
        }

        // 2. �����Χ�ϰ���
        if (GhostShadow.AroundHaveGameObject)
        {
            string obstacleInfo = GhostShadow.obstacleCollider != null ?
                $"{GhostShadow.obstacleCollider.gameObject.name} (λ��: {GhostShadow.obstacleCollider.transform.position})" :
                "δ֪��ײ��";

            Debug.LogWarning($"[������װ] ��װʧ��: ��⵽�ϰ��� - {obstacleInfo}");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.red, 5f);
            return false;
        }

        // 3. ����������
        float distance = Vector2.Distance(item.transform.position, GhostShadow.transform.position);
        if (distance > Data.maxVisibleDistance)
        {
            Debug.LogWarning($"[������װ] ��װʧ��: ���볬������ {distance:F2}m (�������: {Data.maxVisibleDistance:F2}m)");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.yellow, 5f);
            return false;
        }

        // 4. �����Ʒ����
        if (item.itemData.Stack.Amount <= 0)
        {
            Debug.LogError($"[������װ] ��װʧ��: ��Ʒ�������� (��ǰ: {item.itemData.Stack.Amount})");
            return false;
        }

        return true;
    }

    private void ExecuteInstallation()
    {
        // ������Ʒ
        item.itemData.Stack.Amount--;

        // ����Ѫ��
        Data.Hp = Data.MaxHp.Value;
        damageReceiver.Hp = damageReceiver.MaxHp.Value;

        // ������Ʒ״̬
        item.itemData.Stack.CanBePickedUp = false;
        item.OnUIRefresh?.Invoke();

        // ʵ��������
        var runtimeItem = GameItemManager.Instance.InstantiateItem(
            item.itemData.IDName,
            GhostShadow.transform.position
        );

        if (runtimeItem != null)
        {
            // ������ʵ��
            runtimeItem.transform.localScale = Vector3.one;
            runtimeItem.itemData = FastCloner.FastCloner.DeepClone(runtimeItem.itemData);
            runtimeItem.itemData.Stack.Amount = 1;
            runtimeItem.itemData.Stack.CanBePickedUp = false;
            EnableChildColliders(true, runtimeItem.transform);
        }

        // ������Ʒ�ľ�
        if (item.itemData.Stack.Amount <= 0)
        {
            CleanupGhost();
            Destroy(item.transform.gameObject);
        }
    }
    #endregion

    #region ��������
    private void HandleGhostShadow()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;

        // ���� Shadow ʵ������������ڣ�
        if (GhostShadow == null)
        {
            CreateGhostShadow();
        }

        if (GhostShadow == null) return;

        // ������Ӱ͸������λ��
        float distance = Vector2.Distance(item.transform.position, mouseWorldPos);
        float alpha = Mathf.InverseLerp(Data.maxVisibleDistance, Data.minVisibleDistance, distance);
        alpha = Mathf.Clamp01(alpha);

        GhostShadow.UpdateAlpha(alpha);

        if (GhostShadow.ShadowRenderer != null && GhostShadow.ShadowRenderer.enabled)
        {
            GhostShadow.SmoothMove(mouseWorldPos);
        }
        else
        {
            Debug.LogWarning("[Shadow����] ShadowRenderer δ����");
        }

        GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
    }

    private void CreateGhostShadow()
    {
        GameObject shadowPrefab = null;
        try
        {
            shadowPrefab = GameRes.Instance.InstantiatePrefab("BuildingShadow");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Shadow����] ʵ����Ԥ����ʧ��: {ex.Message}");
            return;
        }

        if (shadowPrefab == null)
        {
            Debug.LogError("[Shadow����] �޷�ʵ����BuildingShadowԤ����");
            return;
        }

        GhostShadow = shadowPrefab.GetComponent<BuildingShadow>();
        if (GhostShadow == null)
        {
            Debug.LogError("[Shadow����] BuildingShadowԤ����ȱ��BuildingShadow���");
            Destroy(shadowPrefab);
            return;
        }

        if (item.Sprite != null)
        {
            GhostShadow.InitShadow(item.Sprite);
        }
        else
        {
            Debug.LogError("[Shadow����] hostRendererΪ�գ��޷���ʼ����Ӱ");
        }
    }

    protected void EnableChildColliders(bool enable, Transform root = null)
    {
        root = root ?? transform;
        foreach (var col in root.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = enable;
        }
        root.GetComponent<BoxCollider2D>().isTrigger = false;
    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }

    public bool IsInstalled()
    {
        // ��Ѫ������0ʱ����ʾ�����Ѿ��ɹ���װ
        return damageReceiver.Hp > 0;
    }
    #endregion
}