using UnityEngine.UIElements;

namespace TheKiwiCoder
{
    public class SplitView : TwoPaneSplitView
    {
        public new class UxmlFactory : UxmlFactory<SplitView, UxmlTraits> { }

        public new class UxmlTraits : TwoPaneSplitView.UxmlTraits
        {
            // ����������ﶨ���Զ�������
        }
    }
}
