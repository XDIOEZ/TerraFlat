export function execute(argsJson, contextJson) {
  const args = JSON.parse(argsJson || "{}");
  const context = JSON.parse(contextJson || "{}");
  const scene = CS.UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  return JSON.stringify({
    ok: true,
    source: "demo-runtime-js",
    endpointKind: context.endpointKind,
    requestedBy: args.requestedBy || "",
    productName: CS.UnityEngine.Application.productName,
    platform: String(CS.UnityEngine.Application.platform),
    unityVersion: CS.UnityEngine.Application.unityVersion,
    activeScene: {
      name: scene.name,
      path: scene.path,
      buildIndex: scene.buildIndex
    },
    screen: {
      width: CS.UnityEngine.Screen.width,
      height: CS.UnityEngine.Screen.height,
      dpi: CS.UnityEngine.Screen.dpi
    }
  });
}
