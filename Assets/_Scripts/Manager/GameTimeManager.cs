using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameTimeManager : SingletonMono<GameTimeManager>
{/*
    protected override void Awake()
    {
        DontDestroyOnLoad(gameObject); // �糡�����ִ���
    }
    #region ʱ���������
    [Header("ʱ������")]
    [SerializeField] private float _timeScale = 1f;
    public float TimeScale
    {
        get => _timeScale;
        set
        {
            _timeScale = Mathf.Max(0f, value); // ��ֹ����
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
            Time.timeScale = _isPaused ? 0f : _timeScale; // ����ȫ��ʱ������
      
        }
    }


    #endregion

    #region ʱ������߼�
    private void Update()
    {

    }

    #endregion

    #region ��������
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
