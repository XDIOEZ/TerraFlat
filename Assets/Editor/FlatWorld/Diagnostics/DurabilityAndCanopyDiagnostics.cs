using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>独立预览实体与纯时间线验证；不进入 Play、不生成正式掉落或改写玩家存档。</summary>
public static class DurabilityAndCanopyDiagnostics
{
    #region 验证入口

    private static int assertions;

    [MenuItem("FlatWorld/诊断/待办/部位耐久与树果规则检查")]
    public static void Validate()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在非 Play 模式运行隔离规则检查。");
        assertions = 0;
        bool passed = false;
        try
        {
            ValidateDurability();
            ValidateTimeline();
            ValidateOneHit();
            passed = true;
            Debug.Log($"[DurabilityAndCanopyDiagnostics] PASS {assertions} 条断言；护甲顺序、独立生命/耐久、治疗、上限扩展、序列化、果实上下界/补算/一次命中。");
        }
        finally
        {
            Directory.CreateDirectory("Library/FlatWorldTodoValidation");
            File.WriteAllText("Library/FlatWorldTodoValidation/durability-canopy.json",
                JsonConvert.SerializeObject(new { passed, assertions }, Formatting.Indented));
        }
    }

    #endregion

    #region 正式伤害入口与独立耐久

    private static void ValidateDurability()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("部位规则隔离实体");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            GameItem owner = root.AddComponent<GameItem>();
            owner.BindData(new Data_GeneralItem());
            DamageReceiver receiver = root.AddComponent<DamageReceiver>();
            receiver.item = owner;
            receiver.modData = new Ex_ModData();
            receiver.Data = new DamageReceiver.DamageReceiver_SaveData
            {
                Hp = 100f, MaxHp = 100f, UseBodyPartHealth = true, BodyPartDataVersion = 2,
                BodyParts = DamageReceiver.CreateDefaultBodyParts(100f, 100f),
                DefenseValues = new CombatDefense(), DamageInterval = 0f
            };
            object helmet = new object();
            receiver.SetBodyPartArmor(helmet, new[] { BodyPartType.Head }, new CombatDefense { Blunt = 5f });
            var coconut = new FallingFruitDamage(10f);
            Near(receiver.Hurt(coconut), 7.5f, "10钝击先减5护甲，再乘1.5头部系数");
            Near(receiver.Hp, 92.5f, "整体100→92.5");
            receiver.TryGetBodyPart(BodyPartType.Head, out BodyPartHealth head);
            Near(head.Hp, 12.5f, "头部耐久20→12.5");
            Near(receiver.Defense.Blunt, 0f, "头盔没有同时写入全身防御");
            receiver.Hurt(coconut);
            receiver.Hurt(coconut);
            Near(head.Hp, 0f, "头部耐久归零");
            Near(receiver.Hp, 77.5f, "头部归零不死亡");
            Near(receiver.Hurt(coconut), 7.5f, "耗尽的部位仍然可以被命中");
            float hp = receiver.Hp;
            Near(receiver.RestoreBodyPartDurability(BodyPartType.Head, 5f), 5f, "单独恢复部位耐久");
            Near(receiver.Hp, hp, "治疗耐久没有回血");
            Near(receiver.RestoreBodyPartDurability(BodyPartType.Head, 100f), 15f, "耐久治疗不超上限");
            receiver.SetBodyPartMaxDurability(BodyPartType.Head, 40f);
            Near(head.Hp, 20f, "提高耐久上限不自动治疗");
            Near(receiver.MaxHp, 100f, "提高部位上限不改变总生命上限");
            receiver.SetOverallMaxHp(200f);
            Near(receiver.Hp, hp, "提高生命上限不自动回血");
            Near(head.MaxHp, 40f, "提高生命上限不改部位上限");
            var restored = JsonConvert.DeserializeObject<DamageReceiver.DamageReceiver_SaveData>(
                JsonConvert.SerializeObject(receiver.Data));
            Near(restored.Hp, hp, "整体生命序列化独立保存");
            Near(restored.BodyParts.Single(part => part.Part == BodyPartType.Head).MaxHp, 40f,
                "部位上限独立序列化");
            receiver.ResetBodyPartDurability();
            Require(Mod_BodyPartTreatment.SelectTreatmentTarget(receiver,
                new[] { BodyPartType.LeftLeg, BodyPartType.RightLeg }) == null, "健康部位不会消耗夹板");
            receiver.TryGetBodyPart(BodyPartType.LeftLeg, out BodyPartHealth leg);
            leg.Hp = 0f;
            Require(Mod_BodyPartTreatment.SelectTreatmentTarget(receiver,
                new[] { BodyPartType.LeftLeg, BodyPartType.RightLeg }) == leg, "优先治疗受损腿部");
            Require(BodyPartPenaltyRegistry.GetDefaultBuffIds(BodyPartType.Head).Contains("core:concussion"),
                "头部默认脑震荡模板");
            receiver.Hp = 0f;
            receiver.Hp = 100f;
            Near(leg.Hp, leg.MaxHp, "显式复活恢复部位耐久");
            receiver.RemoveBodyPartArmor(helmet);
            Near(receiver.Defense.Blunt, 0f, "卸下头盔不改变自然全身防御");
            receiver.Unload();
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    #endregion

    #region 树果时间与一次性命中

    private sealed class Sink : ICanopyFruitSink
    {
        public int Began;
        public readonly HashSet<int> Landed = new HashSet<int>();
        public void BeginFall(CanopyFruitRecord fruit) { Began++; }
        public void Land(CanopyFruitRecord fruit) { Require(Landed.Add(fruit.Id), "同一果实不可重复落地"); }
    }

    private static void ValidateTimeline()
    {
        var settings = new CanopyFruitSettings { GrowthSeconds = 10, GrowthJitterSeconds = 0, CycleSeconds = 100 };
        settings.Validate();
        var state = new CanopyFruitState();
        var sink = new Sink();
        CanopyFruitTimeline.Initialize(state, 0, 123);
        CanopyFruitTimeline.Advance(state, settings, 0, sink, random: (minimum, maximum) => minimum);
        Require(state.Fruits.Count == 1, "每批下界1颗");
        CanopyFruitRecord fruit = state.Fruits[0];
        Near(fruit.Growth01(5), 0.5f, "独立生长由小到大");
        Require(!CanopyFruitTimeline.CanDropOnDeath(fruit, 9), "未成熟砍树不掉果");
        Require(CanopyFruitTimeline.CanDropOnDeath(fruit, 10), "成熟砍树可掉果");
        CanopyFruitTimeline.Advance(state, settings, 10, sink, random: (minimum, maximum) => minimum);
        Near((float)fruit.FallAt, 11f, "成熟后等待下界1秒");
        CanopyFruitTimeline.Advance(state, settings, 11, sink, random: (minimum, maximum) => minimum);
        Require(state.Fruits.Count == 0 && state.Flights.Count == 1 && sink.Began == 1, "脱冠果离开树库存");
        var saved = JsonConvert.DeserializeObject<CanopyFruitState>(JsonConvert.SerializeObject(state));
        CanopyFruitTimeline.Advance(state, settings, 11.65, sink);
        CanopyFruitTimeline.Advance(state, settings, 11.65, sink);
        Require(sink.Landed.Count == 1, "相同时刻不会重复掉落");
        var restoredSink = new Sink();
        CanopyFruitTimeline.Advance(saved, settings, 11.65, restoredSink);
        Require(restoredSink.Landed.SetEquals(sink.Landed), "保存恢复保留飞行身份和到期时刻");

        state = new CanopyFruitState(); sink = new Sink();
        CanopyFruitTimeline.Initialize(state, 0, 123);
        CanopyFruitTimeline.Advance(state, settings, 10, sink, random: (minimum, maximum) => maximum - 1);
        Require(state.Fruits.Count == 5, "每批上界5颗");
        Require(state.Fruits.All(value => value.FallAt == 1450), "成熟后等待上界1440秒");

        var direct = new CanopyFruitState(); var sliced = new CanopyFruitState();
        CanopyFruitTimeline.Initialize(direct, 0, 999); CanopyFruitTimeline.Initialize(sliced, 0, 999);
        var a = new Sink(); var b = new Sink();
        CanopyFruitTimeline.Advance(direct, settings, 10000, a, budget: 10000);
        int attempts = 0;
        while (!CanopyFruitTimeline.Advance(sliced, settings, 10000, b, budget: 1))
            Require(++attempts < 10000, "补算预算保持前进");
        Require(JsonConvert.SerializeObject(direct) == JsonConvert.SerializeObject(sliced),
            "分帧补算与一次推进得到相同状态和随机流");
        Require(a.Landed.SetEquals(b.Landed), "分帧补算不丢果、不重复产果");
    }

    private static void ValidateOneHit()
    {
        var fruit = new CanopyFruitRecord { FallAt = 1, LandAt = 2 };
        int hits = 0;
        Require(!FallingFruitDamage.TryHit(fruit, 0, () => ++hits, () => 0, 0.5), "起飞前没有临时伤害");
        Require(FallingFruitDamage.TryHit(fruit, 1.5, () => { hits++; return 7.5f; }, () => 0.499, 0.5),
            "首次有效碰撞可伤害");
        Require(fruit.Split, "小于50%转半果");
        Require(!FallingFruitDamage.TryHit(fruit, 1.6, () => ++hits, () => 0, 0.5) && hits == 1,
            "同次坠落不重复伤害");
        fruit = new CanopyFruitRecord { FallAt = 1, LandAt = 2 };
        FallingFruitDamage.TryHit(fruit, 1.5, () => 0f, () => 0.5, 0.5);
        Require(!fruit.Split, "50%边界不转半果，护甲零伤害仍算接触");
        Require(!FallingFruitDamage.TryHit(new CanopyFruitRecord { FallAt = 1, LandAt = 2 },
            2, () => ++hits, () => 0, 0.5), "落地立即失去伤害权限");
    }

    #endregion

    #region 断言

    private static void Near(float actual, float expected, string reason) =>
        Require(Mathf.Abs(actual - expected) < 0.001f, $"{reason}: {actual}，预期 {expected}");
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    #endregion
}
