"""检查 Unity 对五个 SVG 矢量图标的导入及 Prefab 绑定。"""

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from UnityMcpFast import UnityMcp, default_url


ROOT = Path(__file__).resolve().parents[2] / "Assets/6_Art/UI/MainMenuIcons"
EXPECTED = {
    "选择存档": "continue",
    "新游戏": "new-world",
    "联机模式": "multiplayer",
    "MOD管理": "mods",
    "设置": "settings",
}

for icon in EXPECTED.values():
    source = ET.parse(ROOT / f"{icon}.svg").getroot()
    assert source.attrib.get("viewBox") == "0 0 24 24", icon
    assert source.attrib.get("fill") == "none", icon
    assert not (ROOT / f"{icon}.png").exists(), "主菜单不应再依赖 PNG: " + icon

script = """
const path='Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab';
const asset=CS.UnityEditor.AssetDatabase.LoadAssetAtPath(path,puer.$typeof(CS.UnityEngine.GameObject));
const buttons=asset.GetComponentsInChildren(puer.$typeof(CS.UnityEngine.UI.Button),true);
const iconType=puer.$typeof(CS.Unity.VectorGraphics.SVGImage);
const icons=[];
for(const name of ['选择存档','新游戏','联机模式','MOD管理','设置']){
  const node=asset.transform.Find(name==='设置'?name:'旅程菜单卡/'+name);
  if(!node)throw new Error('缺少菜单按钮: '+name);
  const button=node.GetComponent(puer.$typeof(CS.UnityEngine.UI.Button));
  const transform=button.transform.Find('菜单图标');
  const icon=transform?transform.GetComponent(iconType):null;
  const importedName={'选择存档':'continue','新游戏':'new-world','联机模式':'multiplayer','MOD管理':'mods','设置':'settings'}[name];
  const svgPath='Assets/6_Art/UI/MainMenuIcons/'+importedName+'.svg';
  const importer=CS.UnityEditor.AssetImporter.GetAtPath(svgPath);
  const importedAssets=CS.UnityEditor.AssetDatabase.LoadAllAssetsAtPath(svgPath);
  const assetTypes=[];
  for(let i=0;i<importedAssets.Length;i++)
    assetTypes.push(importedAssets.GetValue(i).GetType().FullName);
  const source=CS.UnityEditor.AssetDatabase.LoadAssetAtPath(svgPath,puer.$typeof(CS.UnityEngine.GameObject));
  const importedIcon=source?source.GetComponent(iconType):null;
  const index=button.transform.Find(button.name+'_序号');
  const rect=icon?icon.rectTransform:null;
  const titleTransform=button.transform.Find(name==='设置'?'设置标题':name+'_标题');
  const title=titleTransform.GetComponent(puer.$typeof(CS.UnityEngine.RectTransform));
  const arrowTransform=name==='设置'?button.transform.Find('设置箭头'):null;
  const arrow=arrowTransform?arrowTransform.GetComponent(puer.$typeof(CS.UnityEngine.RectTransform)):null;
  let iconCount=0;
  for(let child=0;child<button.transform.childCount;child++){
    if(button.transform.GetChild(child).name==='菜单图标')iconCount++;
  }
  icons.push({
    button:button.name, sprite:icon&&icon.sprite?icon.sprite.name:null,
    vectorComponent:icon?icon.GetType().FullName:null,
    svgType:importer?Number(importer.SvgType):null,
    targetResolution:importer?Number(importer.TargetResolution):null,
    svgAssetTypes:assetTypes,
    spriteVertices:icon&&icon.sprite?icon.sprite.vertices.Length:0,
    sourceMatches:icon&&importedIcon?icon.sprite.GetInstanceID()===importedIcon.sprite.GetInstanceID():false,
    materialMatches:icon&&importedIcon?icon.material.GetInstanceID()===importedIcon.material.GetInstanceID():false,
    raycast:icon?icon.raycastTarget:null,
    size:rect?[rect.sizeDelta.x,rect.sizeDelta.y]:null,
    indexVisible:index?index.gameObject.activeSelf:null,
    iconCount:iconCount,
    iconRight:rect?rect.anchoredPosition.x+rect.sizeDelta.x:null,
    titleLeft:title.anchoredPosition.x,
    titleRight:title.anchoredPosition.x+title.sizeDelta.x,
    arrowLeft:arrow?button.GetComponent(puer.$typeof(CS.UnityEngine.RectTransform)).rect.width
      +arrow.anchoredPosition.x-arrow.sizeDelta.x:null
  });
}
return JSON.stringify({buttonCount:buttons.Length,icons:icons});
"""

client = UnityMcp(default_url(), timeout=15)
try:
    response, _ = client.request("tools/call", {
        "name": "editor.js.eval",
        "arguments": {"mode": "script", "code": script},
    })
finally:
    client.close()

if response.get("isError"):
    raise RuntimeError(str(response))
result = json.loads(response["structuredContent"]["value"])
assert result["buttonCount"] == 5, result
assert {icon["button"] for icon in result["icons"]} == set(EXPECTED), result
for icon in result["icons"]:
    assert icon["sprite"] == EXPECTED[icon["button"]] + "Sprite", icon
    assert icon["vectorComponent"] == "Unity.VectorGraphics.SVGImage", icon
    assert icon["svgType"] == 4 and icon["spriteVertices"] > 0, icon
    assert icon["targetResolution"] == 256, icon
    assert icon["sourceMatches"] and icon["materialMatches"], icon
    assert icon["raycast"] is False, icon
    assert icon["iconCount"] == 1, icon
    assert icon["size"] == ([28, 28] if icon["button"] == "设置" else [32, 32]), icon
    assert icon["iconRight"] + 4 <= icon["titleLeft"], icon
    if icon["button"] != "设置":
        assert icon["indexVisible"] is False, icon
    else:
        assert icon["titleRight"] + 4 <= icon["arrowLeft"], icon
print("主菜单五个 SVG 矢量资源、SVGImage 与 Prefab 绑定全部通过")
print(" | ".join(f"{icon['button']}：{icon['spriteVertices']} 顶点" for icon in result["icons"]))
