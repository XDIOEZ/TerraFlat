using System;
using UnityEngine;

namespace FlatWorld.AIECS
{
    public struct AiecsSwayPose
    {
        #region 只读表现结果
        public AiecsAnimationFrame Frame;
        public bool Mirror;
        #endregion
    }

    public struct AiecsSwayState
    {
        #region 独立实体动作时钟
        internal float WalkPhase, WalkBlend, IdlePhase, AttackElapsed, PreviousActionElapsed;
        internal AiecsAttackPhase PreviousAttackPhase;
        internal Vector2 AttackDirection;
        internal bool Initialized, Dead;
        internal AiecsSwayPose LastPose, DeathStart;
        internal AiecsAnimationFrame DeathRest;
        #endregion
    }

    /// <summary>只消费表现快照，在共享图片上合成摇摆、撞击和翻身，不反写模拟。</summary>
    public sealed class AiecsSwayAnimation
    {
        #region 物种共享状态
        private readonly AiecsActorVisual visual;
        private readonly AiecsSpriteGeometry[] sprites;
        private readonly AiecsSwaySettings settings;
        private readonly AiecsAnimationFrame stand, move, takeoff, flying, landing;
        public AiecsAnimationFrame StandFrame => stand;

        public AiecsSwayAnimation(AiecsActorVisual visual, AiecsSpriteGeometry[] sprites)
        {
            this.visual = visual;
            this.sprites = sprites;
            settings = visual.Sway;
            stand = FindFrame(visual.Clips[0].Frames[0], "Ground", "Stand", "Idle");
            move = FindFrame(stand, "Move", "Walk");
            takeoff = FindFrame(stand, "TakingOff", "TakeOff");
            flying = FindFrame(takeoff, "Flying", "Fly");
            landing = FindFrame(stand, "Landing", "Land");
        }

        private AiecsAnimationFrame FindFrame(AiecsAnimationFrame fallback, params string[] names)
        {
            foreach (string name in names)
                foreach (AiecsAnimationClip clip in visual.Clips)
                    if (clip.State.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        clip.State.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                        return clip.Frames[0];
            return fallback;
        }

        private AiecsAnimationFrame ResolveFrame(in AiecsDisplayRecord record)
        {
            if (record.FlightCruiseHeight > 0.001f)
            {
                if (record.FlightAirborne != 0)
                    return record.FlightHeight < record.FlightCruiseHeight * 0.5f ? takeoff : flying;
                if (record.FlightHeight > 0.01f) return landing;
            }
            return record.Moving != 0 ? move : stand;
        }
        #endregion

        #region 单图动作采样
        public AiecsSwayPose Sample(ref AiecsSwayState state, in AiecsDisplayRecord record, float deltaTime)
        {
            bool mirror = record.Facing.x < 0f;
            AiecsAnimationFrame rest = ResolveFrame(record);
            float step = Mathf.Max(0f, deltaTime);
            if (!state.Initialized || (state.Dead && record.Dead == 0))
            {
                state = new AiecsSwayState { Initialized = true, AttackElapsed = -1f,
                    IdlePhase = (record.Key.GetHashCode() & 1023) * (Mathf.PI * 2f / 1024f) };
                var initial = rest;
                initial.Position.y += record.FlightHeight;
                state.LastPose = new AiecsSwayPose { Frame = initial, Mirror = mirror };
            }
            if (record.Dead != 0)
            {
                if (!state.Dead)
                {
                    state.Dead = true;
                    state.DeathStart = state.LastPose;
                    state.DeathRest = state.LastPose.Frame;
                    state.DeathRest.Position = rest.Position;
                    state.DeathRest.Scale = rest.Scale;
                    state.DeathRest.Rotation = rest.Rotation;
                }
                state.LastPose = SampleDeath(state, record.ActionElapsed);
                return state.LastPose;
            }

            bool attackStarted = record.AttackPhase == AiecsAttackPhase.Active &&
                (state.PreviousAttackPhase != AiecsAttackPhase.Active || record.ActionElapsed < state.PreviousActionElapsed);
            if (attackStarted)
            {
                state.AttackElapsed = Mathf.Max(0f, record.ActionElapsed);
                Vector2 direction = new Vector2(record.Facing.x, record.Facing.y);
                state.AttackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            }
            else if (state.AttackElapsed >= 0f) state.AttackElapsed += step;
            state.PreviousAttackPhase = record.AttackPhase;
            state.PreviousActionElapsed = record.ActionElapsed;
            float forward = Mathf.Max(0.01f, settings.attackDashForwardTime);
            float back = Mathf.Max(0.01f, settings.attackDashBackTime);
            if (state.AttackElapsed >= forward + back) state.AttackElapsed = -1f;

            bool moving = record.Moving != 0;
            bool running = moving && (record.Behavior == (int)AiecsBehavior.Chase || record.Behavior == (int)AiecsBehavior.Flee);
            bool attacking = state.AttackElapsed >= 0f;
            state.WalkBlend = Mathf.MoveTowards(state.WalkBlend, moving && !attacking ? 1f : 0f,
                step * Mathf.Max(0.01f, settings.walkReturnSpeed));
            if (moving) state.WalkPhase = Mathf.Repeat(state.WalkPhase + step * (running ? settings.runSpeedMultiplier : 1f), 1000f);
            else if (state.WalkBlend <= 0f) state.WalkPhase = 0f;
            state.IdlePhase = Mathf.Repeat(state.IdlePhase + step * 2f, Mathf.PI * 2f);
            float swing = Mathf.Sin(state.WalkPhase * settings.walkSwingSpeed);
            float strength = state.WalkBlend * (running ? settings.runSwingMultiplier : 1f);
            float angle = swing * settings.walkSwingAngle * strength;
            Vector3 offset = Vector3.up * (record.FlightHeight + Mathf.Abs(swing) * settings.walkBobAmount * strength);
            float breath = !moving && !attacking ? Mathf.Sin(state.IdlePhase) * settings.idleBreathAmount : 0f;
            Vector3 scale = new Vector3(1f - breath * 0.5f, 1f + breath, 1f);
            if (attacking)
            {
                float amount = state.AttackElapsed <= forward
                    ? Mathf.SmoothStep(0f, 1f, state.AttackElapsed / forward)
                    : Mathf.SmoothStep(1f, 0f, (state.AttackElapsed - forward) / back);
                Vector2 direction = state.AttackDirection;
                offset += (Vector3)(direction * (Mathf.Max(0f, settings.attackDashDistance) * amount));
                angle -= direction.x * 4f * amount;
            }
            AiecsAnimationFrame frame = rest;
            frame.Rotation += mirror ? -angle : angle;
            frame.Scale = Vector3.Scale(frame.Scale, scale);
            Rect rect = sprites[rest.Sprite].LocalRect;
            Vector3 foot = new Vector3(rect.center.x, visual.FlipY ? rect.yMax : rect.yMin);
            Offset(ref frame, Point(foot, rest, mirror) - Point(foot, frame, mirror) + offset, mirror);
            state.LastPose = new AiecsSwayPose { Frame = frame, Mirror = mirror };
            return state.LastPose;
        }
        #endregion

        #region 死亡冻结与几何对齐
        private AiecsSwayPose SampleDeath(in AiecsSwayState state, float elapsed)
        {
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, settings.deathDuration));
            float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.78f));
            float settle = 1f - Mathf.SmoothStep(0f, 1f, progress);
            bool mirror = state.DeathStart.Mirror;
            var frame = state.DeathStart.Frame;
            frame.Rotation = Mathf.Lerp(frame.Rotation, state.DeathRest.Rotation + (mirror ? -180f : 180f), turn);
            frame.Scale = Vector3.Lerp(frame.Scale, state.DeathRest.Scale, turn);
            Bounds neutral = GetBounds(state.DeathRest, mirror);
            Bounds start = GetBounds(state.DeathStart.Frame, mirror);
            Bounds rotated = GetBounds(frame, mirror);
            float height = (start.min.y - neutral.min.y) * settle +
                Mathf.Max(0f, settings.deathJumpHeight) * 4f * progress * (1f - progress);
            Offset(ref frame, new Vector3(neutral.center.x + (start.center.x - neutral.center.x) * settle - rotated.center.x,
                neutral.min.y + height - rotated.min.y), mirror);
            return new AiecsSwayPose { Frame = frame, Mirror = mirror };
        }

        public Bounds GetBounds(AiecsAnimationFrame frame, bool mirror)
        {
            Rect rect = sprites[frame.Sprite].LocalRect;
            Vector3 p0 = Point(new Vector3(rect.xMin, rect.yMin), frame, mirror);
            Vector3 p1 = Point(new Vector3(rect.xMax, rect.yMin), frame, mirror);
            Vector3 p3 = Point(new Vector3(rect.xMin, rect.yMax), frame, mirror);
            var bounds = new Bounds(p0, Vector3.zero);
            bounds.Encapsulate(p1); bounds.Encapsulate(p3); bounds.Encapsulate(p1 + p3 - p0);
            return bounds;
        }

        private Vector3 Point(Vector3 point, AiecsAnimationFrame frame, bool mirror) =>
            AiecsRenderBatch.TransformPoint(point, default, visual, frame, mirror);

        private static void Offset(ref AiecsAnimationFrame frame, Vector3 worldOffset, bool mirror)
        {
            // 镜像只改变图片局部坐标，攻击和起落仍沿真实世界方向。
            if (mirror) worldOffset.x = -worldOffset.x;
            frame.Position += worldOffset;
        }
        #endregion
    }
}
