#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Callbacks;
using System.IO;

namespace TheKiwiCoder {

    public class BehaviourTreeEditor : EditorWindow {

        BehaviourTreeView treeView;
        BehaviourTree tree;
        InspectorView inspectorView;
        IMGUIContainer blackboardView;
        ToolbarMenu toolbarMenu;
        TextField treeNameField;
        TextField locationPathField;
        Button createNewTreeButton;
        VisualElement overlay;
        BehaviourTreeSettings settings;

        SerializedObject treeObject;
        SerializedProperty blackboardProperty;

        [MenuItem("TheKiwiCoder/BehaviourTreeEditor ...")]
        public static void OpenWindow() {
            BehaviourTreeEditor wnd = GetWindow<BehaviourTreeEditor>();
            wnd.titleContent = new GUIContent("BehaviourTreeEditor");
            wnd.minSize = new Vector2(800, 600);
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line) {
            if (Selection.activeObject is BehaviourTree) {
                OpenWindow();
                return true;
            }
            return false;
        }

        List<T> LoadAssets<T>() where T : UnityEngine.Object {
            string[] assetIds = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            List<T> assets = new List<T>();
            foreach (var assetId in assetIds) {
                string path = AssetDatabase.GUIDToAssetPath(assetId);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                assets.Add(asset);
            }
            return assets;
        }

        public void CreateGUI() {

            settings = BehaviourTreeSettings.GetOrCreateSettings();

            // Each editor window contains a root VisualElement object
            VisualElement root = rootVisualElement;

            // Import UXML
            var visualTree = settings.behaviourTreeXml;
            visualTree.CloneTree(root);

            // A stylesheet can be added to a VisualElement.
            // The style will be applied to the VisualElement and all of its children.
            var styleSheet = settings.behaviourTreeStyle;
            root.styleSheets.Add(styleSheet);

            // Main treeview
            treeView = root.Q<BehaviourTreeView>();
            treeView.OnNodeSelected = OnNodeSelectionChanged;

            // Inspector View
            inspectorView = root.Q<InspectorView>();

            // Blackboard view
            blackboardView = root.Q<IMGUIContainer>();
            blackboardView.onGUIHandler = () => {
                if (treeObject != null && treeObject.targetObject != null) {
                    treeObject.Update();
                    EditorGUILayout.PropertyField(blackboardProperty);
                    treeObject.ApplyModifiedProperties();
                }
            };

            // Toolbar assets menu
            toolbarMenu = root.Q<ToolbarMenu>();
            var behaviourTrees = LoadAssets<BehaviourTree>();
            behaviourTrees.ForEach(tree => {
                toolbarMenu.menu.AppendAction($"{tree.name}", (a) => {
                    Selection.activeObject = tree;
                });
            });
            toolbarMenu.menu.AppendSeparator();
            toolbarMenu.menu.AppendAction("新建行为树", (a) => CreateNewTree("NewBehaviourTree"));

            // New Tree Dialog
            treeNameField = root.Q<TextField>("TreeName");
            locationPathField = root.Q<TextField>("LocationPath");
            overlay = root.Q<VisualElement>("Overlay");
            createNewTreeButton = root.Q<Button>("CreateButton");
            createNewTreeButton.clicked += () => CreateNewTree(treeNameField.value);

            if (tree == null) {
                OnSelectionChange();
            } else {
                SelectTree(tree);
            }
        }

        private void OnEnable() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable() {

            if (tree != null)
            {
                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssets();
            }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange obj) {
            switch (obj) {
                case PlayModeStateChange.EnteredEditMode:
                    OnSelectionChange();
                    break;
                case PlayModeStateChange.ExitingEditMode:
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    OnSelectionChange();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    break;
            }
        }

        private void OnSelectionChange() {
            EditorApplication.delayCall += () => {
                BehaviourTree tree = Selection.activeObject as BehaviourTree;
                if (!tree) {
                    if (Selection.activeGameObject) {
                        BehaviourTreeRunner runner = Selection.activeGameObject.GetComponent<BehaviourTreeRunner>();
                        if (runner) {
                            tree = runner.tree;
                        }
                    }
                }

                SelectTree(tree);
            };
        }

        void SelectTree(BehaviourTree newTree) {

            if (treeView == null) {
                return;
            }

            if (!newTree) {
                return;
            }

            this.tree = newTree;

            overlay.style.visibility = Visibility.Hidden;

            if (Application.isPlaying) {
                treeView.PopulateView(tree);
            } else {
                treeView.PopulateView(tree);
            }

            
            treeObject = new SerializedObject(tree);
            blackboardProperty = treeObject.FindProperty("blackboard");

            EditorApplication.delayCall += () => {
                treeView.FrameAll();
            };
        }

        void OnNodeSelectionChanged(NodeView node) {
            inspectorView.UpdateSelection(node);
        }

        private void OnInspectorUpdate() {
            treeView?.UpdateNodeStates();
        }


        void CreateNewTree(string assetName)
        {
            // 获取 Project 窗口当前选中的文件夹路径
            string folderPath = GetSelectedPathOrFallback();

            // 如果没有选中文件夹，则默认到 "Assets/_BehaviourTrees"
            if (string.IsNullOrEmpty(folderPath) || folderPath == "Assets")
            {
                folderPath = "Assets/_BehaviourTrees";
            }

            // 如果文件夹不存在，则逐层创建
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] parts = folderPath.Split('/');
                string current = "Assets";
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = $"{current}/{parts[i]}";
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }

            // 最终 asset 路径
            string path = Path.Combine(folderPath, $"{assetName}.asset");

            // 如果已有同名文件，则警告
            if (AssetDatabase.LoadAssetAtPath<BehaviourTree>(path) != null)
            {
                Debug.LogWarning($"Asset already exists at: {path}");
                return;
            }

            Debug.Log($"Creating new BehaviourTree at: {path}");

            BehaviourTree tree = ScriptableObject.CreateInstance<BehaviourTree>();
            tree.name = assetName;  // 注意这里是 assetName，不是 treeNameField.value，避免null

            AssetDatabase.CreateAsset(tree, path);
            AssetDatabase.SaveAssets();

            // 自动选中新建的资源
            Selection.activeObject = tree;
            EditorGUIUtility.PingObject(tree);
        }

        // 工具方法：优先获取选中文件夹路径，否则返回默认
        static string GetSelectedPathOrFallback()
        {
            string path = "Assets";

            // 如果选中了某个资源
            foreach (var obj in Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets))
            {
                path = AssetDatabase.GetAssetPath(obj);
                if (File.Exists(path))
                {
                    path = Path.GetDirectoryName(path);
                }
                break;
            }
            return path.Replace("\\", "/"); // 保证路径分隔符
        }

    }
}
#endif