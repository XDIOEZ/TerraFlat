using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Buff 管理系统，负责管理所有 Buff 的添加、移除、运行和保存
/// </summary>
public class BuffManager : Module
{
    #region 字段与属性
    [ShowInInspector]
    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic = new Dictionary<string, BuffRunTime>();

    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data 
    { 
        get { return ModData; } 
        set { ModData = (Ex_ModData_MemoryPackable)value; } 
    }

    /// <summary>
    /// Buff 接收者（通常是 Item）
    /// </summary>
    private Item buffReceiver;
    #endregion

    #region 初始化与生命周期
    public new void Awake()
    {
        base.Awake();
        _Data.ID = ModText.BuffManager;
        buffReceiver = GetComponent<Item>();
        
        if (buffReceiver == null)
        {
            Debug.LogWarning($"?? BuffManager 找不到 Item 组件");
        }
    }
    public override void Load()
    {
        if (ModData == null)
        {
            Debug.LogError("? ModData 为 null，无法加载 Buff 数据");
            return;
        }

        ModData.ReadData(ref BuffRunTimeData_Dic);

  InitializeBuffs();
    }

    public override void Save()
    {
        if (ModData == null)
        {
            Debug.LogError("? ModData 为 null，无法保存 Buff 数据");
            return;
        }

        ModData.WriteData(BuffRunTimeData_Dic);
    }
    #endregion

    #region Buff 初始化
    /// <summary>
    /// 初始化所有 Buff 数据
    /// </summary>
    private void InitializeBuffs()
    {
        if (BuffRunTimeData_Dic == null || BuffRunTimeData_Dic.Count == 0)
            return;

        // 确保接收者引用有效
        if (buffReceiver == null)
        {
            buffReceiver = GetComponent<Item>();
        }

        // 同步 Data 后，初始化 Buff 的接收者信息：
        // 优先通过持久化的 Guid 从 ItemMgr 中查找对应 Item，找不到则退回到本地 buffReceiver
        foreach (var buff in BuffRunTimeData_Dic.Values)
        {
            if (buff == null) continue;

            Item receiver = buffReceiver;
            Item sender = null;

            if (ItemMgr.Instance != null)
            {
                // 根据持久化的 Guid 重新绑定接收者
                if (buff.receiverGuid != 0)
                {
                    var guidReceiver = ItemMgr.Instance.GetItemByGuid(buff.receiverGuid);
                    if (guidReceiver != null)
                    {
                        receiver = guidReceiver;
                    }
                }
                else
                {
                     var guidReceiver = ItemMgr.Instance.GetItemByGuid(buff.receiverGuid);
                    if (guidReceiver != null)
                    {
                        receiver = guidReceiver;
                    }
                    Debug.LogWarning($"?? Buff {buff.buff_IDName} 的 receiverGuid 为 0，无法从 ItemMgr 还原接收者");
                }

                // 根据持久化的 Guid 重新绑定发送者（如果有）
                if (buff.senderGuid != 0)
                {
                    var guidSender = ItemMgr.Instance.GetItemByGuid(buff.senderGuid);
                    if (guidSender != null)
                    {
                        sender = guidSender;
                    }
                }
                else if (sender == null)
                {
                    Debug.LogWarning($"?? Buff {buff.buff_IDName} 的 senderGuid 为 0，无法从 ItemMgr 还原发送者");
                }
            }

            // 这里传入的 sender/receiver 可能为 null，SetBuffData 会只更新非空的引用和 Guid
            buff.SetBuffData(sender: sender, receiver: receiver);
        }

        Debug.Log($"? 已初始化 {BuffRunTimeData_Dic.Count} 个 Buff");
    }
    #endregion

    #region Buff 添加
    /// <summary>
    /// 添加 Buff（简易版本）
    /// </summary>
    /// <param name="buffData_SO">Buff 数据 ScriptableObject</param>
    public void AddBuff(Buff_Data buffData_SO)
    {
        if (buffReceiver == null)
        {
            Debug.LogError("? BuffReceiver 为 null，无法添加 Buff");
            return;
        }

        AddBuffRuntime(buffData_SO, buffReceiver);
    }

    /// <summary>
    /// 添加 Buff 运行时实例
    /// </summary>
    /// <param name="buffData_SO">Buff 数据 ScriptableObject</param>
    /// <param name="receiver">Buff 接收者</param>
    public void AddBuffRuntime(Buff_Data buffData_SO, Item receiver)
    {
        // 验证输入参数
        if (buffData_SO == null)
        {
            Debug.LogWarning("?? buffData_SO 为空，无法添加 Buff");
            return;
        }

        if (receiver == null)
        {
            Debug.LogWarning("?? Buff 接收者为空，无法添加 Buff");
            return;
        }

        if (string.IsNullOrEmpty(buffData_SO.buff_ID))
        {
            Debug.LogError("? Buff ID 为空，无法添加 Buff");
            return;
        }

        // 检查是否应该应用此 Buff
        if (!ShouldApplyBuff(buffData_SO))
        {
            Debug.Log($"? Buff {buffData_SO.buff_ID} 跳过应用（已存在且不可叠加）");
            return;
        }

        // 克隆 Buff_Data 以避免修改原始 ScriptableObject
        Buff_Data clonedBuffData = buffData_SO.Clone();

        if (clonedBuffData == null)
        {
            Debug.LogError("? 克隆 Buff 数据失败");
            return;
        }

        // 创建 Buff 运行时实例
        BuffRunTime newBuff = new BuffRunTime
        {
            buff_IDName = clonedBuffData.buff_ID,
            buff = clonedBuffData,
            buff_Receiver = receiver,
        };

        AddBuffByData(newBuff);
    }

    /// <summary>
    /// 检查是否应该应用此 Buff（根据已有的 Buff 和叠加类型判断）
    /// </summary>
    /// <param name="buffData">要应用的 Buff 数据</param>
    /// <returns>true 应该应用，false 应该跳过</returns>
    private bool ShouldApplyBuff(Buff_Data buffData)
    {
        if (buffData == null || string.IsNullOrEmpty(buffData.buff_ID))
            return false;

        // 如果此 Buff 还不存在，应该应用
        if (!BuffRunTimeData_Dic.ContainsKey(buffData.buff_ID))
        {
            return true;
        }

        // Buff 已存在，根据叠加类型判断
        switch (buffData.buff_StackType)
        {
            case BuffStackType.DurationAdd:
                // 延长持续时间，应该应用
                Debug.Log($"? Buff {buffData.buff_ID} 将延长持续时间");
                return true;

            case BuffStackType.RefreshDuration:
                // 刷新持续时间，应该应用
                Debug.Log($"? Buff {buffData.buff_ID} 将刷新持续时间");
                return true;

            case BuffStackType.StackCount:
                // 堆叠计数，检查是否达到上限
                if (BuffRunTimeData_Dic.TryGetValue(buffData.buff_ID, out var buffWithStack))
                {
                    if (buffWithStack.buff_CurrentStack < buffData.buff_MaxStack)
                    {
                        Debug.Log($"? Buff {buffData.buff_ID} 可以继续堆叠");
                        return true;
                    }
                    else
                    {
                        Debug.Log($"?? Buff {buffData.buff_ID} 已达最大堆叠数，不可继续堆叠");
                        return false;
                    }
                }
                return true;

            case BuffStackType.Keep:
                // 保持现有状态，不应用新的 Buff
                Debug.Log($"? Buff {buffData.buff_ID} 保持现有状态，不应用");
                return false;

            default:
                Debug.LogWarning($"?? 未知的 Buff 叠加类型: {buffData.buff_StackType}");
                return true;
        }
    }

    /// <summary>
    /// 通过 BuffRunTime 数据添加 Buff
    /// </summary>
    /// <param name="newBuff">新的 Buff 运行时数据</param>
    private void AddBuffByData(BuffRunTime newBuff)
    {
        if (newBuff == null)
        {
            Debug.LogError("? newBuff 为 null");
            return;
        }

        string buffID = newBuff.buff_IDName;

        if (BuffRunTimeData_Dic.TryGetValue(buffID, out var existingBuff))
        {
            // Buff 已存在，根据叠加类型处理
            HandleBuffStack(newBuff, existingBuff);
        }
        else
        {
            // 第一次添加该 Buff
            BuffRunTimeData_Dic[buffID] = newBuff;
            newBuff.OnBuff_Start();
//            Debug.Log($"? 添加新 Buff: {buffID}");
        }
    }

    /// <summary>
    /// 处理 Buff 叠加逻辑
    /// </summary>
    private void HandleBuffStack(BuffRunTime newBuff, BuffRunTime existingBuff)
    {
        if (existingBuff == null || newBuff?.buff == null)
        {
            Debug.LogWarning("?? Buff 数据为 null，无法处理叠加");
            return;
        }

        string buffID = newBuff.buff_IDName;

        switch (newBuff.buff.buff_StackType)
        {
            case BuffStackType.DurationAdd:
                // 延长现有 Buff 的持续时间
                float remainingTime = newBuff.buff.buff_Duration - existingBuff.buff_CurrentDuration;
                existingBuff.buff_CurrentDuration += remainingTime;
                Debug.Log($"? Buff {buffID} 延长持续时间：+{remainingTime}s");
                break;

            case BuffStackType.RefreshDuration:
                // 刷新持续时间
                existingBuff.buff_CurrentDuration = 0;
                Debug.Log($"? Buff {buffID} 刷新持续时间");
                break;

            case BuffStackType.StackCount:
                // 增加堆叠数
                if (existingBuff.buff_CurrentStack < existingBuff.buff.buff_MaxStack)
                {
                    existingBuff.buff_CurrentStack += 1;
                    Debug.Log($"? Buff {buffID} 堆叠数增加：{existingBuff.buff_CurrentStack}/{existingBuff.buff.buff_MaxStack}");
                }
                else
                {
                    // 达到最大堆叠，重置持续时间
                    existingBuff.buff_CurrentDuration = 0;
                    Debug.Log($"?? Buff {buffID} 达到最大堆叠，持续时间已重置");
                }
                break;

            case BuffStackType.Keep:
                // 保持现有状态，什么都不做
                Debug.Log($"? Buff {buffID} 保持现有状态（不叠加）");
                break;

            default:
                Debug.LogWarning($"?? 未知的 Buff 叠加类型: {newBuff.buff.buff_StackType}");
                break;
        }
    }
    #endregion

    #region Buff 查询
    /// <summary>
    /// 检查是否存在指定 ID 的 Buff
    /// </summary>
    /// <param name="buffId">Buff ID</param>
    /// <returns>是否存在</returns>
    public bool HasBuff(string buffId)
    {
        if (string.IsNullOrEmpty(buffId))
            return false;

        return BuffRunTimeData_Dic.ContainsKey(buffId);
    }

    /// <summary>
    /// 获取指定 ID 的 Buff 运行时数据
    /// </summary>
    /// <param name="buffId">Buff ID</param>
    /// <param name="buff">输出的 Buff 数据</param>
    /// <returns>是否获取成功</returns>
    public bool TryGetBuff(string buffId, out BuffRunTime buff)
    {
        return BuffRunTimeData_Dic.TryGetValue(buffId, out buff);
    }
    #endregion

    #region Buff 移除
    /// <summary>
    /// 移除指定 ID 的 Buff
    /// </summary>
    /// <param name="buffId">Buff ID</param>
    public void RemoveBuff(string buffId)
    {
        if (string.IsNullOrEmpty(buffId))
        {
            Debug.LogWarning("?? Buff ID 为空");
            return;
        }

        if (!BuffRunTimeData_Dic.TryGetValue(buffId, out var buff))
        {
            Debug.LogWarning($"?? 找不到 Buff: {buffId}");
            return;
        }

        buff?.OnBuff_Stop();
        BuffRunTimeData_Dic.Remove(buffId);
      //  Debug.Log($"? 移除 Buff: {buffId}");
    }

    /// <summary>
    /// 清除所有 Buff
    /// </summary>
    public void ClearAllBuffs()
    {
        foreach (var buff in BuffRunTimeData_Dic.Values)
        {
            buff?.OnBuff_Stop();
        }

        BuffRunTimeData_Dic.Clear();
       // Debug.Log($"? 已清除所有 Buff");
    }
    #endregion

    #region Buff 更新
    /// <summary>
    /// 固定更新，处理所有 Buff 的运行逻辑
    /// </summary>
    public void FixedUpdate()
    {
        if (BuffRunTimeData_Dic.Count == 0)
            return;

        // 遍历并运行所有 Buff
        var buffList = new List<BuffRunTime>(BuffRunTimeData_Dic.Values);
        foreach (var buff in buffList)
        {
            if (buff == null) continue;
            buff.Run();
        }
    }
    #endregion
}
