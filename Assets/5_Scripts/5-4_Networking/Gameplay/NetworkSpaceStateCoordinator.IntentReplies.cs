using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 操作回执与连接中断
        private readonly Dictionary<uint, Action<bool, string>> intentReplies = new();

        // 回执与超时都只完成同一序号一次，防止旧回复推进已结束的界面操作。
        private bool SendIntentWithReply(string profile, FlatWorld.Spaceflight.SpaceNetworkIntent intent, Action<bool, string> completed)
        {
            if (!SendIntent(profile, intent)) return false;
            uint sequence = intentSequence;
            intentReplies.Add(sequence, completed);
            StartCoroutine(WaitForIntentReply(sequence));
            return true;
        }

        private IEnumerator WaitForIntentReply(uint sequence)
        {
            yield return new WaitForSecondsRealtime(15f);
            if (intentReplies.Remove(sequence, out Action<bool, string> completed))
                completed?.Invoke(false, "服务器操作回复超时，请确认当前世界状态后重试。");
        }

        private void CompleteIntentReply(NetworkSpaceIntentResponse response)
        {
            if (intentReplies.Remove(response.Sequence, out Action<bool, string> completed))
                completed?.Invoke(response.Accepted, response.Reason);
        }

        private void ClearIntentReplies()
        {
            var pending = new List<Action<bool, string>>(intentReplies.Values);
            intentReplies.Clear();
            foreach (Action<bool, string> completed in pending)
                completed?.Invoke(false, "联机会话已结束。");
        }
        #endregion
    }
}
