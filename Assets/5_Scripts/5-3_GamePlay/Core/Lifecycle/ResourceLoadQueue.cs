using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>有界资源请求队列；完成的槽位立即接续下一请求，具体资源仍由调用方会话持有。</summary>
internal static class ResourceLoadQueue
{
    #region 请求调度

    public static IEnumerator Run<TRequest>(int count, Func<int, TRequest> begin,
        Func<TRequest, bool> isDone, Action<int, TRequest> complete,
        Func<bool> shouldYield, Action<int> progress = null, int maxInFlight = 4)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (maxInFlight <= 0) throw new ArgumentOutOfRangeException(nameof(maxInFlight));
        if (begin == null) throw new ArgumentNullException(nameof(begin));
        if (isDone == null) throw new ArgumentNullException(nameof(isDone));
        if (complete == null) throw new ArgumentNullException(nameof(complete));

        var pending = new List<(int Index, TRequest Request)>(Math.Min(count, maxInFlight));
        int issued = 0, completed = 0;
        while (completed < count)
        {
            while (issued < count && pending.Count < maxInFlight)
            {
                pending.Add((issued, begin(issued)));
                issued++;
                if (shouldYield?.Invoke() == true) yield return null;
            }

            bool advanced = false;
            for (int i = 0; i < pending.Count;)
            {
                var request = pending[i];
                bool done = isDone(request.Request);
                if (shouldYield?.Invoke() == true) yield return null;
                if (!done) { i++; continue; }
                complete(request.Index, request.Request);
                pending.RemoveAt(i);
                completed++;
                progress?.Invoke(completed);
                advanced = true;
                if (shouldYield?.Invoke() == true) yield return null;
            }

            // 只在请求仍未完成时等下一帧，已有空槽立即补发。
            if (!advanced && completed < count) yield return null;
        }
    }

    #endregion
}
