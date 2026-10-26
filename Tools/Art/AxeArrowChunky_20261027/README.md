# 大像素斧头与箭矢（2026-10-27）

- 本轮替换6把斧头、4种箭矢。用户已认可的青铜斧、铜斧、燧石斧直接使用原生成PNG，其余图使用新版绘图Skill。
- `asset-integration.json` 保存覆盖前后SHA256、GUID、PPU、Pivot、可见范围、握点像素、世界尺寸和源图路径；`axes-prompts.json`、`arrows-prompts.json` 保存其余7图的真实提示词。
- PNG逐字节复制，没有缩图、裁图、限色或改Alpha。只调整导入设置、PPU和Pivot，保留原GUID、地址、Sprite身份和游戏配置。
- 6斧保留各自原可见最长边，原来已存在的物种间尺寸差异不在本轮统一。握柄点按旧可见范围内的相对位置映射，均落在实心木柄。
- 4箭保留原45°前向轴总长，Pivot由旧可见主体中点映射到新图；`SpriteForwardAngleDegrees=45`原样保留。PCA主体轴角分别约44.85°、43.72°、44.86°、44.09°，不做图像旋转或非均匀拉伸。
- 铁箭保留两道浅色接头标记；定点移除被图像编辑审核拒绝后，主控接受已生成的可用原图。
- `validation.json` 是现成世界尺寸检测器的10项原始结果；另检查源字节一致、GUID、PPU、Pivot、Point、无Mipmap/压缩和平台不降采样。

## 复验

在项目根目录运行下面脚本；只读取文件，不操作Unity：

```python
import json, subprocess, sys
from pathlib import Path
record = json.loads(Path("Tools/Art/AxeArrowChunky_20261027/asset-integration.json").read_text("utf-8"))
for asset in record["assets"]:
    subprocess.run([sys.executable, "Tools/Art/validate_sprite_world_size.py", asset["path"],
                    "--alpha-threshold", "128", "--target-longest", str(max(asset["before"]["worldVisibleSize"])),
                    "--tolerance", "0.05"], check=True)
```

尺寸为源资产静态预测；正式持握、挥砍、飞行/碰撞与游戏中观感仍需在资源重载后验收。本轮没有主动刷新、导入或切换PlayMode。
