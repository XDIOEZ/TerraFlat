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

    // NodeView ����Ϊ���༭����ÿ���ڵ���ͼ�ν����ϵĿ��ӻ���ʾ
    public class NodeView : UnityEditor.Experimental.GraphView.Node
    {
        public Action<NodeView> OnNodeSelected; // �ڵ㱻ѡ��ʱ�Ļص�
        public Node node; // ��Ӧ������ģ��
        public Port input; // ����˿�
        public Port output; // ����˿�

        // ���캯�� - ����UXML���ֲ���ʼ���ڵ�
        public NodeView(Node node) : base(AssetDatabase.GetAssetPath(BehaviourTreeSettings.GetOrCreateSettings().nodeXml))
        {
            this.node = node;
            this.node.name = node.GetType().Name;
            this.title = node.name.Replace("(Clone)", "").Replace("Node", "");
            this.viewDataKey = node.guid;

            // ���ýڵ�λ��
            style.left = node.position.x;
            style.top = node.position.y;

            // ��ʼ���˿ں���ʽ
            CreateInputPorts();
            CreateOutputPorts();
            SetupClasses();
            SetupDataBinding();
        }

        // �����ݣ�ʹ UI Label ��ʾ�ڵ�������ͨ�� SerializedObject ʵ�����ݰ󶨣�
        private void SetupDataBinding()
        {
            Label descriptionLabel = this.Q<Label>("description"); // ��ѯ UXML �� id Ϊ description �� Label
            descriptionLabel.bindingPath = "description"; // ��·��
            descriptionLabel.Bind(new SerializedObject(node)); // �󶨵��ڵ����
        }

        // ���ݽڵ��������ö�Ӧ�� CSS ����
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

        // ��������˿ڣ�ֻ�� Root �ڵ�û������
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

        // ��������˿ڣ����ݽڵ����Ͳ�ͬ��Ϊ�����/�����/�����
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

        // ���ڵ�λ�ñ��ʱ��ͬ����������ģ���б����λ��
        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(node, "Behaviour Tree (Set Position)");
            node.position.x = newPos.xMin;
            node.position.y = newPos.yMin;
            EditorUtility.SetDirty(node); // ���Ϊ���޸�
        }

        // ���û��ڱ༭����ѡ�нڵ�ʱ�����ûص�
        public override void OnSelected()
        {
            base.OnSelected();
            if (OnNodeSelected != null)
            {
                OnNodeSelected.Invoke(this);
            }
        }

        // Composite �ڵ�����ж���ӽڵ㣬���������Ա��� UI �и����߼�
        public void SortChildren()
        {
            if (node is CompositeNode composite)
            {
                composite.children.Sort(SortByHorizontalPosition);
            }
        }

        // �Ƚ������ӽڵ�ĺ����꣬��������
        private int SortByHorizontalPosition(Node left, Node right)
        {
            return left.position.x < right.position.x ? -1 : 1;
        }

        // ������ʱ���½ڵ�״̬��ʽ��������/�ɹ�/ʧ�ܣ�
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
