using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    #region 散落物保存模型
    [Serializable]
    public sealed class SpaceLooseItemState
    {
        public string Id = Guid.NewGuid().ToString("N"), Snapshot, WorldKey;
        public bool InSpace;
        public SpaceMotionState Motion = new();
        public double RotationDegrees;
        public double AngularVelocityDegreesPerSecond;
        public double SurfaceHeight, SurfaceVerticalSpeed;
        [JsonConverter(typeof(ScaleJsonConverter))]
        public Vector3 Scale = Vector3.one;

        // 缩放只保存三个分量，避免 Vector3 的 normalized 等计算属性递归进入存档。
        public sealed class ScaleJsonConverter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WriteEndObject();
            }

            public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                JObject components = JObject.Load(reader);
                return new Vector3(components.Value<float>("x"), components.Value<float>("y"), components.Value<float>("z"));
            }
        }
    }
    #endregion
}
