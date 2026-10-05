using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 资源接触伤害

        private sealed partial class Record
        {
            public ContactDamageRuntime ContactDamage;
        }

        private static readonly HashSet<Record> contactDamageSources = new();
        private static readonly List<Record> contactDamageBatch = new();
        private static float contactDamageElapsed;

        private static void RefreshContactDamageSource(Record record)
        {
            if (record.Profile.ContactDamage == null)
            {
                RemoveContactDamageSource(record);
                return;
            }
            record.ContactDamage ??= new ContactDamageRuntime(() => record.IsValid && record.ContactDamage != null);
            contactDamageSources.Add(record);
        }

        private static void RemoveContactDamageSource(Record record)
        {
            contactDamageSources.Remove(record);
            record.ContactDamage = null;
        }

        private static void ClearContactDamageSources()
        {
            contactDamageSources.Clear();
            contactDamageBatch.Clear();
            contactDamageElapsed = 0f;
        }

        private static void TickContactDamage(float deltaTime)
        {
            if (!GameNetwork.HasStateAuthority || deltaTime <= 0f || contactDamageSources.Count == 0) return;
            contactDamageElapsed += deltaTime;
            if (contactDamageElapsed < ContactDamageRuntime.QueryInterval) return;
            contactDamageElapsed = 0f; // 卡顿或区块恢复后不补发历史伤害。
            DimensionManager dimension = DimensionManager.ExistingInstance;
            if (dimension == null || !dimension.ActiveAddress.IsValid) return;
            string worldKey = dimension.ActiveAddress.WorldKey;
            contactDamageBatch.Clear();
            contactDamageBatch.AddRange(contactDamageSources);
            for (int index = 0; index < contactDamageBatch.Count; index++)
            {
                Record record = contactDamageBatch[index];
                if (!record.IsValid || record.ContactDamage == null ||
                    record.DimensionId != dimension.ActiveAddress.DimensionId) continue;
                var body = simulation.GetBody(record.Handle.Id);
                if (body.Suspended != 0) continue;
                var identity = new CombatIdentity
                {
                    Backend = CombatBackend.Entity,
                    Value = (1UL << 63) | (uint)record.Handle.Id,
                    Generation = (uint)record.Handle.Generation
                };
                Matrix4x4 root = BodyMatrix(body);
                Vector3 localPosition = WorldLocalPresentation.ProjectPosition(new Vector3(body.Position.x, body.Position.y));
                root.SetColumn(3, new Vector4(localPosition.x, localPosition.y, localPosition.z, 1f));
                record.ContactDamage.Tick(record.Profile.ContactDamage, root, identity,
                    worldKey, record.Snapshot.FactionId);
            }
            contactDamageBatch.Clear();
        }

        #endregion
    }
}
