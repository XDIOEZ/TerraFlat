using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 世界居民跨世界移交
        private sealed class InteriorEntityTransfer : IDisposable
        {
            private readonly string source, destination;
            private readonly List<(Item Item, Vector3 Position, Quaternion Rotation, Vector2 Velocity, float Angular, bool Resident, List<Item> Children)> moved = new();
            private bool finished;

            internal InteriorEntityTransfer(string source, string destination, Predicate<Vector2> select,
                Func<Vector2, Vector2> project, float rotationDegrees)
            {
                this.source = source; this.destination = destination;
                var items = new List<Item>();
                GameplayWorldRegistry.CopyWorldItems(source, items);
                try
                {
                    using var scope = GameplayWorldRegistry.UseWorld(source);
                    foreach (Item item in items)
                    {
                        if (item == null || item is Player || item.DestructionHandled || item.InHand || item.Owner != null ||
                            item.PersistenceOwner != null || item.itemData == null ||
                            !RuntimeWorldResidentEntityUtility.IsPersistentWorldEntity(item) && item.itemData.Stack?.CanBePickedUp != true ||
                            !select(item.transform.position)) continue;
                        Rigidbody2D body = item.GetComponent<Rigidbody2D>();
                        var original = (Item: item, Position: item.transform.position, Rotation: item.transform.rotation,
                            Velocity: body != null ? body.velocity : Vector2.zero, Angular: body != null ? body.angularVelocity : 0f,
                            Resident: item.itemData.PhysicsState?.PersistentWorldResident == true, Children: new List<Item>());
                        Item[] children = item.GetComponentsInChildren<Item>(true);
                        Vector2 next = project(original.Position);
                        Quaternion turn = Quaternion.Euler(0f, 0f, rotationDegrees);
                        ItemMgr manager = ItemMgr.GetExistingInstance() ?? throw new InvalidOperationException("物品管理器尚未就绪。");
                        try
                        {
                            if (!original.Resident) manager.SetRuntimeWorldResidentPersistence(item, true);
                            MoveItem(item, destination, next, turn * original.Rotation, turn * original.Velocity, original.Angular);
                        }
                        catch
                        {
                            if (!original.Resident) manager.SetRuntimeWorldResidentPersistence(item, false);
                            throw;
                        }
                        moved.Add(original);
                        foreach (Item child in children)
                        {
                            if (child == null || child == item || GameplayWorldRegistry.GetItemWorldKey(child) != source) continue;
                            GameplayWorldRegistry.AssignItemToWorld(child, destination);
                            original.Children.Add(child);
                        }
                    }
                }
                catch { Dispose(); throw; }
            }

            internal void Commit() => finished = true;
            public void Dispose()
            {
                if (finished) return;
                for (int i = moved.Count - 1; i >= 0; i--)
                {
                    var item = moved[i];
                    MoveItem(item.Item, source, item.Position, item.Rotation, item.Velocity, item.Angular);
                    foreach (Item child in item.Children)
                        if (child != null && GameplayWorldRegistry.GetItemWorldKey(child) == destination)
                            GameplayWorldRegistry.AssignItemToWorld(child, source);
                    if (!item.Resident) ItemMgr.GetExistingInstance()?.SetRuntimeWorldResidentPersistence(item.Item, false);
                }
                finished = true;
            }

            private static void MoveItem(Item item, string worldKey, Vector2 logicalPosition,
                Quaternion rotation, Vector2 velocity, float angularVelocity)
            {
                string originalWorld = GameplayWorldRegistry.GetItemWorldKey(item);
                Vector3 originalPosition = item.transform.position;
                Quaternion originalRotation = item.transform.rotation;
                Rigidbody2D body = item.GetComponent<Rigidbody2D>();
                Vector2 originalVelocity = body != null ? body.velocity : Vector2.zero;
                float originalAngular = body != null ? body.angularVelocity : 0f;
                try
                {
                    GameplayWorldRegistry.AssignItemToWorld(item, worldKey);
                    using var scope = GameplayWorldRegistry.UseWorld(worldKey);
                    SetPose(item, logicalPosition, rotation, velocity, angularVelocity);
                }
                catch (Exception error)
                {
                    try
                    {
                        GameplayWorldRegistry.AssignItemToWorld(item, originalWorld);
                        using var scope = GameplayWorldRegistry.UseWorld(originalWorld);
                        SetPose(item, originalPosition, originalRotation, originalVelocity, originalAngular);
                    }
                    catch (Exception rollback) { throw new AggregateException("散落物移交及回滚均失败。", error, rollback); }
                    throw;
                }
            }

            private static void SetPose(Item item, Vector2 logicalPosition, Quaternion rotation, Vector2 velocity, float angularVelocity)
            {
                Vector2 position = WorldLocalPresentation.ProjectPosition(logicalPosition);
                item.transform.SetPositionAndRotation(new Vector3(position.x, position.y, item.transform.position.z), rotation);
                item.itemData.transform ??= new ItemTransform();
                item.itemData.transform.position = new Vector3(logicalPosition.x, logicalPosition.y, item.transform.position.z);
                item.itemData.transform.rotation = rotation;
                item.itemData.PhysicsState ??= new ItemPhysicsRuntimeState();
                item.itemData.PhysicsState.Velocity = velocity;
                item.itemData.PhysicsState.AngularVelocity = angularVelocity;
                Rigidbody2D body = item.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.position = position;
                    body.rotation = rotation.eulerAngles.z;
                    if (body.bodyType != RigidbodyType2D.Static)
                    { body.velocity = velocity; body.angularVelocity = angularVelocity; }
                }
                GameplayWorldRegistry.RegisterItemPhysics(item);
                ItemMgr.GetExistingInstance()?.NotifyRuntimeItemMoved(item);
            }
        }
        #endregion
    }
}
