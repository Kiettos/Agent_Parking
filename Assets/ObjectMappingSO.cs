using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ObjectEntry
{
    public char symbol;
    public GameObject prefab;
    public bool isObstacle;
    public Vector3 rotationOffset;
}

[CreateAssetMenu(fileName = "ObjectMapping", menuName = "Parking/Object Mapping")]
public class ObjectMappingSO : ScriptableObject
{
    public List<ObjectEntry> entries = new List<ObjectEntry>();

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