using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ITeam
{
    string TeamID { get; set; }

    Dictionary<string, RelationType> Relations { get; set; }

    void AddRelation(string otherTeamID, RelationType relationType)
    {
        if (Relations == null)
            Relations = new Dictionary<string, RelationType>();

        Relations[otherTeamID] = relationType;
    }

    void RemoveRelation(string otherTeamID)
    {
        Relations?.Remove(otherTeamID);
    }

    void ChangeRelation(string otherTeamID, RelationType relationType)
    {
        if (Relations != null && Relations.ContainsKey(otherTeamID))
        {
            Relations[otherTeamID] = relationType;
        }
    }

    RelationType CheckRelation(string otherTeamID)
    {
        if (Relations != null && Relations.TryGetValue(otherTeamID, out var relation))
        {
            return relation;
        }
        return RelationType.Neutral;
    }
}


public enum RelationType
{
    Neutral,  // ������Ĭ�ϣ�
    Ally,     // ����
    Enemy     // ����
}

