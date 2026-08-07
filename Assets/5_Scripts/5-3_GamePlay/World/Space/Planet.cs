using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Planet : MonoBehaviour
{
    [ShowInInspector]
    public PlanetData planetData; // 行星运行数据

    [SerializeField]
    public Transform OrbitCenter; // 该行星自己的公转中心

    public Vector3 GetOrbitCenterPosition()
    {
        if (OrbitCenter == null)
        {
            throw new System.InvalidOperationException($"[Planet] OrbitCenter 为空，行星对象: {name}");
        }

        return OrbitCenter.position;
    }
}
