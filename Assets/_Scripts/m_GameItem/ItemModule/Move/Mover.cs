using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mover 用于处理游戏对象的移动逻辑
/// </summary>
public partial class Mover : Module, IMove
{
    #region 保存数据类
    [System.Serializable]
    [MemoryPack.MemoryPackable]
    public partial class Mover_SaveData
    {
        [Header("移动设置")]
        public GameValue_float Speed = new GameValue_float(10f);
        public float slowDownSpeed = 5f;
        public float endSpeed = 0.1f;

        // 可根据需要添加更多需要保存的字段
    }
    #endregion

    #region 字段

    [Header("移动设置")]
    [Tooltip("速度源")]
    [SerializeField]
    public Mover_SaveData saveData = new Mover_SaveData();

    // 将需要保存的字段改为属性，引用saveData中的字段
    public GameValue_float Speed
    {
        get => saveData.Speed;
        set => saveData.Speed = value;
    }

    public float slowDownSpeed
    {
        get => saveData.slowDownSpeed;
        set => saveData.slowDownSpeed = value;
    }

    public float endSpeed
    {
        get => saveData.endSpeed;
        set => saveData.endSpeed = value;
    }

    [Tooltip("移动目标")]
    public Vector3 TargetPosition;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    [Tooltip("移动结束事件")]
    public UltEvent OnMoveEnd;

    [Tooltip("移动持续事件")]
    public UltEvent OnMoveStay;

    [Tooltip("移动开始事件")]
    public UltEvent OnMoveStart;

    private InputAction moveAction;

    public Mod_Stamina stamina;

    //每秒精力消耗速度
    public GameValue_float staminaConsumeSpeed = new(1);

    #endregion

    #region 属性

    protected Rigidbody2D Rb;

    #endregion

    #region 数据管理
    public Ex_ModData_MemoryPack ModDataMemoryPack = new Ex_ModData_MemoryPack();

    public override ModuleData _Data { get => ModDataMemoryPack; set => ModDataMemoryPack = (Ex_ModData_MemoryPack)value; }
    #endregion

    #region Unity 生命周期

    public override void Awake()
    {
        if (_Data.Name == "")
        {
            _Data.Name = ModText.Mover;
        }
    }

    public override void Load()
    {
        ModDataMemoryPack.ReadData(ref saveData);

        Rb = GetComponentInParent<Rigidbody2D>();

        if (item.Mods.ContainsKey(ModText.Controller))
        {
            var controller = item.Mods[ModText.Controller].GetComponent<PlayerController>();
            moveAction = controller._inputActions.Win10.Move_Player;
        }

        if (item.Mods.ContainsKey(ModText.Stamina))
        {
            stamina = item.Mods[ModText.Stamina].GetComponent<Mod_Stamina>();
        }
    }

    public void Update()
    {
        if (moveAction == null) return;

        Vector2 input = moveAction.ReadValue<Vector2>();

        if (input.sqrMagnitude > 0.001f)
        {
            Vector2 target = Rb.position + input.normalized;
            Move(target);
            OnMoveStay?.Invoke();
            if (stamina != null)
                stamina.CurrentValue -= Time.deltaTime * staminaConsumeSpeed.Value;
        }
        else
        {
            Move(Rb.position); // 触发停止检测
        }
    }

    #endregion

    #region 公共方法
    public virtual void Move(Vector2 TargetPosition)
    {
        bool isZero = Vector2.Distance(Rb.position, TargetPosition) < endSpeed;

        // 移动起止状态切换
        if (IsMoving != !isZero)
        {
            IsMoving = !isZero;
            if (IsMoving)
                OnMoveStart?.Invoke();
            else
                OnMoveEnd?.Invoke();
        }

        if (isZero)
        {
            Rb.velocity = Vector2.zero;
            return;
        }

        // 计算最终速度
        Rb.velocity = (TargetPosition - Rb.position).normalized * Speed.Value;
    }

    #endregion

    #region 私有方法

    public override void Save()
    {
         ModDataMemoryPack.WriteData(saveData);
        item.Item_Data.ModuleDataDic[_Data.Name] = _Data;
    }

    #endregion
}