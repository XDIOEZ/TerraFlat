using System.Collections.Generic;
using UnityEngine;
using TheKiwiCoder;

/// <summary>
/// �������Ŀ��λ�ã�������ͬ��������
/// </summary>
public class RandomPosition : ActionNode
{
    #region �ֶ�

    public Vector2 min = Vector2.one * -10;
    public Vector2 max = Vector2.one * 10;
    public bool TrueRandom;

    [Header("ͬ����������")]
    public float sameTypeAttraction = 3f;

    private Vector3 lastValidPosition = Vector3.zero;
    private const int maxTries = 5;

    // �����б����GC����
    private readonly List<Vector2> sameTypePositions = new List<Vector2>(8);
    
    // Gizmos��ʾ���
    private Vector3 gizmoPosition = Vector3.zero;
    private bool showGizmo = false;

    #endregion

    #region ��������

    protected override void OnStart()
    {
        lastValidPosition = context.transform.position;
    }

    protected override void OnStop()
    {
        // �������
    }

    #endregion

    #region ��Ϊ�߼�

    protected override State OnUpdate()
    {
        Vector2 sameTypeDirection = GetSameTypeAverageDirection();

        if (!TrueRandom && context.tileEffectReceiver.Cache_map != null)
        {
            return HandleMapConstrainedMovement(sameTypeDirection);
        }
        else
        {
            return HandleFreeMovement(sameTypeDirection);
        }
    }

    #endregion

    #region ˽�з���

private State HandleMapConstrainedMovement(Vector2 sameTypeDirection)
{
    Vector3 chosenPosition = Vector3.zero;
    bool found = false;
    int attempts = 0;
    const int maxAttempts = 5; // ���ӳ��Դ���

    Vector3 originalPosition = context.transform.position;

    while (attempts < maxAttempts)
    {
        Vector2 randomOffset = new Vector2(
            Random.Range(min.x, max.x),
            Random.Range(min.y, max.y)
        );

        // ��ӷ���ƫ��
        if (lastValidPosition != originalPosition)
        {
            Vector2 direction = ((Vector2)originalPosition - (Vector2)lastValidPosition).normalized;
            randomOffset += direction * 0.5f;
        }

        // ���ͬ������
        if (sameTypeDirection.sqrMagnitude > 0.001f)
        {
            randomOffset += sameTypeDirection * sameTypeAttraction;
        }

        // ���ɲ���λ��A
        Vector3 testPositionA = originalPosition + (Vector3)randomOffset;
        Vector2Int testPosIntA = Vector2Int.FloorToInt(testPositionA);

        // ���λ��A�Ƿ�Σ��
        if (IsDangerousTile(testPosIntA))
        {
            // λ��AΣ�գ�����Գ�λ��B
            Vector3 positionB = originalPosition - (Vector3)randomOffset;
            Vector2Int posIntB = Vector2Int.FloorToInt(positionB);

            // ���λ��B�Ƿ�ȫ
            if (!IsDangerousTile(posIntB))
            {
                chosenPosition = positionB;
                found = true;
                gizmoPosition = chosenPosition;
                showGizmo = true;
                break;
            }
            else
            {
                // λ��BҲΣ�գ�����λ��D (B-C����*2)
                Vector3 vectorBC = positionB - originalPosition;
                Vector3 positionD = positionB + vectorBC;
                Vector2Int posIntD = Vector2Int.FloorToInt(positionD);

                // ���λ��D�Ƿ�ȫ
                if (!IsDangerousTile(posIntD))
                {
                    chosenPosition = positionD;
                    found = true;
                    gizmoPosition = chosenPosition;
                    showGizmo = true;
                    break;
                }
                else
                {
                    // λ��DҲΣ�գ���Բ������������
                    float radius = vectorBC.magnitude;
                    bool circlePointFound = false;
                    
                    // ��������Բ�������
                    for (int i = 0; i < 3; i++)
                    {
                        // ����BΪԲ�ģ��뾶Ϊradius��Բ��������ɵ�
                        Vector2 randomInCircle = Random.insideUnitCircle * radius;
                        Vector3 positionEFG = positionB + (Vector3)randomInCircle;
                        Vector2Int posIntEFG = Vector2Int.FloorToInt(positionEFG);

                        if (!IsDangerousTile(posIntEFG))
                        {
                            chosenPosition = positionEFG;
                            found = true;
                            circlePointFound = true;
                            gizmoPosition = chosenPosition;
                            showGizmo = true;
                            break;
                        }
                    }

                    if (circlePointFound)
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            // λ��A��ȫ
            chosenPosition = testPositionA;
            found = true;
            gizmoPosition = chosenPosition;
            showGizmo = true;
            break;
        }

        attempts++;
    }

    // ���ǰ�����г��Զ�ʧ���ˣ�ʹ��һ��ǿ�Ƶ�����㣨�����Ƿ�Σ�գ�
    if (!found)
    {
        Vector2 finalOffset = new Vector2(
            Random.Range(min.x * 2, max.x * 2),
            Random.Range(min.y * 2, max.y * 2)
        );
        
        if (sameTypeDirection.sqrMagnitude > 0.001f)
            finalOffset += sameTypeDirection * sameTypeAttraction;

        chosenPosition = originalPosition + (Vector3)finalOffset;
        gizmoPosition = chosenPosition;
        showGizmo = true;
        Debug.Log("ʹ��ǿ�Ƶ����λ�ã�����Σ��");
    }

    SetTargetPosition(chosenPosition);
    return State.Success;
}

    // α���뷽�����ж��Ƿ�ΪΣ��tile
    private bool IsDangerousTile(Vector2Int tilePosition)
    {
        // ��ȡָ��λ�õ�TileData
        TileData tileData = context.tileEffectReceiver.Cache_map?.GetTile(tilePosition);
        if (tileData == null)
            return true; // ��λ����ΪΣ��
            
        // ���ͷ�ֵ�Ƿ���ڵ���5000
        return tileData.Penalty >= 5000;
    }

    private State HandleFreeMovement(Vector2 sameTypeDirection)
    {
        Vector2 randomOffset = new Vector2(
            Random.Range(min.x, max.x),
            Random.Range(min.y, max.y)
        );

        if (sameTypeDirection.sqrMagnitude > 0.001f)
            randomOffset += sameTypeDirection * sameTypeAttraction;

        Vector3 targetPosition = context.transform.position + (Vector3)randomOffset;
        // ����Gizmos��ʾ
        gizmoPosition = targetPosition;
        showGizmo = true;
        
        SetTargetPosition(targetPosition, randomOffset);
        return State.Success;
    }

    private void SetTargetPosition(Vector3 worldPosition, Vector2? relativeOffset = null)
    {
        context.mover.TargetPosition = worldPosition;
        blackboard.TargetPosition = relativeOffset ?? (worldPosition - context.transform.position);
        lastValidPosition = context.transform.position;
    }

    /// <summary>
    /// ����ͬ��ƽ������
    /// </summary>
    private Vector2 GetSameTypeAverageDirection()
    {
        sameTypePositions.Clear();

        if (context.itemDetector == null || context.item == null ||
            context.item.itemData == null || context.itemDetector.CurrentItemsInArea == null)
        {
            return Vector2.zero;
        }

        string selfId = context.item.itemData.IDName;

        foreach (var collider in context.itemDetector.CurrentItemsInArea)
        {
            if (collider == null || collider.transform == context.transform || collider.itemData == null)
                continue;

            if (collider.itemData.IDName == selfId)
                sameTypePositions.Add(collider.transform.position);
        }

        if (sameTypePositions.Count == 0) return Vector2.zero;

        Vector2 averagePosition = Vector2.zero;
        for (int i = 0; i < sameTypePositions.Count; i++)
        {
            averagePosition += sameTypePositions[i];
        }
        averagePosition /= sameTypePositions.Count;

        Vector2 direction = averagePosition - (Vector2)context.transform.position;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.zero;
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && context != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(context.transform.position, 5f); // ���滻Ϊʵ�ʼ�ⷶΧ
        }
    }
    
    public override void OnDrawGizmos()
    {
        if (showGizmo)
        {
            // ����ԭʼGizmos��ɫ
            Color originalColor = Gizmos.color;
            
            // ����Gizmos��ɫ
            Gizmos.color = Color.yellow;
            
            // ����һ��Բ�α��
            Gizmos.DrawWireSphere(gizmoPosition, 0.5f);
            
            // ����һ��X���
            float size = 0.3f;
            Gizmos.DrawLine(gizmoPosition + new Vector3(size, size, 0), gizmoPosition + new Vector3(-size, -size, 0));
            Gizmos.DrawLine(gizmoPosition + new Vector3(-size, size, 0), gizmoPosition + new Vector3(size, -size, 0));
            
            // ����һ���㣨ͨ������С��ʵ�֣�
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(gizmoPosition, 0.1f);
            
            // �ָ�ԭʼ��ɫ
            Gizmos.color = originalColor;
        }
    }

    #endregion
}