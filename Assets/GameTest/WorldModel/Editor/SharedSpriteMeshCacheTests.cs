using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.GameTest.WorldModel
{
    /// <summary>在编辑模式验证网格身份、几何、兜底与 BRG 重建边界；不创建世界或访问存档。</summary>
    public sealed class SharedSpriteMeshCacheTests
    {
        #region 隔离资源

        private Texture2D texture; // 测试自有纹理。
        private Sprite first, second; // 名称相同但 Unity 身份不同的精灵。

        [SetUp]
        public void SetUp()
        {
            Assert.That(Application.isPlaying, Is.False, "此夹具只允许 EditMode，避免清理活动资源会话。");
            Assert.That(SharedSpriteMeshCache.Count, Is.Zero, "已有资源会话，不能破坏其缓存。");
            texture = new Texture2D(8, 8);
            first = Sprite.Create(texture, new Rect(0, 0, 4, 8), new Vector2(0.25f, 0.75f), 4);
            second = Sprite.Create(texture, new Rect(4, 0, 4, 8), Vector2.one * 0.5f, 4);
            first.name = second.name = "SameName";
        }

        [TearDown]
        public void TearDown()
        {
            // SetUp 未通过保护条件时，不得清理别人的会话。
            if (texture == null) return;
            SharedSpriteMeshCache.Clear();
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        #endregion

        #region 缓存与生命周期

        [Test]
        public void Prewarm_DeduplicatesUnityIdentity_AndPreservesGeometry()
        {
            SharedSpriteMeshCache.Prewarm(new[] { first, first, null, second });
            Mesh mesh = SharedSpriteMeshCache.GetOrCreate(first);
            Assert.That(SharedSpriteMeshCache.Count, Is.EqualTo(2));
            Assert.That(SharedSpriteMeshCache.GetOrCreate(first), Is.SameAs(mesh));
            Assert.That(SharedSpriteMeshCache.GetOrCreate(second), Is.Not.SameAs(mesh));
            Assert.That(mesh.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Vector2[] vertices = first.vertices;
            Vector3[] actual = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) Assert.That(actual[i], Is.EqualTo((Vector3)vertices[i]));
            CollectionAssert.AreEqual(first.uv, mesh.uv);
            CollectionAssert.AreEqual(Array.ConvertAll(first.triangles, value => (int)value), mesh.triangles);
        }

        [Test]
        public void UnwarmedSprite_FallsBackOnce_AfterPrewarm()
        {
            SharedSpriteMeshCache.Prewarm(new[] { first });
            Mesh mesh = SharedSpriteMeshCache.GetOrCreate(second);
            for (int i = 0; i < 100; i++)
                Assert.That(SharedSpriteMeshCache.GetOrCreate(second), Is.SameAs(mesh));
            Assert.That(SharedSpriteMeshCache.Count, Is.EqualTo(2));
        }

        [Test]
        public void Clear_DestroysOldMeshes_AndAllowsNextSession()
        {
            Mesh previous = SharedSpriteMeshCache.GetOrCreate(first);
            SharedSpriteMeshCache.Clear();
            SharedSpriteMeshCache.Dispose();
            Assert.That(previous == null, Is.True);
            Assert.That(SharedSpriteMeshCache.Count, Is.Zero);
            Mesh current = SharedSpriteMeshCache.GetOrCreate(first);
            Assert.That(current, Is.Not.SameAs(previous));
            Assert.That(current != null, Is.True);
        }

        [Test]
        public void BackendRelease_PreservesMesh_AndNewBackendRegistersAgain()
        {
            Type service = typeof(SharedSpriteMeshCache).Assembly.GetType("ChunkBatchRendererGroupService", true);
            Mesh shared = SharedSpriteMeshCache.GetOrCreate(first);
            object previousBackend = Invoke(service, null, "EnsureBackend");
            AssertRegisteredMesh(previousBackend, first, shared);
            Invoke(service, null, "ReleaseUnusedBackend");
            Assert.That(shared != null, Is.True);
            Assert.That(SharedSpriteMeshCache.GetOrCreate(first), Is.SameAs(shared));

            object currentBackend = Invoke(service, null, "EnsureBackend");
            Assert.That(currentBackend, Is.Not.SameAs(previousBackend));
            AssertRegisteredMesh(currentBackend, first, shared);
            SharedSpriteMeshCache.Clear();
            Assert.That(shared == null, Is.True);
            Assert.That(service.GetField("backend", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), Is.Null);
        }

        /// <summary>通过实际 BRG 查询验证注册结果，不假定不同 BRG 的数字 ID 必须不同。</summary>
        private static void AssertRegisteredMesh(object backend, Sprite sprite, Mesh expected)
        {
            object registration = Invoke(backend.GetType(), backend, "GetOrCreateMesh", sprite);
            var id = (BatchMeshID)registration.GetType().GetProperty("Id").GetValue(registration);
            var group = (BatchRendererGroup)backend.GetType()
                .GetField("rendererGroup", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(backend);
            Assert.That(group.GetRegisteredMesh(id), Is.SameAs(expected));
        }

        /// <summary>仅测试程序集使用反射访问后端，避免为测试扩大运行时公开 API。</summary>
        private static object Invoke(Type type, object target, string name, params object[] args) =>
            type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Invoke(target, args);

        #endregion
    }
}
