using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Experimental.GraphView;
using UnityEditor;

namespace TheKiwiCoder
{

    // NodeView 是行为树编辑器中每个节点在图形界面上的可视化表示
    public class NodeView : UnityEditor.Experimental.GraphView.Node
    {
        public Action<NodeView> OnNodeSelected; // 节点被选中时的回调
        public Node node; // 对应的数据模型
        public Port input; // 输入端口
        public Port output; // 输出端口

        // 构造函数 - 加载UXML布局并初始化节点
        public NodeView(Node node) : base(AssetDatabase.GetAssetPath(BehaviourTreeSettings.GetOrCreateSettings().nodeXml))
        {
            this.node = node;
            this.node.name = node.GetType().Name;
            this.title = node.name.Replace("(Clone)", "").Replace("Node", "");
            this.viewDataKey = node.guid;

            // 设置节点位置
            style.left = node.position.x;
            style.top = node.position.y;

            // 初始化端口和样式
            CreateInputPorts();
            CreateOutputPorts();
            SetupClasses();
            SetupDataBinding();
        }

        // 绑定数据，使 UI Label 显示节点描述（通过 SerializedObject 实现数据绑定）
        private void SetupDataBinding()
        {
            Label descriptionLabel = this.Q<Label>("description"); // 查询 UXML 中 id 为 description 的 Label
            descriptionLabel.bindingPath = "description"; // 绑定路径
            descriptionLabel.Bind(new SerializedObject(node)); // 绑定到节点对象
        }

        // 根据节点类型设置对应的 CSS 类名
        private void SetupClasses()
        {
            if (node is ActionNode)
            {
                AddToClassList("action");
            }
            else if (node is CompositeNode)
            {
                AddToClassList("composite");
            }
            else if (node is DecoratorNode)
            {
                AddToClassList("decorator");
            }
            else if (node is RootNode)
            {
                AddToClassList("root");
            }
        }

        // 创建输入端口，只有 Root 节点没有输入
        private void CreateInputPorts()
        {
            if (node is ActionNode || node is CompositeNode || node is DecoratorNode)
            {
                input = new NodePort(Direction.Input, Port.Capacity.Single);
            }

            if (input != null)
            {
                input.portName = "";
                input.style.flexDirection = FlexDirection.Column;
                inputContainer.Add(input);
            }
        }

        // 创建输出端口，根据节点类型不同分为单输出/多输出/无输出
        private void CreateOutputPorts()
        {
            if (node is CompositeNode)
            {
                output = new NodePort(Direction.Output, Port.Capacity.Multi);
            }
            else if (node is DecoratorNode || node is RootNode)
            {
                output = new NodePort(Direction.Output, Port.Capacity.Single);
            }

            if (output != null)
            {
                output.portName = "";
                output.style.flexDirection = FlexDirection.ColumnReverse;
                outputContainer.Add(output);
            }
        }

        // 当节点位置变更时，同步更新数据模型中保存的位置
        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(node, "Behaviour Tree (Set Position)");
            node.position.x = newPos.xMin;
            node.position.y = newPos.yMin;
            EditorUtility.SetDirty(node); // 标记为已修改
        }

        // 当用户在编辑器中选中节点时，调用回调
        public override void OnSelected()
        {
            base.OnSelected();
            if (OnNodeSelected != null)
            {
                OnNodeSelected.Invoke(this);
            }
        }

        // Composite 节点可以有多个子节点，进行排序以便在 UI 中更有逻辑
        public void SortChildren()
        {
            if (node is CompositeNode composite)
            {
                composite.children.Sort(SortByHorizontalPosition);
            }
        }

        // 比较两个子节点的横坐标，用于排序
        private int SortByHorizontalPosition(Node left, Node right)
        {
            return left.position.x < right.position.x ? -1 : 1;
        }

        // 在运行时更新节点状态样式（运行中/成功/失败）
        public void UpdateState()
        {
            RemoveFromClassList("running");
            RemoveFromClassList("failure");
            RemoveFromClassList("success");

            if (Application.isPlaying)
            {
                switch (node.state)
                {
                    case Node.State.Running:
                        if (node.started)
                        {
                            AddToClassList("running");
                        }
                        break;
                    case Node.State.Failure:
                        AddToClassList("failure");
                        break;
                    case Node.State.Success:
                        AddToClassList("success");
                        break;
                }
            }
        }
    }
}
