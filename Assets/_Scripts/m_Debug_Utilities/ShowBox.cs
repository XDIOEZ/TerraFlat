using UnityEngine;
using UnityEngine.Tilemaps;

public class ShowBox : MonoBehaviour
{
    // �ڼ��������ʾ���ɱ༭�ı���
    [SerializeField]
    private int minX = -100;
    [SerializeField]
    private int maxX = 99;
    [SerializeField]
    private int minY = -100;
    [SerializeField]
    private int maxY = 99;
    [ContextMenu("CutTiles")]
    public void CutTiles()
    {
        Tilemap tilemap = GetComponent<Tilemap>();
        if (tilemap == null)
        {
            Debug.LogError("Tilemap component not found!");
            return;
        }

        // ��ȡ������Ƭ��λ�÷�Χ
        BoundsInt bounds = tilemap.cellBounds;

        // �������п��ܰ�����Ƭ��λ��
        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (tilemap.HasTile(pos))
            {
                // ����Ƿ���Ŀ��������
                bool shouldKeep = pos.x >= minX && pos.x <= maxX
                               && pos.y >= minY && pos.y <= maxY;

                // ɾ�����������Ƭ
                if (!shouldKeep)
                {
                    tilemap.SetTile(pos, null);
                }
            }
        }

        Debug.Log("��Ƭ�������");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        // ���Ƽ�������
        Gizmos.DrawWireCube(transform.position, new Vector3(maxX - minX, maxY - minY, 0));
    }
}
