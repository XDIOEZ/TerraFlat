using System;
using FlatWorld.Gameplay.Building;
using UnityEngine;

// 建筑预览与朝向输入。
public partial class Mod_Building
{
    #region 建筑预览与朝向输入
    /// <summary>四向建筑旋转九十度，仅横向镜像的建筑继续保持原占地。</summary>
    public void RotatePlacement()
    {
        if (!CanRotatePlacement)
            return;

        if (AllowsQuarterTurnPlacement)
        { _placementQuarterTurns = (_placementQuarterTurns + 1) & 3; return; }
        _placementMirrorX = !_placementMirrorX;
        ApplyHorizontalMirrorPreview();
    }

    private void BindPlacementRotationInput()
    {
        if ((Data?.AllowHorizontalMirrorPlacement != true && !AllowsQuarterTurnPlacement) || item?.Owner == null)
            return;

        Mod_GameController controller = item.Owner.itemMods?.GetMod_ByID<Mod_GameController>(ModText.Controller)
            ?? item.Owner.GetComponentInChildren<Mod_GameController>(true);
        if (controller == null)
            return;
        if (_rotationInputBound && ReferenceEquals(_ownerController, controller))
            return;

        UnbindPlacementRotationInput();
        _ownerController = controller;
        _ownerController.BuildingRotationRequested += RotatePlacement;
        _rotationInputBound = true;
    }

    private void UnbindPlacementRotationInput()
    {
        if (_rotationInputBound && _ownerController != null)
            _ownerController.BuildingRotationRequested -= RotatePlacement;
        _rotationInputBound = false;
    }

    private void CaptureBaseSpriteFlip()
    {
        if (_baseSpriteFlipCaptured || item?.Sprite == null)
            return;
        _baseSpriteFlipX = item.Sprite.flipX;
        _baseSpriteFlipCaptured = true;
    }

    private void ApplyHorizontalMirrorVisual()
    {
        if (item?.Sprite == null)
            return;
        CaptureBaseSpriteFlip();
        item.Sprite.flipX = _baseSpriteFlipX ^
            (Data?.Role == BuildingRole.PlacedBuilding && Data.HorizontalMirrorX);
    }

    private void ApplyHorizontalMirrorPreview()
    {
        if (GhostShadow?.ShadowRenderer == null || !_ghostBaseFlipCaptured ||
            Data?.AllowHorizontalMirrorPlacement != true)
            return;
        GhostShadow.ShadowRenderer.flipX = _ghostBaseFlipX ^ _placementMirrorX;
    }

    private void HandleGhostShadow()
    {
        Vector3 mouse = NormalizeRequestedPlacement(GetPointerWorldPosition());
        Vector3 authorityPosition = GetAuthorityPosition();
        float maximumPlacementDistance = GetMaxPlacementDistance();
        bool withinReach = IsWithinPlacementDistance(authorityPosition, mouse, maximumPlacementDistance);
        if (!withinReach)
        {
            // 虚影可见范围与实际可提交范围一致，桌面和手机均不保留越界红色虚影。
            CleanupGhost();
            return;
        }

        if (GhostShadow == null)
        {
            CreateGhostShadow();
            if (GhostShadow == null)
                return;
        }

        FlatWorld.Spaceflight.SpaceSession session = FlatWorld.Spaceflight.SpaceSession.Current;
        Player actor = ResolvePlacementActor();
        GhostShadow.transform.position = session != null && session.TryGetPlacementWorldShip(actor, out FlatWorld.Spaceflight.ShipState ship)
            ? (Vector3)session.ProjectShipInteriorPosition(ship, mouse) : WorldLocalPresentation.ProjectPosition(mouse);
        GhostShadow.UpdateAlpha(1f);
        IBuildingPlacementExtension extension = BuildingPlacementLifecycle.GetExtension(item);
        extension?.ApplyPreview(GhostShadow);
        int quarterTurns = PlacementQuarterTurns;
        float angle = quarterTurns * 90f;
        if (session?.TrySnapPlacement(item.itemData, mouse, quarterTurns, out _, out float snappedAngle, actor) == true)
            angle = snappedAngle;
        // 机械扩展负责子图层安装角，预览根只承担船角；无船体时也清除上一帧的船角。
        GhostShadow.transform.rotation = BuildingShadow.ResolvePlacementRootRotation(angle,
            extension is IBuildingPreviewRotation ? quarterTurns : 0);
        ApplyHorizontalMirrorPreview();
        GhostShadow.UpdateColor(!ValidatePlacement(mouse, authorityPosition, out _));
    }

    /// <summary>按目标格子的最近边缘校验距离，保留每轴半格的格心吸附余量。</summary>
    public static bool IsWithinPlacementDistance(
        Vector3 authorityPosition,
        Vector3 placement,
        float maximumPlacementDistance)
    {
        Vector2 wrappedDelta = FlatWorld.Spaceflight.SpaceSession.Current?.IsSpaceView == true ? (Vector2)(placement - authorityPosition) : WorldTopologyRuntime.ShortestDelta(
            new Vector2(authorityPosition.x, authorityPosition.y),
            new Vector2(placement.x, placement.y));
        Vector2 distanceToCell = new(
            Mathf.Max(0f, Mathf.Abs(wrappedDelta.x) - PlacementCellHalfExtent),
            Mathf.Max(0f, Mathf.Abs(wrappedDelta.y) - PlacementCellHalfExtent));
        float allowedDistance = Mathf.Max(0f, maximumPlacementDistance) + BoundsEpsilon;
        return distanceToCell.sqrMagnitude <= allowedDistance * allowedDistance;
    }

    private void CreateGhostShadow()
    {
        if (_ghostCreationFailed || GameRes.Instance == null || item == null)
            return;

        GameObject shadowObject = null;
        try
        {
            shadowObject = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            GhostShadow = shadowObject != null
                ? shadowObject.GetComponentInChildren<BuildingShadow>(true)
                : null;

            if (GhostShadow == null ||
                !TryGetBuildingPreviewVisual(out SpriteRenderer source,
                    out Transform sourceRoot))
                throw new MissingComponentException("BuildingShadow 预制体或建筑 SpriteRenderer 配置不完整");

            GhostShadow.InitShadow(
                source,
                sourceRoot);
            _ghostBaseFlipX = GhostShadow.ShadowRenderer.flipX;
            _ghostBaseFlipCaptured = true;
            ApplyHorizontalMirrorPreview();
        }
        catch (Exception exception)
        {
            _ghostCreationFailed = true;
            if (shadowObject != null)
                Destroy(shadowObject);
            GhostShadow = null;
            Debug.LogError($"[建筑预览] 创建失败：{exception.Message}", item);
        }
    }

    private Vector3 GetPointerWorldPosition()
    {
        if (_ownerController == null && item?.Owner != null)
        {
            _ownerController = item.Owner.itemMods?.GetMod_ByID<Mod_GameController>(ModText.Controller);
            if (_ownerController == null)
                _ownerController = item.Owner.GetComponent<Mod_GameController>();
        }

        return _ownerController != null
            ? _ownerController.GetMouseWorldPosition()
            : GetAuthorityPosition();
    }

    /// <summary>按同一层级返回预览图片和局部坐标根节点，避免跨对象计算出世界坐标偏移。</summary>
    private bool TryGetBuildingPreviewRenderer(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot)
    {
        sourceRenderer = null;
        sourceRoot = null;

        // 格子建筑直接读取最终 Tile 的图片和变换，保持高墙 Pivot、缩放与格心偏移一致。
        if (!string.IsNullOrWhiteSpace(Data?.TileBlockId))
        {
            GameObject tilePreview = GetTilePreviewSource();
            sourceRenderer = tilePreview.GetComponentInChildren<SpriteRenderer>(true);
            sourceRoot = tilePreview.transform;
            return true;
        }

        // 运行时物品定义可能复用通用 Prop 外壳；自身就是建筑时必须使用已应用定义 Sprite，
        // 否则建筑预览会误读 Prop 预制体上的默认贴图。
        if (UsesCurrentItemAsBuildingPrefab() && TryGetCurrentItemPreviewRenderer(out sourceRenderer, out sourceRoot))
            return true;

        if (TryGetDefinitionPreviewSource(out GameObject definitionPreview))
        {
            sourceRenderer = definitionPreview.GetComponentInChildren<SpriteRenderer>(true);
            sourceRoot = definitionPreview.transform;
            if (sourceRenderer != null && sourceRenderer.sprite != null)
                return true;
        }

        if (TryGetBuildingBodyPrefab(out GameObject buildingPrefab))
        {
            Item buildingItem = buildingPrefab.GetComponent<Item>();
            SpriteRenderer bodyRenderer = buildingItem != null ? buildingItem.Sprite : null;
            if (bodyRenderer != null && bodyRenderer.sprite != null)
            {
                sourceRenderer = bodyRenderer;
                sourceRoot = buildingPrefab.transform;
                return true;
            }

            SpriteRenderer[] bodyRenderers = buildingPrefab.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                SpriteRenderer candidate = bodyRenderers[i];
                if (candidate == null || candidate.sprite == null)
                    continue;

                sourceRenderer = candidate;
                sourceRoot = buildingPrefab.transform;
                return true;
            }
        }

        if (item == null)
            return false;

        if (item.Sprite != null && item.Sprite.sprite != null)
        {
            sourceRenderer = item.Sprite;
            sourceRoot = item.transform;
            return true;
        }

        SpriteRenderer[] itemRenderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            SpriteRenderer candidate = itemRenderers[i];
            if (candidate == null || candidate.sprite == null)
                continue;

            sourceRenderer = candidate;
            sourceRoot = item.transform;
            return true;
        }

        return false;
    }

    private bool TryGetCurrentItemPreviewRenderer(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot)
    {
        sourceRenderer = null;
        sourceRoot = null;
        if (item == null)
            return false;

        if (item.Sprite != null && item.Sprite.sprite != null)
        {
            sourceRenderer = item.Sprite;
            sourceRoot = item.transform;
            return true;
        }

        SpriteRenderer[] itemRenderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            SpriteRenderer candidate = itemRenderers[i];
            if (candidate == null || candidate.sprite == null)
                continue;

            sourceRenderer = candidate;
            sourceRoot = item.transform;
            return true;
        }

        return false;
    }

    /// <summary>返回真实放置预览使用的图片和局部坐标根节点；占地由独立格层维护。</summary>
    public bool TryGetBuildingPreviewVisual(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot)
    {
        TryGetBuildingPreviewRenderer(out sourceRenderer, out sourceRoot);
        return sourceRenderer != null && sourceRoot != null;
    }

    /// <summary>以格心为根节点复刻静态 Tile 的视觉变换，占地由独立格层记录。</summary>
    private GameObject GetTilePreviewSource()
    {
        string previewId = "tile:" + Data.TileBlockId;
        if (_definitionPreviewSource != null &&
            string.Equals(_definitionPreviewItemId, previewId, StringComparison.Ordinal))
            return _definitionPreviewSource;

        UnityEngine.Tilemaps.Tile tile = GameRes.Instance?.GetTileBlock(Data.TileBlockId)?.
            GetTileBaseAsset() as UnityEngine.Tilemaps.Tile;
        if (tile == null || tile.sprite == null)
            throw new InvalidOperationException($"格子建筑 {Data.TileBlockId} 缺少静态 Tile 或 Sprite。");

        if (_definitionPreviewSource != null)
            Destroy(_definitionPreviewSource);
        _definitionPreviewSource = new GameObject($"{Data.TileBlockId}_BuildingPreview")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        GameObject renderObject = new GameObject("Render")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        renderObject.transform.SetParent(_definitionPreviewSource.transform, false);
        renderObject.transform.localPosition = tile.transform.GetColumn(3);
        renderObject.transform.localRotation = tile.transform.rotation;
        renderObject.transform.localScale = tile.transform.lossyScale;
        SpriteRenderer renderer = renderObject.AddComponent<SpriteRenderer>();
        renderer.sprite = tile.sprite;
        renderer.color = tile.color;
        _definitionPreviewSource.SetActive(false);
        _definitionPreviewItemId = previewId;
        return _definitionPreviewSource;
    }

    /// <summary>按建筑本体 JSON 创建一个无模块、不可见的轻量预览源。</summary>
    private bool TryGetDefinitionPreviewSource(out GameObject previewSource)
    {
        previewSource = null;
        string buildingId = Data?.BuildingPrefabId;
        if (string.IsNullOrWhiteSpace(buildingId))
            buildingId = GetBuildingPrefabId(item?.itemData?.IDName);
        if (string.IsNullOrWhiteSpace(buildingId) || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(buildingId, out RuntimeItemDefinition definition) ||
            definition?.Sprite == null)
        {
            return false;
        }

        if (_definitionPreviewSource == null ||
            !string.Equals(_definitionPreviewItemId, buildingId, StringComparison.Ordinal))
        {
            if (_definitionPreviewSource != null)
                Destroy(_definitionPreviewSource);
            _definitionPreviewSource = CreateDefinitionPreviewSource(definition);
            _definitionPreviewItemId = buildingId;
        }

        previewSource = _definitionPreviewSource;
        return previewSource != null;
    }

    /// <summary>把运行时定义中的图片与渲染参数还原到纯视觉预览对象。</summary>
    private static GameObject CreateDefinitionPreviewSource(RuntimeItemDefinition definition)
    {
        GameObject root = new GameObject($"{definition.Id}_BuildingPreview")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        GameObject renderObject = new GameObject("Render")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        renderObject.transform.SetParent(root.transform, false);

        ItemVisualDefinitionDto visual = definition.Visual;
        SpriteRenderer renderer = renderObject.AddComponent<SpriteRenderer>();
        renderer.sprite = definition.Sprite;
        if (definition.Material != null)
            renderer.sharedMaterial = definition.Material;
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        if (visual?.RendererLocalPosition.HasValue == true)
            renderObject.transform.localPosition = visual.RendererLocalPosition.Value;
        if (visual?.RendererLocalEulerAngles.HasValue == true)
            renderObject.transform.localEulerAngles = visual.RendererLocalEulerAngles.Value;
        if (visual?.RendererLocalScale.HasValue == true)
            renderObject.transform.localScale = visual.RendererLocalScale.Value;
        if (visual?.Color.HasValue == true)
            renderer.color = visual.Color.Value;
        if (visual?.FlipX.HasValue == true)
            renderer.flipX = visual.FlipX.Value;
        if (visual?.FlipY.HasValue == true)
            renderer.flipY = visual.FlipY.Value;
        if (!string.IsNullOrWhiteSpace(visual?.SortingLayerName))
            renderer.sortingLayerName = visual.SortingLayerName;
        if (visual?.SortingOrder.HasValue == true)
            renderer.sortingOrder = visual.SortingOrder.Value;

        root.SetActive(false);
        return root;
    }

    private bool UsesCurrentItemAsBuildingPrefab()
    {
        return item != null &&
               string.Equals(Data?.BuildingPrefabId, item.itemData?.IDName, StringComparison.Ordinal);
    }

    private bool TryGetBuildingBodyPrefab(out GameObject buildingPrefab)
    {
        buildingPrefab = null;
        string buildingPrefabId = Data?.BuildingPrefabId;
        if (string.IsNullOrWhiteSpace(buildingPrefabId))
            buildingPrefabId = GetBuildingPrefabId(item?.itemData?.IDName);

        return GameRes.Instance != null &&
               GameRes.Instance.AllPrefabs != null &&
               GameRes.Instance.AllPrefabs.TryGetValue(buildingPrefabId, out buildingPrefab) &&
               buildingPrefab != null;
    }

    private Vector3 GetAuthorityPosition()
        => item?.Owner != null ? item.Owner.transform.position : item != null ? item.transform.position : transform.position;

    /// <summary>建筑预览和提交共用放置距离，创造背包启用后解除距离上限。</summary>
    public float GetMaxPlacementDistance(Player placementActor = null)
    {
        Player actor = placementActor != null ? placementActor : item?.Owner as Player;
        return ResolveMaxPlacementDistance(actor, Data?.maxVisibleDistance ?? Mod_InteractSender.DefaultMaxInteractDistance,
            item?.Owner);
    }

    /// <summary>预览、手机准星与服务器提交共用玩家实际交互半径。</summary>
    public static float ResolveMaxPlacementDistance(Player actor, float fallbackDistance, Item owner = null)
    {
        if (CreativeInventoryState.IsEnabled(actor))
            return float.PositiveInfinity;

        Mod_InteractSender interactionSender = actor != null
            ? actor.GetComponentInChildren<Mod_InteractSender>(true)
            : owner?.GetComponentInChildren<Mod_InteractSender>(true);
        return interactionSender != null
            ? Mathf.Max(0.01f, interactionSender.maxInteractDistance)
            : Mathf.Max(0.01f, fallbackDistance);
    }
    #endregion
}
