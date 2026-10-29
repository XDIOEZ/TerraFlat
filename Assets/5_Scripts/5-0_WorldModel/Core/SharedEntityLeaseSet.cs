using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>共享实体只选一个表现宿主，最后一名引用离开时才允许释放实体。</summary>
    public sealed class SharedEntityLeaseSet<T> where T : class
    {
        #region 引用所有权
        private readonly List<T> members = new();
        public int Count => members.Count;
        public T Primary => members.Count > 0 ? members[0] : null;
        public bool Add(T member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (members.Contains(member)) return false;
            members.Add(member);
            return true;
        }
        public bool Remove(T member) => members.Remove(member);
        public void Clear() => members.Clear();
        #endregion
    }
}
