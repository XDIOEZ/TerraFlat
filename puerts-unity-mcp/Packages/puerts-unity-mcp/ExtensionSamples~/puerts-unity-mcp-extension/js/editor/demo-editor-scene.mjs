export function execute(argsJson, contextJson) {
  const args = JSON.parse(argsJson || "{}");
  const context = JSON.parse(contextJson || "{}");
  const scene = CS.UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
  const scenes = CS.UnityEditor.EditorBuildSettings.scenes;
  return JSON.stringify({
    ok: true,
    source: "demo-editor-js",
    endpointKind: context.endpointKind,
    requestedBy: args.requestedBy || "",
    activeScene: {
      name: scene.name,
      path: scene.path,
      isLoaded: scene.isLoaded
    },
    buildSettingsSceneCount: scenes ? scenes.Length : 0
  });
}
