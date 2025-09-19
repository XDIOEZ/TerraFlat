using System;
using UnityEngine;
using XLua;
using Sirenix.OdinInspector;

[Serializable]
public class ItemTransformData
{
    [Tooltip("控制物品变换的Lua脚本")]
    [TextArea(5, 10)]
    public string Function_Lua;
}

public class Mod_NewItemTransformController : Module
{
    #region 数据与Lua环境
    public Ex_ModData ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData)value; } }

    public ItemTransformData Data = new ItemTransformData();

    private Item _spawnedItem;
    private string _currentItemName;
    private const string DEFAULT_ITEM_NAME = "DefaultItem";

    private LuaEnv _luaEnv;
    private LuaFunction _updateFunction;
    private float _currentAngle = 0;
    private bool _isLuaEnvDisposed = false; // 跟踪Lua环境是否已释放

    [Tooltip("是否在激活时强制重新加载Lua脚本")]
    public bool forceReloadOnAct = true;
    #endregion

    #region 生命周期
    public override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(_Data.ID))
            _Data.ID = "ItemTransformController";

        if (ItemMgr.Instance == null)
        {
            Debug.LogError("ItemMgr.Instance 未初始化！");
        }

        InitializeLuaEnv();
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref Data);

        if (string.IsNullOrEmpty(Data.Function_Lua))
        {
            Data.Function_Lua = GetDefaultOrbitScript();
            Debug.LogWarning("使用默认Lua脚本");
        }

        InitializeLuaScript();
        SpawnItemAsChild();
    }

    public override void Save()
    {
        DestroySpawnedItem();
        ModSaveData.WriteData(Data);
    }

    public override void Act()
    {
        base.Act();

        Debug.Log("触发热更新，重新加载Lua脚本");

        if (string.IsNullOrEmpty(Data.Function_Lua))
        {
            Data.Function_Lua = GetDefaultOrbitScript();
            Debug.LogWarning("使用默认Lua脚本");
        }

        // 恢复Act方法中的必要逻辑，确保脚本正确重载
        if (_updateFunction != null)
        {
            _updateFunction.Dispose();
            _updateFunction = null;
        }

        if (forceReloadOnAct)
            InitializeLuaEnv();

        _isLuaEnvDisposed = false;
        InitializeLuaScript();
        SpawnItemAsChild();
    }

    public override void ModUpdate(float deltaTime)
    {
        base.ModUpdate(deltaTime);

        if (_spawnedItem == null || _updateFunction == null || _isLuaEnvDisposed)
            return;

        try
        {
            _updateFunction.Call(deltaTime);
        }
        catch (InvalidOperationException ex)
        {
            Debug.LogError($"Lua环境已释放但仍被调用: {ex.Message}");
            _updateFunction = null;
        }
    }

    private void OnDestroy()
    {
        DisposeLuaResources();
        DestroySpawnedItem();
    }

    private void DisposeLuaResources()
    {
        if (_updateFunction != null)
        {
            _updateFunction.Dispose();
            _updateFunction = null;
        }

        if (_luaEnv != null)
        {
            _luaEnv.Dispose();
            _luaEnv = null;
            _isLuaEnvDisposed = true;
        }
    }
    #endregion

    #region Lua环境与脚本
    private void InitializeLuaEnv()
    {
        DisposeLuaResources();

        _luaEnv = new LuaEnv();
        _luaEnv.AddLoader(CustomLuaLoader);
        _isLuaEnvDisposed = false;
    }

    private void InitializeLuaScript()
    {
        if (_luaEnv == null)
        {
            Debug.LogError("Lua环境未初始化，无法加载脚本");
            return;
        }

        if (_updateFunction != null)
        {
            _updateFunction.Dispose();
            _updateFunction = null;
        }

        // 向Lua暴露必要的对象和类，新增Quaternion相关
        _luaEnv.Global.Set("moduleTransform", transform);
        _luaEnv.Global.Set("controller", this);
        _luaEnv.Global.Set("Vector3D", (Func<float, float, float, Vector3>)((x, y, z) => new Vector3(x, y, z)));

        // 暴露Quaternion相关功能（关键新增部分）
        _luaEnv.Global.Set("Quaternion_identity", Quaternion.identity);
        _luaEnv.Global.Set("Quaternion_AngleAxis", (Func<float, Vector3, Quaternion>)Quaternion.AngleAxis);

        try
        {
            _luaEnv.DoString(Data.Function_Lua);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Lua脚本执行失败: {ex.Message}\n脚本内容: {Data.Function_Lua}");
            _luaEnv.DoString(GetDefaultOrbitScript());
        }

        var config = _luaEnv.Global.Get<LuaTable>("Config");
        if (config == null)
        {
            Debug.LogError("Lua脚本中未定义Config表，使用默认物品名称");
            _currentItemName = DEFAULT_ITEM_NAME;
        }
        else
        {
            var itemName = config.Get<string>("itemName");
            _currentItemName = string.IsNullOrEmpty(itemName) ? DEFAULT_ITEM_NAME : itemName;
            Debug.Log($"从Lua获取物品名称: {_currentItemName}");
        }

        _updateFunction = _luaEnv.Global.Get<LuaFunction>("Update");
        if (_updateFunction == null)
        {
            Debug.LogWarning("Lua脚本中未找到Update函数，使用默认环绕运动");
            _luaEnv.DoString(GetDefaultOrbitScript());
            _updateFunction = _luaEnv.Global.Get<LuaFunction>("Update");
        }

        Debug.Log("Lua脚本已加载/更新");
    }

    private byte[] CustomLuaLoader(ref string filePath)
    {
        return System.Text.Encoding.UTF8.GetBytes(Data.Function_Lua);
    }

    // 更新默认脚本以支持自旋转
    private string GetDefaultOrbitScript()
    {
        return @"
Config = { 
    itemName = ""Apple"",
    orbitRadius = 2.0, 
    orbitSpeed = 1.0, 
    zPosition = 0.0,
    rotateSpeed = 9000.0,   -- 自旋转速度（度/秒）
    rotateAxis = {x=0, y=1, z=0}  -- 旋转轴（Y轴）
}

function Init(itemTransform)
    local centerPos = moduleTransform.position
    local x = centerPos.x + Config.orbitRadius
    local y = centerPos.y
    itemTransform.position = Vector3D(x, y, Config.zPosition)
    itemTransform.rotation = Quaternion_identity  -- 初始无旋转 
end

function Update(deltaTime)
    local centerPos = moduleTransform.position
    local angle = controller:GetCurrentAngle()
    angle = angle + deltaTime * Config.orbitSpeed
    controller:SetCurrentAngle(angle)

    -- 计算轨道位置
    local x = centerPos.x + math.cos(angle) * Config.orbitRadius
    local y = centerPos.y + math.sin(angle) * Config.orbitRadius

    local itemTransform = controller:GetSpawnedItemTransform()
    if itemTransform then
        -- 更新位置
        itemTransform.position = Vector3D(x, y, Config.zPosition)
        
        -- 计算自旋转
        local rotateRadians = math.rad(Config.rotateSpeed) * deltaTime
        local rotateAxis = Vector3D(Config.rotateAxis.x, Config.rotateAxis.y, Config.rotateAxis.z)
        
        -- 应用旋转
        itemTransform.rotation = itemTransform.rotation * Quaternion_AngleAxis(rotateRadians, rotateAxis)
    end
end
        ";
    }

    public void RefreshConfigFromLua()
    {
        if (_luaEnv == null || _isLuaEnvDisposed) return;

        var config = _luaEnv.Global.Get<LuaTable>("Config");
        if (config != null)
        {
            _currentItemName = config.Get<string>("itemName");
        }
    }

    public Transform GetSpawnedItemTransform()
    {
        return _spawnedItem != null ? _spawnedItem.transform : null;
    }
    #endregion

    #region 物品管理
    private void SpawnItemAsChild()
    {
        DestroySpawnedItem();

        if (string.IsNullOrEmpty(_currentItemName))
        {
            Debug.LogError("物品名称为空，使用默认物品");
            _currentItemName = DEFAULT_ITEM_NAME;
        }

        if (ItemMgr.Instance == null)
        {
            Debug.LogError("ItemMgr.Instance 为空，无法生成物品");
            return;
        }

        _spawnedItem = ItemMgr.Instance.InstantiateItem(_currentItemName);
        if (_spawnedItem == null)
        {
            Debug.LogError($"物品生成失败！尝试默认物品: {DEFAULT_ITEM_NAME}");
            _spawnedItem = ItemMgr.Instance.InstantiateItem(DEFAULT_ITEM_NAME);
            if (_spawnedItem == null)
            {
                Debug.LogError($"默认物品 {DEFAULT_ITEM_NAME} 也无法生成");
                return;
            }
        }

        _spawnedItem.transform.SetParent(transform);
        _spawnedItem.transform.localPosition = Vector3.zero;
        _spawnedItem.transform.localRotation = Quaternion.identity;

        var initFunc = _luaEnv.Global.Get<LuaFunction>("Init");
        if (initFunc != null)
        {
            initFunc.Call(_spawnedItem.transform);
            initFunc.Dispose();
        }
        else
        {
            Debug.LogWarning("Lua脚本中未找到Init函数");
        }
    }

    private void DestroySpawnedItem()
    {
        if (_spawnedItem != null)
        {
            Destroy(_spawnedItem.gameObject);
            _spawnedItem = null;
        }
    }
    #endregion

    #region Lua访问角度
    public float GetCurrentAngle() => _currentAngle;
    public void SetCurrentAngle(float angle) => _currentAngle = angle;
    #endregion
}
