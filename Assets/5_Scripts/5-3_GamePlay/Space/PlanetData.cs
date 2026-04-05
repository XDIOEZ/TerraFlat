using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class PlanetData
{
   public string name; // 兼容旧字段
   public string BodyId; // 行星自身标识ID
   public string PrefabName; // 运行时使用的预制体名称
   public string OrbitCenterBodyId; // 围绕星体的标识ID
   public float OrbitRadius = 20f; // 轨道半径
   public float OrbitHeight = 0f; // 2D中作为Z轴偏移
   public float OrbitAngularSpeed = 8f; // 轨道角速度（度/秒）
   public float OrbitStartAngle = 0f; // 初始角度（度）
   public bool OrbitClockwise = false; // 是否顺时针公转
   public float SelfRotateSpeed = 20f; // 自转速度（度/秒）

   [System.NonSerialized]
   public float RuntimeAngle; // 运行时当前角度

   [System.NonSerialized]
   public List<Vector3> RuntimeOrbitTrail = new(); // 运行时轨迹点

   public string RuntimePlanetName
   {
      get
      {
         if (!string.IsNullOrEmpty(Name)) return Name;
         if (!string.IsNullOrEmpty(name)) return name;
         return PrefabName;
      }
   }

   public void InitializeRuntime() // 初始化运行时状态
   {
      RuntimeAngle = OrbitStartAngle;
      RuntimeOrbitTrail.Clear();
   }

   public void RunPlanet(Transform planetTransform, Vector3 orbitCenter, float deltaTime) // 行星运行
   {
      if (planetTransform == null)
      {
         throw new System.ArgumentNullException(nameof(planetTransform), "planetTransform 不能为空");
      }

      float direction = OrbitClockwise ? -1f : 1f;
      RuntimeAngle += OrbitAngularSpeed * direction * deltaTime;

      float radian = RuntimeAngle * Mathf.Deg2Rad;
      Vector3 newPosition = orbitCenter + new Vector3(
         Mathf.Cos(radian) * OrbitRadius,
         Mathf.Sin(radian) * OrbitRadius,
         OrbitHeight
      );

      planetTransform.position = newPosition;

      if (SelfRotateSpeed != 0f)
      {
         planetTransform.Rotate(Vector3.forward, SelfRotateSpeed * deltaTime, Space.Self);
      }

      RuntimeOrbitTrail.Add(newPosition);
      if (RuntimeOrbitTrail.Count > 240)
      {
         RuntimeOrbitTrail.RemoveAt(0);
      }
   }
}
