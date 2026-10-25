using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 矿车输入和真实库存事务采样
    private static void RegisterMinecartOperations(Dictionary<string, Func<JObject, JObject>> result)
    {
        result.Add("mechanical/inventory_acceptance", MinecartInventoryAcceptance);
        result.Add("mechanical/minecart_contracts", MinecartMotionRules);
        result.Add("mechanical/minecart_held_turn", MinecartHeldTurnRules);
        result.Add("mechanical/minecart_surface", MinecartSurfaceRules);
        result.Add("mechanical/minecart_manual_drive", MinecartManualDriveRules);
    }
    private static JObject MinecartManualDriveRules(JObject args)
    {
        var root = new GameObject("MinecartManualDriveRules");
        root.SetActive(false);
        try
        {
            Mod_Stamina stamina = root.AddComponent<Mod_Stamina>();
            stamina.Data = new Mod_Stamina.StaminaData
            { CurrentStamina = (float)Number(args, "stamina", 10), MaxStamina = 100f };
            int transactions = 0;
            string source = string.Empty;
            stamina.StaminaConsumed += record => { transactions++; source = record.SourceId; };
            float power = MinecartDrivingRules.TrySupplyManualPower(Flag(args, "missingStamina", false) ? null : stamina,
                Flag(args, "onRail", true), (float)Number(args, "throttle", 1),
                (float)Number(args, "enginePower", 0), .25f, 5f, (float)Number(args, "seconds", 1));
            return new JObject
            {
                ["power"] = power, ["stamina"] = stamina.CurrentValue,
                ["transactions"] = transactions, ["source"] = source,
                ["singleSpeed"] = MinecartDrivingRules.NextSpeed(0f, 1f, 5f, power, 1f, 3f, 6f, 1f),
                ["trainSpeed"] = MinecartDrivingRules.NextSpeed(0f, 1f, 5f, power, 4f, 3f, 6f, 1f)
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static JObject MinecartSurfaceRules(JObject args)
    {
        static JObject Vector(Vector2 value) => new() { ["x"] = value.x, ["y"] = value.y };
        float groundMultiplier = (float)Number(args, "groundMultiplier", .025);
        float captureDistance = Mod_Minecart.DefaultRailCaptureDistance;
        var curve = new ConveyorPath(2, 3);
        float curveCenterDistance = RailPathGeometry.Evaluate(curve,
            RailPathGeometry.Project(curve, Vector2.zero)).magnitude;
        var straight = ConveyorPath.Straight(0);
        Vector2 parallelMidpoint = Vector2.up * .5f;
        float parallelMidpointDistance = (parallelMidpoint - RailPathGeometry.Evaluate(straight,
            RailPathGeometry.Project(straight, parallelMidpoint))).magnitude;
        Vector2 pushIntent = new(3f, 4f), externalVelocity = new(2f, 2f);
        Vector2 limitedSubmit = Mod_Mover.LimitPushIntentVelocity(pushIntent + externalVelocity,
            pushIntent, Vector2.right, .1f);
        Vector2 repeatedSubmit = Mod_Mover.LimitPushIntentVelocity(pushIntent + externalVelocity,
            pushIntent, Vector2.right, .1f);
        return new JObject
        {
            ["rails"] = MinecartDrivingRules.SurfaceMultiplier(4, 0, groundMultiplier),
            ["ground"] = MinecartDrivingRules.SurfaceMultiplier(4, 4, groundMultiplier),
            ["mixed"] = MinecartDrivingRules.SurfaceMultiplier(2, 1, groundMultiplier),
            ["lighterGroundFraction"] = MinecartDrivingRules.SurfaceMultiplier(4, 1, groundMultiplier),
            ["railPush"] = Vector(MinecartDrivingRules.ResolvePushVelocity(new Vector2(3f, 4f), Vector2.right, true, 1f)),
            ["groundPush"] = Vector(MinecartDrivingRules.ResolvePushVelocity(new Vector2(3f, 4f), Vector2.right, false, 1f)),
            ["crossPush"] = Vector(MinecartDrivingRules.ResolvePushVelocity(Vector2.up * 4f, Vector2.right, true, 1f)),
            ["scaledPush"] = Vector(MinecartDrivingRules.ResolvePushVelocity(new Vector2(3f, 4f), Vector2.right, false, .5f)),
            ["railStop"] = Vector(MinecartDrivingRules.NextSurfaceVelocity(Vector2.right * 5f, Vector2.zero, 3f, 6f, .1f)),
            ["groundStop"] = Vector(MinecartDrivingRules.NextSurfaceVelocity(Vector2.right * 5f, Vector2.zero, 3f, 60f, .1f)),
            ["acceleration"] = Vector(MinecartDrivingRules.NextSurfaceVelocity(Vector2.zero, Vector2.right * 5f, 3f, 6f, 1f)),
            ["zeroTime"] = Vector(MinecartDrivingRules.NextSurfaceVelocity(Vector2.right * 5f, Vector2.zero, 3f, 60f, 0f)),
            ["captureNear"] = MinecartDrivingRules.ShouldCaptureRail(.2f, captureDistance, 0f, false),
            ["captureFar"] = MinecartDrivingRules.ShouldCaptureRail(.26f, captureDistance, 0f, false),
            ["captureEndpointOutward"] = MinecartDrivingRules.ShouldCaptureRail(.2f, captureDistance, .5f, true),
            ["captureEndpointStopped"] = MinecartDrivingRules.ShouldCaptureRail(.2f, captureDistance, 0f, true),
            ["captureEndpointInward"] = MinecartDrivingRules.ShouldCaptureRail(.2f, captureDistance, -.5f, true),
            ["captureOrdinarySegment"] = MinecartDrivingRules.ShouldCaptureRail(.2f, captureDistance, .5f, false),
            ["captureInvalid"] = MinecartDrivingRules.ShouldCaptureRail(float.NaN, captureDistance, 0f, false),
            ["curveCenterDistance"] = curveCenterDistance,
            ["captureCurvePlacement"] = MinecartDrivingRules.ShouldCaptureRail(curveCenterDistance, captureDistance, 0f, false),
            ["parallelMidpointDistance"] = parallelMidpointDistance,
            ["captureParallelMidpoint"] = MinecartDrivingRules.ShouldCaptureRail(parallelMidpointDistance, captureDistance, 0f, false),
            ["limitedSubmit"] = Vector(limitedSubmit),
            ["repeatedSubmit"] = Vector(repeatedSubmit),
            ["preservedIntent"] = Vector(pushIntent),
            ["externalOnlySubmit"] = Vector(Mod_Mover.LimitPushIntentVelocity(externalVelocity,
                Vector2.zero, Vector2.right, .1f)),
            ["tangentSubmit"] = Vector(Mod_Mover.LimitPushIntentVelocity(new Vector2(2f, 6f),
                Vector2.up * 4f, Vector2.right, .1f))
        };
    }
    private static JObject MinecartHeldTurnRules(JObject args)
    {
        var paths = new JArray();
        for (int input = 0; input < 4; input++) for (int output = 0; output < 4; output++)
        {
            if (input == output) continue;
            var route = new ConveyorPath(input, output);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2 heldDirection = Vector2.zero;
                float heldSign = 0f, minimum = 1f;
                Vector2 key = -RailPathGeometry.Direction(sign > 0 ? input : output);
                for (int step = 0; step <= 20; step++)
                {
                    float progress = route.Length * (sign > 0 ? step : 20 - step) / 20f;
                    float throttle = MinecartDrivingRules.ResolveThrottle(key,
                        RailPathGeometry.Tangent(route, progress), ref heldDirection, ref heldSign);
                    minimum = Mathf.Min(minimum, throttle * sign);
                }
                Vector2 endTangent = RailPathGeometry.Tangent(route, sign > 0 ? route.Length : 0f);
                float reverse = MinecartDrivingRules.ResolveThrottle(-key, endTangent,
                    ref heldDirection, ref heldSign);
                float released = MinecartDrivingRules.ResolveThrottle(Vector2.zero, endTangent,
                    ref heldDirection, ref heldSign);
                bool cleared = heldDirection == Vector2.zero && heldSign == 0f;
                float restarted = MinecartDrivingRules.ResolveThrottle(key, endTangent,
                    ref heldDirection, ref heldSign);
                float changed = MinecartDrivingRules.ResolveThrottle(endTangent * sign, endTangent,
                    ref heldDirection, ref heldSign);
                float analog = MinecartDrivingRules.ResolveThrottle(endTangent * (sign * .4f), endTangent,
                    ref heldDirection, ref heldSign);
                paths.Add(new JObject
                {
                    ["curved"] = route.Curved, ["sign"] = sign, ["minimum"] = minimum,
                    ["reverse"] = reverse, ["released"] = released, ["cleared"] = cleared,
                    ["restarted"] = restarted, ["changed"] = changed, ["analog"] = analog
                });
            }
        }
        return new JObject { ["paths"] = paths };
    }
    private static JObject MinecartMotionRules(JObject args)
    {
        float inputX = (float)Number(args, "x", 1), inputY = (float)Number(args, "y", 0);
        float tangentX = (float)Number(args, "tx", 1), tangentY = (float)Number(args, "ty", 0);
        float throttle = MinecartDrivingRules.ResolveThrottle(new Vector2(inputX, inputY), new Vector2(tangentX, tangentY));
        float power = (float)Number(args, "power", 1);
        return new JObject
        {
            ["throttle"] = throttle,
            ["speed"] = MinecartDrivingRules.NextSpeed(0f, throttle, 5f, power, 1f, 3f, 6f, 1f),
            ["zeroPower"] = MinecartDrivingRules.NextSpeed(4f, throttle, 5f, 0f, 1f, 3f, 6f, 1f),
            ["idle"] = MinecartDrivingRules.NextSpeed(4f, 0f, 5f, 1f, 1f, 3f, 6f, 1f)
        };
    }
    private static JObject MinecartInventoryAcceptance(JObject args)
    {
        ItemData Item(string id, int guid) => new Data_GeneralItem
        { IDName = id, Guid = guid, Stack = new ItemStack { Amount = 1f, Stackable = false, Weight = 1f, Volume = 1f } };
        string sourceId = Text(args, "source", "ore"), targetId = Text(args, "target", "");
        string mode = Text(args, "mode", "quantity");
        var sourceData = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = Item(sourceId, 1001) } }, "source");
        var targetData = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = string.IsNullOrEmpty(targetId) ? null : Item(targetId, 1002) } }, "engines");
        bool policy = Flag(args, "policy", true);
        if (policy) targetData.SetItemAcceptancePolicy((incoming, _) => incoming.IDName == "engine");
        if (Flag(args, "sourcePolicy", false)) sourceData.SetItemAcceptancePolicy((incoming, _) => incoming.IDName == "engine");
        ItemSlot from = sourceData.itemSlots[0], to = targetData.itemSlots[0];
        ItemData original = from.itemData, originalTarget = to.itemData;
        int sourceEvents = 0, targetEvents = 0;
        sourceData.Event_OnDataChanged += _ => sourceEvents++;
        targetData.Event_OnDataChanged += _ => targetEvents++;
        int preview = sourceData.GetTransferableQuantityTo(from, targetData, to, 1);
        bool uiPreview = new Inventory { Data = targetData }.CanAcceptQuickTransfer(from, to);
        bool accepted;
        if (mode == "swap" || mode == "drag") accepted = sourceData.DropItemSlotTo(from, targetData, to);
        else if (mode == "add") accepted = targetData.TryAddItem(original, false);
        else if (mode == "batch") accepted = sourceData.TryTransferAllToEmptyInventory(targetData, out _);
        else if (mode == "expired")
        {
            var source = new Inventory { Data = sourceData, RuntimeAccessGuard = () => false };
            accepted = source.TryTransferQuantityTo(0, new Inventory { Data = targetData }, 0, 1);
        }
        else accepted = sourceData.TransferItemQuantityTo(from, targetData, to, 1);
        return new JObject
        {
            ["accepted"] = accepted, ["preview"] = preview, ["uiPreview"] = uiPreview,
            ["source"] = from.itemData?.IDName ?? "", ["target"] = to.itemData?.IDName ?? "",
            ["sourceUnchanged"] = ReferenceEquals(from.itemData, original),
            ["targetUnchanged"] = ReferenceEquals(to.itemData, originalTarget),
            ["sourceEvents"] = sourceEvents, ["targetEvents"] = targetEvents
        };
    }
    #endregion
}
