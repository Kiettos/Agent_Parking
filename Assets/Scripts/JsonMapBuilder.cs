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
                char tileChar = map.tiles[r][c];
                if (tileDict.TryGetValue(tileChar, out var tilePrefab) && tilePrefab != null)
                {
                    GameObject tile = Instantiate(tilePrefab, transform);
                    tile.transform.localPosition = pos;
                    tile.name = $"Tile_{r}_{c}_{tileChar}";
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

        Debug.Log($"[JsonMapBuilder] ✅ Đã build xong map: {mapName} ({rows}x{map.tiles[0].Length})");
    }
}