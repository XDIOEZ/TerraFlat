using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace TheKiwiCoder
{
    [UxmlElement]
    public partial class InspectorView : VisualElement
    {
        private Editor editor;

        public InspectorView()
        {
            // 构造函数中可以添加初始化代码
        }

        internal void UpdateSelection(NodeView nodeView)
        {
            Clear();

            if (editor != null)
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }

            editor = Editor.CreateEditor(nodeView.node);
            IMGUIContainer container = new IMGUIContainer(() => {
                if (editor != null && editor.target != null)
                {
                    editor.OnInspectorGUI();
                }
            });
            Add(container);
        }
    }
}
