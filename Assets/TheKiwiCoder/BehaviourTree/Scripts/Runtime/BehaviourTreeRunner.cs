using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UltEvents;

namespace TheKiwiCoder
{
    public class BehaviourTreeRunner : MonoBehaviour
    {
        public BehaviourTree tree;
        Context context;

        private bool isRunning = true; // 控制行为树运行

        void Start()
        {
            GetComponent<Item>().OnStopWork_Event += StopTree;
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
            return Context.CreateFromGameObject(gameObject);
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
    }
}
