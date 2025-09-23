using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Smelting : Module
{
    [MemoryPackable]
    [System.Serializable]
    public partial class ModSmeltingData
    {
        [ShowInInspector]
        public Dictionary<string, Inventory_Data> InvData = new Dictionary<string, Inventory_Data>();
        //��ǰ����������
        public float SmeltingProgress = 10f;
        //��¯����������ٶ�
        public float MaxSmeltingSpeed = 10f;
        //��¯�ĵ�ǰ�����ٶ�
        public float SmeltingSpeed = 10f;
        //��ǰ��¯���¶�
        public float Temperature = 20f;
        //�ɴ�����¶�ֵ����ȼ�Ͼ�����
        public float MaxTemperature = 0f;
        //��¯���������¶����ƣ���¯���������ƣ�
        public float MaxTemperatureLimit = 1000f;
        //IsSmelting �Ƿ���������
        public bool IsSmelting = false;
        //�¶������ٶ�
        public float TemperatureUpSpeed = 10f;
        //�¶��½��ٶ�
        public float TemperatureDownSpeed = 30f;
    }

    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }
    public ModSmeltingData Data;

    //��ʱ����
    public Inventory inputInventory;
    public Inventory outputInventory;
    public Inventory fuelInventory;
    public Mod_Fuel mod_Fuel;//ȼ��ģ��
    public BasePanel panel;
    public Button WorkButton;
    [Tooltip("����������")]
    public Slider progressSlider;
    [Tooltip("ȼ��������")]
    public Slider fuelSlider;
    [Tooltip("�¶���ʾ��")]
    public Slider temperatureSlider;
    [Tooltip("�¶���ֵ�ı�")]
    public TextMeshProUGUI TemperatureText;

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Smelting;
        }
    }
    
    public override void ModUpdate(float deltaTime)
    {
        if (Data.IsSmelting) // �Ѿ���������״̬
        {
            // ���ȼ��ģ���Ƿ��ڵ�ȼ״̬
            if (mod_Fuel.GetIgnitedState())
            {
                SmeltingProcess(deltaTime);
            }
            else
            {
                // ���ȼ�ϲ���Ƿ���ȼ����Ʒ
                var fuelItem = fuelInventory.Data.GetModuleByID(ModText.Fuel);
                if (fuelItem != null)
                {
                    // ����Ʒת��Ϊȼ��ֵ
                    ItemSlot slot = fuelInventory.Data.GetItemSlotByModuleID(fuelItem.ID);
                    slot.itemData.Stack.Amount -= 1; // �� 1 ��ȼ����Ʒ
                    slot.RefreshUI();

                    Ex_ModData_MemoryPackable fuelData = fuelItem as Ex_ModData_MemoryPackable;
                    fuelData.OutData(out FuelData fuel);

                    mod_Fuel.AddFuel(fuel.Fuel.x);

                    // ��ȼȼ��
                    mod_Fuel.SetIgnited(true);
                    
                    // �¶�����ȡ����ȼ��
                    Data.MaxTemperature = fuel.MaxTemperature;

                    SmeltingProcess(deltaTime); // ��������
                }
                else
                {
                    // ����ȼ�Ϻľ� �� ֹͣ����
                    Data.IsSmelting = false;
                    Debug.Log("ȼ�Ϻľ�������ֹͣ��");
                }
            }
        }
        else
        {
            // δ��������ֹͣ �� �¶Ȼ����½��� 20��
            Data.Temperature = Mathf.Max(Data.Temperature - Data.TemperatureDownSpeed * deltaTime, 20f);
            Data.SmeltingSpeed = 0f;
            
            // ���ȼ��ģ���ǵ�ȼ�ģ�����ҲϨ��
            if (mod_Fuel.GetIgnitedState())
            {
                mod_Fuel.SetIgnited(false);
            }
        }

        // ͬ������UI
        UpdateUI();
    }

    private void UpdateUI()
    {
        // ����������
        if (progressSlider != null)
            progressSlider.value = Data.SmeltingProgress / 100f;

        // ȼ����
        if (fuelSlider != null)
            fuelSlider.value = mod_Fuel.Data.Fuel.x / mod_Fuel.Data.Fuel.y;

        // �¶�����ʹ����¯�����¶���Ϊ���ֵ��
        if (temperatureSlider != null)
        {
            float actualMaxTemp = Data.MaxTemperature > 0 ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;
            temperatureSlider.value = Data.Temperature / actualMaxTemp;
        }
        
        // �¶���ֵ�ı�
        if (TemperatureText != null)
        {
            float actualMaxTemp = Data.MaxTemperature > 0 ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;
            TemperatureText.text = $"{Mathf.RoundToInt(Data.Temperature)}��C / {Mathf.RoundToInt(actualMaxTemp)}��C";
        }
    }

    private void SmeltingProcess(float deltaTime)
    {
        // ���������Ƿ�����Ʒ
        bool hasInputItem = false;
        foreach (var slot in inputInventory.Data.itemSlots)
        {
            if (slot.itemData != null) // ���� HasItem �����жϸò�����Ʒ�ķ���
            {
                hasInputItem = true;
                break;
            }
        }

        // ����ʵ�ʵ�����¶ȣ���������¯���������¶����ƣ�
        float actualMaxTemp = Data.MaxTemperature > 0 ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;

        // ���û����Ʒ �� ���ȹ��㣨��ʾ���գ�
        if (!hasInputItem)
        {
            Data.SmeltingProgress = 0f;
            // �¶���Ȼ��������ȼ����������ޣ�����������¯����
            Data.Temperature = Mathf.Min(Data.Temperature + Data.TemperatureUpSpeed * 2f * deltaTime, actualMaxTemp);
            // ��������ȼ�ϣ���ѡ��������ղ���ȼ�Ͼ�ɾ����һ�У�
            mod_Fuel.ConsumeFuel(deltaTime);
            return; // �����������߼�
        }

        // ===== ���������������߼� =====

        // �¶���ʱ������������������¯����
        Data.Temperature = Mathf.Min(Data.Temperature + Data.TemperatureUpSpeed * deltaTime, actualMaxTemp);

        // �����¶ȼ��㵱ǰ�����ٶ�
        float tempRatio = Data.Temperature / actualMaxTemp;
        Data.SmeltingSpeed = Mathf.Lerp(1f, Data.MaxSmeltingSpeed, tempRatio);

        // ����ǰ�ٶ��ƽ�����
        Data.SmeltingProgress += Data.SmeltingSpeed * deltaTime;

        // ����ȼ��
        mod_Fuel.ConsumeFuel(deltaTime);

        // �������
        if (Data.SmeltingProgress >= 100f)
        {
            Data.SmeltingProgress = 0f;
            CompleteSmelting();
        }
    }

    private void OnButtonClick()
    {
        //������Ϊtag��ΪIgnition����Ʒ
        var ignitionItem = fuelInventory.Data.FindItemByTagTypeAndTag("FunctionTag", "Ignition");
        if (ignitionItem == null)
        {
            Debug.LogWarning("�޷���ȼ��ȱ�ٵ��װ�ã�");
            return;
        }

        // �л�����״̬
        Data.IsSmelting = !Data.IsSmelting;
        
        if (Data.IsSmelting)
        {
            // ����Ĭ������¶�Ϊ��¯�����¶ȣ������û��ȼ���ṩ�¶ȵĻ���
            if (Data.MaxTemperature <= 0)
            {
                Data.MaxTemperature = Data.MaxTemperatureLimit;
            }
            
            // ��ȼȼ��ģ��
            mod_Fuel.SetIgnited(true);
            Debug.Log("��¯�ѵ�ȼ����ʼ������");
        }
        else
        {
            // Ϩ��ȼ��ģ��
            mod_Fuel.SetIgnited(false);
            Debug.Log("��¯��ֹͣ������");
        }
    }

    public void CompleteSmelting()
    {
        // �����䷽��
        string recipeKey = string.Join(",",
            inputInventory.Data.itemSlots.Select(slot => slot.itemData?.IDName ?? ""));

        // �ҵ��䷽
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out Recipe recipe))
        {
            Debug.LogError($"����ʧ�ܣ��Ҳ����䷽ {recipeKey}");
            return;
        }

        CookRecipe cookRecipe = recipe as CookRecipe;
        if (cookRecipe == null)
        {
            Debug.LogError($"�䷽���ʹ���{recipeKey} ���� CookRecipe");
            return;
        }

        // �¶ȼ��
        if (cookRecipe.Temperature > Data.Temperature)
        {
            Debug.LogWarning($"����ʧ�ܣ������¶� {cookRecipe.Temperature} ��ǰ�¶� {Data.Temperature} �� ��������ʧ��");

            // �ͷ�������۳� 1~2 ���������
            System.Random rand = new System.Random();
            foreach (var slot in inputInventory.Data.itemSlots)
            {
                if (slot.itemData != null && slot.itemData.Stack.Amount > 0)
                {
                    // �۳����� = 1 �� 2������������ǰ����
                    float lossAmount = rand.Next(1, 3); // 1~2
                    lossAmount = (Math.Min(lossAmount, slot.itemData.Stack.Amount)); // ��������������

                    slot.itemData.Stack.Amount -= lossAmount;
                    Debug.LogWarning($"�ͷ��۳���{slot.itemData.IDName} x{lossAmount}");

                    if (slot.itemData.Stack.Amount <= 0)
                    {
                        // �����Ʒ
                        inputInventory.Data.RemoveItemAll(slot, inputInventory.Data.itemSlots.IndexOf(slot));
                    }
                }
            }

            // ˢ�� UI
            inputInventory.RefreshUI();
            return;
        }
        // ׼�������Ʒ
        var itemsToAdd = new List<ItemData>();
        foreach (var output in cookRecipe.outputs.results)
        {
            if (!GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out var prefab) || prefab == null)
            {
                Debug.LogError($"Ԥ���岻���ڣ�{output.ItemName}���䷽��{recipe.name}��");
                return;
            }

            Item item = prefab.GetComponent<Item>();
            ItemData newItem = item.IsPrefabInit();
            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }

        // �������Ƿ��㹻 & ����ռ�
        if (!CheckEnough(inputInventory, outputInventory, cookRecipe.inputs, itemsToAdd))
        {
            Debug.LogError("����ʧ�ܣ����ϲ��������ռ䲻��");
            return;
        }

        // ��Ӳ���
        foreach (var item in itemsToAdd)
        {
            outputInventory.Data.TryAddItem(item);
            Debug.Log($"����������{item.Stack.Amount}x{item.IDName}");
        }

        // �۳��������
        for (int i = 0; i < inputInventory.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory.Data.itemSlots[i];
            var required = cookRecipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            Debug.Log($"�۳����ϣ�{required.ItemName} x{required.amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                inputInventory.Data.RemoveItemAll(slot, i);
            }
            inputInventory.RefreshUI(i);
        }

        Debug.Log($"������ɣ�{recipe.name}");
        outputInventory.RefreshUI();
    }

    private bool CheckEnough(Inventory inputInventory_,
                               Inventory outputInventory_,
                               Input_List inputList,
                               List<ItemData> itemsToAdd)
    {
        // ���ÿ����۵���Ʒ�Ƿ�����Ҫ��
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // ����ò�۲���Ҫ��Ʒ������
            if (required.amount == 0) continue;

            // �����Ʒ����������ƥ��
            if (slot.itemData == null ||
                slot.itemData.IDName != required.ItemName)
                return false;

            // ��������㹻
            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // �������ռ�
        foreach (var item in itemsToAdd)
            if (!outputInventory_.Data.TryAddItem(item, false))
                return false;

        return true;
    }

    public override void Load()
    {
        // �� SaveData ��ȡ
        SaveData.ReadData(ref Data);
        // ͬ������
        if (Data.InvData.Count == 0)
        {
            Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
            Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
            Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
        }
        else
        {
            inputInventory.Data = Data.InvData[inputInventory.Data.Name];
            outputInventory.Data = Data.InvData[outputInventory.Data.Name];
            fuelInventory.Data = Data.InvData[fuelInventory.Data.Name];
        }

        // ��ť�¼�
        WorkButton.onClick.AddListener(OnButtonClick);

        // ������ֳ�ģ�飬����Ĭ��Ŀ��
        if (item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInv = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
            inputInventory.DefaultTarget_Inventory = handInv;
            outputInventory.DefaultTarget_Inventory = handInv;
        }

        // ��ʼ�����
        inputInventory.Init();
        outputInventory.Init();
        fuelInventory.Init();

        // ��ȡ����ģ������
        var interactMod = item.itemMods.GetMod_ByID(ModText.Interact);
        interactMod.OnAction_Start += Interact_Start;
        interactMod.OnAction_Start += _ => panel.Toggle();
        interactMod.OnAction_Stop += _ => panel.Close();

        panel.Close();

        inputInventory.RefreshUI();
        outputInventory.RefreshUI();
        fuelInventory.RefreshUI();
    }

    public void Interact_Start(Item item_)
    {
        var handInventory = item_.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;

        inputInventory.DefaultTarget_Inventory = handInventory;
        outputInventory.DefaultTarget_Inventory = handInventory;
        fuelInventory.DefaultTarget_Inventory = handInventory;
    }

    public override void Save()
    {
        Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
        Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
        Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
        SaveData.WriteData(Data);
    }
    
    /// <summary>
    /// ����ȼ��״̬
    /// </summary>
    /// <param name="isBurning">�Ƿ�ȼ��</param>
    public void SetBurningState(bool isBurning)
    {
        Data.IsSmelting = isBurning;
        mod_Fuel.SetIgnited(isBurning);
        
        if (isBurning)
        {
            Debug.Log("��¯��ʼȼ�գ�");
        }
        else
        {
            Debug.Log("��¯ֹͣȼ�գ�");
        }
    }
    
    /// <summary>
    /// ��ȡȼ��״̬
    /// </summary>
    /// <returns>�Ƿ�����ȼ��</returns>
    public bool GetBurningState()
    {
        return Data.IsSmelting && mod_Fuel.GetIgnitedState();
    }
    
    /// <summary>
    /// �л�ȼ��״̬
    /// </summary>
    public void ToggleBurningState()
    {
        SetBurningState(!GetBurningState());
    }
}