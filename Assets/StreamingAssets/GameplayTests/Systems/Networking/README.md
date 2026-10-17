# 联机与同步

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`networking/endpoint、item_payload、roll_snapshot、roll_stamina_handoff`。

已编写的规则覆盖：NetworkConnectionEndpoint 的 IPv4/IPv6/域名端点、显式端口优先、缺省端口回退和非法输入拒绝；显式0、空端口、超范围及非数字端口不得被Uri默认端口语义吞掉；正式 FWI1 物品网络负载、身份和损坏头拒绝。

2026-10-18：翻滚新增服务器许可、动作快照和体力扣费交接，随 Gameplay 协议升级发布。规则采样正式 Mirror 快照往返、double 时标补偿、到期/暂停进度及旧客户端状态覆盖保护；假人专项补服务器真实请求、重复请求拒绝、结束后零冷却连续发动、体力扣费与无敌收尾。仅已编写，未编译、未运行；真实 owned Client 请求回程、延迟下的移动手感、远端贴图和两端伤害仍由用户验收。

关键流程待补：真实 Host + Client 的入世、重连、断线；权威拾取/建造/战斗；跨星区兴趣管理；消息分片/旧序号拒绝和远端表现。快速结果不表示多人验收通过。

2026-10-17：Gameplay 协议升为 18，机器快速转移回执新增同次源/目标库存快照及精确收货槽/数量/目标 GUID，客户端成功应用后才播放槽间动画。库存 Lua 已补正式提交反馈、Host 表现抑制，以及通过网络程序集夹具调用真实回放器的有效/重复/越界/错误 GUID/超量/非有限数/超过 256 条回执断言，尚未编译或运行；真实 Client token、超时/拒绝、快照应用顺序及 Host/Client 显示仍未覆盖，需用户自行验收。

本轮规则案例已通过，见 [统一执行记录](../../执行记录与失败待办_2026-10-14.md)。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

## PlayMode 共享物品链路

入口：[物品、MOD与网络副本测试集](../ItemRuntime/gameplay-suite.json)，Lua业务断言在 [共享脚本](../ItemRuntime/gameplay.lua)。三个系统按同一实际实例链路组合，目录表映射同一案例，不重复执行。

已编写：真实已加载物品通过 FWI1 Capture→身份读取→正式 InstantiateNetworkItem→远程视觉注销→ApplyRemoteReplica，检查副本耐久/特殊状态、权威索引隔离和损坏包拒绝。

覆盖边界：这是实际网络物品收发端处理链的同进程现场测试，未开启Host/Client或网络传输；连接、重连、断线、协议序号、权威拾取/建造/战斗和跨区兴趣管理仍未覆盖。



## 同进程联机假人专项

2026-10-18：Gameplay 协议升为 20，拾取改为客户端只读容量计划、服务器真实入包及按实际原份数扣量、客户端成功应用权威库存后确认。部分拾取保留同一世界来源的余量；Host 共享库存仅服务器入包一次。已补正式消息夹具和 Lua：只够三份时十份来源余七、真实服务器库存、重复提交幂等、确认前旧库存包拒绝、零/NaN/Infinity/超额/整件小数拒收、真实零接收容量、未提交超时与断线释放。未编译或执行；独立真实 Client 回执应用、Host 接管、太空余量和跨进程时延仍需用户验收。测试适配仅位于 Networking/Gameplay/Testing，业务无测试依赖。

入口：[gameplay-suite.json](gameplay-suite.json)。执行：`python Tools/GameplayTesting/run_suite.py GameplayTests/Systems/Networking/gameplay-suite.json`；`--systems networking --integration` 会同时选择本专项和上面的共享物品链路。

假人是独立的 Mirror 连接和正式 NetworkWorldPlayer/CorePlayer，不打开网络端口，也不启动真实 NetworkClient。假人自有自动拾取触发器关闭，拾取由明确消息发起，避免无客户端库存的视觉副本自动触发。消息经过正式 Pack、Batcher、服务端传输回调和生产处理器；服务端回复经过正常发送队列与逐连接 Unbatcher，再交 Lua 验证原始数据。发送完成不能代替业务断言。

| Lua | 现场和断言 |
| --- | --- |
| [bots-session.lua](bots-session.lua) | 四假人同名加入、独立角色/数据/库存容器、完整世界快照分片/字节/校验和、提前 AddPlayer 拒绝、重复就绪、已占用短名和本地主角名字申请；未加入/仅 Hello，版本/MOD API/MOD 集合不符各连续三轮并检查拒绝回复确实送达；同档案三轮重连及连接 ID 复用 |
| [bots-input.lua](bots-input.lua) | 连接绑定的真实乘员输入，重复/旧/零序号、NaN/越界、未就绪拒绝；两连接独立序号和真实意图拒绝回复；失联清零、断线清输入和运行实例且保留重连档案 |
| [bots-pickup.lua](bots-pickup.lua) | 正式地表拾取请求/提交/库存确认，租约独占及越权拒绝、部分三份余七、真实入包、重复幂等、确认前旧库存包保护、非法数量与零容量拒收、超时/断线释放和最终完整回收 |
| [bots-actions.lua](bots-actions.lua) | 两名真实服务器假人入世，Mirror 移动命令驱动权威坐标与角色位置；正式武器动画与伤害窗口；拾取授权、服务器真实背包收货、正式丢弃世界物与现场截图；清理全部自有网络对象 |

夹具在 `Assets/5_Scripts/5-4_Networking/Gameplay/Testing/GameplayTestNetworkBots*.cs` 与 `GameplayTestBotTransport.cs`，仅 Editor/Development Build 编译；正式 Lua 入口仍要求 Editor PlayMode 的 Library 隔离存档。每个操作用 finally 收尾，Lua 另断言服务器/连接/网络对象/自有档案/乘员无残留，本地主角和场景不变，原传输器恢复。正常断线保留玩家存档用于重连，整份测试结束才移除自有测试档案。

库存独立性读取正式 `Inventory_ModuleData` 模块状态及其存储字典，逐假人断言模块确实存在，且状态和字典引用不共享；不使用已不承载运行库存的旧玩家字典。正式拒绝回复按 Mirror 网络发送周期刷新后断开，避免异步批次尚未发送便丢失原因。原有三份协议 Lua 的历史断言统计只代表当时版本，新动作现场为第四份。

PlayMode 的 `bots-actions.lua` 及其余偏协议 Lua 都由宿主自动定时截图并在收尾补拍；协议正确性仍须看 Lua 断言。行动假人启用正式服务端战斗模块，拾取经过服务器真实库存事务，客户端应用确认由测试消息适配，不代表独立 Client 已经接收。该组仍不覆盖独立真实 NetworkClient 的本地背包/攻击输入、KCP/LAN、跨进程时延/丢包、设备差异、Host 本地连接、远端插值表现、真实角色受击和太空兴趣分片。当前现场快照为单片，超过 24 KiB 的大世界多片边界与缺片重传仍待补。
