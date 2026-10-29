#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GameplayTestPersistentHierarchyRuntimeProbe
{
    #region 常驻对象跨场景回归
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "core_lifecycle", "persistent_object_hierarchy", Capture);

    private sealed class Fixture : IDisposable
    {
        public Scene Scene;
        public GameObject Owner;
        public GameObject Peer;
        public void Dispose()
        {
            if (Owner != null) Object.Destroy(Owner);
            if (Peer != null) Object.Destroy(Peer);
        }
        public IEnumerator ReleaseScene()
        {
            if (Scene.IsValid() && Scene.isLoaded) yield return SceneManager.UnloadSceneAsync(Scene);
        }
    }

    private static IEnumerator Capture(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var fixture = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(fixture.ReleaseScene);
        using (fixture)
        {
            Scene initialScene = SceneManager.GetActiveScene();
            Scene playerScene = player.gameObject.scene;
            fixture.Scene = SceneManager.CreateScene("PersistentHierarchyProbe_" + Guid.NewGuid().ToString("N"));
            fixture.Owner = new GameObject("PersistentHierarchyProbe.Owner");
            SceneManager.MoveGameObjectToScene(fixture.Owner, fixture.Scene);
            var child = new GameObject("PersistentHierarchyProbe.Child");
            child.transform.SetParent(fixture.Owner.transform, false);
            var transient = new GameObject("PersistentHierarchyProbe.Transient");
            SceneManager.MoveGameObjectToScene(transient, fixture.Scene);
            fixture.Owner.transform.position = new Vector3(17, -11, 3);
            fixture.Owner.transform.localScale = new Vector3(2, 3, 1);
            Matrix4x4 pose = fixture.Owner.transform.localToWorldMatrix;
            fixture.Owner.SetActive(false);

            bool moved = PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(
                fixture.Owner, PersistentRuntimeObjectCategory.Testing);
            var hierarchy = PersistentRuntimeObjectHierarchy.ExistingInstance;
            Transform category = fixture.Owner.transform.parent;
            bool repeated = PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(
                fixture.Owner, PersistentRuntimeObjectCategory.Testing);
            bool nested = PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(child, PersistentRuntimeObjectCategory.Debug);
            var result = new JObject
            {
                ["persistentScene"] = moved && fixture.Owner.scene.name == "DontDestroyOnLoad",
                ["idempotent"] = repeated && fixture.Owner.transform.parent == category,
                ["ownedChildPreserved"] = nested && child.transform.parent == fixture.Owner.transform,
                ["inactiveAndPosePreserved"] = !fixture.Owner.activeSelf && !fixture.Owner.activeInHierarchy &&
                    fixture.Owner.transform.localToWorldMatrix == pose
            };
            fixture.Peer = new GameObject("PersistentHierarchyProbe.Peer");
            PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(fixture.Peer, PersistentRuntimeObjectCategory.Testing);
            yield return SceneManager.UnloadSceneAsync(fixture.Scene);
            result["sourceSceneUnloaded"] = !fixture.Scene.IsValid() || !fixture.Scene.isLoaded;
            result["transientDestroyed"] = transient == null;
            result["persistentTreeSurvived"] = fixture.Owner != null && child != null && fixture.Peer != null;
            result["activeSceneUnchanged"] = SceneManager.GetActiveScene() == initialScene;

            Object.Destroy(fixture.Owner);
            yield return null;
            result["ownerReleasedIndependently"] = fixture.Owner == null && child == null && fixture.Peer != null &&
                PersistentRuntimeObjectHierarchy.ExistingInstance == hierarchy;
            fixture.Peer.transform.SetParent(null, true);
            PersistentRuntimeObjectHierarchy.OrganizeUnregisteredPersistentRoots();
            result["ungroupedPersistentRootCollected"] = hierarchy.TryGetObjectCategory(fixture.Peer, out var categoryId) &&
                categoryId == PersistentRuntimeObjectCategory.Infrastructure;
            result["gameManagerGrouped"] = hierarchy.TryGetObjectCategory(GameManager.ExistingInstance.gameObject, out _);
            var ui = Object.FindObjectOfType<UIManager>();
            var audio = Object.FindObjectOfType<FlatWorld.Audio.AudioService>();
            result["uiGrouped"] = ui != null && hierarchy.TryGetObjectCategory(ui.gameObject, out var uiCategory) &&
                uiCategory == PersistentRuntimeObjectCategory.UI;
            result["audioGrouped"] = audio != null && hierarchy.TryGetObjectCategory(audio.gameObject, out var audioCategory) &&
                audioCategory == PersistentRuntimeObjectCategory.Audio;
            int visibleRoots = 0;
            foreach (GameObject root in hierarchy.gameObject.scene.GetRootGameObjects())
                if ((root.hideFlags & HideFlags.HideInHierarchy) == 0) visibleRoots++;
            result["oneVisiblePersistentRoot"] = visibleRoots == 1 && hierarchy.transform.parent == null;
            result["playerWorldNotPromoted"] = player.gameObject.scene == playerScene &&
                player.GetComponentInParent<RuntimeSceneObjectHierarchy>() != null;
            completed(result);
        }
    }
    #endregion
}
#endif
