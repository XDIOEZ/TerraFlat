#if UNITY_EDITOR
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.Editor.ContentWorkshop
{
    /// <summary>内容工坊通过正式草稿往返采样强化字段，防止保存时丢失配方条件。</summary>
    internal static class GameplayTestWorkshopRecipeProbe
    {
        #region 配方草稿采样
        [InitializeOnLoadMethod]
        private static void Register() => GameplayTestSystemProbe.Register("crafting", "reinforcement_editor_roundtrip", Capture);

        private static JObject Capture(JObject args)
        {
            bool shield = args["shield"]?.Value<bool>() == true;
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath,
                shield ? "GameConfig/Recipes/crafting/shields.json" : "GameConfig/Recipes/crafting/tool_reinforcement.json");
            RecipeDto source = RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(path)).Recipes[shield ? 1 : 0];
            WorkshopRecipeDraft draft = WorkshopRecipeDraft.FromRecord(new WorkshopRecipeRecord
            { Definition = source, PackageId = shield ? "crafting/shields" : "crafting/tool_reinforcement" });
            draft.Outputs[0] = draft.Outputs[0].Clone();
            RecipeDto restored = draft.BuildDto();
            RecipeRuntimeFactory.BuildCatalog(new RecipeCatalogDto { SchemaVersion = 1, Recipes = new() { restored } },
                _ => true, out _);
            return new JObject
            {
                ["temperature"] = restored.Inputs.FirstOrDefault(value => value.ItemId == "Resin")?.MinTemperature,
                ["excludeTag"] = restored.Inputs.First(value => value.ExcludeTag != null).ExcludeTag,
                ["copyInputTag"] = restored.Outputs[0].CopyInputTag,
                ["copyInputItemId"] = restored.Outputs[0].CopyInputItemId,
                ["addTag"] = restored.Outputs[0].AddTag,
                ["durabilityMultiplier"] = restored.Outputs[0].DurabilityMultiplier,
                ["reinforce"] = restored.Outputs[0].ReinforceWithAdhesive,
                ["outputAmount"] = restored.Outputs[0].Amount
            };
        }
        #endregion
    }
}
#endif
