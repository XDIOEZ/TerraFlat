using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.GameTest.PlayerInteraction
{
    public sealed class PlayerWorldWrapSmokeTests
    {

        [Test]
        [Category("PlayerInteraction.Smoke")]
        [Category("Smoke")]
        public void LocalPlayerWrapsAcrossFourEdgesAndCornersWhilePreservingVelocityAndData()
        {
            SaveDataMgr manager = Object.FindObjectOfType<SaveDataMgr>();
            GameObject managerOwner = null;
            if (manager == null)
            {
                managerOwner = new GameObject("WorldWrapSaveDataMgr");
                manager = managerOwner.AddComponent<SaveDataMgr>();
            }

            GameSaveData previousSave = manager.SaveData;
            string sceneName = SceneManager.GetActiveScene().name;
            var planet = new PlanetData
            {
                Name = sceneName,
                Radius = 16,
                ChunkSize = new Vector2Int(16, 16),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            manager.SaveData = new GameSaveData
            {
                PlanetData_Dict = new Dictionary<string, PlanetData> { [sceneName] = planet }
            };

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Gameplay/Player/Player.prefab");
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Mod_ChunkLoader loader = instance.GetComponentInChildren<Mod_ChunkLoader>(true);
                if (loader != null)
                    Object.DestroyImmediate(loader);

                Player player = instance.GetComponent<Player>();
                Rigidbody2D body = instance.GetComponent<Rigidbody2D>();
                PlayerWorldWrapController controller = instance.GetComponent<PlayerWorldWrapController>();
                player.BindData(new Data_Player());
                player.SetProfileContext(true, false);

                var cases = new[]
                {
                    (input: new Vector2(17.25f, 0f), expected: new Vector2(-14.75f, 0f)),
                    (input: new Vector2(-17.25f, 0f), expected: new Vector2(14.75f, 0f)),
                    (input: new Vector2(0f, 18.5f), expected: new Vector2(0f, -13.5f)),
                    (input: new Vector2(0f, -18.5f), expected: new Vector2(0f, 13.5f)),
                    (input: new Vector2(17.25f, -18.5f), expected: new Vector2(-14.75f, 13.5f))
                };

                foreach (var item in cases)
                {
                    instance.transform.position = new Vector3(item.input.x, item.input.y, 7f);
                    body.position = item.input;
                    body.velocity = new Vector2(3f, -4f);
                    Assert.That(controller.TryWrapNow(), Is.True, item.input.ToString());
                    Assert.That(body.position, Is.EqualTo(item.expected), item.input.ToString());
                    Assert.That(body.velocity, Is.EqualTo(new Vector2(3f, -4f)));
                    Assert.That(instance.transform.position.z, Is.EqualTo(7f));
                    Assert.That(player.Data.transform.position, Is.EqualTo(instance.transform.position));
                }

                planet.TopologyMode = WorldTopologyMode.Infinite;
                body.position = new Vector2(17.25f, 0f);
                Assert.That(controller.TryWrapNow(), Is.False);
                Assert.That(body.position, Is.EqualTo(new Vector2(17.25f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
                manager.SaveData = previousSave;
                if (managerOwner != null)
                    Object.DestroyImmediate(managerOwner);
            }
        }

    }
}
