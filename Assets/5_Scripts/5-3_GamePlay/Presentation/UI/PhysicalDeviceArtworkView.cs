using System;
using UnityEngine;
using UnityEngine.UI;
using static PhysicalDeviceArtworkCatalog;
using Motion = PhysicalDeviceArtworkCatalog.Motion;

/// <summary>器物图层只呈现只读运行状态，点击反馈不会提交第二次加工。</summary>
public sealed class PhysicalDeviceArtworkView : MonoBehaviour
{
    #region 序列化外观与只读状态
    [Serializable]
    public struct OperatingState : IEquatable<OperatingState>
    {
        public float SignedRpm, LinearSpeed, Heat, Liquid, Gas, Charge, Fuel, Progress;
        public bool Working, Burning, Open, FireSeed;
        public Sprite InputAttachment;
        public Sprite Contents0, Contents1, Contents2, Contents3;
        public Vector4 ContentsScale;
        public void SetContents(int index, Sprite sprite, float scale = 1f)
        {
            switch (index) { case 0: Contents0 = sprite; break; case 1: Contents1 = sprite; break;
                case 2: Contents2 = sprite; break; case 3: Contents3 = sprite; break; }
            ContentsScale[index] = scale;
        }
        public Sprite GetContents(int index) => index switch { 0 => Contents0, 1 => Contents1, 2 => Contents2, _ => Contents3 };
        public bool Equals(OperatingState other) => SignedRpm == other.SignedRpm && LinearSpeed == other.LinearSpeed &&
            Heat == other.Heat && Liquid == other.Liquid && Gas == other.Gas && Charge == other.Charge &&
            Fuel == other.Fuel && Progress == other.Progress && Working == other.Working &&
            Burning == other.Burning && Open == other.Open && FireSeed == other.FireSeed && InputAttachment == other.InputAttachment &&
            Contents0 == other.Contents0 && Contents1 == other.Contents1 && Contents2 == other.Contents2 &&
            Contents3 == other.Contents3 && ContentsScale == other.ContentsScale;
    }

    [SerializeField] private PhysicalDeviceArtworkCatalog catalog = null;
    [SerializeField] private string defaultAppearance = string.Empty;
    [SerializeField] private Image[] layers = Array.Empty<Image>();
    [SerializeField] private Button actionButton;
    [SerializeField] private Vector2 controlDesignSize = Vector2.zero;
    [SerializeField] private float artworkWidth = 0f;
    private Appearance appearance;
    private Func<OperatingState> readState;
    private BasePanel panel;
    private float phase, strokeRemaining, strokePhase;
    private double wheelDistance;
    private OperatingState state;
    private bool hasMechanicalMotion, hasSmoke, hasFlame, hasEmber, hasWheels, hasFlow;
    public OperatingState RenderedState => state;
    public int StrokeRevision { get; private set; }
    public string AppearanceId { get; private set; }
    public Vector2 ControlDesignSize => controlDesignSize;
    public float ArtworkWidth => artworkWidth;

    private void Awake()
    {
        panel = GetComponentInParent<BasePanel>();
        actionButton ??= panel.GetButton("合成按钮") ?? panel.GetButton("FWUI_Toggle") ?? panel.GetButton("FWUI_ElevatorSend");
        panel.Closed += ResetTransientPresentation;
        if (actionButton != null) actionButton.onClick.AddListener(PlayFrictionOrWorkingStroke);
        ApplyAppearance(defaultAppearance);
    }

    public void Bind(string itemId, Func<OperatingState> sample)
    {
        readState = sample;
        ApplyAppearance(catalog.Find(itemId) != null ? itemId : defaultAppearance);
        ResetTransientPresentation();
    }

    private void ApplyAppearance(string id)
    {
        Appearance next = catalog.Find(id);
        if (ReferenceEquals(appearance, next)) return;
        appearance = next;
        AppearanceId = id;
        hasMechanicalMotion = hasSmoke = hasFlame = hasEmber = hasWheels = hasFlow = false;
        for (int i = 0; i < layers.Length; i++)
        {
            Image image = layers[i];
            bool visible = appearance != null && i < appearance.Layers.Length;
            image.gameObject.SetActive(visible);
            if (!visible) continue;
            Layer layer = appearance.Layers[i];
            hasMechanicalMotion |= layer.Movement is Motion.Rotate or Motion.Reciprocate or Motion.Compress or Motion.SliderCrank or Motion.ConnectingRod;
            hasSmoke |= layer.Movement == Motion.Smoke;
            hasFlame |= layer.Movement == Motion.Flame;
            hasEmber |= layer.Movement == Motion.Ember;
            hasWheels |= layer.Movement == Motion.Wheel;
            hasFlow |= layer.Movement == Motion.FlowPulse;
            image.sprite = layer.Sprite;
            image.raycastTarget = false;
            image.color = layer.Tint;
            RectTransform rect = image.rectTransform;
            rect.pivot = layer.Pivot;
            rect.anchoredPosition = new Vector2(layer.Rect.x, layer.Rect.y);
            rect.sizeDelta = new Vector2(layer.Rect.z, layer.Rect.w);
            rect.localRotation = Quaternion.Euler(0, 0, layer.Rotation);
            rect.localScale = Vector3.one;
        }
    }

    public void PlayFrictionOrWorkingStroke()
    {
        if (panel == null || !panel.IsOpen() || actionButton != null && !actionButton.interactable) return;
        // 连点延续当前相位，不堆积动画，也不改变每次点击的业务工作量。
        if (strokeRemaining <= 0f) strokePhase = 0f;
        strokeRemaining = .45f;
        StrokeRevision++;
    }

    private void LateUpdate()
    {
        if (panel == null || !panel.IsOpen() || appearance == null) return;
        OperatingState next = readState != null ? readState() : default;
        bool animated = strokeRemaining > 0f || hasMechanicalMotion && next.SignedRpm != 0f ||
            hasWheels && next.LinearSpeed != 0f || hasSmoke && next.Heat > .35f ||
            hasFlame && next.Burning || hasEmber && next.FireSeed || hasFlow && next.Working;
        if (!animated && state.Equals(next)) return;
        state = next;
        float seconds = Time.deltaTime;
        phase = (float)MachineArticulatedKinematics.AdvanceContinuousPhase(phase, state.SignedRpm, seconds);
        wheelDistance += state.LinearSpeed * seconds;
        strokeRemaining = Mathf.Max(0f, strokeRemaining - Time.unscaledDeltaTime);
        if (strokeRemaining > 0f) strokePhase += Time.unscaledDeltaTime * (Mathf.PI * 4f / .45f);
        RenderLayers(Time.time);
    }

    public void ResetTransientPresentation()
    {
        strokeRemaining = strokePhase = 0f;
        phase = 0f; wheelDistance = 0d;
        state = default;
        RenderLayers(0f);
    }

#if UNITY_EDITOR
    public void PreviewArtwork(OperatingState snapshot, float elapsedSeconds, bool workingStroke = false, string appearanceId = null)
    {
        ApplyAppearance(appearanceId ?? defaultAppearance);
        state = snapshot;
        phase = (float)MachineArticulatedKinematics.AdvanceContinuousPhase(0, state.SignedRpm, elapsedSeconds);
        wheelDistance = state.LinearSpeed * elapsedSeconds;
        strokeRemaining = workingStroke ? Mathf.Max(0f, .45f - elapsedSeconds) : 0f;
        strokePhase = elapsedSeconds * (Mathf.PI * 4f / .45f);
        RenderLayers(elapsedSeconds);
    }
#endif

    private void OnDisable() => ResetTransientPresentation();
    private void OnDestroy()
    {
        if (panel != null) panel.Closed -= ResetTransientPresentation;
        if (actionButton != null) actionButton.onClick.RemoveListener(PlayFrictionOrWorkingStroke);
        readState = null;
    }
    #endregion

    #region 部件运动与情景效果
    private void RenderLayers(float now)
    {
        if (appearance == null) return;
        float stroke = strokeRemaining > 0f ? Mathf.Sin(strokePhase) * Mathf.Min(1f, strokeRemaining / .08f) : 0f;
        for (int i = 0; i < appearance.Layers.Length; i++)
        {
            Layer layer = appearance.Layers[i];
            Image image = layers[i];
            RectTransform rect = image.rectTransform;
            Vector2 position = new(layer.Rect.x, layer.Rect.y);
            Vector2 size = new(layer.Rect.z, layer.Rect.w);
            Vector3 scale = Vector3.one;
            float rotation = layer.Rotation;
            Color tint = layer.Tint;
            switch (layer.Movement)
            {
                case Motion.Contents0:
                case Motion.Contents1:
                case Motion.Contents2:
                case Motion.Contents3:
                    int contentIndex = layer.Movement - Motion.Contents0;
                    image.sprite = state.GetContents(contentIndex);
                    tint.a *= image.sprite != null ? 1f : 0f;
                    scale *= state.ContentsScale[contentIndex];
                    break;
                case Motion.Rotate:
                    rotation += phase * Mathf.Rad2Deg;
                    break;
                case Motion.Wheel:
                    rotation += (float)Math.IEEERemainder(wheelDistance / layer.WheelRadius, Math.PI * 2d) * Mathf.Rad2Deg;
                    break;
                case Motion.Reciprocate:
                    position.y += Mathf.Sin(phase) * layer.Travel;
                    break;
                case Motion.SliderCrank:
                case Motion.ConnectingRod:
                    if (MachineArticulatedKinematics.TrySolveVerticalSliderCrank(phase,
                        layer.CrankCenter.x, layer.CrankCenter.y, layer.CrankRadius,
                        layer.SliderAxis, layer.RodLength, layer.SliderBranch, out var pose))
                    {
                        position = layer.Movement == Motion.SliderCrank
                            ? new Vector2((float)pose.SliderPinX, (float)pose.SliderPinY)
                            : new Vector2((float)pose.CrankPinX, (float)pose.CrankPinY);
                        if (layer.Movement == Motion.ConnectingRod)
                            rotation += (float)pose.ConnectingRodAngleRadians * Mathf.Rad2Deg;
                    }
                    break;
                case Motion.WorkingNeedle:
                    rotation += state.Working ? 45f : -130f;
                    break;
                case Motion.Compress:
                    scale.y = 1f + Mathf.Sin(phase) * layer.Compression;
                    break;
                case Motion.Stroke:
                    position.x += stroke * layer.Travel;
                    break;
                case Motion.InputAttachment:
                case Motion.ToolStroke:
                    image.sprite = state.InputAttachment;
                    tint.a *= state.InputAttachment != null ? 1f : 0f;
                    if (layer.Movement == Motion.ToolStroke) position.y -= Mathf.Abs(stroke) * layer.Travel;
                    break;
                case Motion.VerticalStroke:
                    position.y += stroke * layer.Travel;
                    break;
                case Motion.Drill:
                    scale.x = 1f + (strokeRemaining > 0 ? Mathf.Sin(strokePhase * 3f) * .12f : 0f);
                    break;
                case Motion.Flame:
                    tint.a *= state.Burning ? .88f + .12f * Mathf.Sin(now * 12f + i) : 0f;
                    scale.y = 1f + (state.Burning ? Mathf.Sin(now * 9f) * .09f : 0f);
                    break;
                case Motion.Smoke:
                    float age = Mathf.Repeat(now * .65f + i * .19f, 1f);
                    tint.a *= Mathf.InverseLerp(.35f, .85f, state.Heat) * Mathf.Sin(age * Mathf.PI);
                    position += new Vector2(Mathf.Sin(age * 4f + i) * 13f, age * layer.Travel);
                    scale *= .6f + age * .6f;
                    break;
                case Motion.Ember:
                    tint.a *= state.FireSeed ? .85f + .15f * Mathf.Sin(now * 4f) : 0f;
                    break;
                case Motion.Liquid:
                    scale.y = Mathf.Clamp01(state.Liquid);
                    break;
                case Motion.Charge:
                    scale.y = Mathf.Clamp01(state.Charge);
                    break;
                case Motion.Gas:
                    scale.y = Mathf.Clamp01(state.Gas);
                    break;
                case Motion.Fuel:
                    scale.y = Mathf.Clamp01(state.Fuel);
                    break;
                case Motion.Valve:
                    rotation += state.Open ? 0f : 90f;
                    break;
                case Motion.OpenLift:
                    position.y += state.Open ? layer.Travel : 0f;
                    rotation += state.Open ? 12f : 0f;
                    break;
                case Motion.Powered:
                    tint.a *= state.Working ? 1f : 0f;
                    break;
                case Motion.FlowPulse:
                    float flowAge = Mathf.Repeat(now * .8f + i * .25f, 1f);
                    position.x += flowAge * layer.Travel;
                    tint.a *= state.Working ? Mathf.Sin(flowAge * Mathf.PI) : 0f;
                    break;
                case Motion.Heat:
                    tint.a *= Mathf.Clamp01(state.Heat);
                    break;
                case Motion.Progress:
                    scale.y = Mathf.Clamp01(state.Progress);
                    break;
                case Motion.CordLeft:
                case Motion.CordRight:
                case Motion.PumpCordLeft:
                case Motion.PumpCordRight:
                    Vector2 end = new(layer.Rect.x + stroke * layer.Travel, layer.Rect.y);
                    if (layer.Movement is Motion.PumpCordLeft or Motion.PumpCordRight)
                        end = new Vector2(layer.Rect.x, layer.Rect.y + stroke * layer.Travel);
                    Vector2 origin = layer.CordAnchor;
                    position = (origin + end) * .5f;
                    Vector2 delta = end - origin;
                    size = new Vector2(delta.magnitude, 3f);
                    rotation = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                    break;
            }
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0, 0, rotation);
            rect.localScale = scale;
            image.color = tint;
        }
    }
    #endregion
}
