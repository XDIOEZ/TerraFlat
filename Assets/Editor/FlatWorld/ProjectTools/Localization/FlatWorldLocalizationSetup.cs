#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Platform.Android;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using TMPro;

namespace FlatWorld.Localization.Editor
{
    /// <summary>
    /// 创建/同步 FlatWorld 的简体中文与英语 Locale 和物品 String Table。
    /// 中文沿用当前 JSON 文本，英语由本工具生成；菜单可重复执行且不会覆盖人工英文翻译。
    /// </summary>
    public static class FlatWorldLocalizationSetup
    {
        #region 路径与配置

        private const string RootFolder = "Assets/Localization";
        private const string LocaleFolder = RootFolder + "/Locales";
        private const string TableFolder = RootFolder + "/StringTables";
        private const string SettingsPath = RootFolder + "/LocalizationSettings.asset";
        private const string ChineseLocalePath = LocaleFolder + "/zh-CN.asset";
        private const string EnglishLocalePath = LocaleFolder + "/en.asset";

        private static readonly Dictionary<string, string> EnglishNameOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Apple", "Apple" },
                { "Axe_Bronze", "Bronze Axe" },
                { "Axe_Copper", "Copper Axe" },
                { "Axe_Flint", "Flint Axe" },
                { "Axe_Iron", "Iron Axe" },
                { "Axe_RawIron", "Raw Iron Axe" },
                { "Axe_Stone", "Stone Axe" },
                { "Berry", "Wild Berry" },
                { "Bone", "Bone" },
                { "CharredMatter", "Charred Matter" },
                { "Coconut_Addle", "Cooked Coconut" },
                { "Coconut_Green", "Green Coconut" },
                { "Coconut_Half", "Half Coconut" },
                { "Coconut_Nude", "Husked Coconut" },
                { "Coconut_Shell", "Coconut Shell" },
                { "Coconut_Water", "Coconut Water" },
                { "Coconut_WaterSalt", "Salted Coconut Water" },
                { "CoconutMeat", "Coconut Meat" },
                { "Dagger_Bone", "Bone Dagger" },
                { "Dagger_Copper", "Copper Dagger" },
                { "Dagger_Stone", "Stone Dagger" },
                { "Earth", "Earth" },
                { "Egg", "Egg" },
                { "Egg_Cooked", "Cooked Egg" },
                { "Fat", "Fat" },
                { "Ingot_Bronze", "Bronze Ingot" },
                { "Ingot_Copper", "Copper Ingot" },
                { "Ingot_RawIron", "Raw Iron Ingot" },
                { "Ingot_Steel", "Steel Ingot" },
                { "Ingot_Tin", "Tin Ingot" },
                { "Ingot_WroughtIron", "Wrought Iron Ingot" },
                { "Knife_Flint", "Flint Knife" },
                { "Leaf", "Leaf" },
                { "Leather", "Leather" },
                { "Log", "Log" },
                { "Meat", "Raw Meat" },
                { "Meat_Cooked", "Cooked Meat" },
                { "Meat_Dehydrate", "Dehydrated Meat" },
                { "Meat_Rotten", "Rotten Meat" },
                { "Ore_Coal", "Coal Ore" },
                { "Ore_Copper", "Copper Ore" },
                { "Ore_Flint", "Flint Ore" },
                { "Ore_Iron", "Iron Ore" },
                { "Ore_MagicalStone", "Magic Stone Ore" },
                { "Ore_Tin", "Tin Ore" },
                { "Pickaxe_Bronze", "Bronze Pickaxe" },
                { "Pickaxe_Copper", "Copper Pickaxe" },
                { "Pickaxe_Iron", "Iron Pickaxe" },
                { "Pickaxe_RawIron", "Raw Iron Pickaxe" },
                { "Pickaxe_Stone", "Stone Pickaxe" },
                { "Plank", "Plank" },
                { "RawHide", "Raw Hide" },
                { "Rope", "Rope" },
                { "Seed_Apple", "Apple Seed" },
                { "Spear_Copper", "Copper Spear" },
                { "Spear_Iron", "Iron Spear" },
                { "Spear_Stone", "Stone Spear" },
                { "Spear_Stone_Animation", "Stone Spear" },
                { "Stick_Wood", "Wooden Stick" },
                { "Tea", "Tea" },
                { "Twine", "Twine" }
            };

        private static readonly Dictionary<string, string> EnglishDescriptionOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Apple", "A fresh apple. An apple a day keeps the doctor away." },
                { "Berry", "A small wild berry that restores a little hunger when eaten." },
                { "Coconut_Addle", "A cooked coconut dish." },
                { "Coconut_Green", "A young green coconut." },
                { "Coconut_Half", "Half of a coconut." },
                { "Coconut_Nude", "A coconut with its husk removed." },
                { "Coconut_Shell", "An empty coconut shell." },
                { "Coconut_Water", "Refreshing water from a coconut." },
                { "Coconut_WaterSalt", "Salted coconut water." },
                { "CoconutMeat", "Edible coconut flesh." },
                { "Dagger_Bone", "A small blade made from bone." },
                { "Dagger_Copper", "A small blade made from copper." },
                { "Dagger_Stone", "A small blade made from stone." },
                { "Egg", "An egg from an animal." },
                { "Egg_Cooked", "A cooked egg." },
                { "Knife_Flint", "A small blade made from flint." },
                { "Meat", "Eating raw meat has a 50% chance to cause infection for 120 seconds." },
                { "Meat_Cooked", "Cooked meat." },
                { "Meat_Dehydrate", "Dehydrated meat." },
                { "Meat_Rotten", "Rotten meat. It should not be eaten." },
                { "Seed_Apple", "A seed that can grow into an apple tree." },
                { "Tea", "A warm drink made from tea leaves." }
            };

        /// <summary>本体任务内容按稳定 key 进入 FlatWorld 表，避免把多语言字段堆回任务业务 JSON。</summary>
        private static readonly Dictionary<string, string> EnglishQuestOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "quest.flatworld.first_chipped_tool.title", "First Stone Tool" },
                {
                    "quest.flatworld.first_chipped_tool.description",
                    "Craft a chipped stone tool and learn the basic crafting flow."
                },
                {
                    "quest.flatworld.first_chipped_tool.objective.craft_chipped_tool",
                    "Craft one chipped stone tool"
                },
                { "quest.flatworld.debug_open_inventory.title", "Test: Open Inventory" },
                {
                    "quest.flatworld.debug_open_inventory.description",
                    "Open the inventory once, then claim the quest from the GM quest page."
                },
                {
                    "quest.flatworld.debug_open_inventory.objective.open_once",
                    "Open the inventory once"
                },
                { "quest.flatworld.debug_pickup_items.title", "Test: Pick Up Items" },
                {
                    "quest.flatworld.debug_pickup_items.description",
                    "Pick up any three items. The quest turns in automatically."
                },
                {
                    "quest.flatworld.debug_pickup_items.objective.pickup_three",
                    "Pick up any 3 items"
                },
                { "quest.flatworld.debug_own_sticks.title", "Test: Own Sticks" },
                {
                    "quest.flatworld.debug_own_sticks.description",
                    "Own five wooden sticks in the bag or hotbar to test a state-based objective."
                },
                {
                    "quest.flatworld.debug_own_sticks.objective.own_five_sticks",
                    "Own 5 wooden sticks"
                },
                { "quest.flatworld.debug_craft_and_build.title", "Test: Craft and Build" },
                {
                    "quest.flatworld.debug_craft_and_build.description",
                    "Craft a chipped stone tool, then place any building to test stage progression."
                },
                {
                    "quest.flatworld.debug_craft_and_build.objective.craft_chipped_tool",
                    "Craft one chipped stone tool"
                },
                {
                    "quest.flatworld.debug_craft_and_build.objective.place_any_building",
                    "Place any building once"
                }
            };

        private static readonly Dictionary<string, string> EnglishUiOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // 设置页的镜头预判术语保持稳定，供正式 Prefab 扫描并同步英文表。
                { "收起", "Collapse" },
                { "展开", "Expand" },
                { "设置", "Settings" },
                { "退出游戏", "Exit Game" },
                { "确定要退出游戏吗？", "Are you sure you want to exit the game?" },
                { "继续旅程", "Continue Journey" },
                { "开始旅程", "Begin Journey" },
                { "新建世界", "New World" },
                { "创建新世界", "Create New World" },
                { "生成新世界", "Generate World" },
                { "选择存档", "Select Save" },
                { "载入存档", "Load Save" },
                { "保存时间：--", "Save time: --" },
                { "保存时间：{0}", "Save time: {0}" },
                { "批量删除存档", "Delete Multiple Saves" },
                { "确认删除所选", "Delete Selected" },
                { "取消批量选择", "Cancel Selection" },
                { "确认批量删除存档？", "Delete Selected Saves?" },
                { "取消并返回", "Cancel and Return" },
                { "永久删除所选存档", "Permanently Delete Selected" },
                { "将永久删除已选中的存档。\n此操作无法撤销。", "The selected saves will be permanently deleted.\nThis action cannot be undone." },
                { "批量选择：已选 {0} 个存档", "Batch selection: {0} save(s) selected" },
                { "将永久删除已选中的 {0} 个存档。\n{1}\n此操作无法撤销。", "The selected {0} save(s) will be permanently deleted.\n{1}\nThis action cannot be undone." },
                { "返回主界面", "Return to Main Menu" },
                { "选择一种方式进入世界", "Choose a way to enter the world." },
                { "身份与存档", "Identity & Save" },
                { "玩家名称", "Player Name" },
                { "存档名称", "Save Name" },
                { "可选，例如：旅人", "Optional, e.g. Traveler" },
                { "可选，例如：篝火以北", "Optional, e.g. North of the Campfire" },
                { "两个名称都可留空，将使用默认名称。", "Both names may be left blank; default names will be used." },
                { "星球半径", "Planet Radius" },
                { "世界坐标缩放", "World Coordinate Scale" },
                { "留空自动生成随机种子", "Leave blank to generate a random seed" },
                { "世界参数", "World Parameters" },
                { "世界", "World" },
                { "生存", "Survival" },
                { "战斗", "Combat" },
                { "生产", "Production" },
                { "难度设置", "Difficulty Settings" },
                { "官方预设", "Official Preset" },
                { "自定义", "Custom" },
                { "自定义规则", "Custom Rules" },
                { "简单", "Easy" },
                { "困难", "Hard" },
                { "确认选择", "Confirm Selection" },
                { "玩家伤害", "Player Damage" },
                { "生物伤害", "Creature Damage" },
                { "生物生命", "Creature Health" },
                { "环境伤害", "Environmental Damage" },
                { "饥饿消耗", "Hunger Drain" },
                { "耐力消耗", "Stamina Consumption" },
                { "耐力恢复", "Stamina Recovery" },
                { "治疗效果", "Healing Effect" },
                { "时间流逝", "Time Speed" },
                { "生成频率", "Spawn Frequency" },
                { "种群上限", "Population Cap" },
                { "战利品", "Loot" },
                { "作物生长", "Crop Growth" },
                { "熔炼速度", "Smelting Speed" },
                { "燃料消耗", "Fuel Consumption" },
                { "制作产量", "Crafting Output" },
                { "死亡掉落全部随身物品", "Drop All Items on Death" },
                { "窗口大小", "Window Size" },
                { "显示模式", "Display Mode" },
                { "显示", "Display" },
                { "画质", "Graphics" },
                { "画质预设", "Graphics Preset" },
                { "特效质量", "Effects Quality" },
                { "游戏语言", "Language" },
                { "语言", "Language" },
                { "简体中文", "Simplified Chinese" },
                { "返回", "Back" },
                { "关闭", "Close" },
                { "恢复默认", "Restore Defaults" },
                { "恢复所有设置", "Restore All Settings" },
                { "所有设置已恢复默认", "All settings restored to defaults" },
                { "选项", "Options" },
                { "UI设置", "UI Settings" },
                { "UI 设置", "UI Settings" },
                { "界面", "Interface" },
                { "界面设置", "Interface Settings" },
                { "保存与退出", "Save & Exit" },
                { "界面缩放", "UI Scale" },
                { "左侧触控区", "Left Touch Zone" },
                { "右侧触控区", "Right Touch Zone" },
                { "左侧触控区比例", "Left Touch Zone Ratio" },
                { "右侧触控区比例", "Right Touch Zone Ratio" },
                { "镜头控制", "Camera Controls" },
                { "镜头前探", "Camera Lookahead" },
                { "预判平滑", "Lookahead Smoothing" },
                { "缩放影响系数", "Zoom Influence" },
                { "浮动移动摇杆（关闭则固定）", "Floating Move Joystick (Off: Fixed)" },
                { "双指缩放（关闭则禁用）", "Pinch Zoom (Off: Disabled)" },
                { "安全区域适配：开启（推荐）", "Safe Area: On (Recommended)" },
                { "主音量", "Master Volume" },
                { "音乐音量", "Music Volume" },
                { "音效音量", "SFX Volume" },
                { "语音音量", "Voice Volume" },
                { "环境音量", "Ambience Volume" },
                { "保存模式", "Save Mode" },
                { "间隔（分钟）", "Interval (minutes)" },
                { "取消", "Cancel" },
                { "应用", "Apply" },
                { "完成", "Done" },
                { "区块流送性能", "Chunk Streaming Performance" },
                { "性能模式", "Performance Mode" },
                { "当前：自动平衡。", "Current: Automatic Balance." },
                { "行囊", "Inventory" },
                { "整理", "Organize" },
                { "整理携带物资 · 拖拽交换位置", "Organize carried supplies · drag to swap positions" },
                { "物品详情", "Item Details" },
                { "物品操作", "Item Actions" },
                { "查看物品信息", "View Item Info" },
                { "使用物品", "Use Item" },
                { "手机菜单", "Mobile Menu" },
                { "使用", "Use" },
                { "菜单", "Menu" },
                { "状态", "Status" },
                { "丢弃一个", "Drop One" },
                { "丢弃整组", "Drop Stack" },
                { "再次确认", "Confirm Again" },
                { "镜头缩放", "Camera Zoom" },
                { "删除", "Delete" },
                { "重命名", "Rename" },
                { "篝火", "Campfire" },
                { "开始处理", "Start Processing" },
                { "作业状态", "Work Status" },
                { "投入", "Input" },
                { "产出", "Output" },
                { "投入材料", "Input Materials" },
                { "控制燃料与温度 · 等待冶炼完成", "Control fuel and temperature · wait for smelting to finish" },
                { "熔炉", "Furnace" },
                { "启动熔炼", "Start Smelting" },
                { "输入:", "Input:" },
                { "输出:", "Output:" },
                { "晾肉架", "Drying Rack" },
                { "堆肥箱", "Compost Bin" },
                { "联机模式", "Multiplayer" },
                { "创建主机", "Create Host" },
                { "加入好友", "Join Friend" },
                { "离线", "Offline" },
                { "断开连接", "Disconnect" },
                { "连接设置", "Connection Settings" },
                { "主机 / 默认端口", "Host / Default Port" },
                { "主机 / UDP 穿透地址", "Host / UDP Traversal Address" },
                { "当前连接", "Current Connection" },
                { "会话状态", "Session Status" },
                { "世界同步", "World Sync" },
                { "世界存档", "World Save" },
                { "玩家：0 / 2", "Players: 0 / 2" },
                { "与好友共同生存", "Survive together with friends." },
                { "角色会在这里说话", "Characters will speak here." },
                { "输入消息，按 Enter 发送（/ 开头可用于命令）", "Enter a message and press Enter to send (/ at the beginning can be used for commands)." },
                { "创建你的世界，或粘贴好友提供的地址", "Create your world or paste the address provided by a friend." },
                { "纯地形数据在后台生成；Tilemap、碰撞和导航始终在主线程逐帧绘制。", "Terrain data is generated in the background; tilemaps, collisions, and navigation are drawn frame by frame on the main thread." },
                { "选择官方预设，或创建自己的规则", "Choose an official preset or create your own rules." },
                { "难度属于当前世界存档；进入游戏后可在设置面板中切换官方预设。", "Difficulty belongs to the current world save. After entering the game, you can switch official presets in the settings panel." },
                { "当前为界面预览；设置功能将在后续版本接入。", "This is a UI preview. Settings will be connected in a later version." },
                { "调整显示大小、画质与语言。选项为界面预览，功能将在后续接入。", "Adjust display size, graphics, and language. These options are a UI preview and will be connected later." },
                { "高（推荐）", "High (Recommended)" },
                { "中", "Medium" },
                { "低", "Low" },
                { "自动", "Automatic" },
                { "开启", "On" },
                { "关闭（推荐）", "Off (Recommended)" },
                { "功能列表", "Feature List" },
                { "返回游戏", "Return to Game" },
                { "暂停", "Pause" },
                { "帮助", "Help" },
                { "确认", "Confirm" },
                { "确定", "OK" },
                { "是", "Yes" },
                { "否", "No" },
                { "设置状态", "Settings Status" },
                { "当前语言：简体中文", "Current language: Simplified Chinese" },
                { "当前语言：English", "Current language: English" },
                { "物资清单", "Resource List" },
                { "维持燃料 · 处理食物与基础材料", "Maintain fuel · process food and basic materials" },
                { "投入可腐物 · 等待自然转化", "Add compostables · wait for natural conversion" },
                { "JOURNEY INTERRUPTED / 生存记录", "JOURNEY INTERRUPTED / SURVIVAL LOG" },
                { "旅程暂告一段落", "Journey Paused" },
                { "带着这次留下的经验，再次回到这片世界。", "Return to this world with what you learned." },
                { "重新醒来", "Wake Up Again" },
                { "结束本次旅程", "End This Journey" },
                { "装备", "Equipment" },
                { "将装备拖入槽位以更新生存配置", "Drag equipment into slots to update your survival setup" },
                { "钻木取火", "Fire Drill" },
                { "按住操作键推进过程 · 松开即可暂停", "Hold the action key to continue · release to pause" },
                { "预期产物", "Expected Output" },
                { "执行", "Execute" },
                { "燧石取火", "Start Fire with Flint" },
                { "MODULES  /  界面模块", "MODULES  /  INTERFACE MODULES" },
                { "UI菜单下拉列表", "UI Menu Dropdown" },
                { "手工制作", "Handcrafting" },
                { "放入材料 · 核对产物 · 开始制作", "Add materials · check the output · start crafting" },
                { "制作台", "Workbench" },
                { "组合多种材料 · 产物完成后移入背包", "Combine materials · move the finished product to your inventory" },
                { "材料矩阵", "Material Matrix" },
                { "制作结果", "Crafting Result" },
                { "保持通风 · 留意加工进度", "Keep it ventilated · watch the processing progress" },
                { "选择一项操作继续", "Choose an action to continue" },
                { "可用操作", "Available Actions" },
                { "保存并回到主界面按钮", "Save and Return to Main Menu Button" },
                { "保存游戏", "Save Game" },
                { "保存并退出游戏按钮", "Save and Exit Game Button" },
                { "不保存直接退出", "Exit Without Saving" },
                { "音量调节", "Volume Controls" },
                { "自动保存", "Auto Save" },
                { "游戏难度", "Game Difficulty" },
                { "按键绑定", "Key Bindings" },
                { "控制方式", "Control Method" },
                { "电脑键鼠控制", "Keyboard & Mouse" },
                { "手柄控制", "Gamepad" },
                { "手机触屏控制", "Mobile Touch" },
                { "先选择玩法控制方式；键鼠与手柄按键可在下方分别修改。", "Choose a gameplay control method first; keyboard, mouse, and gamepad bindings can be changed below." },
                { "控制方式已切换为：{0}。", "Control method changed to: {0}." },
                { "流送性能", "Streaming Performance" },
                { "测试", "Test" },
                { "从上一次篝火继续旅程，并选择本次操控的角色。", "Continue from the previous campfire and choose the character you will control." },
                { "选择一个世界；右键条目可管理存档", "Select a world; right-click an entry to manage its save" },
                { "尚未选择存档", "No Save Selected" },
                { "删除存档", "Delete Save" },
                { "可用角色", "Available Characters" },
                { "载入存档后选择角色", "Choose a character after loading a save" },
                { "本次角色", "Current Character" },
                { "选择角色或输入新名称", "Choose a character or enter a new name" },
                { "角色名称决定进入世界后操控的身份。\n首次进入也可以直接创建新角色。", "The character name determines who you control after entering the world.\nYou can also create a new character on your first entry." },
                { "选择世界  >  载入存档  >  选择角色", "Select World  >  Load Save  >  Choose Character" },
                { "进入世界", "Enter World" },
                { "FLAT WORLD  /  生存沙盒", "FLAT WORLD  /  SURVIVAL SANDBOX" },
                { "平坦世界", "Flat World" },
                { "从一簇篝火开始，活过这个辽阔世界。", "Start at a campfire and survive this vast world." },
                { "载入已有世界", "Load Existing World" },
                { "自定义你的开局", "Customize Your Start" },
                { "DEVELOPMENT BUILD  /  世界仍在生长", "DEVELOPMENT BUILD  /  THE WORLD IS STILL GROWING" },
                { "探索  ·  建造  ·  生存", "EXPLORE  ·  BUILD  ·  SURVIVE" },
                { "MULTIPLAYER  /  网络会话", "MULTIPLAYER  /  NETWORK SESSION" },
                { "创建你的世界，或粘贴好友提供的", "Create your world, or paste the address provided by a friend" },
                { "你在联机世界中的显示名称", "Your display name in the multiplayer world" },
                { "例如 tunnel.example.com:24567", "For example: tunnel.example.com:24567" },
                { "可直接粘贴 域名:端口；穿透协议必须为", "You can paste domain:port directly; the traversal protocol must be" },
                { "移动  WASD / 方向键\n关闭  使用右上角按钮", "Move  WASD / Arrow Keys\nClose  Use the top-right button" },
                { "名称可选；决定这个世界最初的轮廓。", "Name is optional; it determines the initial shape of this world." },
                { "可留空，自动生成带前缀的名称", "Leave blank to generate a prefixed name automatically" },
                { "两个名称都可留空；系统会自动填写 Player_ 和 World_ 前缀的随机名称。", "Both names may be left blank; the system will fill in matching Player_ and World_ names." },
                { "越大，探索范围越广", "Larger values provide a wider exploration range" },
                { "越小舒展，越大密集", "Smaller values are more spacious; larger values are denser" },
                { "有限循环世界", "Finite Loop World" },
                { "越过上下左右边界后从对侧返回；关闭则使用原有无限世界。", "Cross a boundary to return from the opposite side; turn it off to use the original infinite world." },
                { "留空则随机 · 支持数字或文字", "Leave blank for random · numbers or text are supported" },
                { "难度设置  ·  简单", "DIFFICULTY SETTINGS  ·  EASY" },
                { "确认身份  >  调整规则  >  生成世界", "CONFIRM IDENTITY  >  ADJUST RULES  >  GENERATE WORLD" },
                { "DIFFICULTY  /  沙盒规则", "DIFFICULTY  /  SANDBOX RULES" },
                { "预设会持续扩充，并保持规则组合清晰。", "Presets will continue to expand while keeping rule combinations clear." },
                { "死亡保留全部物品 · 保持当前游戏配置。玩家死亡后不会掉落随身物品。", "Keep all items on death · preserve the current game setup. The player drops no carried items on death." },
                { "死亡掉落全部物品 · 敌对生物更危险、生存消耗更快、恢复更慢，且死亡会掉落全部随身物品。", "Drop all items on death · hostile creatures are more dangerous, survival drains are faster, recovery is slower, and all carried items are dropped on death." },
                { "17 项规则均已接入实际游戏系统。", "All 17 rules are connected to the actual game systems." },
                { "玩家及手持武器造成的伤害", "Damage dealt by the player and held weapons" },
                { "非玩家攻击者造成的伤害", "Damage dealt by non-player attackers" },
                { "生物与可破坏实体的等效耐久", "Equivalent durability of creatures and destructible entities" },
                { "饥饿、温度、流血与真实伤害", "Hunger, temperature, bleeding, and true damage" },
                { "营养与水分自然消耗速度", "Natural drain rate of nutrition and hydration" },
                { "移动、奔跑与攻击耐力消耗", "Stamina cost of moving, running, and attacking" },
                { "营养充足时的耐力恢复", "Stamina recovery when well nourished" },
                { "食物、睡眠和其他治疗效果", "Food, sleep, and other healing effects" },
                { "昼夜与游戏日推进速度", "Day/night and in-game day progression speed" },
                { "每日生成窗口与每次生成数量", "Daily spawn window and amount spawned each time" },
                { "生态预算与生物存活上限", "Ecosystem budget and creature population cap" },
                { "生物、资源与植物产出数量", "Output quantity of creatures, resources, and plants" },
                { "种子、作物和浆果成熟速度", "Maturity speed of seeds, crops, and berries" },
                { "熔炉生产进度速度", "Furnace production progress speed" },
                { "所有燃料模块的消耗速度", "Consumption speed of all fuel modules" },
                { "手工、工作台与熔炉产量", "Handcrafting, workbench, and furnace output" },
                { "保持当前游戏配置。玩家死亡后不会掉落随身物品。", "Keep the current game setup. The player drops no carried items on death." },
                { "当前已接入规则", "Rules Currently Connected" },
                { "战斗：玩家 100% / 生物伤害", "Combat: Player 100% / Creature Damage" },
                { "官方预设与自定义面板共享同一套存档规则，后续扩充时不需要玩家重新创建世界。", "Official presets and the custom panel share the same save rules, so players will not need to recreate worlds as the system expands." },
                { "完成设置", "Setup Complete" },
                { "Debug信息面板", "Debug Information Panel" },
                { "角色参数", "Character Stats" },
                { "碳水", "Carbohydrates" },
                { "脂肪", "Fat" },
                { "蛋白质", "Protein" },
                { "水", "Water" },
                { "维生素", "Vitamins" },
                { "体温", "Body Temperature" },
                { "RESTING / 世界在篝火外继续流动", "RESTING / THE WORLD CONTINUES OUTSIDE THE CAMPFIRE" },
                { "主音量控制全部声音；其他通道可以单独调整。设置会自动保存。", "Master volume controls all sounds; other channels can be adjusted separately. Settings are saved automatically." },
                { "调整会立即应用并自动保存。", "Changes apply immediately and are saved automatically." },
                { "调整会立即应用并自动保存；界面缩放会统一放大或缩小所有正式 UI。", "Changes apply immediately and are saved automatically. UI scale resizes all production UI consistently." },
                { "左右触控区决定移动和普通指向摇杆的响应范围；中间区域保留给后续操作。调整会立即保存。", "The left and right touch zones control the move and aim joysticks. The center stays free for future actions. Changes are saved immediately." },
                { "触控区域比例：左 33%｜中 34%｜右 33%", "Touch Zones: Left 33% | Center 34% | Right 33%" },
                { "触控区域比例：左 {0}｜中 {1}｜右 {2}", "Touch Zones: Left {0} | Center {1} | Right {2}" },
                { "双指缩放默认关闭。镜头前探正值为提前跟随，负值为惯性；缩放影响系数为正时拉远会增强预测，为负时会减弱。", "Pinch zoom is off by default. Positive lookahead moves the camera ahead; negative values add inertia. Positive zoom influence strengthens prediction when zoomed out, while negative values weaken it." },
                { "调整会立即应用并自动保存。镜头前探正值为提前跟随，负值为惯性；负值绝对值越大，惯性越强。预判平滑越大越稳，但响应越慢。", "Changes apply immediately and are saved automatically. Positive lookahead moves the camera ahead; negative values add inertia, and more negative means stronger inertia. Higher smoothing is steadier but slower to respond." },
                { "自动保存只在游戏世界中按现实时间运行，设置会立即保存。", "Auto-save runs in the game world using real time; settings are saved immediately." },
                { "当前设置：每 10 分钟自动保存。", "Current setting: auto-save every 10 minutes." },
                { "难度属于当前存档并立即生效。选择预设后点击应用。", "Difficulty belongs to the current save and takes effect immediately. Choose a preset and click Apply." },
                { "敌对生物更危险、生存消耗更快、恢复更慢，且死亡会掉落全部随身物品。", "Hostile creatures are more dangerous, survival drains are faster, recovery is slower, and all carried items are dropped on death." },
                { "当前存档难度：简单", "Current save difficulty: Easy" },
                { "向上移动", "Move Up" },
                { "修改", "Modify" },
                { "清除", "Clear" },
                { "分别设置键鼠与手柄；重复绑定会被拦截，修改后自动保存。", "Configure keyboard/mouse and gamepad separately; duplicate bindings are blocked and changes are saved automatically." },
                { "键鼠", "Keyboard & Mouse" },
                { "手柄", "Gamepad" },
                { "选择一项后按下新按键。", "Select an action and press a new key." },
                { "调整会立即应用并自动保存；Prefab", "Changes apply immediately and are saved automatically; Prefab" },
                { "适配屏幕安全区域", "Fit Screen Safe Area" },
                { "游戏设置", "Game Settings" },
                { "全屏窗口", "Fullscreen Window" },
                { "高", "High" },
                { "自动适合多数设备；流畅优先减少", "Automatically fits most devices; prioritizes smooth performance by reducing" },
                { "正在进入世界", "Entering World" },
                { "正在准备世界数据…", "Preparing World Data…" },
                { "请稍候，世界准备完成后将自动进入。", "Please wait. You will enter automatically when the world is ready." },
                { "存档条目", "Save Entries" },
                { "选择  /  右键管理", "Select  /  Right-click to Manage" },
                { "安全区域适配：关闭", "Safe Area: Off" },
                { "生存状态", "Survival Status" },
                { "调试面板", "Debug Panel" },
                { "整理呼吸，再次回到这片世界。", "Catch your breath, then return to this world." },
                { "INVENTORY  /  随身物资", "INVENTORY  /  FIELD KIT" },
                { "EQUIPMENT  /  生存配置", "EQUIPMENT  /  SURVIVAL SETUP" },
                { "CRAFTING  /  基础工艺", "CRAFTING  /  BASIC CRAFT" },
                { "WORKBENCH  /  精细工艺", "WORKBENCH  /  FINE CRAFT" },
                { "FURNACE  /  冶炼作业", "FURNACE  /  SMELTING" },
                { "BONFIRE  /  火源管理", "BONFIRE  /  FIRE MANAGEMENT" },
                { "COMPOST  /  资源循环", "COMPOST  /  RESOURCE CYCLE" },
                { "MEAT RACK  /  食物处理", "MEAT RACK  /  FOOD PROCESSING" },
                { "FIRECRAFT  /  生火作业", "FIRECRAFT  /  FIRE STARTING" },
                { "SURVIVAL  /  模块状态", "SURVIVAL  /  MODULE STATUS" },
                { "ITEM  /  观察记录", "ITEM  /  OBSERVATION LOG" },
                { "ACTIONS  /  快捷入口", "ACTIONS  /  QUICK ACCESS" },
                { "DEVELOPMENT  /  运行信息", "DEVELOPMENT  /  RUNTIME INFO" },
                { "游戏难度：{0}", "Game Difficulty: {0}" },
                { "当前存档难度：{0}", "Current Save Difficulty: {0}" },
                { "已选择：{0}。点击“应用”后生效。", "Selected: {0}. Click Apply to take effect." },
                { "已应用：{0}。设置将在正常存档时写入磁盘。", "Applied: {0}. The setting will be written to disk on the next normal save." },
                { "死亡掉落", "Drop on Death" },
                { "死亡保留", "Keep on Death" },
                { "使用玩家自定义规则；死亡时掉落全部随身物品。", "Using custom player rules; all carried items are dropped on death." },
                { "使用玩家自定义规则；死亡后保留全部随身物品。", "Using custom player rules; all carried items are kept after death." },
                { "当前没有已加载的游戏存档，无法修改难度。", "No game save is currently loaded; difficulty cannot be changed." },
                { "战斗：玩家 {0} / 生物伤害 {1} / 生物生命 {2}\n生存：饥饿 {3} / 耐力消耗 {4} / {5}\n世界：时间 {6} / 生成 {7} / 战利品 {8}\n生产：生长 {9} / 熔炼 {10} / 制作 {11}", "Combat: Player {0} / Creature Damage {1} / Creature Health {2}\nSurvival: Hunger {3} / Stamina {4} / {5}\nWorld: Time {6} / Spawns {7} / Loot {8}\nProduction: Growth {9} / Smelting {10} / Crafting {11}" },
                { "难度设置  ·  {0}", "Difficulty Settings  ·  {0}" },
                { "永远不自动保存", "Never Auto Save" },
                { "每 {0} 分钟", "Every {0} Minutes" },
                { "自定义间隔", "Custom Interval" },
                { "已选择：永远不自动保存。点击“应用”后生效。", "Selected: Never Auto Save. Click Apply to take effect." },
                { "请输入 1–1440 分钟，然后点击“应用”。", "Enter 1–1440 minutes, then click Apply." },
                { "已选择：每 {0} 分钟自动保存。", "Selected: Auto-save every {0} minutes." },
                { "请输入 1–1440 之间的整数分钟数。", "Enter an integer number of minutes between 1 and 1440." },
                { "当前设置：永远不自动保存。", "Current setting: Never Auto Save." },
                { "当前设置：每 {0} 分钟自动保存。", "Current setting: Auto-save every {0} minutes." },
                { "等待输入…", "Waiting for input…" },
                { "正在修改“{0}”；Esc / 手柄 B 取消，绑定该键时改用 Backspace / Start。", "Rebinding “{0}”; press Esc / Gamepad B to cancel. Use Backspace / Start when binding that key." },
                { "“{0}”已保存。", "“{0}” saved." },
                { "已取消本次修改。", "This change was canceled." },
                { "该按键已用于“{0}”，未作修改。", "This key is already used by “{0}”; no changes were made." },
                { "清除绑定失败。", "Failed to clear the binding." },
                { "“{0}”的绑定已清除。", "The binding for “{0}” was cleared." },
                { "其他操作", "another action" },
                { "修改失败：{0}", "Rebind failed: {0}" },
                { "未知错误", "Unknown error" },
                { "当前：{0}。选择一项后输入新控制；冲突会被拦截并自动保存。", "Current: {0}. Select an action and enter a new control; conflicts are blocked and changes are saved automatically." },
                { "自动（推荐）", "Automatic (Recommended)" },
                { "流畅优先（单后台线程）", "Smooth First (Single Background Thread)" },
                { "高吞吐（安全多线程）", "High Throughput (Safe Multithreading)" },
                { "当前：单后台线程生成 + 主线程逐帧绘制（{0} 个生成任务并发）。", "Current: single background-thread generation + frame-by-frame main-thread drawing ({0} generation tasks concurrent)." },
                { "当前：安全多线程高吞吐（{0} 个生成任务并发）。", "Current: safe multithreaded high throughput ({0} generation tasks concurrent)." },
                { "当前：自动平衡（{0} 个生成任务并发）。", "Current: automatic balance ({0} generation tasks concurrent)." },
                { "正在切换维度", "Switching Dimension" },
                { "维度跃迁", "Dimension Travel" },
                { "正在前往：{0}", "Traveling to: {0}" },
                { "维度稳定后将自动抵达。", "You will arrive automatically once the dimension stabilizes." },
                { "未知维度", "Unknown Dimension" },
                { "正在创建新世界", "Creating New World" },
                { "正在准备新存档数据…", "Preparing new save data…" },
                { "正在生成世界种子…", "Generating world seed…" },
                { "正在创建星球数据…", "Creating planet data…" },
                { "正在写入首个存档…", "Writing the first save…" },
                { "存档已创建，正在进入世界…", "Save created; entering the world…" },
                { "正在进入存档", "Entering Save" },
                { "正在保存…", "Saving…" },
                { "保存失败", "Save Failed" },
                { "状态效果 / BUFFS", "Status Effects / BUFFS" },
                { "暂无状态", "No Active Effects" },
                { "永久", "Permanent" },
                { "剩余 {0}s", "Remaining {0}s" },
                { "剩余 30s", "Remaining 30s" },
                { "任务追踪 / QUESTS", "QUEST TRACKER / QUESTS" },
                { "暂无进行中的任务", "No Active Quests" },
                { "进行中", "Active" },
                { "可领取", "Ready" },
                { "已完成", "Completed" },
                { "任务目标已完成", "Objectives Complete" },
                { "暂无任务目标", "No Quest Objectives" },
                { "正在加载星球：{0}", "Loading planet: {0}" },
                { "正在创建玩家并准备出生区域…", "Creating the player and preparing the spawn area…" },
                { "正在根据世界种子定位安全出生点…", "Locating a safe spawn point from the world seed…" },
                { "正在加载玩家周围区域…", "Loading the area around the player…" },
                { "正在生成并加载周围区块…", "Generating and loading nearby chunks…" },
                { "正在保存当前维度…", "Saving the current dimension…" },
                { "正在创建目标维度…", "Creating the target dimension…" },
                { "正在加载目标维度…", "Loading the target dimension…" },
                { "正在生成目标区块…", "Generating target chunks…" },
                { "正在固定矿洞出口…", "Securing the cave exit…" },
                { "正在完成目标维度加载…", "Finalizing the target dimension…" },
                { "维度切换完成", "Dimension Travel Complete" },
                { "目标维度已经准备完毕。", "The target dimension is ready." },
                { "维度切换失败，正在恢复原世界…", "Dimension travel failed; restoring the source world…" },
                { "维度切换失败，已恢复到原世界。", "Dimension travel failed; the source world was restored." },
                { "维度切换失败，原世界恢复也发生异常。", "Dimension travel failed, and restoring the source world also encountered an error." },
                { "{0}绑定已恢复默认值。", "{0} bindings restored to defaults." },
                { "向下移动", "Move Down" },
                { "向左移动", "Move Left" },
                { "向右移动", "Move Right" },
                { "主要操作", "Primary Action" },
                { "次要操作", "Secondary Action" },
                { "交互", "Interact" },
                { "喝水", "Drink" },
                { "丢弃", "Drop" },
                { "背包", "Inventory" },
                { "装备面板", "Equipment Panel" },
                { "奔跑", "Run" },
                { "切换奔跑", "Toggle Run" },
                { "长按奔跑", "Hold to Run" },
                { "角色参数面板", "Character Stats Panel" },
                { "镜头缩放修饰键", "Camera Zoom Modifier" },
                { "打开聊天框", "Open Chat" },
                { "快捷栏 1", "Hotbar 1" },
                { "快捷栏 2", "Hotbar 2" },
                { "快捷栏 3", "Hotbar 3" },
                { "快捷栏 4", "Hotbar 4" },
                { "快捷栏 5", "Hotbar 5" },
                { "快捷栏 6", "Hotbar 6" },
                { "快捷栏 7", "Hotbar 7" },
                { "快捷栏 8", "Hotbar 8" },
                { "快捷栏 9", "Hotbar 9" },
                { "关闭面板 / 打开设置", "Close Panel / Open Settings" },
                { "虚拟光标", "Virtual Cursor" },
                { "营养面板", "Nutrition Panel" },
                { "快捷栏上一格", "Previous Hotbar Slot" },
                { "快捷栏下一格", "Next Hotbar Slot" }
            };

        private static readonly KeyValuePair<string, string>[] EnglishUiPhraseReplacements =
        {
            new KeyValuePair<string, string>("玩家", "Player"),
            new KeyValuePair<string, string>("生物", "Creature"),
            new KeyValuePair<string, string>("世界", "World"),
            new KeyValuePair<string, string>("中的布局就是运行时看到的基础布局", "the layout in the Prefab is the base layout shown at runtime"),
            new KeyValuePair<string, string>("自带端口时会覆盖默认端口", "includes a port and overrides the default port"),
            new KeyValuePair<string, string>("高吞吐会在安全上限内使用多个后台线程", "high throughput uses multiple background threads within safe limits"),
            new KeyValuePair<string, string>("CPU 争用", "CPU contention"),
            new KeyValuePair<string, string>("进入World", "Enter World"),
            new KeyValuePair<string, string>("选择世界", "Select World"),
            new KeyValuePair<string, string>("载入存档", "Load Save"),
            new KeyValuePair<string, string>("选择角色", "Choose Character"),
            new KeyValuePair<string, string>("创建你的世界", "Create your world"),
            new KeyValuePair<string, string>("粘贴好友提供的", "paste the address provided by a friend"),
            new KeyValuePair<string, string>("可直接粘贴", "You can paste"),
            new KeyValuePair<string, string>("域名", "domain"),
            new KeyValuePair<string, string>("端口", "port"),
            new KeyValuePair<string, string>("穿透协议", "traversal protocol"),
            new KeyValuePair<string, string>("穿透", "traversal"),
            new KeyValuePair<string, string>("加入旅程", "to join the journey"),
            new KeyValuePair<string, string>("必须为", "must be"),
            new KeyValuePair<string, string>("默认端口", "default port"),
            new KeyValuePair<string, string>("地址", "address"),
            new KeyValuePair<string, string>("角色", "Character"),
            new KeyValuePair<string, string>("进入世界", "Enter World"),
            new KeyValuePair<string, string>("战斗：", "Combat: "),
            new KeyValuePair<string, string>("调整会立即应用并自动保存", "Changes apply immediately and are saved automatically"),
            new KeyValuePair<string, string>("自动适合多数设备", "Automatically fits most devices"),
            new KeyValuePair<string, string>("流畅优先", "smooth performance first"),
            new KeyValuePair<string, string>("减少", "reducing"),
            new KeyValuePair<string, string>("设置", "Settings"),
            new KeyValuePair<string, string>("存档", "Save"),
            new KeyValuePair<string, string>("选择", "Select"),
            new KeyValuePair<string, string>("创建", "Create"),
            new KeyValuePair<string, string>("生成", "Generate"),
            new KeyValuePair<string, string>("开始", "Start"),
            new KeyValuePair<string, string>("返回", "Back"),
            new KeyValuePair<string, string>("关闭", "Close"),
            new KeyValuePair<string, string>("取消", "Cancel"),
            new KeyValuePair<string, string>("应用", "Apply"),
            new KeyValuePair<string, string>("确认", "Confirm"),
            new KeyValuePair<string, string>("名称", "Name"),
            new KeyValuePair<string, string>("语言", "Language"),
            new KeyValuePair<string, string>("伤害", "Damage"),
            new KeyValuePair<string, string>("生命", "Health"),
            new KeyValuePair<string, string>("消耗", "Consumption"),
            new KeyValuePair<string, string>("恢复", "Recovery"),
            new KeyValuePair<string, string>("效果", "Effect"),
            new KeyValuePair<string, string>("速度", "Speed"),
            new KeyValuePair<string, string>("频率", "Frequency"),
            new KeyValuePair<string, string>("上限", "Limit"),
            new KeyValuePair<string, string>("生长", "Growth"),
            new KeyValuePair<string, string>("燃料", "Fuel"),
            new KeyValuePair<string, string>("制作", "Crafting"),
            new KeyValuePair<string, string>("物品", "Item"),
            new KeyValuePair<string, string>("详情", "Details"),
            new KeyValuePair<string, string>("操作", "Actions"),
            new KeyValuePair<string, string>("输入", "Input"),
            new KeyValuePair<string, string>("输出", "Output"),
            new KeyValuePair<string, string>("音量", "Volume"),
            new KeyValuePair<string, string>("音效", "SFX"),
            new KeyValuePair<string, string>("音乐", "Music"),
            new KeyValuePair<string, string>("语音", "Voice"),
            new KeyValuePair<string, string>("当前", "Current"),
            new KeyValuePair<string, string>("玩家：", "Players: "),
            new KeyValuePair<string, string>("已开启", "Enabled"),
            new KeyValuePair<string, string>("已关闭", "Disabled"),
            new KeyValuePair<string, string>("简体中文", "Simplified Chinese"),
            new KeyValuePair<string, string>("推荐", "Recommended"),
            new KeyValuePair<string, string>("默认", "Default"),
            new KeyValuePair<string, string>("自动", "Automatic"),
            new KeyValuePair<string, string>("返回", "Back")
        };

        #endregion

        #region 菜单入口

        /// <summary>创建本地化资源并从本体物品 JSON 补齐名称/说明条目。</summary>
        [MenuItem("FlatWorld/Localization/Setup Default Tables")]
        public static void SetupDefaultTables()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(LocaleFolder);
            EnsureFolder(TableFolder);

            LocalizationSettings settings = EnsureSettings();
            Locale chinese = EnsureLocale("zh-CN", ChineseLocalePath);
            Locale english = EnsureLocale("en", EnglishLocalePath);
            SetDefaultLocale(chinese);
            var locales = new List<Locale> { chinese, english };

            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(
                FlatWorldLocalizationService.DefaultTable);
            if (collection == null)
            {
                collection = LocalizationEditorSettings.CreateStringTableCollection(
                    FlatWorldLocalizationService.DefaultTable,
                    TableFolder,
                    locales);
            }

            StringTable chineseTable = EnsureTable(collection, chinese);
            StringTable englishTable = EnsureTable(collection, english);
            int itemCount = SyncItemEntries(chineseTable, englishTable);
            int questTextCount = SyncQuestEntries(chineseTable, englishTable);

            StringTableCollection uiCollection = LocalizationEditorSettings.GetStringTableCollection(
                FlatWorldLocalizationService.UiTable);
            if (uiCollection == null)
            {
                uiCollection = LocalizationEditorSettings.CreateStringTableCollection(
                    FlatWorldLocalizationService.UiTable,
                    TableFolder,
                    locales);
            }

            StringTable uiChineseTable = EnsureTable(uiCollection, chinese);
            StringTable uiEnglishTable = EnsureTable(uiCollection, english);
            int uiCount = SyncUiEntries(uiChineseTable, uiEnglishTable);
            EnsureAndroidAppInfoMetadata(settings, uiCollection);

            LocalizationEditorSettings.SetPreloadTableFlag(chineseTable, true);
            LocalizationEditorSettings.SetPreloadTableFlag(englishTable, true);
            LocalizationEditorSettings.SetPreloadTableFlag(uiChineseTable, true);
            LocalizationEditorSettings.SetPreloadTableFlag(uiEnglishTable, true);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(chineseTable);
            EditorUtility.SetDirty(englishTable);
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(uiCollection);
            EditorUtility.SetDirty(uiChineseTable);
            EditorUtility.SetDirty(uiEnglishTable);
            EditorUtility.SetDirty(uiCollection.SharedData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FlatWorld Localization] 已完成设置：Locale=zh-CN/en，物品条目={itemCount}，任务文本={questTextCount}，UI 文本={uiCount}，Tables={FlatWorldLocalizationService.DefaultTable}/{FlatWorldLocalizationService.UiTable}");
        }

        #endregion

        #region 平台元数据

        /// <summary>将 Android 桌面名称绑定到已有的“平坦世界”多语言条目。</summary>
        internal static void EnsureAndroidAppInfoMetadata(
            LocalizationSettings settings,
            StringTableCollection uiCollection)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (uiCollection == null)
                throw new ArgumentNullException(nameof(uiCollection));

            string appNameKey = FlatWorldLocalizationService.GetUiTextKey("平坦世界");
            SharedTableData.SharedTableEntry appNameEntry = uiCollection.SharedData.GetEntry(appNameKey);
            if (appNameEntry == null)
                throw new InvalidOperationException($"FlatWorldUI 缺少 Android 应用名称条目：{appNameKey}");

            AppInfo appInfo = LocalizationSettings.Metadata.GetMetadata<AppInfo>();
            if (appInfo == null)
            {
                appInfo = new AppInfo();
                LocalizationSettings.Metadata.AddMetadata(appInfo);
            }

            appInfo.DisplayName = new LocalizedString(
                uiCollection.TableCollectionNameReference,
                appNameEntry.Id);
            EditorUtility.SetDirty(settings);
        }

        #endregion

        #region UI 文本同步

        /// <summary>扫描正式 UI Prefab 的静态中文 TMP 文本并同步到独立 UI 表。</summary>
        private static int SyncUiEntries(StringTable chineseTable, StringTable englishTable)
        {
            const string uiPrefabRoot = "Assets/2_Prefabs/2-1_UI";
            string chineseTablePath = AssetDatabase.GetAssetPath(chineseTable);
            string englishTablePath = AssetDatabase.GetAssetPath(englishTable);
            if (string.IsNullOrEmpty(chineseTablePath) || string.IsNullOrEmpty(englishTablePath))
                throw new InvalidOperationException("UI StringTable 缺少稳定资源路径，无法安全扫描 Prefab。");
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { uiPrefabRoot });
            var syncedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (string prefabGuid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                GameObject prefabRoot = null;
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                    TMP_Text[] texts = prefabRoot.GetComponentsInChildren<TMP_Text>(true);
                    foreach (TMP_Text text in texts)
                    {
                        if (text == null || string.IsNullOrWhiteSpace(text.text) || !ContainsChinese(text.text))
                            continue;

                        string sourceText = text.text;
                        string key = FlatWorldLocalizationService.GetUiTextKey(sourceText);
                        string englishText = GetEnglishUiText(sourceText);
                        SetChineseValue(chineseTable, key, sourceText);
                        SetEnglishValue(englishTable, key, englishText, sourceText, key);
                        syncedKeys.Add(key);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[FlatWorld Localization] 跳过无法扫描的 UI Prefab：{prefabPath}\n{exception.Message}");
                }
                finally
                {
                    if (prefabRoot != null)
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                }

                // Load/UnloadPrefabContents 可能触发资源刷新，使此前缓存的 StringTable 句柄失效。
                // 每个 Prefab 扫描后按稳定 Asset 路径重新获取，避免后续写表访问已销毁对象。
                chineseTable = ReloadStringTable(chineseTablePath);
                englishTable = ReloadStringTable(englishTablePath);
            }

            // 同步脚本运行时会使用的模板，避免动态状态文本绕过 UI 表。
            foreach (KeyValuePair<string, string> runtimeEntry in EnglishUiOverrides)
            {
                if (string.IsNullOrWhiteSpace(runtimeEntry.Key) || !ContainsChinese(runtimeEntry.Key))
                    continue;

                string key = FlatWorldLocalizationService.GetUiTextKey(runtimeEntry.Key);
                SetChineseValue(chineseTable, key, runtimeEntry.Key);
                SetEnglishValue(englishTable, key, runtimeEntry.Value, runtimeEntry.Key, key);
                syncedKeys.Add(key);
            }

            SyncStableUiEntry(chineseTable, englishTable, "dimension.surface.name", "地表", "Surface");
            SyncStableUiEntry(chineseTable, englishTable, "dimension.cave.name", "地下矿洞", "Underground Cave");
            syncedKeys.Add("dimension.surface.name");
            syncedKeys.Add("dimension.cave.name");

            return syncedKeys.Count;
        }

        private static void SyncStableUiEntry(
            StringTable chineseTable,
            StringTable englishTable,
            string key,
            string chinese,
            string english)
        {
            SetChineseValue(chineseTable, key, chinese);
            SetEnglishValue(englishTable, key, english, chinese, key);
        }

        /// <summary>资源刷新后重新加载 StringTable；句柄仍有效时直接返回。</summary>
        private static StringTable ReloadStringTable(string assetPath)
        {
            StringTable reloaded = AssetDatabase.LoadAssetAtPath<StringTable>(assetPath);
            return reloaded != null
                ? reloaded
                : throw new InvalidOperationException($"无法重新加载 StringTable：{assetPath}");
        }

        /// <summary>获取 UI 文本的英文翻译；未覆盖的文本不会把中文带入英语表。</summary>
        private static string GetEnglishUiText(string sourceText)
        {
            string normalized = sourceText?.Trim() ?? string.Empty;
            string translatedText;
            if (EnglishUiOverrides.TryGetValue(normalized, out translatedText))
                return translatedText;

            if (normalized.StartsWith("Assets\\", StringComparison.Ordinal))
                return "Debug message";
            if (normalized.IndexOf("创建你的世界", StringComparison.Ordinal) >= 0)
                return "Create your world, or use UDP traversal to join the journey.";
            if (normalized.IndexOf("镜头前探", StringComparison.Ordinal) >= 0 &&
                normalized.IndexOf("预判平滑", StringComparison.Ordinal) >= 0)
                return "Changes apply immediately and are saved automatically. Positive lookahead moves the camera ahead; negative values add inertia, and more negative means stronger inertia. Higher smoothing is steadier but slower to respond.";
            if (normalized.IndexOf("镜头前探", StringComparison.Ordinal) >= 0)
                return "Camera Lookahead";
            if (normalized.IndexOf("预判平滑", StringComparison.Ordinal) >= 0)
                return "Lookahead Smoothing";

            translatedText = normalized;
            var orderedReplacements = new List<KeyValuePair<string, string>>(EnglishUiPhraseReplacements);
            orderedReplacements.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
            foreach (KeyValuePair<string, string> replacement in orderedReplacements)
                translatedText = translatedText.Replace(replacement.Key, replacement.Value);

            if (ContainsChinese(translatedText))
            {
                Debug.LogWarning($"[FlatWorld Localization] UI 文本缺少英文翻译，已使用占位文本：{normalized}");
                return "UI Text";
            }

            return translatedText;
        }

        #endregion

        #region 资源创建

        private static LocalizationSettings EnsureSettings()
        {
            LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                    settings.name = "FlatWorld Localization Settings";
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }

                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }

            return settings;
        }

        private static Locale EnsureLocale(string code, string assetPath)
        {
            Locale locale = LocalizationEditorSettings.GetLocale(code);
            if (locale == null)
            {
                locale = AssetDatabase.LoadAssetAtPath<Locale>(assetPath);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(code);
                    locale.name = code;
                    AssetDatabase.CreateAsset(locale, assetPath);
                }

                LocalizationEditorSettings.AddLocale(locale);
            }

            return locale;
        }

        private static StringTable EnsureTable(StringTableCollection collection, Locale locale)
        {
            StringTable table = collection.GetTable(locale.Identifier) as StringTable;
            if (table == null)
                table = collection.AddNewTable(locale.Identifier) as StringTable;

            if (table == null)
                throw new InvalidOperationException($"无法创建 String Table：{locale.Identifier.Code}");

            return table;
        }

        private static void SetDefaultLocale(Locale locale)
        {
            foreach (IStartupLocaleSelector selector in LocalizationSettings.StartupLocaleSelectors)
            {
                if (selector is SpecificLocaleSelector specificLocaleSelector)
                    specificLocaleSelector.LocaleId = locale.Identifier;
            }

            LocalizationSettings.ProjectLocale = locale;
            EditorUtility.SetDirty(LocalizationSettings.Instance);
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        #endregion

        #region JSON 同步

        /// <summary>从本体任务分包同步标题、说明和目标标签到内容表。</summary>
        private static int SyncQuestEntries(StringTable chineseTable, StringTable englishTable)
        {
            string questRoot = Path.Combine(Application.dataPath, "StreamingAssets/GameConfig/Quests");
            if (!Directory.Exists(questRoot))
                return 0;

            var syncedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(questRoot, "*.json", SearchOption.AllDirectories))
            {
                JObject root;
                try
                {
                    root = JObject.Parse(File.ReadAllText(file));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[FlatWorld Localization] 跳过无法解析的任务 JSON：{file}\n{exception.Message}");
                    continue;
                }

                if (!(root["quests"] is JArray quests))
                    continue;

                foreach (JToken token in quests)
                {
                    if (!(token is JObject quest))
                        continue;

                    SyncQuestText(
                        chineseTable,
                        englishTable,
                        quest.Value<string>("titleKey"),
                        quest.Value<string>("title"),
                        syncedKeys);
                    SyncQuestText(
                        chineseTable,
                        englishTable,
                        quest.Value<string>("descriptionKey"),
                        quest.Value<string>("description"),
                        syncedKeys);

                    if (!(quest["stages"] is JArray stages))
                        continue;

                    foreach (JToken stageToken in stages)
                    {
                        if (!(stageToken is JObject stage) || !(stage["objectives"] is JArray objectives))
                            continue;

                        foreach (JToken objectiveToken in objectives)
                        {
                            if (!(objectiveToken is JObject objective))
                                continue;

                            SyncQuestText(
                                chineseTable,
                                englishTable,
                                objective.Value<string>("labelKey"),
                                objective.Value<string>("label"),
                                syncedKeys);
                        }
                    }
                }
            }

            return syncedKeys.Count;
        }

        private static void SyncQuestText(
            StringTable chineseTable,
            StringTable englishTable,
            string key,
            string sourceText,
            ISet<string> syncedKeys)
        {
            string normalizedKey = key?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(sourceText))
                return;

            SetChineseValue(chineseTable, normalizedKey, sourceText);
            SetEnglishValue(
                englishTable,
                normalizedKey,
                GetEnglishQuestText(normalizedKey, sourceText),
                sourceText,
                normalizedKey);
            syncedKeys.Add(normalizedKey);
        }

        private static int SyncItemEntries(StringTable chineseTable, StringTable englishTable)
        {
            string itemRoot = Path.Combine(Application.dataPath, "StreamingAssets/GameConfig/Items");
            if (!Directory.Exists(itemRoot))
                return 0;

            int itemCount = 0;
            foreach (string file in Directory.GetFiles(itemRoot, "*.json", SearchOption.AllDirectories))
            {
                JObject root;
                try
                {
                    root = JObject.Parse(File.ReadAllText(file));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[FlatWorld Localization] 跳过无法解析的物品 JSON：{file}\n{exception.Message}");
                    continue;
                }

                if (!(root["items"] is JArray items))
                    continue;

                foreach (JToken token in items)
                {
                    if (!(token is JObject item) || item.Value<bool?>("abstract") == true)
                        continue;

                    string itemId = item.Value<string>("id")?.Trim();
                    if (string.IsNullOrWhiteSpace(itemId))
                        continue;

                    string labelKey = item.Value<string>("labelKey");
                    string descriptionKey = item.Value<string>("descriptionKey");
                    labelKey = string.IsNullOrWhiteSpace(labelKey)
                        ? FlatWorldLocalizationService.GetItemLabelKey(itemId)
                        : labelKey.Trim();
                    descriptionKey = string.IsNullOrWhiteSpace(descriptionKey)
                        ? FlatWorldLocalizationService.GetItemDescriptionKey(itemId)
                        : descriptionKey.Trim();

                    string legacyName = item.Value<string>("gameName") ?? itemId;
                    string legacyDescription = item.Value<string>("description") ?? string.Empty;
                    string englishName = GetEnglishName(itemId);
                    string englishDescription = GetEnglishDescription(itemId, englishName);
                    SetChineseValue(chineseTable, labelKey, legacyName);
                    SetChineseValue(chineseTable, descriptionKey, legacyDescription);
                    SetEnglishValue(englishTable, labelKey, englishName, legacyName, itemId);
                    SetEnglishValue(englishTable, descriptionKey, englishDescription, legacyDescription, itemId);
                    itemCount++;
                }
            }

            return itemCount;
        }

        private static void SetChineseValue(StringTable table, string key, string value)
        {
            StringTableEntry entry = table.GetEntry(key);
            if (entry == null)
            {
                table.AddEntry(key, value ?? string.Empty);
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Value) || IsDebugItemDescription(entry.Value))
                entry.Value = value ?? string.Empty;
        }

        /// <summary>只识别旧 ItemData.ToString 污染，不覆盖人工维护的正常中文翻译。</summary>
        private static bool IsDebugItemDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("物品名称：", StringComparison.Ordinal) ||
                   value.Contains("物品堆叠信息：", StringComparison.Ordinal) ||
                   value.Contains("全局唯一标识：", StringComparison.Ordinal) ||
                   value.Contains("TagDictionary:", StringComparison.Ordinal);
        }

        private static void SetEnglishValue(
            StringTable table,
            string key,
            string value,
            string legacyValue,
            string itemId)
        {
            StringTableEntry entry = table.GetEntry(key);
            if (entry == null)
            {
                table.AddEntry(key, value ?? string.Empty);
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Value)
                || string.Equals(entry.Value, legacyValue, StringComparison.Ordinal)
                || string.Equals(entry.Value, itemId, StringComparison.Ordinal)
                || string.Equals(entry.Value?.Trim(), "UI Text", StringComparison.Ordinal)
                || ContainsChinese(entry.Value))
                entry.Value = value ?? string.Empty;
        }

        #endregion

        #region 英文翻译

        private static string GetEnglishQuestText(string key, string sourceText)
        {
            if (EnglishQuestOverrides.TryGetValue(key, out string translatedText))
                return translatedText;
            if (!ContainsChinese(sourceText))
                return sourceText;

            Debug.LogWarning($"[FlatWorld Localization] 任务文本缺少英文翻译：{key} / {sourceText}");
            return "Quest Text";
        }

        private static string GetEnglishName(string itemId)
        {
            string translatedName;
            if (EnglishNameOverrides.TryGetValue(itemId, out translatedName))
                return translatedName;

            return SplitIdentifier(itemId);
        }

        private static string GetEnglishDescription(string itemId, string englishName)
        {
            string translatedDescription;
            if (EnglishDescriptionOverrides.TryGetValue(itemId, out translatedDescription))
                return translatedDescription;

            if (itemId.StartsWith("Axe_", StringComparison.Ordinal))
                return "A tool for chopping wood.";
            if (itemId.StartsWith("Pickaxe_", StringComparison.Ordinal))
                return "A tool for mining stone and ore.";
            if (itemId.StartsWith("Spear_", StringComparison.Ordinal))
                return "A weapon for attacking from a short distance.";
            if (itemId.StartsWith("Dagger_", StringComparison.Ordinal)
                || string.Equals(itemId, "Knife_Flint", StringComparison.Ordinal))
                return "A small blade for close combat.";
            if (itemId.StartsWith("Ore_", StringComparison.Ordinal))
                return "Raw material that can be processed into useful resources.";
            if (itemId.StartsWith("Ingot_", StringComparison.Ordinal))
                return "A refined metal bar used for crafting.";
            if (itemId.StartsWith("Coconut_", StringComparison.Ordinal)
                || string.Equals(itemId, "CoconutMeat", StringComparison.Ordinal))
                return "A coconut ingredient used for food and drink.";
            if (itemId.StartsWith("Meat", StringComparison.Ordinal))
                return "Food that can be eaten.";
            if (string.Equals(itemId, "Seed_Apple", StringComparison.Ordinal))
                return "A seed that can grow into an apple tree.";

            return $"{englishName}.";
        }

        private static string SplitIdentifier(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return string.Empty;

            string value = itemId
                .Replace("RawIron", "Raw Iron")
                .Replace("WroughtIron", "Wrought Iron")
                .Replace("MagicalStone", "Magic Stone")
                .Replace("CharredMatter", "Charred Matter")
                .Replace("CoconutMeat", "Coconut Meat")
                .Replace("RawHide", "Raw Hide");
            string[] parts = value.Split('_');
            return string.Join(" ", parts);
        }

        private static bool ContainsChinese(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char character in value)
            {
                if (character >= '\u4E00' && character <= '\u9FFF')
                    return true;
            }

            return false;
        }

        #endregion
    }
}
#endif
