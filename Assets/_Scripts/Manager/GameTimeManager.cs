using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameTimeManager : SingletonMono<GameTimeManager>
{/*
    protected override void Awake()
    {
        DontDestroyOnLoad(gameObject); // 跨场景保持存在
    }
    #region 时间管理属性
    [Header("时间设置")]
    [SerializeField] private float _timeScale = 1f;
    public float TimeScale
    {
        get => _timeScale;
        set
        {
            _timeScale = Mathf.Max(0f, value); // 防止负数
        }
    }

    [SerializeField] private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value) return;

            _isPaused = value;
            Time.timeScale = _isPaused ? 0f : _timeScale; // 控制全局时间缩放
      
        }
    }


    #endregion

    #region 时间更新逻辑
    private void Update()
    {

    }

    #endregion

    #region 公共方法
    public void Pause()
    {
        IsPaused = true;
    }

    public void Resume()
    {
        IsPaused = false;
    }

    public void SetTimeScale(float scale)
    {
        TimeScale = scale;
    }
    #endregion*/
}
