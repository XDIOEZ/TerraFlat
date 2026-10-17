-- 观察正式入世已完成的本地玩家、资源闸门和可见区块，不模拟 Scene 流程。
test.runtime("core_lifecycle", "live_entry", {}, function(data)
    if data.editorSpriteTextures ~= nil then
        assert(#data.editorSpriteTextures.textures > 0, "正式目录没有可核对的编辑器 Sprite 纹理")
        for _, texture in ipairs(data.editorSpriteTextures.textures) do
            assert(texture.found and texture.localId == 2800000,
                "编辑器 Sprite 异步预热的主纹理身份不匹配：" .. texture.path)
        end
    end
    assert(data.scope == "live-isolated-world" and data.resourceState == "Ready" and data.startupReady, "正式资源没有完成加载")
    assert(data.inWorld and data.gameplayReady and not data.entryInProgress, "正式入世闸门尚未释放")
    assert(data.localProfile and data.playerInitialized and data.playerActive and data.profile ~= nil, "本地测试角色未完成生命周期")
    assert(data.footViewReady and data.windowReady, "输入已解锁但脚下或可见区块表现未就绪")
    assert(data.hotbarCount == 1 and data.handCount == 1
        and data.currentHotbarRegistered and data.currentHandRegistered,
        "旧世界的快捷栏/手持面板残留，或当前玩家 HUD 未正确注册")
end, "正式入世、玩家生命周期和表现就绪")
