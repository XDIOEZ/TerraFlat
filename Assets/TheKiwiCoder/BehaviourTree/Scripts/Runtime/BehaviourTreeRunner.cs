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

        private bool isRunning = true; // 控制行为树运行
        public Ex_ModData_MemoryPackable ModData;
        public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData_MemoryPackable)value; }

        public override void Awake()
        {
            if (_Data.ID == "")
            {
                _Data.ID = gameObject.name;
            }
        }

        void InitTree()
        {
            context = CreateBehaviourTreeContext();
            tree = tree.Clone();
            tree.Bind(context);
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
            return Context.CreateFromGameObject(item.gameObject);
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
        }

        public override void Save()
        {
            StopTree();
        }
    }
}
