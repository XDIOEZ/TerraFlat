// �½��ļ���EnvironmentInfoDisplay.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// ������Ϣ��ʾ�࣬����ʵʱ��ʾ�����ͣλ�õĻ�������
/// </summary>
public class EnvironmentInfoDisplay : MonoBehaviour
{
    #region �ֶκ�����
    
    [Header("��ʾ����")]
    public KeyCode toggleKey = KeyCode.F3;
    public Vector2 panelSize = new Vector2(300, 150);
    public Vector2 offset = new Vector2(20, 20);
    
    [Header("Prefab����")]
    public GameObject hoverIndicatorPrefab; // ��ָͣʾ��Prefab
    private GameObject hoverIndicatorInstance; // ��ָͣʾ��ʵ��
    
    [Header("��ʽ����")]
    public Color backgroundColor = new Color(0, 0, 0, 0.7f);
    public Color textColor = Color.white;
    public int fontSize = 12;
    
    // ����
    private RandomMapGenerator mapGenerator;
    private Camera mainCamera;
    private Grid mapGrid;
    private Tilemap targetTilemap;
    private Map map;
    private List<BiomeData> biomes;
    
    // ��ʾ����
    private bool isVisible = true;
    private Vector3 mouseWorldPos = Vector3.zero;
    private Vector2 mouseScreenPos = Vector2.zero;
    private Vector2Int hoveredGridPos = Vector2Int.zero;
    private EnvironmentFactors hoveredEnvFactors = new EnvironmentFactors();
    private string hoveredBiomeName = "δ֪";
    private TileData hoveredTileData = null;
    private bool isValidPosition = false;
    
    // GUI��ʽ
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private Texture2D backgroundTexture;
    private bool stylesCreated = false;
    
    #endregion

    #region Unity��������

    private void Awake()
    {
        // ��ȡ��Ҫ������
        mapGenerator = GetComponent<RandomMapGenerator>();
        map = GetComponent<Map>();
        if (mapGenerator != null)
        {
            map = mapGenerator.map;
            mapGrid = mapGenerator.mapGrid;
            targetTilemap = mapGenerator.targetTilemap;
            biomes = mapGenerator.biomes;
        }
        
        // ������ָͣʾ��ʵ��
        CreateHoverIndicator();
    }

    private void Update()
    {
        // �л���ʾ״̬
        if (Input.GetKeyDown(toggleKey))
        {
            isVisible = !isVisible;
        }
        
        // ���������Ļλ��
        mouseScreenPos = Input.mousePosition;
        
        // �������λ����Ϣ
        UpdateMouseInfo();
        
        // ������ָͣʾ��λ��
        UpdateHoverIndicator();
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !isVisible) return;
        
        // ȷ��GUI��ʽ�Ѵ���
        if (!stylesCreated)
        {
            CreateGUIStyles();
            stylesCreated = true;
        }
        
        DrawInfoPanel();
    }

    private void OnDrawGizmos()
    {
        // ����ʹ��OnDrawGizmos������Prefabʵ��
    }

    #endregion

    #region ���Ĺ���

    /// <summary>
    /// ������ָͣʾ��
    /// </summary>
    private void CreateHoverIndicator()
    {
        if (hoverIndicatorPrefab != null && mapGrid != null)
        {
            hoverIndicatorInstance = Instantiate(hoverIndicatorPrefab);
            hoverIndicatorInstance.SetActive(false); // ��ʼ����
        }
    }

    /// <summary>
    /// ������ָͣʾ��λ��
    /// </summary>
    private void UpdateHoverIndicator()
    {
        if (hoverIndicatorInstance == null) return;
        
        if (isValidPosition && isVisible && mapGrid != null)
        {
            hoverIndicatorInstance.SetActive(true);
            
            // ������������뵽����
            Vector3Int cellPos = mapGrid.WorldToCell(mouseWorldPos);
            Vector3 cellWorldPos = mapGrid.CellToWorld(cellPos);
            
            // ����λ�õ���������
            cellWorldPos.x += 0.5f;
            cellWorldPos.y += 0.5f;
            cellWorldPos.z = 0; // ȷ�������ϲ���ʾ
            
            hoverIndicatorInstance.transform.position = cellWorldPos;
        }
        else
        {
            hoverIndicatorInstance.SetActive(false);
        }
    }

/// <summary>
/// ��ȡ�������
/// </summary>
private Camera GetMainCamera()
{
    if (mainCamera == null)
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = GameObject.FindGameObjectWithTag("MainCamera")?.GetComponent<Camera>();
        }
        if (mainCamera == null)
        {
            // ������Ҳ��������Դ��Ӷ����ȡ
            Camera[] childCameras = GetComponentsInChildren<Camera>(true);
            if (childCameras != null && childCameras.Length > 0)
            {
                mainCamera = childCameras[0];
            }
        }
        if (mainCamera == null)
        {
            // ����Բ��ҳ��������е������
            Camera[] cameras = FindObjectsOfType<Camera>();
            if (cameras.Length > 0)
            {
                mainCamera = cameras[0];
            }
        }
    }
    return mainCamera;
}

/// <summary>
/// �������λ�õĻ�����Ϣ
/// </summary>
private void UpdateMouseInfo()
{
    // ��ȡ�����
    Camera cam = GetMainCamera();
    
    // �����û��mapGrid��targetTilemap�����Դ��Ӷ����ȡ
    if (mapGrid == null)
    {
        mapGrid = GetComponentInChildren<Grid>(true);
    }
    
    if (targetTilemap == null)
    {
        Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(true);
        if (childTilemaps != null && childTilemaps.Length > 0)
        {
            targetTilemap = childTilemaps[0];
        }
    }
    
    // ǰ�ü��
    if (mapGrid == null || targetTilemap == null || cam == null)
        return;

    // 1. �����Ļ���� �� ��������
    Vector3 mouseScreenPos3D = new Vector3(mouseScreenPos.x, mouseScreenPos.y, 0);
    mouseScreenPos3D.z = Mathf.Abs(cam.transform.position.z - targetTilemap.transform.position.z);
    mouseWorldPos = cam.ScreenToWorldPoint(mouseScreenPos3D);
    mouseWorldPos.z = 0;

    // 2. �������� �� Tilemap��������
    Vector3Int cellPos = mapGrid.WorldToCell(mouseWorldPos);
    Vector2Int gridPos = new Vector2Int(cellPos.x, cellPos.y);

    // 3. ���ø����Ƿ����Tile
    if (!targetTilemap.HasTile(cellPos))
    {
        isValidPosition = false;
        return;
    }

    // 4. ���㱾������
    Vector2Int localGridPos = gridPos - map.Data.position;

    // 5. ����Ƿ�����Ч��Χ��
    if (mapGenerator == null || mapGenerator.EnvFactorsGrid == null ||
        localGridPos.x < 0 || localGridPos.x >= mapGenerator.EnvFactorsGrid.GetLength(0) ||
        localGridPos.y < 0 || localGridPos.y >= mapGenerator.EnvFactorsGrid.GetLength(1))
    {
        isValidPosition = false;
        return;
    }

    // 6. ��ȡ������Ϣ
    isValidPosition = true;
    hoveredGridPos = gridPos;
    hoveredEnvFactors = mapGenerator.EnvFactorsGrid[localGridPos.x, localGridPos.y];
    hoveredTileData = map.GetTile(gridPos);
    
    // ƥ������Ⱥϵ
    hoveredBiomeName = "δ֪";
    if (biomes != null)
    {
        foreach (var biome in biomes)
        {
            if (biome != null && biome.IsEnvironmentValid(hoveredEnvFactors))
            {
                hoveredBiomeName = biome.BiomeName;
                break;
            }
        }
    }
}

    /// <summary>
    /// ������Ϣ���
    /// </summary>
    private void DrawInfoPanel()
    {
        if (!isValidPosition) return;
        
        // ����GUIλ�ã�������꣬����������Ļ�ڣ�
        // ע�⣺GUI��Y���Ǵ��ϵ��µģ�����������Y���Ǵ��µ��ϵ�
        float guiX = Mathf.Clamp(mouseScreenPos.x + offset.x, 0, Screen.width - panelSize.x);
        // ת��Y���꣺Screen.height - mouseScreenPos.y �����Y����ת��ΪGUI����ϵ
        float guiY = Mathf.Clamp(Screen.height - mouseScreenPos.y - panelSize.y - offset.y, 0, Screen.height - panelSize.y);
        
        // ������Ϣ���
        GUILayout.BeginArea(new Rect(guiX, guiY, panelSize.x, panelSize.y), boxStyle);
        
        GUILayout.Label($"<b>������Ϣ</b>", labelStyle);
        GUILayout.Label($"����: ({hoveredGridPos.x}, {hoveredGridPos.y})", labelStyle);
        GUILayout.Label($"����Ⱥϵ: {hoveredBiomeName}", labelStyle);
        GUILayout.Label($"�¶�: {hoveredEnvFactors.Temperature:F2} | ʪ��: {hoveredEnvFactors.Humidity:F2}", labelStyle);
        GUILayout.Label($"��ˮ��: {hoveredEnvFactors.Precipitation:F2} | ��̶�: {hoveredEnvFactors.Solidity:F2}", labelStyle);
        GUILayout.Label($"�߶�: {hoveredEnvFactors.Hight:F2}", labelStyle);
        if (hoveredTileData != null)
        {
            GUILayout.Label($"��Ƭ: {hoveredTileData.Name_ItemName}", labelStyle);
        }
        
        GUILayout.Label($"�� {toggleKey} ���л���ʾ", labelStyle);
        
        GUILayout.EndArea();
    }

    /// <summary>
    /// ����GUI��ʽ
    /// </summary>
    private void CreateGUIStyles()
    {
        // ������������
        backgroundTexture = new Texture2D(2, 2);
        Color[] colors = new Color[4];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = backgroundColor;
        }
        backgroundTexture.SetPixels(colors);
        backgroundTexture.Apply();
        
        // ����Box��ʽ
        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = backgroundTexture;
        
        // ����Label��ʽ
        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = textColor;
        labelStyle.fontSize = fontSize;
        labelStyle.richText = true;
    }

    #endregion

    #region ��������

    /// <summary>
    /// ��ʾ��Ϣ���
    /// </summary>
    public void Show()
    {
        isVisible = true;
    }

    /// <summary>
    /// ������Ϣ���
    /// </summary>
    public void Hide()
    {
        isVisible = false;
    }

    /// <summary>
    /// �л���ʾ״̬
    /// </summary>
    public void Toggle()
    {
        isVisible = !isVisible;
    }

    /// <summary>
    /// �����л���
    /// </summary>
    public void SetToggleKey(KeyCode key)
    {
        toggleKey = key;
    }

    #endregion

    #region ����

    private void OnDestroy()
    {
        if (backgroundTexture != null)
        {
            Destroy(backgroundTexture);
        }
        
        if (hoverIndicatorInstance != null)
        {
            Destroy(hoverIndicatorInstance);
        }
    }

    #endregion
}