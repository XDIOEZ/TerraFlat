using Force.DeepCloner;
using JetBrains.Annotations;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// ����࣬�̳���Item��ʵ�ֶ��ֽӿ�
/// </summary>
public class Player : Item
{
    #region �ֶ�������
    
    [Tooltip("�������")]
    public Data_Player Data;

    [Tooltip("�ӽ�ֵ")]
    public float PovValue
    {
        get => Data.PlayerPov;
        set => Data.PlayerPov = value;
    }

    [Header("ʱ�����")]
    [Tooltip("ʱ�������ٶ�")]
    public float timeScale = 1.0f; // Ĭ��ֵ��Ϊ1.0�������ٶȣ�
    
    [Tooltip("ÿ�ε���ʱ���ٶȵ�����")]
    public float timeScaleStep = 0.5f;
    
    [Tooltip("��Сʱ���ٶ�")]
    public float minTimeScale = 0.1f;
    
    [Tooltip("���ʱ���ٶ�")]
    public float maxTimeScale = 10.0f;

    [Header("ʱ����ʾGUI")]
    [Tooltip("ʱ����ʾ��ʾʱ�����룩")]
    public float timeScaleHintDuration = 1.0f;
    
    // ʱ����ʾ��ر���
    private string timeScaleHintText = "";
    private float timeScaleHintTimer = 0f;
    private bool showTimeScaleHint = false;

    #endregion
    
    public override ItemData itemData
    {
        get => Data;
        set
        {
            Data = value as Data_Player;
        }
    }

    #region �¼�ϵͳ

    #endregion

    #region ��������
    public override void Start()
    {
        ItemMgr.Instance.Player_DIC[Data.Name_User] = this;
        base.Start();
        
        // ȷ����Ϸ��ʼʱʱ�������ٶ�Ϊ�����ٶ�
        timeScale = 1.0f;
        Time.timeScale = timeScale;
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    public override void Load()
    {
        // �����������Ƿ�Ϊ����Ա 
        if (Data.Name_User == "����Ա")
        {
            // �����Ӷ����ȡMod_Inventory
            Mod_Inventory[] mod_Inventory = GetComponentsInChildren<Mod_Inventory>();
            foreach (var inventory in mod_Inventory)
            {
                inventory.Data.InventoryInitName = "����ģʽ";
            }
        }
        
        transform.position = itemData.transform.position;
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
        base.Load();
    }
    
    // ��Update��������Ӱ������ʾ��
    new void Update()
    {
        base.Update();
        
        // ����ʱ����ʾ��ʾ
        UpdateTimeScaleHint();
        
        // ֻ�й���Ա���Կ���ʱ��
        if (Data.Name_User == "����Ա")
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                TeleportToMousePosition();
            }
            
            // ����ʱ�������ٶ�
            HandleTimeScaleControl();
        }
    }
    #endregion

    #region ��������
    [Button("��¡����")]
    public void CloneTest()
    {
        this.itemData = this.itemData.DeepClone();
        Debug.Log("��¡�ɹ�");
    }

    /// <summary>
    /// �����������
    /// </summary>
    public void Death()
    {
        Application.Quit();
        Application.OpenURL("https://space.bilibili.com/353520649");
    }
    
    /// <summary>
    /// ����Ҵ��͵������������λ��
    /// </summary>
    public void TeleportToMousePosition()
    {
        // ��ȡ�������Ļ�ϵ�λ��
        Vector3 mouseScreenPosition = Input.mousePosition;
        
        // ����Ļ����ת��Ϊ��������
        // z������Ϊ0����Ϊ����2D��Ϸ
        Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(
            new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, 0));
        
        // ����z��Ϊ0��2D��Ϸ��
        mouseWorldPosition.z = 0;
        
        // �������λ�õ������������
        transform.position = mouseWorldPosition;
        
        Debug.Log($"����Ѵ��͵�λ��: {mouseWorldPosition}");
    }
    
    /// <summary>
    /// ����ʱ�������ٶȿ���
    /// </summary>
    private void HandleTimeScaleControl()
    {
        bool timeScaleChanged = false;
        
        // ����ʱ������ (����+��)
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            timeScale = Mathf.Clamp(timeScale + timeScaleStep, minTimeScale, maxTimeScale);
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = $"ʱ���ٶ�: {timeScale}x";
            Debug.Log($"ʱ���ٶ����ӵ�: {timeScale}x");
        }
        
        // ����ʱ������ (����-��)
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            timeScale = Mathf.Clamp(timeScale - timeScaleStep, minTimeScale, maxTimeScale);
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = $"ʱ���ٶ�: {timeScale}x";
            Debug.Log($"ʱ���ٶȼ��ٵ�: {timeScale}x");
        }
        
        // ����ʱ���ٶ� (����0��)
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
        {
            timeScale = 1.0f;
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = "ʱ���ٶ�������Ϊ�����ٶ�";
            Debug.Log("ʱ���ٶ�������Ϊ�����ٶ�");
        }
        
        // ���ʱ���ٶ��б仯����ʾ��ʾ
        if (timeScaleChanged)
        {
            ShowTimeScaleHint();
        }
    }
    
    /// <summary>
    /// ��ʾʱ���ٶ���ʾ
    /// </summary>
    private void ShowTimeScaleHint()
    {
        showTimeScaleHint = true;
        timeScaleHintTimer = timeScaleHintDuration;
    }
    
    /// <summary>
    /// ����ʱ����ʾ��ʾ
    /// </summary>
    private void UpdateTimeScaleHint()
    {
        if (showTimeScaleHint)
        {
            timeScaleHintTimer -= Time.unscaledDeltaTime;
            if (timeScaleHintTimer <= 0)
            {
                showTimeScaleHint = false;
                timeScaleHintTimer = 0;
            }
        }
    }
    
    /// <summary>
    /// ����ʱ����ʾGUI
    /// </summary>
    private void OnGUI()
    {
        if (showTimeScaleHint && !string.IsNullOrEmpty(timeScaleHintText))
        {
            // ����͸���ȣ�����ʧЧ����
            float alpha = Mathf.Clamp01(timeScaleHintTimer / timeScaleHintDuration);
            
            // ����GUI��ʽ
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 24;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(1, 1, 1, alpha); // ��ɫ���֣���͸����
            style.alignment = TextAnchor.MiddleCenter;
            
            // ����GUIλ�ã���Ļ����ƫ�ϣ�
            Rect position = new Rect(0, Screen.height * 0.25f, Screen.width, 50);
            
            // ������ʾ����
            GUI.Label(position, timeScaleHintText, style);
        }
    }
    #endregion
}