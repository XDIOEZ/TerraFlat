-- 正式菜单的十二图引用、淡化及面板生命周期由同一控制器推进。
local slideshow_steps = {
    {delta=12}, {delta=.75}, {delta=.75},
    {delta=12.75}, {action="close"}, {delta=50}, {action="open"},
    {delta=11.75}, {delta=.25}, {action="disable"}, {delta=50}, {action="enable"},
}
for _ = 1, 12 do slideshow_steps[#slideshow_steps+1] = {delta=13.5} end
test.system("ui_settings", "main_menu_background_slideshow", {timeScale=0, steps=slideshow_steps}, function(data)
    assert(#data.paths == 12 and data.uniqueCount == 12 and data.validCount == 12, "背景必须是十二张不同的有效 Sprite")
    for i, path in ipairs(data.paths) do
        assert(not string.find(path, "test_background", 1, true), "测试图不能进入轮播")
        assert((string.find(path, "/PixelCarousel/", 1, true) ~= nil) == (i > 6), "轮播顺序必须是已有六张后接新增六张")
    end
    assert(data.holdDuration == 12 and data.fadeDuration == 1.5, "轮播时长不符合约定")
    assert(data.prefabBindings and data.inputTransparent and data.fullScreen and data.envelope, "背景引用断开、未铺满或拦截了菜单输入")
    local initial, samples = data.initial, data.samples
    assert(initial.running and initial.index >= 0 and initial.index < 12, "首次初始化后没有随机启动轮播")
    assert(samples[1].fading and samples[1].topAlpha == 0 and samples[1].nextMatchesIndex, "十二秒边界未开始下一张淡化")
    assert(math.abs(samples[2].topAlpha-.5) < .001 and samples[2].bottomAlpha == 1, "淡化中途出现露底")
    assert(samples[3].index == (initial.index+1)%12 and not samples[3].topEnabled, "淡化完成后未交换图层")
    assert(samples[4].fading and math.abs(samples[4].topAlpha-.5) < .001, "第二轮淡化不正确")
    for _, i in ipairs({5,6,10,11}) do
        assert(not samples[i].running and not samples[i].fading and not samples[i].topEnabled and samples[i].elapsed == 0,
            "面板隐藏或物体停用后仍推进背景")
    end
    assert(samples[7].index == samples[3].index and samples[7].elapsed == 0 and samples[7].running, "重开未保留当前图或重置计时")
    assert(not samples[8].fading and samples[9].fading, "重开后没有完整停留十二秒")
    assert(samples[12].running and samples[12].elapsed == 0, "物体重新启用未恢复轮播")
    for i = 13, 24 do
        assert(samples[i].index == (samples[12].index+i-12)%12, "十二图顺序或循环回首错误")
    end
    for _, sample in ipairs(samples) do
        assert(sample.bottomAlpha == 1 and sample.bottomMatchesIndex and sample.layerOrder, "底图身份、不透明度或层级错误")
    end
end, "主菜单十二图轮播与暂停、隐藏、重启")

test.system("ui_settings", "main_menu_background_slideshow", {initializeClosed=true,
    steps={{delta=50},{action="open"},{delta=12.75}}}, function(data)
    assert(not data.initial.running and not data.samples[1].running, "InitClosed 后仍推进隐藏背景")
    assert(data.samples[2].running and data.samples[2].elapsed == 0, "首次打开隐藏菜单未开始完整停留")
    assert(data.samples[3].fading and math.abs(data.samples[3].topAlpha-.5) < .001, "首次打开后的淡化不正确")
end, "主菜单关闭初始化与首次打开")
