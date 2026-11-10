using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TileEntry
{
    public char symbol;           // Ký tự trong JSON
    public GameObject prefab;     // Prefab tương ứng
    public bool isObstacle;       // Có phải vật cản không
    public Vector3 rotationOffset; // Xoay thêm (nếu cần)
}

[CreateAssetMenu(fileName = "TileMapping", menuName = "Parking/Tile Mapping")]
public class TileMappingSO : ScriptableObject
{
    public List<TileEntry> entries = new List<TileEntry>();

    public Dictionary<char, GameObject> BuildLookup()
    {
        var dict = new Dictionary<char, GameObject>();
        foreach (var e in entries)
        {
            if (e.prefab == null) continue;
            dict[e.symbol] = e.prefab;
        }
        return dict;
    }
}