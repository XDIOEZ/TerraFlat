using System.Collections.Generic;
using UnityEngine;

public partial class Mod_AI_Ghost : IActorAnimationMotionSource
{
    #region 单图主动移动与接触攻击
    private Mod_AnimatorController _animationBody;
    private Vector2 _animationDrivenVelocity;
    private readonly List<Mod_Damage> _animationDamageSources = new List<Mod_Damage>();

    private void BindGhostPresentation()
    {
        _animationBody = item.itemMods.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);
        _animationDrivenVelocity = Vector2.zero;
        foreach (Module module in item.itemMods.Mods.Values)
            if (module is Mod_Damage damage)
            {
                damage.OnReceiverDamageResolved -= PlayGhostContactAttack;
                damage.OnReceiverDamageResolved += PlayGhostContactAttack;
                _animationDamageSources.Add(damage);
            }
    }

    public bool TryGetAnimationMotion(out ActorAnimationMotion motion)
    {
        motion = default;
        if (!IsRuntimeLoaded || !IsAlive) return false;
        // 直追和导航均报告叠加风、水流之前的主动速度。
        Vector2 velocity = _state == GhostState.Chase ? _animationDrivenVelocity :
            _pathAgent != null ? _pathAgent.DrivenVelocity : Vector2.zero;
        motion.IsMoving = velocity.sqrMagnitude > 0.0001f;
        motion.IsRunning = motion.IsMoving && _state == GhostState.Chase;
        return true;
    }

    private void PlayGhostContactAttack(Mod_DamageReceiver target, float damage)
    {
        if (damage >= 0f && target != null && target.item != null)
            _animationBody?.PlayAttack(WorldTopologyRuntime.ShortestDelta(item.transform.position, target.item.transform.position));
    }

    protected override void OnUnload()
    {
        for (int i = 0; i < _animationDamageSources.Count; i++)
            if (_animationDamageSources[i] != null)
                _animationDamageSources[i].OnReceiverDamageResolved -= PlayGhostContactAttack;
        _animationDamageSources.Clear();
        _animationBody = null;
        StopMoving();
        if (visualTransform != null && _hasVisualBasePosition)
            visualTransform.localPosition = _visualBaseLocalPosition;
    }
    #endregion
}
