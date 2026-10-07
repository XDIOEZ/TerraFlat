using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltEvents;
using Sirenix.OdinInspector;

namespace TheKiwiCoder
{
    public class Mod_BehaviourTreeRunner : Module
    {
        #region 运行态树与调度

        public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
        public BehaviourTree tree;
        private BehaviourTree templateTree;
        [ShowInInspector]
        Context context;

        public bool isRunning = false; // 默认为停止状态
        public Ex_ModData_MemoryPackable ModData;
        public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData_MemoryPackable)value; }

        public void OnValidate()
        {
            _Data.ID = ModText.AI;
        }

        void InitTree()
        {
            context = CreateBehaviourTreeContext();
            templateTree ??= tree;
            if (templateTree == null || templateTree.rootNode == null)
                throw new System.InvalidOperationException("行为树模块缺少有效的树模板。");
            tree = templateTree.Clone();
            tree.Bind(context);
            tree.Init();
        }

        public override void ModUpdate(float deltaTime)
        {
            if (isRunning && tree != null)
            {
                tree.Update();
            }
        }

        void FixedUpdate()
        {
            if (IsRuntimeLoaded && Enabled && isRunning && tree != null && tree.rootNode != null)
            {
                tree.rootNode.FixedUpdate();
            }
        }

        Context CreateBehaviourTreeContext()
        {
            return Context.CreateFromItem(item);
        }

        public void StopTree()
        {
            isRunning = false;
        }

        public void StartTree()
        {
            isRunning = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!tree)
            {
                return;
            }

            BehaviourTree.Traverse(tree.rootNode, (n) => {
                if (n.drawGizmos)
                {
                    n.OnDrawGizmos();
                }
            });
        }

        protected override void OnLoad()
        {
            InitTree();
            StartTree();
        }

        protected override void OnSave()
        {
   
        }
        
        public void OnDestroy()
        {
            Unload();
        }

        protected override void OnUnload()
        {
            StopTree();
            BehaviourTree runtimeTree = tree;
            tree = templateTree;
            context = null;
            if (runtimeTree == null || runtimeTree == templateTree) return;
            // 克隆树与节点只属于这一轮运行态，卸载后必须一并释放。
            try { runtimeTree.rootNode?.Abort(); }
            finally
            {
                foreach (Node node in runtimeTree.nodes)
                    if (node != null) Destroy(node);
                Destroy(runtimeTree);
            }
        }

        #endregion
    }
}
