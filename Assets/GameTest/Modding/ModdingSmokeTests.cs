using FlatWorld.GameTest.Shared;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Modding
{
    /// <summary>MOD 基础冒烟测试：保护运行时、manifest、Lua 与模板入口。</summary>
    public sealed class ModdingSmokeTests
    {
        [Test]
        [Category("Modding.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModRuntimeManager.cs", "ModRuntimeManager");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModManifest.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModLuaRuntime.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-2_Editor/Mods/ModTemplateCreator.cs");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Module/Mod_LuaBehaviour.prefab");
        }

        [Test]
        [Category("Modding.Smoke")]
        [Category("Smoke")]
        public void ActorDefinitionsAndLuaExtensionAreExposedToMods()
        {
            const string json = @"{
  'assets': [],
  'items': [],
  'actors': [{
    'id': 'example.mod:forest_wolf',
    'parent': 'Wolf',
    'visual': { 'spriteBundle': 'actors', 'spriteAsset': 'forest_wolf' },
    'modules': { 'lua': {
      'prefab': 'Mod_LuaBehaviour',
      'id': 'Mod_LuaBehaviour',
      'parameters': { 'scriptPath': 'Lua/actor.lua' }
    }}
  }]
}";
            ModDefinitionDocument document = JObject.Parse(json).ToObject<ModDefinitionDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(document.Actors, Has.Count.EqualTo(1));
            Assert.That(document.Actors[0].Parent, Is.EqualTo("Wolf"));
            Assert.That(document.Actors[0].Visual.SpriteBundle, Is.EqualTo("actors"));

            GameObject luaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Module/Mod_LuaBehaviour.prefab");
            Assert.That(luaPrefab?.GetComponent<Mod_LuaBehaviour>(), Is.Not.Null);
            Assert.That(luaPrefab.GetComponent<Mod_LuaBehaviour>().CanonicalModuleId,
                Is.EqualTo(Mod_LuaBehaviour.ModuleId));
        }
    }
}
