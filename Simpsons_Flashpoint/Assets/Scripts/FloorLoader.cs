using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class TileData
{
    public int x;
    public int y;
    public string type; // inside, outside, kitchen, garage, safe, spawn...
}

[Serializable]
public class EdgeData
{
    public int ax;
    public int ay;
    public int bx;
    public int by;
    public string type; // "wall" o "door"
}

[Serializable]
public class FloorResponse
{
    public int width;
    public int height;
    public TileData[] tiles;
    public EdgeData[] edges;
}

public class FloorLoader : MonoBehaviour
{
    [Header("API")]
    public string url = "http://localhost:8585/floor";

    [Header("Prefabs - Piso")]
    public GameObject defaultFloorPrefab;   // fallback
    public GameObject insideFloorPrefab;
    public GameObject outsideFloorPrefab;
    public GameObject kitchenFloorPrefab;
    public GameObject garageFloorPrefab;
    public GameObject safeFloorPrefab;      // opcional (para SAFE SPACES)
    public GameObject spawnFloorPrefab;     // opcional (para SPAWN SPACES)

    [Header("Prefabs - Paredes y Puertas")]
    // Pared entre celdas izquierda-derecha (línea vertical en el grid)
    public GameObject wallVerticalPrefab;
    // Pared entre celdas arriba-abajo (línea horizontal en el grid)
    public GameObject wallHorizontalPrefab;

    public GameObject doorVerticalPrefab;
    public GameObject doorHorizontalPrefab;

    [Header("Grid config")]
    public float cellSize = 1f;        // 1 => pisos en 0.5, 1.5, 2.5...
    public float yLevel = 0f;
    public Vector3 originOffset = Vector3.zero;

    [Header("Orientation")]
    public bool mirrorX = true;   // ya lo tenías en true para corregir espejo
    public bool mirrorZ = false;

    void Start()
    {
        // pon cualquier seed fija o desde un GameManager
        StartCoroutine(RequestFloorLayout(897));
    }

    IEnumerator RequestFloorLayout(int seed)
    {
        string bodyJson = "{\"seed\": " + seed + "}";

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(bodyJson);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error al pedir /floor: " + www.error);
                yield break;
            }

            string json = www.downloadHandler.text;
            Debug.Log("Respuesta floor: " + json);

            FloorResponse floor = JsonUtility.FromJson<FloorResponse>(json);
            if (floor == null || floor.tiles == null)
            {
                Debug.LogError("No se pudo parsear el JSON de floor");
                yield break;
            }

            BuildFromResponse(floor);
        }
    }

    void BuildFromResponse(FloorResponse floor)
    {
        // Limpia hijos anteriores (por si recargas)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        BuildTiles(floor);
        BuildEdges(floor);
    }

    // --------------------------
    // PISOS
    // --------------------------
    void BuildTiles(FloorResponse floor)
    {
        foreach (TileData tile in floor.tiles)
        {
            GameObject prefab = ChooseFloorPrefab(tile.type);
            if (prefab == null) continue;

            int gridX = tile.x;
            int gridY = tile.y;

            if (mirrorX)
                gridX = (floor.width - 1) - gridX;
            if (mirrorZ)
                gridY = (floor.height - 1) - gridY;

            float worldX = gridX * cellSize + cellSize * 0.5f;
            float worldZ = gridY * cellSize + cellSize * 0.5f;
            Vector3 pos = new Vector3(worldX, yLevel, worldZ) + originOffset;

            Quaternion rot = prefab.transform.rotation;
            Instantiate(prefab, pos, rot, this.transform);
        }
    }

    GameObject ChooseFloorPrefab(string type)
    {
        switch (type)
        {
            case "kitchen":
                if (kitchenFloorPrefab != null) return kitchenFloorPrefab;
                break;
            case "garage":
                if (garageFloorPrefab != null) return garageFloorPrefab;
                break;
            case "safe":
                if (safeFloorPrefab != null) return safeFloorPrefab;
                // si no asignas safe, que use inside
                if (insideFloorPrefab != null) return insideFloorPrefab;
                break;
            case "spawn":
                if (spawnFloorPrefab != null) return spawnFloorPrefab;
                if (insideFloorPrefab != null) return insideFloorPrefab;
                break;
            case "inside":
                if (insideFloorPrefab != null) return insideFloorPrefab;
                break;
            case "outside":
                if (outsideFloorPrefab != null) return outsideFloorPrefab;
                break;
        }

        return defaultFloorPrefab;
    }

    // --------------------------
    // PAREDES Y PUERTAS
    // --------------------------
    void BuildEdges(FloorResponse floor)
    {
        if (floor.edges == null) return;

        foreach (EdgeData e in floor.edges)
        {
            // copiamos para poder espejar sin modificar el original
            int ax = e.ax;
            int ay = e.ay;
            int bx = e.bx;
            int by = e.by;

            if (mirrorX)
            {
                ax = (floor.width - 1) - ax;
                bx = (floor.width - 1) - bx;
            }
            if (mirrorZ)
            {
                ay = (floor.height - 1) - ay;
                by = (floor.height - 1) - by;
            }

            bool isVerticalEdge = (ax != bx);    // entre celdas izquierda-derecha
            bool isHorizontalEdge = (ay != by);  // entre celdas arriba-abajo

            // Centro entre las dos celdas (misma lógica que en Python)
            float cx = ((ax + bx) * 0.5f) * cellSize + cellSize * 0.5f;
            float cz = ((ay + by) * 0.5f) * cellSize + cellSize * 0.5f;
            Vector3 pos = new Vector3(cx, yLevel, cz) + originOffset;

            GameObject prefab = null;

            if (e.type == "wall")
            {
                if (isVerticalEdge && wallVerticalPrefab != null)
                    prefab = wallVerticalPrefab;
                else if (isHorizontalEdge && wallHorizontalPrefab != null)
                    prefab = wallHorizontalPrefab;
            }
            else if (e.type == "door")
            {
                if (isVerticalEdge && doorVerticalPrefab != null)
                    prefab = doorVerticalPrefab;
                else if (isHorizontalEdge && doorHorizontalPrefab != null)
                    prefab = doorHorizontalPrefab;
            }

            if (prefab == null) continue;

            Quaternion rot = prefab.transform.rotation;
            Instantiate(prefab, pos, rot, this.transform);
        }
    }
}