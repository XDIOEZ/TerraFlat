using Force.DeepCloner;
using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Mod_Building;

public class Mod_Building : Module
{
    public Building_Data _Data = new Building_Data();
    public Ex_ModData BuildingData;
    public override ModuleData Data { get => BuildingData; set => BuildingData = (Ex_ModData)value; }

    public BuildingShadow GhostShadow;
    public BoxCollider2D boxCollider2D;

    /*
        public float Hp = 100f;
        public GameValue_float MaxHp = new(100f);
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;*/

    [Serializable]
    public class Building_Data
    {
        public float Hp = -1f;
        public GameValue_float MaxHp = new(100f);
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;
    }
    public bool itemBeTake => item.BelongItem != null;

    public override void Load()
    {
        BuildingData.ReadData(ref _Data);

        boxCollider2D = item.GetComponentInChildren<BoxCollider2D>();
        item.Mods["�˺�ģ��"].OnAction += Hit;
        item.OnAct += Install;
    }

    public void Start()
    {
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }

    }

    public override void Save()
    {
        BuildingData.WriteData(_Data);
        item.Item_Data.ModuleDataDic[Data.Name] = Data;
    }

    public void Hit(float damage)
    {
        _Data.Hp -= damage;
        if (_Data.Hp <= 0)
        {
            UnInstall();
        }
        Debug.Log("�˺���" + damage);
    }
    [Button]
    public virtual void Install()
    {
        // 1. �������ͶӰ
        if (GhostShadow == null)
        {
            Debug.LogError($"[������װ] ��װʧ��: ����ͶӰ���󲻴��� (����λ��: {item.transform.position})");
            return;
        }

        // 2. �����Χ�ϰ���
        if (GhostShadow.AroundHaveGameObject)
        {
            string obstacleInfo = GhostShadow.obstacleCollider != null ?
                $"{GhostShadow.obstacleCollider.gameObject.name} (λ��: {GhostShadow.obstacleCollider.transform.position})" :
                "δ֪��ײ��";

            Debug.LogWarning($"[������װ] ��װʧ��: ��⵽�ϰ��� - {obstacleInfo}");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.red, 5f);
            return;
        }

        // 3. ����������
        float distance = Vector2.Distance(item.transform.position, GhostShadow.transform.position);
        if (distance > _Data.maxVisibleDistance)
        {
            Debug.LogWarning($"[������װ] ��װʧ��: ���볬������ {distance:F2}m (�������: {_Data.maxVisibleDistance:F2}m)");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.yellow, 5f);
            return;
        }

        // 4. �����Ʒ���
        if (item == null)
        {
            Debug.LogError("[������װ] ��װʧ��: ��Ʒ���δ����");
            return;
        }

        // 5. �����Ʒ����
        if (item.Item_Data.Stack.Amount <= 0)
        {
            Debug.LogError($"[������װ] ��װʧ��: ��Ʒ�������� (��ǰ: {item.Item_Data.Stack.Amount})");
            return;
        }

        // ��ʽ��װ����
        item.Item_Data.Stack.Amount--;

        _Data.Hp = _Data.MaxHp.Value;

        item.Item_Data.Stack.CanBePickedUp = false;

        if (item.UpdatedUI_Event != null)
        {
            item.UpdatedUI_Event.Invoke();
        }

        // ʵ��������
        var runtimeItem = RunTimeItemManager.Instance.InstantiateItem(
            item.Item_Data.IDName,
            GhostShadow.transform.position
        );

        if (runtimeItem == null || runtimeItem.gameObject == null)
        {
            Debug.LogError("[������װ] ��װʧ��: ʵ�������ؿն���");
            // �ع���Ʒ����
            item.Item_Data.Stack.Amount++;
            return;
        }

        SetupInstalledItem(runtimeItem.gameObject, item);

        // ������Ʒ�ľ����
        if (item.Item_Data.Stack.Amount <= 0)
        {
            CleanupGhost();
            Destroy(item.transform.gameObject);
        }

        Destroy(item.gameObject);
    }

    [Button]
    public virtual void UnInstall()
    {
        item.transform.localScale *= 0.5f;

        if (boxCollider2D != null) boxCollider2D.isTrigger = true;

        if (item.Item_Data != null)
        {
            item.Item_Data.Stack.CanBePickedUp = true;
        }

        _Data.Hp = -1;

        Vector2 pos = (Vector2)item.transform.position;

        ItemMaker itemMaker = new ItemMaker();
        itemMaker.DropItemWithAnimation(
            item.transform,
            item.transform.position,
            pos + (UnityEngine.Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();
    }
    /// <summary>
    /// ��װ�����ĳ�ʼ�����ã��������ݿ�¡���������á���ײ�����õȡ�
    /// </summary>
    /// <param name="installed">��ʵ�����İ�װ����</param>
    /// <param name="sourceItem">��Դ��Ʒ���ݣ����ڿ�¡��</param>
    protected virtual void SetupInstalledItem(GameObject installed, Item sourceItem)
    {
        // ��ȫ��飬ȷ�����������Ϊ��
        if (installed == null || sourceItem == null) return;

        // ���ð�װ���������ΪĬ��ֵ��1,1,1��
        installed.transform.localScale = Vector3.one;

        // ���Ի�ȡ��װ�����ϵ� Item ���
        Item installedItem = installed.GetComponentInChildren<Item>();

        if (installedItem != null)
        {
            // ʹ�����������Դ��Ʒ�����ݣ������������ó�ͻ
            installedItem.Item_Data = FastCloner.FastCloner.DeepClone(sourceItem.Item_Data);

            // ��װ��Ʒ�ѵ�����Ĭ����Ϊ1
            installedItem.Item_Data.Stack.Amount = 1;

            // ���ø����������Ӷ����ϵ���ײ��
            EnableChildColliders(true, installedItem.transform);

            // ���� BoxCollider2D �� isTrigger ��Ϊ false������������ײ
            BoxCollider2D boxCollider = installedItem.GetComponent<BoxCollider2D>();

            if (boxCollider != null) boxCollider.isTrigger = false;

            // ���������ʵ���˽����ӿڣ����Ϊ���Ѱ�װ��
            var buildingComp = installed.GetComponent<IBuilding>();
            if (buildingComp != null)
            {
                buildingComp.IsInstalled = true;
            }
        }
    }

    protected void EnableChildColliders(bool enable, Transform root = null)
    {
        root = root ?? transform;
        foreach (var col in root.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = enable;
        }
    }

    public new void OnDestroy()
    {
        base.OnDestroy();
        CleanupGhost();
        Debug.Log($"[BaseBuilding] ��������٣�����GhostShadow");

        // ȡ���¼�����
        if (item != null) item.OnAct -= Install;

    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }
    protected virtual void Update()
    {
        // �������
        //bool isPlayerTaken = item.BelongItem != null;//�����ڿ� ��ʾ����������
        bool isNotInstalled = boxCollider2D.isTrigger;//�Ƿ��ǹ���? �ǹ����ʾ�ڵ��ϲ����ٴΰ�װ��
        bool isBuildingValid = _Data.Hp > 0f; //�Ƿ���

        if (IsInstalled()==true)
        {
            CleanupGhost();
            return;
        }

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;

        // ���� Shadow ʵ������������ڣ�
        if (GhostShadow == null)
        {
            GameObject shadowPrefab = null;
            try
            {
                shadowPrefab = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            }
            catch (System.Exception ex)
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

            GhostShadow.transform.position = mouseWorldPos;
        }

        // ������Ӱ͸������λ��
        float distance = Vector2.Distance(item.transform.position, mouseWorldPos);
        float alpha = Mathf.InverseLerp(_Data.maxVisibleDistance, _Data.minVisibleDistance, distance);
        alpha = Mathf.Clamp01(alpha);

        if (GhostShadow != null)
        {
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
    }


    public bool IsInstalled()
    {
        //��Ʒ��������Ϊ�� ��ʾ����������
        if(item.BelongItem != null && _Data.Hp <= 0)
        {

            return false;
        }
        else
        {
            return true;
        }
    }
}
