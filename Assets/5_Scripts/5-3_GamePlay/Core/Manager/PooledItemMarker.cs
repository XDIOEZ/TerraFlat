using UnityEngine;

public sealed class PooledItemMarker : MonoBehaviour
{
    public string PoolKey;
    public bool InPool;
    public bool PoolingDisabled;

    private Transform[] _transforms;
    private Vector3[] _localPositions;
    private Quaternion[] _localRotations;
    private Vector3[] _localScales;

    public void CaptureBaseline()
    {
        _transforms = GetComponentsInChildren<Transform>(true);
        _localPositions = new Vector3[_transforms.Length];
        _localRotations = new Quaternion[_transforms.Length];
        _localScales = new Vector3[_transforms.Length];

        for (int i = 0; i < _transforms.Length; i++)
        {
            _localPositions[i] = _transforms[i].localPosition;
            _localRotations[i] = _transforms[i].localRotation;
            _localScales[i] = _transforms[i].localScale;
        }
    }

    public bool HasOriginalHierarchy()
    {
        return _transforms != null && GetComponentsInChildren<Transform>(true).Length == _transforms.Length;
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
}

public interface IItemPoolLifecycle
{
    void OnItemTakenFromPool();
    void OnItemReturnedToPool();
}
