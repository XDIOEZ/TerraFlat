using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltEvents;
using Sirenix.OdinInspector;

namespace TheKiwiCoder
{
    public class BehaviourTreeRunner : Module
    {
        public BehaviourTree tree;
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
            tree = tree.Clone();
            tree.Bind(context);
            tree.Init();
        }

        void Update()
        {
            if (isRunning && tree != null)
            {
                tree.Update();
            }
        }

        void FixedUpdate()
        {
            if (isRunning && tree != null && tree.rootNode != null)
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

        public override void Load()
        {
            InitTree();
            StartTree();
        }

        public override void Save()
        {
   
        }
        
        public void OnDestroy()
        {
            StopTree();
        }
    }
}