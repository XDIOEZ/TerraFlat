using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// 纯树执行器只保存上下文引用，离线夹具不创建 Unity 对象。
public sealed class AnimalBehaviorTreeContext
{
    public float Nutrition;
    public float GetNutritionRate() => Nutrition;
}

internal static class Program
{
    private sealed class CatalogAction : AnimalBehaviorTreeNode
    {
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime) => AnimalBehaviorTreeStatus.Success;
    }

    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "foraging")
        {
            ForagingDecisionFixture.Verify();
            return;
        }
        if (args.Length > 0 && args[0] == "aquatic-hunt")
        {
            AquaticHuntCleanupFixture.Verify();
            return;
        }
        if (args.Length > 0 && args[0] == "catalog")
        {
            VerifyCatalog(args[1], args[2], args[3]);
            return;
        }
        ActorDefinitionCatalogLoader.DefinitionsFolder = args[0];
        ActorDefinitionCatalogLoader.SharedPath = args[1];
        var argument = new JObject();
        var samples = new JObject
        {
            ["behavior_tree_priority"] = GameplayTestAnimalBehaviorTreeScenarios.Priority(argument),
            ["behavior_tree_sequence"] = GameplayTestAnimalBehaviorTreeScenarios.Sequence(argument),
            ["behavior_tree_subtrees"] = GameplayTestAnimalBehaviorTreeScenarios.Subtrees(argument),
            ["behavior_tree_validation"] = GameplayTestAnimalBehaviorTreeScenarios.Validation(argument),
            ["behavior_tree_service"] = GameplayTestAnimalBehaviorTreeScenarios.Service(argument),
            ["animal_tree_inheritance"] = GameplayTestSystemProbe.CaptureInheritance(argument),
            ["aiecs_tree_parameters"] = FlatWorld.AIECS.Gameplay.AiecsBehaviorTreeParameterTestProbe.Run(argument),
            ["state_machine_open"] = GameplayTestAnimalBehaviorTreeScenarios.Transitions(new JObject { ["blocked"] = false }),
            ["state_machine_blocked"] = GameplayTestAnimalBehaviorTreeScenarios.Transitions(new JObject { ["blocked"] = true })
        };
        System.Console.WriteLine(samples.ToString(Formatting.None));
    }

    // 节点名来自正式注册源码，离线环境只验证生产编译器对真实树的展开。
    private static void VerifyCatalog(string actorFolder, string sharedPath, string registeredPath)
    {
        JObject names = JObject.Parse(System.IO.File.ReadAllText(registeredPath));
        foreach (JToken name in (JArray)names["actions"])
            AnimalBehaviorTreeRegistry.RegisterAction((string)name, (_, _) => new CatalogAction());
        foreach (JToken name in (JArray)names["conditions"])
            AnimalBehaviorTreeRegistry.RegisterCondition((string)name, (_, _) => () => false);
        JObject shared = (JObject)JObject.Parse(System.IO.File.ReadAllText(sharedPath))["subtrees"];
        var catalogs = new System.Collections.Generic.List<string>();
        foreach (string file in System.IO.Directory.GetFiles(actorFolder, "*.json"))
        {
            JObject source = JObject.Parse(System.IO.File.ReadAllText(file));
            catalogs.Add(new JObject { ["schemaVersion"] = 1, ["items"] = source["actors"].DeepClone() }.ToString(Formatting.None));
        }
        var compiled = new System.Collections.Generic.List<string>();
        foreach (JObject actor in ItemDefinitionCatalogLoader.ResolveDefinitionObjects(catalogs))
        {
            if (actor["modules"]?["ai"]?["parameters"]?["behaviorTree"] is not JObject tree) continue;
            string id = (string)actor["id"];
            if ((id == "Bear" || id == "SnowLeopard") && tree["capabilities"]?["pack"] != null)
                throw new System.InvalidOperationException(id + " 错误继承了狼群能力");
            if (id == "Rabbit" && (tree["capabilities"]?["production"] != null || actor["modules"]?["生产模块"] != null))
                throw new System.InvalidOperationException("兔子错误继承了产蛋能力或模块");
            var definition = tree.ToObject<AnimalBehaviorTreeDefinition>();
            var subtrees = shared.ToObject<System.Collections.Generic.Dictionary<string, AnimalBehaviorSubtreeDefinition>>();
            if (definition.Subtrees != null)
                foreach (var pair in definition.Subtrees) subtrees[pair.Key] = pair.Value;
            definition.Subtrees = subtrees;
            AnimalBehaviorTreeCompiler.Compile(definition);
            compiled.Add(id);
        }
        foreach (string required in new[] { "Bear", "Bird", "Seagull", "Bee", "Chicken", "WildBoar", "Wolf", "Ghost",
                     "Zombie", "SmallFish", "Sheep", "Rabbit", "SnowLeopard" })
            if (!compiled.Contains(required)) throw new System.InvalidOperationException("缺少显式行为树：" + required);
        System.Console.WriteLine($"PASS {compiled.Count} resolved Actor trees compiled with actual shared subtree JSON");
    }
}

public static partial class GameplayTestSystemProbe
{
    public static JObject CaptureInheritance(JObject args) => EntityAnimalTreeModuleInheritance(args);
    public static JObject Capture(string system, string operation, JObject args)
        => system == "ai" && operation == "animal_tree_mapping_inputs"
            ? EntityAnimalTreeMappingInputs(args) : throw new System.InvalidOperationException(operation);
}
