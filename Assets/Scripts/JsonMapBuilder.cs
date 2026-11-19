using System;
using System.Collections.Generic;
using UnityEngine;

// --- CÁC CLASS CHỨA DỮ LIỆU JSON ---
[Serializable]
public class MapCollection
{
    public MapData[] maps;
}

[Serializable]
public class MapData
{
    public string name;
    public string displayName;
    public float cellSize;
    public string[] tiles;
    public string[] objects;
}

// --- BUILDER CHÍNH ---
public class JsonMapBuilder : MonoBehaviour
{
    [Header("Mapping Assets")]
    public TileMappingSO tileMapping;
    public ObjectMappingSO objectMapping;

    [Header("Map Selection")]
    public string mapName = "VuongGoc";
    public string mapFile = "Maps/all_maps";

    [Header("Runtime Info (Read-Only)")]
    [SerializeField] private Vector3 spawnPosition;
    [SerializeField] private Vector3 targetPosition;

    public Vector3 SpawnPosition => spawnPosition;
    public Vector3 TargetPosition => targetPosition;

    // --- ỨNG VIÊN CHO SINH NGẪU NHIÊN ---
    // Toạ độ (local của Map) của mọi ô đường 'R' và mọi ô đỗ 'P'.
    // TrainingArea dùng 2 danh sách này để bốc ngẫu nhiên mỗi episode.
    [Header("Random spawn/target (Runtime Info)")]
    public List<Vector3> roadCells = new List<Vector3>();
    public List<Vector3> parkingCells = new List<Vector3>();

    [Header("Kích thước map (Runtime Info)")]
    public int mapRows;
    public int mapCols;
    public float mapCellSize;

    // Vùng hợp lệ của map trong không gian LOCAL. Dùng để kiểm tra xe ra ngoài biên.
    public Bounds GetLocalBounds()
    {
        float c = mapCellSize <= 0f ? 6f : mapCellSize;
        int r = mapRows <= 0 ? 9 : mapRows;
        int cc = mapCols <= 0 ? 9 : mapCols;

        Vector3 min = new Vector3(-c * 0.5f, -1f, -c * 0.5f);
        Vector3 max = new Vector3((cc - 0.5f) * c, 10f, (r - 0.5f) * c);

        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    void Awake()
    {
        BuildMap();
    }

    [ContextMenu("Build Map (2 Layers)")]
    public void BuildMap()
    {
        if (tileMapping == null || objectMapping == null)
        {
            Debug.LogError("[JsonMapBuilder] Chưa gán tileMapping / objectMapping!");
            return;
        }

        // 1. Đọc JSON
        TextAsset json = Resources.Load<TextAsset>(mapFile);
        if (json == null)
        {
            Debug.LogError($"[JsonMapBuilder] Không thấy Resources/{mapFile}.json");
            return;
        }
        
        MapCollection collection = JsonUtility.FromJson<MapCollection>(json.text);
        MapData map = Array.Find(collection.maps, m => m.name == mapName);
        
        if (map == null)
        {
            Debug.LogError($"[JsonMapBuilder] Không có map tên '{mapName}' trong file JSON!");
            return;
        }

        // 2. Xoá map cũ (nếu bấm Build lại)
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        // Xoá danh sách ứng viên cũ trước khi build lại
        roadCells.Clear();
        parkingCells.Clear();

        // 3. Lấy bảng Mapping (Dictionary)
        Dictionary<char, GameObject> tileDict = tileMapping.BuildLookup();
        Dictionary<char, GameObject> objDict = objectMapping.BuildLookup();

        // 4. Duyệt từng ô và tạo Prefab
        int rows = map.tiles.Length;
        for (int r = 0; r < rows; r++)
        {
            int cols = map.tiles[r].Length;
            string objRow = (r < map.objects.Length) ? map.objects[r] : "";

            for (int c = 0; c < cols; c++)
            {
                // Tính vị trí (cột là X, hàng là Z)
                Vector3 pos = new Vector3(c * map.cellSize, 0, (rows - 1 - r) * map.cellSize);

                // ----- LỚP 1: TILE -----
                               // ----- LỚP 1: TILE -----
                char tileChar = map.tiles[r][c];
                if (tileDict.TryGetValue(tileChar, out GameObject tilePrefab) && tilePrefab != null)
                {
                    GameObject tile = Instantiate(tilePrefab, transform);
                    tile.transform.localPosition = pos;
                    tile.name = $"Tile_{r}_{c}_{tileChar}";

                    // Ghi lại ô đường / ô đỗ để TrainingArea bốc ngẫu nhiên
                    if (tileChar == 'R') roadCells.Add(pos);
                    else if (tileChar == 'P') parkingCells.Add(pos);

                    // Tường / hàng rào 'H' → tag Obstacle để va chạm bị tính là fail
                    if (tileChar == 'H')
                        foreach (var col in tile.GetComponentsInChildren<Collider>(true))
                            col.gameObject.tag = "Obstacle";

                    // --- ĐOẠN CODE TÍNH SCALE FACTOR CHUẨN ---
                    if (r == 0 && c == 0) 
                    {
                        Renderer[] renderers = tile.GetComponentsInChildren<Renderer>();
                        if (renderers.Length > 0)
                        {
                            Bounds b = renderers[0].bounds;
                            foreach (var ren in renderers) b.Encapsulate(ren.bounds);
                            
                            // Kích thước hiện tại của viên gạch
                            float currentSize = Mathf.Max(b.size.x, b.size.z);
                            
                            // Tính toán Scale Factor cần thiết để kích thước = cellSize (thường là 1)
                            float requiredScale = map.cellSize / currentSize;
                            
                            Debug.Log($"<color=cyan>Kích thước hiện tại: {b.size.x:F2} x {b.size.z:F2} mét</color>");
                            Debug.Log($"<color=green>=> BẠN CẦN NHẬP SCALE FACTOR LÀ: {requiredScale:F2}</color>");
                        }
                    }
                    // -----------------------------------------
                }
                // ----- LỚP 2: OBJECT -----
                char objChar = (c < objRow.Length) ? objRow[c] : '-';
                if (objChar != '-' && objDict.TryGetValue(objChar, out var objPrefab) && objPrefab != null)
                {
                    GameObject obj = Instantiate(objPrefab, transform);
                    // Nhô lên 0.01 để không bị chìm/z-fighting với mặt sàn
                    obj.transform.localPosition = pos + Vector3.up * 0.01f; 
                    obj.name = $"Obj_{r}_{c}_{objChar}";

                    // Lưu lại vị trí Spawn và Target cho Agent dùng sau này
                    if (objChar == 'S') spawnPosition = pos;
                    if (objChar == 'T') targetPosition = pos;
                }
            }
        }

        // Lưu kích thước map để agent kiểm tra biên
        mapRows = rows;
        mapCols = map.tiles[0].Length;
        mapCellSize = map.cellSize;

        // Tường vô hình bao quanh map
        BuildBoundaryWalls();

        Debug.Log($"[JsonMapBuilder] ✅ Đã build xong map: {mapName} ({rows}x{map.tiles[0].Length})");
    }

    // 4 bức tường vô hình quanh rìa map: xe không thể chạy ra ngoài
    void BuildBoundaryWalls()
    {
        var old = transform.Find("Walls");
        if (old != null) DestroyImmediate(old.gameObject);

        if (mapCols <= 0 || mapRows <= 0) return;

        Transform root = new GameObject("Walls").transform;
        root.SetParent(transform, false);

        Bounds b = GetLocalBounds();
        const float t = 2f;    // độ dày tường
        const float h = 4f;    // chiều cao

        AddWall(root, "Wall_X_Min",
            new Vector3(b.min.x - t * 0.5f, h * 0.5f, b.center.z),
            new Vector3(t, h, b.size.z + t * 2f));

        AddWall(root, "Wall_X_Max",
            new Vector3(b.max.x + t * 0.5f, h * 0.5f, b.center.z),
            new Vector3(t, h, b.size.z + t * 2f));

        AddWall(root, "Wall_Z_Min",
            new Vector3(b.center.x, h * 0.5f, b.min.z - t * 0.5f),
            new Vector3(b.size.x + t * 2f, h, t));

        AddWall(root, "Wall_Z_Max",
            new Vector3(b.center.x, h * 0.5f, b.max.z + t * 0.5f),
            new Vector3(b.size.x + t * 2f, h, t));
    }

    static void AddWall(Transform parent, string wallName, Vector3 localCenter, Vector3 size)
    {
        var go = new GameObject(wallName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;

        var box = go.AddComponent<BoxCollider>();
        box.size = size;

        // KHÔNG gắn tag "Obstacle": tường chỉ chặn vật lý, không làm fail episode.
        // Muốn chạm rìa là fail thì bỏ comment dòng dưới:
        // go.tag = "Obstacle";
    }
}