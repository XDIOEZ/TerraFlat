using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheKiwiCoder
{
    public partial class BehaviourTreeView : GraphView
    {
        // ��¼��ǰ����� contentViewContainer �е�λ�ã�����ճ��
        public new class UxmlFactory : UxmlFactory<BehaviourTreeView, GraphView.UxmlTraits> { }
        private Vector2 lastMouseContentPos;
        public Action<NodeView> OnNodeSelected;
        private BehaviourTree tree;
        private BehaviourTreeSettings settings;

        // �����嵥�������ڿ�������ճ��
        private static class Clipboard
        {
            public static List<Node> Nodes = new List<Node>();
            public static Dictionary<string, List<string>> Connections = new Dictionary<string, List<string>>();
            public static BehaviourTree SourceTree;
        }

        public struct ScriptTemplate
        {
            public TextAsset templateFile;
            public string defaultFileName;
            public string subFolder;
        }


        private void CopyToClipboard(List<NodeView> originals)
        {
            Clipboard.Nodes.Clear();
            Clipboard.Connections.Clear();
            Clipboard.SourceTree = tree;

            // ����ӳ��� GUID ���½ڵ�
            var cloneMap = new Dictionary<string, Node>();

            foreach (var view in originals)
            {
                var clone = tree.CloneANode(view.node);

                // �ؼ�����������ӽڵ�����
                tree.RemoveAllChildren(clone);

                cloneMap[view.node.guid] = clone;
                Clipboard.Nodes.Add(clone);
            }


            // ��¼���ӹ�ϵ���� GUID��
            foreach (var view in originals)
            {
                var originalGuid = view.node.guid;
                if (!cloneMap.ContainsKey(originalGuid)) continue;

                var clone = cloneMap[originalGuid];
                var originalChildren = BehaviourTree.GetChildren(view.node);
                var newChildrenGuids = originalChildren
                    .Where(c => originals.Any(o => o.node == c))
                    .Select(c => cloneMap[c.guid].guid)
                    .ToList();

                Clipboard.Connections[clone.guid] = newChildrenGuids;
            }

            Debug.Log($"Copied {Clipboard.Nodes.Count} cloned nodes to clipboard from tree '{tree.name}'");
        }

        private void PasteFromClipboard(Vector2 pastePosition)
        {
            if (Clipboard.Nodes == null || Clipboard.Nodes.Count == 0)
                return;

            Undo.RegisterCompleteObjectUndo(tree, "Paste Nodes");

            var map = new Dictionary<string, Node>();
            float minX = Clipboard.Nodes.Min(n => n.position.x);
            float minY = Clipboard.Nodes.Min(n => n.position.y);
            float offsetX = pastePosition.x - minX;
            float offsetY = pastePosition.y - minY;

            // ʵ�������Ƴ�������
            foreach (var orig in Clipboard.Nodes)
            {
                var clone = tree.CloneNode(orig);
                clone.position = new Vector2(orig.position.x + offsetX, orig.position.y + offsetY);

                // ���ԭʼ�ӽڵ�ṹ���������ӻ���
                tree.RemoveAllChildren(clone);

                map[orig.guid] = clone;
                CreateNodeView(clone);
            }

            // �ؽ�����
            foreach (var kv in Clipboard.Connections)
            {
                if (!map.ContainsKey(kv.Key))
                    continue;

                var parentClone = map[kv.Key];
                foreach (var childGuid in kv.Value)
                {
                    if (!map.ContainsKey(childGuid))
                        continue;

                    var childClone = map[childGuid];

                    tree.AddChild(parentClone, childClone);

                    // �ؽ�����
                    var pv = FindNodeView(parentClone);
                    var cv = FindNodeView(childClone);
                    if (pv != null && cv != null)
                    {
                        var edge = pv.output.ConnectTo(cv.input);
                        AddElement(edge);
                    }
                }
            }

            // ѡ��ճ�����½ڵ�
            ClearSelection();
            foreach (var clone in map.Values)
            {
                var nv = FindNodeView(clone);
                if (nv != null)
                    AddToSelection(nv);
            }

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();

            Debug.Log($"Pasted {map.Count} nodes into tree '{tree.name}'");
        }



        public ScriptTemplate[] scriptFileAssets = {
            new ScriptTemplate {
                templateFile = BehaviourTreeSettings.GetOrCreateSettings().scriptTemplateActionNode,
                defaultFileName = "NewActionNode.cs",
                subFolder = "Actions"
            },
            new ScriptTemplate {
                templateFile = BehaviourTreeSettings.GetOrCreateSettings().scriptTemplateCompositeNode,
                defaultFileName = "NewCompositeNode.cs",
                subFolder = "Composites"
            },
            new ScriptTemplate {
                templateFile = BehaviourTreeSettings.GetOrCreateSettings().scriptTemplateDecoratorNode,
                defaultFileName = "NewDecoratorNode.cs",
                subFolder = "Decorators"
            },
        };

        public BehaviourTreeView()
        {
            settings = BehaviourTreeSettings.GetOrCreateSettings();
            Insert(0, new GridBackground());
            AddManipulators();
            styleSheets.Add(settings.behaviourTreeStyle);
            Undo.undoRedoPerformed += OnUndoRedo;

            // ֧�ּ��̿�ݼ�
            focusable = true;
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            RegisterCallback<PointerMoveEvent>(e =>
            {
                lastMouseContentPos = this.ChangeCoordinatesTo(contentViewContainer, e.localPosition);
            });
            void AddManipulators()
            {
                this.AddManipulator(new ContentZoomer());
                this.AddManipulator(new ContentDragger());
                this.AddManipulator(new DoubleClickSelection());
                this.AddManipulator(new SelectionDragger());
                this.AddManipulator(new RectangleSelector());
            }
        }

        #region ���̿�ݼ�

        private void OnKeyDown(KeyDownEvent e)
        {
            bool ctrl = Application.platform == RuntimePlatform.OSXEditor ? e.commandKey : e.ctrlKey;
            if (!ctrl) return;

            // Ctrl+C ����
            if (e.keyCode == KeyCode.C)
            {
                var selected = selection.OfType<NodeView>().ToList();
                if (selected.Any())
                {
                    CopyToClipboard(selected);
                    e.StopPropagation();
                }
            }
            // Ctrl+V ճ��
            else if (e.keyCode == KeyCode.V)
            {
                if (Clipboard.Nodes.Any())
                {
                    PasteFromClipboard(lastMouseContentPos);
                    e.StopPropagation();
                }
            }
            // Ctrl+X ���У����� + ɾ����
           
            // Ctrl+A ȫѡ
            else if (e.keyCode == KeyCode.A)
            {
                ClearSelection();
                foreach (var node in nodes.OfType<NodeView>())
                {
                    AddToSelection(node);
                }
                e.StopPropagation();
            }
        }
        #endregion


        private void OnUndoRedo()
        {
            PopulateView(tree);
            AssetDatabase.SaveAssets();
        }

        public NodeView FindNodeView(Node node)
        {
            return GetNodeByGuid(node.guid) as NodeView;
        }

        internal void PopulateView(BehaviourTree tree)
        {
            this.tree = tree;

            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements.ToList());
            graphViewChanged += OnGraphViewChanged;

            if (tree.rootNode == null)
            {
                tree.rootNode = tree.CreateNode(typeof(RootNode)) as RootNode;
                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssets();
            }

            // �����ڵ���ͼ
            tree.nodes.ForEach(n => CreateNodeView(n));

            // Create edges������Ӹ� null ��飩
            tree.nodes.ForEach(n => {
                var children = BehaviourTree.GetChildren(n);
                children.ForEach(c => {
                    if (n == null || c == null) return;

                    NodeView parentView = FindNodeView(n);
                    NodeView childView = FindNodeView(c);

                    if (parentView == null || childView == null) return;

                    Edge edge = parentView.output.ConnectTo(childView.input);
                    AddElement(edge);
                });
            });

        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList().Where(endPort =>
                endPort.direction != startPort.direction &&
                endPort.node != startPort.node).ToList();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.elementsToRemove != null)
            {
                graphViewChange.elementsToRemove.ForEach(elem => {
                    NodeView nodeView = elem as NodeView;
                    if (nodeView != null)
                    {
                        tree.DeleteNode(nodeView.node);
                    }

                    Edge edge = elem as Edge;
                    if (edge != null)
                    {
                        NodeView parentView = edge.output.node as NodeView;
                        NodeView childView = edge.input.node as NodeView;
                        tree.RemoveChild(parentView.node, childView.node);
                    }
                });
            }

            if (graphViewChange.edgesToCreate != null)
            {
                graphViewChange.edgesToCreate.ForEach(edge => {
                    NodeView parentView = edge.output.node as NodeView;
                    NodeView childView = edge.input.node as NodeView;
                    tree.AddChild(parentView.node, childView.node);
                });
            }

            nodes.ForEach((n) => {
                NodeView view = n as NodeView;
                view.SortChildren();
            });

            return graphViewChange;
        }
        #region �Ҽ��˵�
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            // ����� GraphView �е�λ��
            Vector2 nodePosition = this.ChangeCoordinatesTo(contentViewContainer, evt.localMousePosition);

            // ��Ӵ����ű��ľ�̬�˵���
            evt.menu.AppendAction("Create Script.../New Action Node", (a) => CreateNewScript(scriptFileAssets[0]));
            evt.menu.AppendAction("Create Script.../New Composite Node", (a) => CreateNewScript(scriptFileAssets[1]));
            evt.menu.AppendAction("Create Script.../New Decorator Node", (a) => CreateNewScript(scriptFileAssets[2]));
            evt.menu.AppendSeparator();

            // �Զ���� Action �ڵ����Ͳ˵�
            AddNodeTypeMenuItems<ActionNode>( evt, nodePosition);

            // �Զ���� Composite �ڵ����Ͳ˵�
            AddNodeTypeMenuItems<CompositeNode>(evt, nodePosition);

            // �Զ���� Decorator �ڵ����Ͳ˵�
            AddNodeTypeMenuItems<DecoratorNode>(evt, nodePosition);
        }

        private void AddNodeTypeMenuItems<T>(ContextualMenuPopulateEvent evt, Vector2 position)
        {
            var types = TypeCache.GetTypesDerivedFrom<T>()
                                 .OrderBy(type =>
                                 {
                                     var attr = type.GetCustomAttribute<NodeMenuAttribute>();
                                     return attr?.Path ?? type.Name;
                                 });

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<NodeMenuAttribute>();
                if (attr != null)
                {
                    evt.menu.AppendAction(attr.Path, (a) => CreateNode(type, position));
                }
                else
                {
                    evt.menu.AppendAction($"{typeof(T).Name}/{type.Name}", (a) => CreateNode(type, position));
                }
            }
        }
        #endregion

        private void CloneSelectedNodes(List<NodeView> originals)
        {
            Undo.RegisterCompleteObjectUndo(tree, "Clone Selected Nodes");

            Dictionary<Node, Node> map = new Dictionary<Node, Node>();

            foreach (var view in originals)
            {
                var clone = tree.CloneNode(view.node);
                clone.position = view.node.position + new Vector2(30, 30);
                CreateNodeView(clone);
                map[view.node] = clone;
            }

            foreach (var view in originals)
            {
                var parent = view.node;
                if (!map.ContainsKey(parent)) continue;

                var cloneParent = map[parent];
                var children = BehaviourTree.GetChildren(parent);

                foreach (var child in children)
                {
                    if (map.ContainsKey(child))
                    {
                        var cloneChild = map[child];
                        tree.AddChild(cloneParent, cloneChild);

                        var parentView = FindNodeView(cloneParent);
                        var childView = FindNodeView(cloneChild);
                        var edge = parentView.output.ConnectTo(childView.input);
                        AddElement(edge);
                    }
                }
            }

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
        }


        /// <summary>
        /// ��¡ָ���Ľڵ㣨���������ӣ�
        /// </summary>
        private void CloneNode(NodeView original)
        {
            Undo.RegisterCompleteObjectUndo(tree, "Clone Node");

            var clone = tree.CloneNode(original.node);
            clone.position = original.node.position + new Vector2(30, 30);
            var cloneView = CreateNodeView(clone);

            ClearSelection();
            AddToSelection(cloneView);

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
        }


        void SelectFolder(string path)
        {
            if (path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);

            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object));
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        void CreateNewScript(ScriptTemplate template)
        {
            SelectFolder($"{settings.newNodeBasePath}/{template.subFolder}");
            var templatePath = AssetDatabase.GetAssetPath(template.templateFile);
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(templatePath, template.defaultFileName);
        }

        void CreateNode(System.Type type, Vector2 position)
        {
            Node node = tree.CreateNode(type);
            node.position = position;
            CreateNodeView(node);
        }


        // �޸� CreateNodeView ���� NodeView �Ա�����
        private NodeView CreateNodeView(Node node)
        {
            var nodeView = new NodeView(node);
            nodeView.OnNodeSelected = OnNodeSelected;
            AddElement(nodeView);
            return nodeView;
        }


        public void UpdateNodeStates()
        {
            nodes.ForEach(n => {
                NodeView view = n as NodeView;
                view.UpdateState();
            });
        }
    }
}
