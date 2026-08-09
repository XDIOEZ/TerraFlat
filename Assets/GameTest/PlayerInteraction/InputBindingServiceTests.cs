using System.Linq;
using InputSystem;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace FlatWorld.GameTest.PlayerInteraction
{
    /// <summary>按键绑定服务回归测试：保护单项清除只禁用当前绑定并立即持久化。</summary>
    public sealed class InputBindingServiceTests
    {
        #region 测试存储

        private sealed class MemoryBindingStore : IInputBindingStore
        {
            public string SavedJson { get; private set; }

            public string Load()
            {
                return SavedJson ?? string.Empty;
            }

            public void Save(string json)
            {
                SavedJson = json ?? string.Empty;
            }

            public void Clear()
            {
                SavedJson = string.Empty;
            }
        }

        #endregion

        #region 清除绑定回归

        [Test]
        [Category("PlayerInteraction.Input")]
        public void ClearBindingDisablesOnlySelectedEntryAndPersists()
        {
            PlayerInputActions inputActions = new PlayerInputActions();
            MemoryBindingStore store = new MemoryBindingStore();
            InputBindingService service = new InputBindingService(inputActions.asset, store);

            try
            {
                InputBindingEntry entry = service
                    .GetEntries(InputBindingDeviceGroup.KeyboardMouse)
                    .First(candidate => candidate.DisplayName == "主要操作");
                InputBindingEntry otherEntry = service
                    .GetEntries(InputBindingDeviceGroup.KeyboardMouse)
                    .First(candidate => candidate.DisplayName == "次要操作");
                string defaultPath = entry.Action.bindings[entry.BindingIndex].path;
                string otherPath = otherEntry.Action.bindings[otherEntry.BindingIndex].effectivePath;
                bool changed = false;
                service.BindingsChanged += () => changed = true;

                Assert.That(defaultPath, Is.Not.Null.And.Not.Empty);
                Assert.That(otherPath, Is.Not.Null.And.Not.Empty);
                Assert.That(service.ClearBinding(entry), Is.True);
                Assert.That(
                    string.IsNullOrEmpty(entry.Action.bindings[entry.BindingIndex].effectivePath),
                    Is.True,
                    "清除后不能继续使用默认绑定路径。");
                Assert.That(service.GetBindingDisplayString(entry), Is.EqualTo("未绑定"));
                Assert.That(
                    otherEntry.Action.bindings[otherEntry.BindingIndex].effectivePath,
                    Is.EqualTo(otherPath),
                    "清除一个操作不能影响同设备组的其他绑定。");
                Assert.That(changed, Is.True);
                Assert.That(store.SavedJson, Is.Not.Null.And.Not.Empty);

                PlayerInputActions reloadedActions = new PlayerInputActions();
                InputBindingService reloadedService = null;
                try
                {
                    reloadedService = new InputBindingService(reloadedActions.asset, store);
                    InputBindingEntry reloadedEntry = reloadedService
                        .GetEntries(InputBindingDeviceGroup.KeyboardMouse)
                        .First(candidate => candidate.DisplayName == "主要操作");
                    Assert.That(
                        string.IsNullOrEmpty(
                            reloadedEntry.Action.bindings[reloadedEntry.BindingIndex].effectivePath),
                        Is.True,
                        "重新加载覆盖配置后，已清除的绑定仍应保持为空。");
                }
                finally
                {
                    reloadedService?.Dispose();
                    reloadedActions.Dispose();
                }
            }
            finally
            {
                service.Dispose();
                inputActions.Dispose();
            }
        }

        #endregion
    }
}
