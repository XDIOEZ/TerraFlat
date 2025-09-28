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
    #region ���ݶ���
    [MemoryPackable]
    [System.Serializable]
    public partial class ModSmeltingData
    {
        [ShowInInspector]
        public Dictionary<string, Inventory_Data> InvData = new Dictionary<string, Inventory_Data>();
        
        [Tooltip("��ǰ����������")]
        public float SmeltingProgress = 10f;
        
        [Tooltip("��¯����������ٶ�")]
        public float MaxSmeltingSpeed = 10f;
        
        [Tooltip("��¯�ĵ�ǰ�����ٶ�")]
        public float SmeltingSpeed = 10f;
        
        [Tooltip("��ǰ��¯���¶�")]
        public float Temperature = 20f;
        
        [Tooltip("�ɴ�����¶�ֵ����ȼ�Ͼ�����")]
        public float MaxTemperature = 0f;
        
        [Tooltip("��¯���������¶����ƣ���¯���������ƣ�")]
        public float MaxTemperatureLimit = 1000f;
        
        [Tooltip("�Ƿ���������")]
        public bool IsSmelting = false;
        
        [Tooltip("�¶������ٶ�")]
        public float TemperatureUpSpeed = 10f;
        
        [Tooltip("�¶��½��ٶ�")]
        public float TemperatureDownSpeed = 30f;
    }
    #endregion

    #region ���л��ֶ�������
    public Ex_ModData_MemoryPackable SaveData;
    public override ModuleData _Data { get { return SaveData; } set { SaveData = (Ex_ModData_MemoryPackable)value; } }
    public ModSmeltingData Data = new ModSmeltingData();

    // ��ʱ����
    public Inventory inputInventory;
    public Inventory outputInventory;
    public Inventory fuelInventory;
    public Mod_Fuel mod_Fuel; // ȼ��ģ��
    public BasePanel panel;
    public Button WorkButton;
    
    [Header("UI���")]
    [Tooltip("����������")]
    public Slider progressSlider;
    [Tooltip("ȼ��������")]
    public Slider fuelSlider;
    [Tooltip("�¶���ʾ��")]
    public Slider temperatureSlider;
    [Tooltip("�¶���ֵ�ı�")]
    public TextMeshProUGUI TemperatureText;
    #endregion

    #region Unity��������
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
                    if (slot != null && slot.itemData != null && slot.itemData.Stack.Amount > 0)
                    {
                        slot.itemData.Stack.Amount -= 1; // �� 1 ��ȼ����Ʒ
                        slot.RefreshUI();

                        Ex_ModData_MemoryPackable fuelData = fuelItem as Ex_ModData_MemoryPackable;
                        if (fuelData != null)
                        {
                            fuelData.OutData(out FuelData fuel);
                            mod_Fuel.AddFuel(fuel.Fuel.x);

                            // ��ȼȼ��
                            mod_Fuel.SetIgnited(true);
                            
                            // �¶�����ȡ����ȼ��
                            Data.MaxTemperature = fuel.MaxTemperature;
                        }

                        SmeltingProcess(deltaTime); // ��������
                    }
                    else
                    {
                        // ����ȼ�Ϻľ� �� ֹͣ����
                        Data.IsSmelting = false;
                        Debug.Log("ȼ�Ϻľ�������ֹͣ��");
                    }
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

    public override void Load()
    {
        // �� SaveData ��ȡ
        SaveData.ReadData(ref Data);
        
        // ͬ������
        if (Data.InvData.Count == 0)
        {
            if (inputInventory != null && inputInventory.Data != null)
                Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
            if (outputInventory != null && outputInventory.Data != null)
                Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
            if (fuelInventory != null && fuelInventory.Data != null)
                Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
        }
        else
        {
            if (inputInventory != null && Data.InvData.ContainsKey(inputInventory.Data.Name))
                inputInventory.Data = Data.InvData[inputInventory.Data.Name];
            if (outputInventory != null && Data.InvData.ContainsKey(outputInventory.Data.Name))
                outputInventory.Data = Data.InvData[outputInventory.Data.Name];
            if (fuelInventory != null && Data.InvData.ContainsKey(fuelInventory.Data.Name))
                fuelInventory.Data = Data.InvData[fuelInventory.Data.Name];
        }

        // ��ť�¼�
        if (WorkButton != null)
            WorkButton.onClick.AddListener(OnButtonClick);

        // ������ֳ�ģ�飬����Ĭ��Ŀ��
        if (item != null && item.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInv = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()?._Inventory;
            if (inputInventory != null)
                inputInventory.DefaultTarget_Inventory = handInv;
            if (outputInventory != null)
                outputInventory.DefaultTarget_Inventory = handInv;
        }

        // ��ʼ�����
        inputInventory?.Init();
        outputInventory?.Init();
        fuelInventory?.Init();

        // ��ȡ����ģ������
        if (item != null && item.itemMods != null)
        {
            var interactMod = item.itemMods.GetMod_ByID(ModText.Interact);
            if (interactMod != null)
            {
                interactMod.OnAction_Start += Interact_Start;
                interactMod.OnAction_Start += _ => panel?.Toggle();
                interactMod.OnAction_Stop += _ => panel?.Close();
            }
        }

        panel?.Close();

        inputInventory?.RefreshUI();
        outputInventory?.RefreshUI();
        fuelInventory?.RefreshUI();
    }

    public override void Save()
    {
        // ��ȫ���
        if (Data == null)
            Data = new ModSmeltingData();
            
        if (Data.InvData == null)
            Data.InvData = new Dictionary<string, Inventory_Data>();
            
        // ����������
        if (inputInventory != null && inputInventory.Data != null)
            Data.InvData[inputInventory.Data.Name] = inputInventory.Data;
        if (outputInventory != null && outputInventory.Data != null)
            Data.InvData[outputInventory.Data.Name] = outputInventory.Data;
        if (fuelInventory != null && fuelInventory.Data != null)
            Data.InvData[fuelInventory.Data.Name] = fuelInventory.Data;
            
        SaveData?.WriteData(Data);
    }
    #endregion

    #region ���������߼�
    private void SmeltingProcess(float deltaTime)
    {
        // ���������Ƿ�����Ʒ
        bool hasInputItem = false;
        if (inputInventory != null && inputInventory.Data != null && inputInventory.Data.itemSlots != null)
        {
            foreach (var slot in inputInventory.Data.itemSlots)
            {
                if (slot != null && slot.itemData != null)
                {
                    hasInputItem = true;
                    break;
                }
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
            // ��������ȼ��
            mod_Fuel?.ConsumeFuel(deltaTime);
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
        mod_Fuel?.ConsumeFuel(deltaTime);

        // �������
        if (Data.SmeltingProgress >= 100f)
        {
            Data.SmeltingProgress = 0f;
            CompleteSmelting();
        }
    }

    public void CompleteSmelting()
    {
        try
        {
            // ��ȫ���
            if (inputInventory == null || inputInventory.Data == null)
            {
                Debug.LogError("������Ϊ�գ��޷��������");
                return;
            }
            
            if (outputInventory == null || outputInventory.Data == null)
            {
                Debug.LogError("������Ϊ�գ��޷��������");
                return;
            }

            // �����䷽���б�
            List<string> recipeKeys = GenerateRecipeKey_List(inputInventory);
            
            Recipe recipe = null;
            string matchedKey = null;
            
            // ����ƥ��ÿ���䷽��
            foreach (string recipeKey in recipeKeys)
            {
                if (GameRes.Instance != null && 
                    GameRes.Instance.recipeDict != null && 
                    GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
                {
                    matchedKey = recipeKey;
                    break;
                }
            }
            
            // ��֤�䷽
            if (recipe == null)
            {
                Debug.LogError($"����ʧ�ܣ��Ҳ����䷽ {string.Join(" �� ", recipeKeys)}");
                return;
            }

            CookRecipe cookRecipe = recipe as CookRecipe;
            if (cookRecipe == null)
            {
                Debug.LogError($"�䷽���ʹ���{matchedKey} ���� CookRecipe");
                return;
            }

            // �¶ȼ��
            if (cookRecipe.Temperature > Data.Temperature)
            {
                Debug.LogWarning($"����ʧ�ܣ������¶� {cookRecipe.Temperature} ��ǰ�¶� {Data.Temperature} �� ��������ʧ��");

                // �ͷ�������۳� 1~2 ���������
                System.Random rand = new System.Random();
                if (inputInventory.Data.itemSlots != null)
                {
                    foreach (var slot in inputInventory.Data.itemSlots)
                    {
                        if (slot != null && slot.itemData != null && slot.itemData.Stack.Amount > 0)
                        {
                            // �۳����� = 1 �� 2������������ǰ����
                            float lossAmount = rand.Next(1, 3); // 1~2
                            lossAmount = Mathf.Min(lossAmount, slot.itemData.Stack.Amount); // ��������������

                            slot.itemData.Stack.Amount -= lossAmount;
                            Debug.LogWarning($"�ͷ��۳���{slot.itemData.IDName} x{lossAmount}");

                            if (slot.itemData.Stack.Amount <= 0)
                            {
                                // �����Ʒ
                                inputInventory.Data.RemoveItemAll(slot, inputInventory.Data.itemSlots.IndexOf(slot));
                            }
                        }
                    }
                }

                // ˢ�� UI
                inputInventory.RefreshUI();
                return;
            }

            // ��֤�����λ����
            if (!ValidateSlotCount(inputInventory, recipe))
                return;

            // ׼�������Ʒ
            var outputItems = PrepareOutputItems(recipe);
            if (outputItems == null)
                return;

            // �����Դ�Ϳռ�
            if (!CheckResourcesAndSpace(inputInventory, outputInventory, recipe, outputItems))
            {
                Debug.LogError("����ʧ�ܣ����ϲ��������ռ䲻��");
                return;
            }

            // ִ������
            ExecuteSmelting(inputInventory, outputInventory, recipe, outputItems);
        }
        catch (Exception ex)
        {
            Debug.LogError($"���������з�������: {ex.Message}");
        }
    }
    #endregion

    #region �䷽�����߼�
    /// <summary>
    /// �����䷽���б�֧��Tagģʽ��itemNameģʽ��
    /// </summary>
    private List<string> GenerateRecipeKey_List(Inventory inputInv)
    {
        List<string> recipeKeys = new List<string>();
        
        // ��ȫ���
        if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null)
            return recipeKeys;
        
        // ���ɻ�����Ʒ���Ƶ��䷽��
        Input_List inputList = new Input_List();
        foreach (ItemSlot slot in inputInv.Data.itemSlots)
        {
            if (slot == null || slot.itemData == null)
            {
                inputList.AddNameItem("");
            }
            else
            {
                inputList.AddNameItem(slot.itemData.IDName);
            }
        }
        recipeKeys.Add(inputList.ToString());
        
        // ���ɻ���Tag���䷽����Ϊÿ����Tag����Ʒ����һ���汾��
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            if (slot != null && slot.itemData != null && slot.itemData.Tags != null)
            {
                // Ϊÿ������Tag����Ʒ����һ������Tag���䷽���汾
                Input_List tagInputList = new Input_List();
                for (int j = 0; j < inputInv.Data.itemSlots.Count; j++)
                {
                    if (j == i && slot.itemData.Tags.MakeTag != null && slot.itemData.Tags.MakeTag.values != null && slot.itemData.Tags.MakeTag.values.Count > 0)
                    {
                        // ʹ�õ�һ��Type��ǩ
                        if (slot.itemData.Tags.MakeTag.values.Count > 0)
                        {
                            tagInputList.AddTagItem(slot.itemData.Tags.MakeTag.values[0]);
                        }
                        else
                        {
                            tagInputList.AddNameItem(slot.itemData?.IDName ?? "");
                        }
                    }
                    else
                    {
                        var otherSlot = inputInv.Data.itemSlots[j];
                        tagInputList.AddNameItem(otherSlot?.itemData?.IDName ?? "");
                    }
                }
                recipeKeys.Add(tagInputList.ToString());
            }
        }
        
        return recipeKeys;
    }

    private bool ValidateSlotCount(Inventory inputInv, Recipe recipe)
    {
        if (inputInv == null || inputInv.Data == null || recipe == null || recipe.inputs == null)
            return false;
            
        if (inputInv.Data.itemSlots == null || recipe.inputs.RowItems_List == null)
            return false;

        if (inputInv.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"�����λ������ƥ�䣺�䷽��Ҫ {recipe.inputs.RowItems_List.Count} ������ۣ���ǰ�� {inputInv.Data.itemSlots.Count} ��");
            return false;
        }
        return true;
    }

    private List<ItemData> PrepareOutputItems(Recipe recipe)
    {
        var itemsToAdd = new List<ItemData>();
        
        if (recipe == null || recipe.outputs == null || recipe.outputs.results == null)
            return null;
            
        foreach (var output in recipe.outputs.results)
        {
            if (string.IsNullOrEmpty(output.ItemName))
            {
                Debug.LogError($"�䷽���������Ϊ�գ��䷽��{recipe.name}��");
                return null;
            }
            
            if (GameRes.Instance == null || GameRes.Instance.AllPrefabs == null)
            {
                Debug.LogError($"GameResʵ����Ԥ�����ֵ�Ϊ�գ�{output.ItemName}���䷽��{recipe.name}��");
                return null;
            }
            
            if (!GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out var prefab) || prefab == null)
            {
                Debug.LogError($"Ԥ���岻���ڣ�{output.ItemName}���䷽��{recipe.name}��");
                return null;
            }
            
            Item outputitem = prefab.GetComponent<Item>();
            if (outputitem == null)
            {
                Debug.LogError($"Ԥ���� {output.ItemName} ���Ҳ���Item������䷽��{recipe.name}��");
                return null;
            }
            
            ItemData newItem = outputitem.Get_NewItemData();
            if (newItem == null)
            {
                Debug.LogError($"�޷����� {output.ItemName} ��ItemData���䷽��{recipe.name}��");
                return null;
            }
            
            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }
        
        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        // ����������
        if (inputInv == null || inputInv.Data == null || inputInv.Data.itemSlots == null ||
            recipe == null || recipe.inputs == null || recipe.inputs.RowItems_List == null)
            return false;

        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            if (slot == null || slot.itemData == null)
                return false;

            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // �������ռ�
        if (outputInv == null || outputInv.Data == null || outputItems == null)
            return false;
            
        foreach (var item in outputItems)
        {
            if (item == null || !outputInv.Data.TryAddItem(item, false))
                return false;
        }

        return true;
    }

    private void ExecuteSmelting(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        if (inputInv == null || inputInv.Data == null || 
            outputInv == null || outputInv.Data == null || 
            recipe == null || recipe.inputs == null || recipe.inputs.RowItems_List == null ||
            outputItems == null)
        {
            Debug.LogError("ִ������ʱ����Ϊ��");
            return;
        }

        Debug.Log($"��ʼ������{recipe.name}");
        Debug.Log($"������ϣ�{string.Join(",", recipe.inputs.RowItems_List.Select(r => $"{r.ItemName}x{r.amount}"))}");
        Debug.Log($"������Ʒ��{string.Join(", ", outputItems.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // ��Ӳ���
        foreach (var item in outputItems)
        {
            if (item != null)
            {
                outputInv.Data.TryAddItem(item);
                Debug.Log($"��Ӳ��{item.Stack.Amount}x{item.IDName}");
            }
        }

        // �۳��������
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0 || slot == null || slot.itemData == null) continue;

            Debug.Log($"�۳����ϣ�{required.ItemName} x{required.amount}����ǰ�� {slot.itemData.Stack.Amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                Debug.Log($"����� {i} �� {required.ItemName} ���þ����Ƴ�");
                inputInv.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"����� {i} ʣ�� {required.ItemName} x{slot.itemData.Stack.Amount}");
            }
            inputInv.RefreshUI(i);
        }
        
        // ִ���䷽����
        if (recipe.action != null)
        {
            foreach(var action in recipe.action)
            {
                if (action != null && inputInv.Data.itemSlots != null && 
                    action.slotIndex >= 0 && action.slotIndex < inputInv.Data.itemSlots.Count)
                {
                    action.Apply(inputInv.Data.itemSlots[action.slotIndex]);
                }
            }
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"������ɣ�{recipe.name}");
    }

    // �����ɰ汾�ļ�鷽���Ա��ּ�����
    private bool CheckEnough(Inventory inputInventory_,
                               Inventory outputInventory_,
                               Input_List inputList,
                               List<ItemData> itemsToAdd)
    {
        // ���ÿ����۵���Ʒ�Ƿ�����Ҫ��
        if (inputInventory_ == null || inputInventory_.Data == null || inputInventory_.Data.itemSlots == null ||
            inputList == null || inputList.RowItems_List == null ||
            outputInventory_ == null || outputInventory_.Data == null ||
            itemsToAdd == null)
            return false;

        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // ����ò�۲���Ҫ��Ʒ������
            if (required.amount == 0) continue;

            // �����Ʒ����������ƥ��
            if (slot == null || slot.itemData == null ||
                slot.itemData.IDName != required.ItemName)
                return false;

            // ��������㹻
            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // �������ռ�
        foreach (var item in itemsToAdd)
        {
            if (item == null || !outputInventory_.Data.TryAddItem(item, false))
                return false;
        }

        return true;
    }
    #endregion

    #region UI�뽻������
private void UpdateUI()
{
    // ����������
    if (progressSlider != null)
        progressSlider.value = Data.SmeltingProgress / 100f;

    // ȼ����
    if (fuelSlider != null && mod_Fuel != null && mod_Fuel.Data != null)
        fuelSlider.value = mod_Fuel.Data.Fuel.y > 0 ? mod_Fuel.Data.Fuel.x / mod_Fuel.Data.Fuel.y : 0;

    // �¶�����ʹ����¯�����¶���Ϊ���ֵ��
    if (temperatureSlider != null)
    {
        // ʼ��ʹ��MaxTemperatureLimit��Ϊ���ֵ��ʾ����Ҳο�
        float maxTempForDisplay = Data.MaxTemperatureLimit;
        temperatureSlider.value = maxTempForDisplay > 0 ? Data.Temperature / maxTempForDisplay : 0;
    }
    
    // �¶���ֵ�ı�
    if (TemperatureText != null)
    {
        // ��ʾʵ�ʵ��¶����ƣ�ȼ�����ƺ�¯�����������еĽ�Сֵ��
        float actualMaxTemp = Data.MaxTemperature > 0 ? Mathf.Min(Data.MaxTemperature, Data.MaxTemperatureLimit) : Data.MaxTemperatureLimit;
        TemperatureText.text = $"{Mathf.RoundToInt(Data.Temperature)}��C / {Mathf.RoundToInt(actualMaxTemp)}��C (¯������: {Mathf.RoundToInt(Data.MaxTemperatureLimit)}��C)";
    }
}

    private void OnButtonClick()
    {
        // ��ȫ���
        if (fuelInventory == null || fuelInventory.Data == null)
        {
            Debug.LogWarning("ȼ�Ͽ��δ��ʼ����");
            return;
        }

        // ������Ϊtag��ΪIgnition����Ʒ
        var ignitionItem = fuelInventory.Data.FindItemByTagTypeAndTag("FunctionTag", "Ignition");
        if (ignitionItem == null)
        {
            Debug.LogWarning("�޷���ȼ��ȱ�ٵ��װ�ã�");
            return;
        }

        // ����Ѿ��������У�����������ֹͣ
        if (Data.IsSmelting)
        {
            Debug.Log("�����Ѿ���ʼ���޷�����ֹͣ��ֻ��ȼ�Ϻľ�ʱ�Ż�ֹͣ��");
            return;
        }
        
        // ��ʼ����
        Data.IsSmelting = true;
        
        // ����Ĭ������¶�Ϊ��¯�����¶ȣ������û��ȼ���ṩ�¶ȵĻ���
        if (Data.MaxTemperature <= 0)
        {
            Data.MaxTemperature = Data.MaxTemperatureLimit;
        }
        
        // ��ȼȼ��ģ��
        mod_Fuel?.SetIgnited(true);
        Debug.Log("��¯�ѵ�ȼ����ʼ������");
    }

    public void Interact_Start(Item item_)
    {
        if (item_ == null || item_.itemMods == null)
            return;
            
        var handInventory = item_.itemMods.GetMod_ByID(ModText.Hand)?.GetComponent<IInventory>()?._Inventory;

        if (inputInventory != null)
            inputInventory.DefaultTarget_Inventory = handInventory;
        if (outputInventory != null)
            outputInventory.DefaultTarget_Inventory = handInventory;
        if (fuelInventory != null)
            fuelInventory.DefaultTarget_Inventory = handInventory;
    }
    #endregion

    #region ȼ��״̬����
    /// <summary>
    /// ����ȼ��״̬
    /// </summary>
    /// <param name="isBurning">�Ƿ�ȼ��</param>
    public void SetBurningState(bool isBurning)
    {
        Data.IsSmelting = isBurning;
        mod_Fuel?.SetIgnited(isBurning);
        
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
        return Data.IsSmelting && (mod_Fuel?.GetIgnitedState() ?? false);
    }
    
    /// <summary>
    /// �л�ȼ��״̬
    /// </summary>
    public void ToggleBurningState()
    {
        SetBurningState(!GetBurningState());
    }
    #endregion
}