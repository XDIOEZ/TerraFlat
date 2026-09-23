using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 由 Item 持有的纯运行时回池状态，不在生成热路径动态创建 MonoBehaviour。
/// 记录完成 JSON 模块装配后的层级与局部变换。
/// 回池前校验子节点身份，取出时只恢复内部节点；根位置由本次生成请求决定。
/// </summary>
public sealed class PooledItemMarker
{
    #region 层级基线

    private readonly Item owner;
    public string PoolKey { get => owner.PoolKey; set => owner.PoolKey = value; }
    public bool InPool { get => owner.IsInPool; set => owner.IsInPool = value; }
    public bool PoolingDisabled { get => owner.PoolingDisabled; set => owner.PoolingDisabled = value; }

    private Transform[] _transforms;
    private Vector3[] _localPositions;
    private Quaternion[] _localRotations;
    private Vector3[] _localScales;
    private readonly List<Transform> _hierarchyBuffer = new(16);

    public PooledItemMarker(Item owner)
    {
        this.owner = owner ?? throw new System.ArgumentNullException(nameof(owner));
    }

    public void CaptureBaseline()
    {
        if (_transforms != null)
            return;

        owner.GetComponentsInChildren(true, _hierarchyBuffer);
        _transforms = new Transform[_hierarchyBuffer.Count];
        _localPositions = new Vector3[_transforms.Length];
        _localRotations = new Quaternion[_transforms.Length];
        _localScales = new Vector3[_transforms.Length];

        for (int i = 0; i < _transforms.Length; i++)
        {
            Transform target = _hierarchyBuffer[i];
            _transforms[i] = target;
            _localPositions[i] = target.localPosition;
            _localRotations[i] = target.localRotation;
            _localScales[i] = target.localScale;
        }
        _hierarchyBuffer.Clear();
    }

    public bool HasOriginalHierarchy()
    {
        if (_transforms == null)
            return false;

        owner.GetComponentsInChildren(true, _hierarchyBuffer);
        bool matches = _hierarchyBuffer.Count == _transforms.Length;
        for (int i = 0; matches && i < _transforms.Length; i++)
            matches = _hierarchyBuffer[i] == _transforms[i];
        _hierarchyBuffer.Clear();
        return matches;
    }

    public void RestoreBaseline()
    {
        if (_transforms == null)
            return;

        for (int i = 1; i < _transforms.Length; i++)
        {
            Transform target = _transforms[i];
            if (target == null)
                continue;

            target.localPosition = _localPositions[i];
            target.localRotation = _localRotations[i];
            target.localScale = _localScales[i];
        }
    }

    #endregion
}

public interface IItemPoolLifecycle
{
    void OnItemTakenFromPool();
    void OnItemReturnedToPool();
}
