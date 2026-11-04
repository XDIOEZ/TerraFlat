"""Run the isolated native Unity artwork preview or read the Console counts."""
import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from UnityMcpFast import UnityMcp, default_url

parser=argparse.ArgumentParser()
parser.add_argument('action',choices=['console','refresh','preview','sync','rules'])
args=parser.parse_args()
code={
    'console': "return __unity_mcp.invokeStatic('PhysicalDeviceArtworkPreview','ReadConsoleSummary');",
    'refresh': "CS.UnityEditor.AssetDatabase.Refresh(); return {requested:true};",
    'sync': "__unity_mcp.invokeStatic('PhysicalDeviceArtworkPrefabBuilder','SynchronizeAllPhysicalDeviceArtwork'); return {synchronized:true};",
    'rules': "return __unity_mcp.invokeStatic('GameplayTestPhysicalDeviceArtworkRules','CaptureEditorPreviewRules');",
    'preview': "__unity_mcp.invokeStatic('PhysicalDeviceArtworkPreview','RenderAll'); return {rendered:true};"
}[args.action]
client=UnityMcp(default_url(),60)
try:
    result,_=client.request('tools/call',{'name':'editor.js.eval','arguments':{'code':code,'mode':'script'}})
    content=result.get('structuredContent',{})
    if not content:
        content=json.loads(result['content'][0]['text'])
    value=content.get('value',content)
    if isinstance(value,str):
        try:value=json.loads(value)
        except ValueError:pass
    output=Path('Library/FlatWorldDeviceArtwork')
    output.mkdir(parents=True,exist_ok=True)
    (output/(args.action+'-receipt.json')).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
    if args.action=='console':
        print(json.dumps({k:v for k,v in value.items() if k!='recent'},ensure_ascii=True))
    else:print(json.dumps(value,ensure_ascii=True))
finally:client.close()
