-- 只检查正式改名服务的内存拒绝和不变名称，不执行磁盘写入。
test.system("save", "rename_feedback", {}, function(data)
    local expected = {
        noSave = "当前存档尚未载入，无法修改角色名称。",
        missingPlayer = "所选角色已不存在，请重新选择。",
        empty = "名称不能为空",
        tooLong = "名称不能超过48个字符。",
        duplicate = "当前存档中已有同名角色。"
    }
    for name, reason in pairs(expected) do
        assert(not data[name].success and data[name].reason == reason, "角色改名错误原因：" .. name)
    end
    assert(data.saveNoSave == "当前存档尚未载入，无法改名。", "未加载存档被误报为名称或磁盘问题")
    assert(data.staleSave == "当前选择的存档已变化，请重新选择。", "旧选择被误报为重名")
    assert(data.invalidSave == "存档名称包含文件名不允许的字符，或以句点结尾。", "非法文件名没有明确原因")
    assert(data.unchangedAccepted and data.saveUnchangedAccepted and data.saveName == "probe_save" and data.playerPreserved,
        "改名拒绝污染名称，或相同名称不再幂等成功")
    assert(data.unchangedReason == nil and data.saveUnchangedReason == nil, "成功改名仍残留拒绝原因")
end, "存档角色改名按真实校验返回原因且不污染原名称")

test.system("save", "resource_reload_feedback", {}, function(data)
    local cause = "probe:actual-publish-failure"
    assert(string.find(data.beforePublish, cause, 1, true) and string.find(data.afterPublish, cause, 1, true),
        "资源更新提示丢失实际失败原因")
    assert(data.beforePublish ~= data.afterPublish and not string.find(data.success, cause, 1, true),
        "资源提交前后错误使用同一结果描述，或成功提示仍包含旧错误")
    assert(type(data.missingReason) == "string" and #data.missingReason > 0, "缺少原因时没有明确回退")
    assert(data.dimensionReason == cause, "维度准备失败被覆盖为连接故障")
    assert(data.nestedReloadReason == cause, "发布后的跨帧玩家重建异常丢失失败原因")
end, "资源更新提交阶段和维度加载真实失败原因")

-- 只在内存中捕获和恢复当前格式，真实玩家存档不读写。
test.system("save", "agriculture_cell_index", {}, function(data)
    assert(data.initiallyMissing and data.identityPreserved and data.baselineSum == data.indexedSum,
        "密集耕地快速索引丢失原有水肥、作物身份或重复创建格子")
    assert(data.indexedBytes == 0 and data.persistedBytesUnchanged and data.roundTrip,
        "已预热的耕地索引仍分配内存或污染 MemoryPack 存档内容")
    assert(data.appended and data.replaced and data.removed and data.listReplaced,
        "外部追加、同数量替换、移除或更换列表后读取了旧农业索引")
    assert(data.originalFirst and data.duplicateNext,
        "重复坐标没有保留 List.Find 首项优先及移除后的后继规则")
end, "密集农业索引、稳定零分配、修改失效与存档一致性")

test.system("save", "snapshot_memory", { amount = 3, stock = 7, maximumDurability = 100, durability = 25, targetMaximumDurability = 200, guid = 143 }, function(data)
    assert(data.bytes > 0 and data.definitionId == "probe:item" and data.guid == 143, "快照丢失身份")
    assert(data.amount == 3 and data.sourceAmount == 3, "恢复数量错误或污染源物品")
    assert(data.maximumDurability == 200 and data.durability == 50, "新定义耐久没有保持原损伤比例")
    assert(data.sourceStock == 7 and data.targetStock == 8, "恢复的模块载荷没有隔离")
    assert(data.specialData == '{"probe":true}', "特殊实例状态丢失")
end, "正式实例快照内存往返与当前定义恢复")

test.system("save", "special_namespace", { counter = 3 }, function(data)
    assert(data.root.other.keep == 42, "写命名空间覆盖了其他系统数据")
    assert(data.copiedCounter == 3 and data.storedCounter == 3, "命名空间读写暴露共享可变引用")
end, "特殊状态命名空间与副本隔离")

test.system("save", "special_namespace", { raw = "非JSON原内容", counter = 5 }, function(data)
    assert(data.root["flatworld.legacyItemSpecialData"] == "非JSON原内容", "未解析根数据被静默删除")
    assert(data.storedCounter == 5, "转换根数据后命名空间写入失败")
end, "未解析特殊状态保留与命名空间写入")

test.system("save", "space_item_chain", { amount = 3, guid = 143 }, function(data)
    assert(data.version == 2 and data.clock == 12.5 and data.sectorX == "-3" and data.sectorY == "4", "JSON 会话的版本、时钟或跨星区身份丢失")
    assert(data.encodedPreserved and data.childAmount == 3 and data.sourceAmount == 3, "JSON/二进制/克隆链丢失库存或污染源对象")
    assert(data.childGuid == data.sourceGuid and data.independentChild, "嵌套物品身份或独立引用错误")
    assert(data.selected == 1 and data.slotCount == 2 and data.secondEmpty, "嵌套库存选择、容量或空槽丢失")
    assert(data.scaleX == -2 and data.scaleY == 0 and data.scaleZ == 3.5 and data.scaleFields == 3, "Vector3 缩放序列化递归或分量丢失")
    assert(data.missingVersion == "InvalidDataException" and data.wrongVersion == "SaveVersionIncompatibleException", "正式解析接受无版本或不同版本档案")
    assert(data.invalidClock == "InvalidDataException", "正式解析接受负时钟")
end, "正式 JSON、Base64、MemoryPack 与独立库存克隆链")

test.system("save", "autosave_budget", { startTime = 0, steps = {
    { now = 0 }, { now = 0.000499 }, { now = 0.0005 },
    { restart = true, now = 15 }, { now = 15 }, { now = 15.00025 }, { now = 15.0006 }
} }, function(data)
    assert(data.sliceSeconds == 0.0005 and data.maxWorkUnits == 16 and data.yieldFrames == 2, "自动保存切片或让帧参数不符合当前约定")
    assert(not data.decisions[1] and not data.decisions[2] and data.decisions[3], "自动保存没有按半毫秒边界交还帧循环")
    assert(not data.decisions[4] and not data.decisions[5] and data.decisions[6], "重启后把长帧等待时间算入新切片预算")
end, "自动保存半毫秒切片与长帧后重新计时")

local budget_steps = {}
for i = 1, 16 do budget_steps[#budget_steps + 1] = { now = 0 } end
budget_steps[#budget_steps + 1] = { restart = true, now = 1000 }
for i = 1, 16 do budget_steps[#budget_steps + 1] = { now = 1000 } end
test.system("save", "autosave_budget", { startTime = 0, steps = budget_steps }, function(data)
    assert(#data.decisions == 32, "自动保存预算丢失工作单元采样")
    for i = 1, 15 do
        assert(not data.decisions[i] and not data.decisions[i + 16], "未达到16个工作单元就因旧计数交还帧循环")
    end
    assert(data.decisions[16] and data.decisions[32], "时间未走动时16工作单元上限失效，或重启没有清空计数")
end, "自动保存工作单元上限与重启计数隔离")

test.system("save", "autosave_chunk_payload", { amount = 3, guid = 143 }, function(data)
    assert(data.bytes > 0 and data.outerMagic == "FWD6" and data.outerVersion == 15, "自动保存外层封装格式错误")
    assert(data.compactVersion == 34, "自动保存独立区块字节格式错误")
    assert(data.coreName == "probe:autosave_core" and data.coreSeed == 2468, "核心数据冻结后被源对象变化污染")
    assert(data.recordCount == 2 and data.firstChunk == "1_0" and data.secondChunk == "0_0", "封装没有保留捕获顺序或区块身份")
    assert(data.planet == "probe:autosave" and data.chunkX == 1 and data.itemCount == 1, "区块身份、位置或物品列表丢失")
    assert(data.guid == 143 and data.definitionId == "probe:item" and data.amount == 3, "区块字节丢失正式物品身份或数量")
    assert(data.sourceGuid == 1143 and data.sourceAmount == 10, "采样未实际修改冻结后的源物品")
    assert(data.childGuid == 153 and data.childAmount == 3 and data.sourceChildAmount == 14, "内嵌库存没有冻结真实子物品状态")
    assert(data.slotCount == 2 and data.selected == 1 and data.secondEmpty, "区块字节往返丢失库存容量、选中槽或空槽")
    assert(data.removedCount == 1 and data.removedGuid == 41, "冻结后的移除列表被源列表变化污染")
    assert(data.terrainCount == 1 and data.localX == 2 and data.localY == 3, "地形差量列表或局部坐标丢失")
    assert(data.ground == 101 and data.back == 102 and data.blocking == 103 and data.biome == 104, "地形差量字段丢失或被后续修改污染")
    assert(data.navigationCost == 105 and data.flags == 2 and data.damage == 12.5, "导航、阻挡标记或建筑损伤未原样保存")
    assert(data.firstBytesPreserved and data.machinesPreserved and data.dropsPreserved and data.modsPreserved, "纯字节封装改写了已捕获的独立载荷")
end, "正式区块冻结字节、嵌套实例与外层存档封装往返")

test.system("save", "autosave_ecology_payload", { amount = 3, guid = 143 }, function(data)
    assert(data.workUnits == 42, "密集生态区块没有按元数据和逐物品提供让帧边界")
    assert(data.cacheCount == 41 and data.coreExcludesChunks and data.sourceChunksRestored, "核心仍重复捕获生态物品、缓存未复用或临时剔除污染源档案")
    assert(data.initialCount == 0 and data.initialGuid == 1000 and data.initialAmount == 3, "生态元数据混入物品或首次冻结身份错误")
    assert(data.worlds == 3 and data.surfaceRemoved == 80 and data.caveChunks == 2, "地表和洞穴混淆或跨帧新增区块丢失")
    assert(data.emptyRoundTrip and data.nestedAmount == 5, "空生态世界不能继续写差量或生态物品的嵌套库存被源对象污染")
    assert(data.itemCount == 39 and data.firstGuid == 1001 and data.firstAmount == 9, "最终联合点没有应用采收删除/替换，或冻结后被源对象污染")
    assert(data.harvestRemoved and data.renewalYear == 8 and data.lateChunkRemoved == 91, "跨帧采收墓碑、恢复年份或新增区块没有对齐最终点")
    assert(data.chunkX == 16 and data.chunkY == -32, "洞穴生态区块坐标丢失")
    assert(data.oldFormat == "SaveVersionIncompatibleException", "生态独立载荷格式接受了旧封装头")
end, "生态逐物品冻结、跨帧采收对齐、维度隔离与正式恢复往返")

test.system("save", "load_failure_state", {}, function(data)
    assert(data.currentVersion == 34 and data.currentName == "probe:current" and data.currentSeed == 2468, "当前核心格式不能正常恢复")
    assert(data.oldFailure == "SaveVersionIncompatibleException" and data.oldPreserved, "旧核心结构没有先被版本拒绝，或拒绝时清空了既有差量")
    assert(data.corruptFailure == "InvalidDataException" and data.corruptPreserved, "区块解析失败留下半份新差量或丢掉旧差量")
    assert(not data.missingLoaded and data.saveCleared and data.playerCleared, "载入失败仍允许使用上一份存档或角色")
    assert(data.missingPreserved, "载入不存在的文件清空了既有差量")
end, "当前格式、旧结构拒绝、载入失败角色失效与区块差量回滚")
