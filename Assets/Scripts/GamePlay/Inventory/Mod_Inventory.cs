using AYellowpaper.SerializedCollections;
using Sirenix.OdinInspector;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Mod_Inventory : Module, IInventory
{
    #region 字段和属性
    public InventoryModuleData Data = new InventoryModuleData();
    public override ModuleData _Data { get => Data; set => Data = (InventoryModuleData)value; }
    public Inventory inventory { get => InventoryRefDic.First().Value; }
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get => inventoryRefDic; set => inventoryRefDic = value; }
    [Tooltip("Inventory对应的BasePanel缓存字典")]
    public SerializedDictionary<Inventory, BasePanel> inventoryBasePanelCache = new();

    // 修改：将单个预制体字段改为与inventoryRefDic对应的序列化字典
    [Tooltip("Inventory面板预制体字典")]
    public SerializedDictionary<string, GameObject> inventoryPanelPrefabs = new();
    [Tooltip("模块面板的预制体/已经弃用")]
    public GameObject Prefab_BasePanel;

    // 新增：UI开关按键绑定字段，让策划可以在编辑器中设置
    [Tooltip("UI面板开关Action名称，对应InputSystem中的Action Name")]
    public string ToggleActionName = "";

    #endregion

    #region 生命周期方法

    public void OnValidate()
    {
        _Data.ID = inventory.Data.Name;
    }

    public override void Load()
    {
        // 修改为使用for循环遍历inventoryRefDic中的所有inventory
        var inventoryPairs = inventoryRefDic.ToArray();

        // 遍历inventoryRefDic中的所有inventory
        for (int i = 0; i < inventoryPairs.Length; i++)
        {
            var kvp = inventoryPairs[i];
            string inventoryId = kvp.Key;
            Inventory currentInventory = kvp.Value;

            // 加载库存数据
            if (Data.Data.Count == 0)
            {
                Data.Data[inventoryId] = currentInventory.Data;
            }
            else if (Data.Data.ContainsKey(inventoryId))
            {
                currentInventory.Data = Data.Data[inventoryId];
            }

            // 查找模块数据
            if (Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
                _Data = Item_Data.ModuleDataDic[_Data.Name];

            // 设置所有者
            currentInventory.item = item;

            // 设置默认目标库存
            if (item.itemMods.GetMod_ByID(ModText.Hand))
            {
                currentInventory.DefaultTarget_Inventory =
                            item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
            }
            else
            {
                currentInventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
            }

            // 初始化库存
            currentInventory.InitData();
            BindController();

            // 尝试初始化物品
            GameRes.Instance.InventoryInitGet(Data.InventoryInitName, out Inventoryinit inventoryInit);

            if (inventoryInit != null)
            {
                currentInventory.TryInitializeItems(inventoryInit);
            }

            // 根据保存的面板状态决定是否提前初始化UI
            // 如果面板之前是打开的，则在Load阶段预先创建，否则延迟到需要时再创建
            if (currentInventory.Data != null && currentInventory.Data.PanelIsOpen)
            {
                EnsurePanelCreated(currentInventory);
                NewMethod(currentInventory);
            }
        }
        // 获取交互模块引用
        if (item != null && item.itemMods != null)
        {
            var interactMod = item.itemMods.GetMod_ByID<Mod_Interaction>(ModText.Interact);
            if (interactMod != null)
            {
                interactMod.OnAction_Start += Interact_Start;
                interactMod.OnAction_Stop += Interact_Stop;
            }
        }
        else
        {
            Debug.LogWarning("Item或Item的itemMods为空，无法绑定交互事件");
        }
    }

    private void NewMethod(Inventory currentInventory)
    {
        // 根据参数决定是否打开面板
        if (currentInventory.Data.PanelIsOpen)
        {
            currentInventory.basePanel.Open();
            // 延迟1帧调用，确保面板在层级系统正确排列
            StartCoroutine(DelayedBringToFront(currentInventory.basePanel.GetComponent<RectTransform>()));
        }
        else
        {
            currentInventory.basePanel.Close();
        }
    }

    /// <summary>
    /// 确保指定Inventory的面板已创建，如果未创建则在此时创建
    /// </summary>
    public void EnsurePanelCreated(Inventory targetInventory = null, bool Open = true)
    {
        Inventory currentInventory = targetInventory ?? inventory;

        // 空检查：确保当前Inventory存在
        if (currentInventory == null)
        {
            Debug.LogError("[Mod_Inventory.EnsurePanelCreated] currentInventory 为空！");
            return;
        }

        // 如果面板已创建，直接返回
        if (currentInventory.basePanel != null)
            return;

        // 空检查：确保inventoryRefDic存在
        if (inventoryRefDic == null || inventoryRefDic.Count == 0)
        {
            Debug.LogError("[Mod_Inventory.EnsurePanelCreated] inventoryRefDic 为空或未初始化！");
            return;
        }

        // 查找对应的inventory id
        string inventoryId = null;
        foreach (var kvp in inventoryRefDic)
        {
            if (kvp.Value == currentInventory)
            {
                inventoryId = kvp.Key;
                break;
            }
        }

        if (string.IsNullOrEmpty(inventoryId))
        {
            Debug.LogError($"无法找到Inventory对应的ID");
            return;
        }

        // 如果预制体存在，创建面板
        if (inventoryPanelPrefabs.ContainsKey(inventoryId) && inventoryPanelPrefabs[inventoryId] != null)
        {
            currentInventory.basePanel = UIManager.Instance.CreatePanelFromGameObject(inventoryPanelPrefabs[inventoryId]).GetComponentInChildren<BasePanel>();

            if (currentInventory.basePanel != null)
            {
                // 获取所有inventory中的索引
                int inventoryIndex = 0;
                foreach (var kvp in inventoryRefDic)
                {
                    if (kvp.Value == currentInventory)
                        break;
                    inventoryIndex++;
                }

                // 只有第一个面板的关闭按钮才会保持显示，其他的关闭按钮都会被隐藏
                var closeBtnGO = currentInventory.basePanel.GetButton("关闭");
                if (inventoryIndex > 0)
                {
                    if (closeBtnGO != null)
                        closeBtnGO.gameObject.SetActive(false); // 隐藏关闭按钮
                }
                else
                {
                    // 第一个面板：确保关闭按钮可见并绑定为关闭所有面板的逻辑
                    if (closeBtnGO != null)
                    {
                        closeBtnGO.gameObject.SetActive(true);
                        var btn = closeBtnGO;
                        if (btn != null)
                        {
                            btn.onClick.RemoveAllListeners();
                            btn.onClick.AddListener(() =>
                            {
                                foreach (var kv in inventoryBasePanelCache)
                                {
                                    var p = kv.Value;
                                    if (p != null)
                                        p.Close();
                                }
                            });
                        }
                    }
                }

                // 将面板缓存到字典中
                inventoryBasePanelCache[currentInventory] = currentInventory.basePanel;

                // 如果此 inventory 中保存了面板位置，则尝试在创建时恢复位置
                if (currentInventory.Data != null)
                {
                    RectTransform rt = null;
                    if (currentInventory.basePanel.Dragger != null)
                        rt = currentInventory.basePanel.Dragger.GetComponent<RectTransform>();
                    if (rt == null)
                        rt = currentInventory.basePanel.GetComponent<RectTransform>();

                    if (rt != null)
                    {
                        var savedPos = currentInventory.Data.PanelPosition;
                        var savedPos2 = new Vector2(savedPos.x, savedPos.y);
                        if (IsValidVector2(savedPos2) && (savedPos2.x != 0 || savedPos2.y != 0))
                        {
                            rt.anchoredPosition = savedPos2;
                        }
                    }
                }

                // 设置窗口信息
                if (currentInventory.basePanel.GetText("窗口信息") != null)
                    currentInventory.basePanel.GetText("窗口信息").text = currentInventory.Data.Name;

                // 调用UI初始化方法（此时basePanel已存在）
                currentInventory.InitUI();

            }
        }
        else
        {
            Debug.LogWarning($"找不到Inventory '{inventoryId}' 对应的面板预制体");
        }
    }

    public virtual void BindController()
    {
        GameController GameController = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);

        if (GameController == null)
        {
            Debug.Log("Owner 未设置为 GameController");
            return;
        }

        // 如果ToggleActionName为空，则不绑定任何按键
        if (string.IsNullOrEmpty(ToggleActionName))
        {
            Debug.Log("ToggleActionName为空，不绑定任何按键");
            return;
        }

        try
        {
            // 方法一：通过GetAction方法获取Action (推荐方式)
            UnityEngine.InputSystem.InputAction toggleAction = GameController._inputActions.FindAction(ToggleActionName);

            if (toggleAction != null)
            {
                // 确保没有重复绑定
                toggleAction.performed -= OnToggleActionPerformed;
                toggleAction.performed += OnToggleActionPerformed;
                Debug.Log($"成功绑定UI开关Action: {ToggleActionName}");
            }
            else
            {
                // 方法二：尝试通过Win10Actions类的属性获取（兼容原代码）
                var win10Actions = GameController._inputActions.Win10;
                var actionProperty = win10Actions.GetType().GetProperty(ToggleActionName);

                if (actionProperty != null)
                {
                    toggleAction = actionProperty.GetValue(win10Actions) as UnityEngine.InputSystem.InputAction;
                    if (toggleAction != null)
                    {
                        toggleAction.performed -= OnToggleActionPerformed;
                        toggleAction.performed += OnToggleActionPerformed;
                        Debug.Log($"成功通过属性绑定UI开关Action: {ToggleActionName}");
                    }
                    else
                    {
                        Debug.LogWarning($"无法获取Action属性值: {ToggleActionName}");
                    }
                }
                else
                {
                    Debug.LogWarning($"无法找到Action: {ToggleActionName}");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Action绑定过程中出现错误: {e.Message}");
        }
    }

    // 辅助方法：绑定默认的切换Action
    private void BindDefaultToggleAction(GameController gameController)
    {
        // 回退到默认B键
        gameController._inputActions.Win10.B.performed -= OnToggleActionPerformed;
        gameController._inputActions.Win10.B.performed += OnToggleActionPerformed;
    }

    // 处理切换事件的方法
    private void OnToggleActionPerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        if (ctx.performed)
        {
            // 确保所有inventory的面板都已创建
            bool anyPanelCreated = false;
            foreach (var kvp in InventoryRefDic)
            {
                if (kvp.Value.basePanel == null)
                {
                    EnsurePanelCreated(kvp.Value);
                    // 根据参数决定是否打开面板
                    if (kvp.Value.Data.PanelIsOpen)
                    {
                        kvp.Value.basePanel.Open();
                        // 延迟1帧调用，确保面板在层级系统正确排列
                        StartCoroutine(DelayedBringToFront(kvp.Value.basePanel.GetComponent<RectTransform>()));
                    }
                    else
                    {
                        kvp.Value.basePanel.Close();
                    }
                    anyPanelCreated = true;
                }
            }

            // 如果本次创建了新面板，直接打开；否则切换面板状态
            if (anyPanelCreated)
            {
                foreach (var kvp in inventoryBasePanelCache)
                {
                    var currentPanel = kvp.Value;
                    if (currentPanel != null && !currentPanel.IsOpen())
                    {
                        currentPanel.Open();
                    }
                }
            }
            else
            {
                // 面板已存在，进行切换操作
                foreach (var kvp in inventoryBasePanelCache)
                {
                    var currentPanel = kvp.Value;
                    if (currentPanel != null)
                    {
                        currentPanel.Toggle();
                    }
                }
            }
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        foreach (var inventory in inventoryRefDic.Values)
        {
            inventory.ModUpdate(deltaTime);
        }
    }

    #endregion

    #region 交互方法
    //玩家与此发生交互
    public void Interact_Start(Item item_)
    {
        // 空检查：确保item_存在
        if (item_ == null)
        {
            Debug.LogError("[Mod_Inventory.Interact_Start] item_ 为空！");
            return;
        }

        // 空检查：确保InventoryRefDic存在
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
        {
            Debug.LogError("[Mod_Inventory.Interact_Start] InventoryRefDic 为空或未初始化！");
            return;
        }



        foreach (var kvp in InventoryRefDic)
        {
            EnsurePanelCreated(kvp.Value);
            if (kvp.Value.basePanel == null)
            {
                Debug.LogWarning($"[Mod_Inventory.Interact_Start] InventoryRefDic 中存在空的 basePanel");
                continue;
            }
            kvp.Value.Interact_Start(item_);
        }

        if (item_.itemMods == null)
        {
            Debug.LogError("[Mod_Inventory.Interact_Start] item_.itemMods 为空！");
            return;
        }

        item_.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod);
        if (handMod == null) return;

        // 设置所有Inventory的默认目标
        foreach (var kvp in InventoryRefDic)
        {
            var currentInventory = kvp.Value;
            currentInventory.DefaultTarget_Inventory = handMod.inventory;
        }
    }


    //玩家结束交互
    public void Interact_Stop(Item item_)
    {
        // 清除所有Inventory的默认目标
        foreach (var kvp in InventoryRefDic)
        {
            var currentInventory = kvp.Value;
            currentInventory.DefaultTarget_Inventory = null;
        }

        // 关闭所有面板
        foreach (var kvp in inventoryBasePanelCache)
        {
            var currentPanel = kvp.Value;
            if (currentPanel != null)
            {
                currentPanel.Close();
            }
        }
    }
    #endregion

    #region 保存方法

    [Button]
    public override void Save()
    {
        // 保存面板开关状态 - 仅保存第一个面板的状态作为参考
        if (inventoryBasePanelCache.Count > 0)
        {
            var firstPanel = inventoryBasePanelCache.First().Value;
            if (firstPanel != null)
            {
                Data.BasePanelIsOpen = firstPanel.IsOpen();
            }
        }

        // 保存面板位置 - 仅保存第一个面板的位置作为参考
        // 保存每个 inventory 对应面板的位置到其 Inventory_Data.PanelPosition 中
        if (inventoryBasePanelCache.Count > 0)
        {
            foreach (var kvp in inventoryBasePanelCache)
            {
                var inv = kvp.Key;
                var panel = kvp.Value;
                if (inv != null && inv.Data != null && panel != null)
                {
                    RectTransform rt = null;
                    if (panel.Dragger != null)
                        rt = panel.Dragger.GetComponent<RectTransform>();
                    if (rt == null)
                        rt = panel.GetComponent<RectTransform>();

                    if (rt != null)
                    {
                        var anchored = rt.anchoredPosition;
                        if (IsValidVector2(anchored))
                        {
                            inv.Data.PanelPosition = new Vector3(anchored.x, anchored.y, inv.Data.PanelPosition.z);
                        }
                    }
                }
            }
        }

        // 保存所有Inventory的数据
        foreach (var kvp in InventoryRefDic)
        {
            kvp.Value.Save();
            Data.Data[kvp.Key] = kvp.Value.Data;
        }

        Item_Data.ModuleDataDic[_Data.Name] = Data;
    }
    #endregion

    #region 辅助方法
    // 辅助方法：检查Vector2是否有效
    private bool IsValidVector2(Vector2 vector)
    {
        // 检查是否包含无效值
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
    }

    // 检测物品是否可拾取，如果可拾取则隐藏面板// 检测物品是否可拾取，如果可拾取则隐藏所有面板
    private void CheckAndHidePanelIfPickable()
    {
        // 检查物品数据是否存在且物品可拾取
        if (item != null && item.itemData != null && item.itemData.Stack.CanBePickedUp)
        {
            // 隐藏所有缓存的面板
            foreach (var kvp in inventoryBasePanelCache)
            {
                var currentPanel = kvp.Value;
                if (currentPanel != null)
                {
                    currentPanel.Close();
                    Debug.Log($"物品 {item.name} 可拾取，自动隐藏面板");
                }
            }
        }
        // 如果物品不可拾取，则保持面板当前状态（不强制显示）
    }


    // 公共方法：根据物品可拾取状态更新面板显示
    public void UpdatePanelVisibilityBasedOnPickableState()
    {
        CheckAndHidePanelIfPickable();
    }

    // 延迟一帧将面板置于最前方的协程方法
    private IEnumerator DelayedBringToFront(RectTransform rectTransform)
    {
        yield return null; // 等待一帧

        BasePanel.BringToFront(rectTransform);

    }
    #endregion
}

public interface IInventory
{
    #region 接口属性和方法
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get; set; }

    [Tooltip("默认返回的目标Inventory")]
    public Inventory GetDefaultTargetInventory()
    {
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
            return null;

        // 返回第一个Inventory
        return InventoryRefDic.Values.First();
    }

    [Tooltip("随机返回一个Inventory")]
    public Inventory GetRandomTargetInventory()
    {
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
            return null;

        // 将值转换为数组并随机选择一个
        var inventories = InventoryRefDic.Values.ToArray();
        int randomIndex = UnityEngine.Random.Range(0, inventories.Length);
        return inventories[randomIndex];
    }
    #endregion
}