using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class TemperatureMgr
{
    #region 每个世界独立的环境温度场
    private sealed class TemperatureWorld
    {
        public ChunkMgr fieldChunkManager;
        public WorldRuntime fieldWorld;
        public long fieldWorldEpoch;
        public PlanetData fieldPlanet;
        public string fieldDimension;
        public WorldTopologyBounds fieldBounds;
        public LocalTemperatureField localTemperatureField;
        public LiquidTemperatureField liquidTemperatureField;
        public int fieldContextFrame = -1;
        public AtmosphereState fieldAtmosphere;
        public long fieldAtmosphereRevision;
        public AtmosphericGreenhouseConfiguration fieldGreenhouseConfiguration;
        public double fieldInitialCarbonDioxidePartialPressureKPa;
        public decimal fieldAtmosphereReferenceMoles;
        public double fieldAtmosphereReferencePressureKPa;
        public float ambientFieldOffset;
        public float seasonTiltOffsetAtPole;
        public float orbitalTemperatureFactor;
        public int fieldChunkWidth;
        public int fieldChunkHeight;
        public int fieldContextVersion;
    }
    private readonly Dictionary<string, TemperatureWorld> thermalWorlds = new(StringComparer.Ordinal);
    private readonly Dictionary<object, LocalTemperatureField> sourceOwners = new();
    private string cachedThermalWorld;
    private TemperatureWorld cachedThermalState;
    private GameManager temperatureLifecycle;
    private TemperatureWorld FieldState
    {
        get
        {
            string key = GameplayWorldRegistry.ExecutionWorldKey ?? string.Empty;
            if (cachedThermalState != null && key == cachedThermalWorld) return cachedThermalState;
            if (!thermalWorlds.TryGetValue(key, out TemperatureWorld state)) thermalWorlds[key] = state = new TemperatureWorld();
            cachedThermalWorld = key; return cachedThermalState = state;
        }
    }
    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;
        temperatureLifecycle = GameManager.Instance;
        if (temperatureLifecycle != null) temperatureLifecycle.Event_GameWorldExit += ClearThermalWorlds;
    }
    protected override void OnDestroy()
    {
        if (temperatureLifecycle != null) temperatureLifecycle.Event_GameWorldExit -= ClearThermalWorlds;
        ClearThermalWorlds();
        base.OnDestroy();
    }
    private void ClearThermalWorlds()
    {
        thermalWorlds.Clear(); sourceOwners.Clear(); cachedThermalState = null; cachedThermalWorld = null;
    }
    #endregion
}
