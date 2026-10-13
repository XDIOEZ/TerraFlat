# FlatWorld 游戏机制 SVG

这里保存解释游戏机制的 SVG 图片和动画，作为项目长期维护的资产。按机制分类，后续继续补充。

| 机制 | 图片或动画 | 内容 |
| --- | --- | --- |
| 环境 | [风力推动与质量](环境/风力推动与质量.svg) | 完整目录轻重质量的中点作为停止推动门槛，越轻越快，风推动最高 0.5 单位/秒。 |
| 太空 | [失重玩家移动惯性](太空/失重玩家移动惯性.svg) | WASD 慢慢起步，松键继续漂，反向输入渐进刹车；人工重力恢复普通步行。 |

创建说明：[flatworld-mechanics-svg](../.agents/skills/flatworld-mechanics-svg/SKILL.md)。

风力规则来源：[WindEnvironmentRules.cs](../Assets/5_Scripts/5-3_GamePlay/World/Environment/WindEnvironmentRules.cs)。图示用于解释规则，玩法效果由用户验收。
