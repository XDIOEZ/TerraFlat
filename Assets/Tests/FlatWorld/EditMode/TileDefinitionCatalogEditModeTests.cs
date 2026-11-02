using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace FlatWorld.Tests.EditMode
{
    /// <summary>使用正式地块解析和构建入口校验所有启用的配置分包。</summary>
    public sealed class TileDefinitionCatalogEditModeTests
    {
        #region 本体地块配置
        [Test]
        public void BuiltInTiles_AllEnabledPackagesDeserializeAndBuild()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "GameConfig", "Tiles");
            string manifestPath = Path.Combine(root, TileDefinitionCatalogLoader.ManifestFileName);
            TileDefinitionManifestDto manifest = TileDefinitionCatalogLoader.DeserializeManifest(File.ReadAllText(manifestPath));
            var definitions = new List<RuntimeTileDefinition>();
            var errors = new List<string>();
            Tile tileAsset = ScriptableObject.CreateInstance<Tile>();

            try
            {
                foreach (TileDefinitionPackageDto package in manifest.Packages)
                {
                    if (!package.Enabled) continue;
                    string path = Path.Combine(root, package.Path.Replace('/', Path.DirectorySeparatorChar));
                    try
                    {
                        TileDefinitionCatalogDto catalog = TileDefinitionFactory.DeserializeCatalog(File.ReadAllText(path));
                        foreach (TileDefinitionDto dto in catalog.Tiles)
                        {
                            try
                            {
                                definitions.Add(TileDefinitionFactory.Build(dto, _ => tileAsset));
                            }
                            catch (Exception error)
                            {
                                errors.Add(package.Id + "/" + dto?.Id + ": " + error.Message);
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        errors.Add(package.Id + ": " + error.Message);
                    }
                }

                if (errors.Count == 0)
                {
                    try { TileDefinitionFactory.ValidateIdentities(definitions); }
                    catch (Exception error) { errors.Add("地块身份: " + error.Message); }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tileAsset);
            }

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(definitions.Count, Is.GreaterThan(0), "地块清单未构建任何地块。");
        }

        [Test]
        public void BuiltInTiles_RejectsRemovedDamageField()
        {
            const string obsolete = "{\"schemaVersion\":1,\"tiles\":[{" +
                                    "\"id\":\"Tile_Test\",\"tileAsset\":\"Tile_Stone\"," +
                                    "\"data\":{\"type\":\"universal\"}," +
                                    "\"damageProfile\":{\"minimumWeaponDamage\":0}}]}";
            Assert.That(() => TileDefinitionFactory.DeserializeCatalog(obsolete),
                Throws.Exception.With.Message.Contains("minimumWeaponDamage"));
        }
        #endregion
    }
}
