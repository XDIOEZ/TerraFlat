using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FlatWorld.Spaceflight
{
    public sealed partial class ShipPieceView
    {
        #region 船上电灯表现
        private MachineEntity lightEntity;
        private ElectricLightLogic lightLogic;
        private Light2D electricLight;
        private SpriteRenderer lightEmission;

        private void BindElectricLightVisual(MachineEntity node)
        {
            MachineEntity source = node?.Definition.Lighting != null ? node : null;
            if (!ReferenceEquals(lightEntity, source))
            {
                UnsubscribeElectricLightVisual();
                lightEntity = source;
                SubscribeElectricLightVisual();
            }
            RefreshElectricLightVisual();
        }

        private void HandleElectricLightChanged(MachineEntity node)
        {
            if (ReferenceEquals(lightEntity, node)) RefreshElectricLightVisual();
        }

        private void RefreshElectricLightVisual()
        {
            ElectricLightLogic logic = lightEntity?.Logic as ElectricLightLogic;
            if (!ReferenceEquals(lightLogic, logic))
            {
                if (lightLogic != null) lightLogic.Changed -= RefreshElectricLightVisual;
                lightLogic = logic;
                if (isActiveAndEnabled && logic != null) logic.Changed += RefreshElectricLightVisual;
            }
            if (visual == null || lightEntity == null)
            {
                DisableElectricLightVisual();
                return;
            }
            RuntimeItemDefinition item = lightEntity.Definition.Content?.Definition;
            if (item == null && GameRes.ExistingInstance?.TryGetItemDefinition(
                lightEntity.Definition.Id, out item) != true)
            {
                DisableElectricLightVisual();
                return;
            }
            visual.sprite = MachineLightPresentation.ResolveBodySprite(lightEntity, item);
            MachineLightDefinition lighting = lightEntity.Definition.Lighting;
            float power = ElectricLightLogic.GetPowerFraction(lightEntity);
            if (power <= 0f)
            {
                DisableElectricLightVisual();
                return;
            }
            if (electricLight == null)
            {
                var child = new GameObject("ShipElectricLight");
                child.transform.SetParent(visual.transform, false);
                electricLight = child.AddComponent<Light2D>();
            }
            electricLight.transform.localPosition = lighting.LocalPosition;
            MachineLightPresentation.ConfigureLight(electricLight, lighting, power);
            if (lightEmission == null)
            {
                var child = new GameObject("ShipLightEmission");
                child.transform.SetParent(visual.transform, false);
                lightEmission = child.AddComponent<SpriteRenderer>();
            }
            // 船上玻璃发光沿用地表的半透明覆盖，并跟随真实设备移动和旋转。
            lightEmission.sortingLayerID = visual.sortingLayerID;
            lightEmission.sortingOrder = visual.sortingOrder + 1;
            MachineLightPresentation.ConfigureEmission(lightEmission, visual.sprite, visual.color, lighting, power);
        }

        private void SubscribeElectricLightVisual()
        {
            if (lightEntity == null || !isActiveAndEnabled) return;
            MachineWorld.NodeStateChanged -= HandleElectricLightChanged;
            MachineWorld.NodeStateChanged += HandleElectricLightChanged;
            if (lightLogic != null)
            {
                lightLogic.Changed -= RefreshElectricLightVisual;
                lightLogic.Changed += RefreshElectricLightVisual;
            }
        }

        private void UnsubscribeElectricLightVisual()
        {
            MachineWorld.NodeStateChanged -= HandleElectricLightChanged;
            if (lightLogic != null) lightLogic.Changed -= RefreshElectricLightVisual;
            lightLogic = null;
        }

        private void ReleaseElectricLightVisual()
        {
            UnsubscribeElectricLightVisual();
            lightEntity = null;
            DisableElectricLightVisual();
        }

        private void DisableElectricLightVisual()
        {
            if (electricLight != null) electricLight.enabled = false;
            if (lightEmission != null) lightEmission.enabled = false;
        }
        #endregion
    }
}
