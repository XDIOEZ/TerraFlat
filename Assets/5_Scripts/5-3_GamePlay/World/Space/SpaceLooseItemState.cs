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
        [Newtonsoft.Json.JsonExtensionData] public System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken> UnknownOptionalFields;
        public string Id = Guid.NewGuid().ToString("N"), Snapshot, WorldKey;
        public bool InSpace;
        public SpaceMotionState Motion = new();
        public double RotationDegrees;
        public double AngularVelocityDegreesPerSecond;
        public double SurfaceHeight, SurfaceVerticalSpeed;
        public string SupportShipId;
        public double SupportLocalX, SupportLocalY, DropRemainingSeconds;
        public double DropVelocityX, DropVelocityY;
        public double PickupAfterSimulationSeconds;
        public bool CanBePickedUpAt(double seconds) => double.IsFinite(seconds) && seconds >= PickupAfterSimulationSeconds;
        [JsonIgnore]
        public Vector3 Scale = Vector3.one;
        [JsonIgnore] private System.Collections.Generic.IDictionary<string, JToken> unknownScaleFields;
        [JsonProperty("Scale", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        private ScaleSnapshot SavedScale
        {
            get => new ScaleSnapshot { X = Scale.x, Y = Scale.y, Z = Scale.z, UnknownOptionalFields = unknownScaleFields };
            set
            {
                if (value == null) return;
                Scale = new Vector3(value.X, value.Y, value.Z);
                unknownScaleFields = value.UnknownOptionalFields;
            }
        }

        // 缩放只保存三个分量和未知扩展字段。
        private sealed class ScaleSnapshot
        {
            [JsonProperty("x")] public float X;
            [JsonProperty("y")] public float Y;
            [JsonProperty("z")] public float Z;
            [JsonExtensionData] public System.Collections.Generic.IDictionary<string, JToken> UnknownOptionalFields;
        }

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
