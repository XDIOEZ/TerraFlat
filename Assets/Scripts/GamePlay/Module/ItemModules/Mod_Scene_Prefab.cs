using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Scene_Prefab : Mod_Scene
{
    public GameObject MapSvePrefab;
    public override void Interact(Item interacter)
    {
        Player player = interacter as Player;
        if (player == null)
        {
            Debug.LogError("Interact 调用失败，交互对象不是 Player");
            return;
        }


        Data_Player playerData = player.Data;
        Vector2 playerPos = player.transform.position;

        // ===== 进入房间 =====
        if (_sceneAssetList.Count > 0)
        {
            string lastSceneName = playerData.CurrentSceneName;

            // 设置初始进入房间的位置
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            //////////以下的操作将在新场景中进行//////////////

            // 切换场景
            GameManager.Instance.ChangeScene_By_SceneNames(lastSceneName, Data.SceneName, () =>
            {
                playerData.CurrentSceneName = Data.SceneName;

                // 清理 Chunk
                ChunkMgr.Instance.CleanEmptyDicValues();

                // 重新加载玩家
                Player newPlayer = ItemMgr.Instance.CreatePlayer(playerData.Name_User);
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;

                //创建新 Chunk

                // 1. 创建新Chunk 设定 Chunk名称
                GameObject ChunkGameObject = new GameObject();
                // 2. 设置 Chunk 组件
                Chunk Chunk = ChunkGameObject.AddComponent<Chunk>();
                // 3. 设置为子对象,并获取组件
                Map_Pit MapCore_Pit = Instantiate(MapSvePrefab,ChunkGameObject.transform).GetComponent<Map_Pit>();
                MapCore_Pit.Act();
                //创建新 Chunk
                foreach (MapSave targetMapSave in planetData.MapData_Dict.Values)
                {
                    // 2. 添加区块管理器
                    MapCore_Pit = MapSvePrefab.GetComponent<Map_Pit>();

                    Chunk chunk = ChunkMgr.Instance.CreateChunk_ByMapSave(targetMapSave);

                    ChunkMgr.Instance.AddActiveChunk(chunk);//添加到激活 Chunk 列表中

                    chunk.LoadChunkFromMapSave();

                    //设置玩家返回点和返回场景
                    if (_sceneAssetList != null && _sceneAssetList.Count > 0)
                    {
                        /*// 遍历所有物品，找到返回点
                        if (chunk.RuntimeItemsGroup.TryGetValue("MapCore_Pit", out var MapCore_Pit))
                        {
                            foreach (var MapCore in MapCore_Pit)
                            {
                              
                            }
                        }*/
                        // 遍历所有物品，找到返回点
                        if (chunk.RuntimeItemsGroup.TryGetValue("Door", out var doors))
                        {
                            foreach (var door in doors)
                            {
                                if (door.itemMods.GetMod_ByID(ModText.Scene) is Mod_Scene sceneMod)
                                {
                                    sceneMod.Data.SceneName = lastSceneName;
                                    sceneMod.Data.PlayerPos = playerPos;

                                    newPlayer.Data.transform.position = (Vector2)door.transform.position + PlayerPosOffset;
                                    this.Data.PlayerPos = door.transform.position;
                                }
                            }
                        }
                    }
                }

                //等待Chunk数据处理完毕后再初始化玩家 因为玩家身上有加载引用CHunk数据的组件
                newPlayer.Load();
                newPlayer.LoadDataPosition();
            });
        }
        // ===== 离开房间 =====
        else
        {
            player.transform.position = this.Data.PlayerPos + PlayerPosOffset;

            GameManager.Instance.ChangeScene_By_SceneNames(playerData.CurrentSceneName, this.Data.SceneName, () =>
            {
                playerData.CurrentSceneName = this.Data.SceneName;//设置当前所在的场景名称


                // 重新加载玩家数据
                Player newPlayer = ItemMgr.Instance.LoadPlayer(playerData.Name_User);
                newPlayer.Load();
                newPlayer.LoadDataPosition();
                ItemMgr.Instance.Player_DIC[playerData.Name_User] = newPlayer;
            });
        }
    }
}
