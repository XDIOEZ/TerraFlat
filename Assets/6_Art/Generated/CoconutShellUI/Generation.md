# 椰子壳交互 UI 剖面

- 用途：空椰子壳的容器剖面与独立液体遮罩，不包含水或其他液体。
- 参考：../ClayJarUI/ClayJar_Cutaway.png 与 ../../Food/椰子壳-空.png。
- CoconutShell_Cutaway.png：128×128，14 色，Alpha 0/255；主体约 x=4..123、y=30..97。
- CoconutShell_Interior.png：同画布白色硬透明遮罩，与剖面叠放，供 UI Mask 裁剪液体层。
- 导入：Sprite Single、中心 Pivot、PPU 128、Point、关闭 Mipmap、无压缩。
- 生成：内置 imagegen；之后机械最近邻采样、16 色候选调色板映射与内腔遮罩提取。
- 正式水容器界面通过 WaterVesselPanel.Appearances 匹配 Coconut_Shell，切换剖面、遮罩、水位范围和左右倾倒出口；构建器保留同一配置。
- 水位范围为 44/128..88/128，出口为 (14/128,84/128) 与 (114/128,84/128)，均按画布左下角为原点。
- 手工验收：把剖面与遮罩按相同尺寸重叠，检查空、半满、满液位以及左右倾倒时液体不超出切壁。

## 原始生成提示词

Use case: stylized-concept. Create one game UI pixel art coconut shell cutaway, matching attached clay jar cutaway UI reference and coconut shell inventory identity reference. Image 1 clay jar is primary style and cutaway function reference. Image 2 empty coconut shell is material/identity reference. Single EMPTY half coconut shell bowl shown straight-on front vertical cutaway with front half removed, broad shallow semicircular bowl, width about twice its height. Dark brown fibrous exterior, warm tan thin cut edges, dark muted brown continuous empty interior. NO white coconut flesh; this is a scraped empty coconut shell. Leave an unobstructed broad interior where liquid can be rendered separately. Rear rim gently curved, side walls and thick rounded bottom visible; NO front rim crossing cavity. Pixel art hard square pixels, limited 12-16 earthy brown colors, restrained clustered texture, chunky stepped silhouette, no smoothing, no gradients, no photorealism. Match visual pixel size of 128x128 clay jar reference. Centered on square genuinely transparent RGBA canvas, generous blank space above and below shallow bowl. No liquid, water, labels, diagrams, extra objects, shadow, floor, checkerboard or background. Deliver a single ready-to-use transparent sprite, preferably 128x128 pixels or clean nearest-neighbor integer enlargement of that logical grid.
