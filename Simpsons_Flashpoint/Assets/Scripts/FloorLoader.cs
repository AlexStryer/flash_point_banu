using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;   // 👈 NEW INPUT SYSTEM

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
public class HazardData
{
    public int x;
    public int y;
    public string kind; // "fire" o "smoke"
}

[Serializable]
public class FloorResponse
{
    public int width;
    public int height;
    public TileData[] tiles;
    public EdgeData[] edges;
    public HazardData[] hazards;
}

[Serializable]
public class StepResponse
{
    public int width;
    public int height;
    public HazardData[] hazards;
    public bool game_over;
    public string result;
}

public class FloorLoader : MonoBehaviour
{
    [Header("API")]
    public string url = "http://localhost:8585/floor";
    public string stepUrl = "http://localhost:8585/step";

    [Header("Prefabs - Piso")]
    public GameObject defaultFloorPrefab;   // fallback
    public GameObject insideFloorPrefab;
    public GameObject outsideFloorPrefab;
    public GameObject kitchenFloorPrefab;
    public GameObject garageFloorPrefab;
    public GameObject safeFloorPrefab;      // SAFE SPACES
    public GameObject spawnFloorPrefab;     // SPAWN SPACES

    [Header("Prefabs - Paredes y Puertas")]
    public GameObject wallVerticalPrefab;
    public GameObject wallHorizontalPrefab;
    public GameObject doorVerticalPrefab;
    public GameObject doorHorizontalPrefab;

    [Header("Prefabs - Hazards")]
    public GameObject firePrefab;
    public GameObject smokePrefab;

    [Header("Grid config")]
    public float cellSize = 1f;
    public float yLevel = 0f;
    public Vector3 originOffset = Vector3.zero;

    [Header("Orientation")]
    public bool mirrorX = true;
    public bool mirrorZ = false;

    // --- Estado interno ---
    int mapWidth;
    int mapHeight;
    List<GameObject> activeHazards = new List<GameObject>();

    void Start()
    {
        StartCoroutine(RequestFloorLayout(897));
    }

    void Update()
    {
        // NEW INPUT SYSTEM: espacio avanza un turno
        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            StartCoroutine(RequestStep());
        }
    }

    // -------- /floor ----------
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
            Debug.Log("Respuesta /floor: " + json);

            FloorResponse floor = JsonUtility.FromJson<FloorResponse>(json);
            if (floor == null || floor.tiles == null)
            {
                Debug.LogError("No se pudo parsear el JSON de /floor");
                yield break;
            }

            BuildFromResponse(floor);
        }
    }

    // -------- /step ----------
    IEnumerator RequestStep()
    {
        using (UnityWebRequest www = new UnityWebRequest(stepUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes("{}");
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error al pedir /step: " + www.error);
                yield break;
            }

            string json = www.downloadHandler.text;
            Debug.Log("Respuesta /step: " + json);

            StepResponse step = JsonUtility.FromJson<StepResponse>(json);
            if (step == null || step.hazards == null)
            {
                Debug.LogError("No se pudo parsear el JSON de /step");
                yield break;
            }

            mapWidth = step.width;
            mapHeight = step.height;

            BuildHazardsFromArray(step.hazards);

            if (step.game_over)
            {
                Debug.Log($"GAME OVER: {step.result}");
            }
        }
    }

    void BuildFromResponse(FloorResponse floor)
    {
        mapWidth = floor.width;
        mapHeight = floor.height;

        // limpia todo
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        activeHazards.Clear();

        BuildTiles(floor);
        BuildEdges(floor);
        BuildHazardsFromArray(floor.hazards);
    }

    // ---------------- PISOS ----------------
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

            Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
        }
    }

    GameObject ChooseFloorPrefab(string type)
    {
        switch (type)
        {
            case "kitchen": if (kitchenFloorPrefab != null) return kitchenFloorPrefab; break;
            case "garage":  if (garageFloorPrefab  != null) return garageFloorPrefab;  break;
            case "safe":
                if (safeFloorPrefab != null) return safeFloorPrefab;
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

    // -------------- PAREDES / PUERTAS --------------
    void BuildEdges(FloorResponse floor)
    {
        if (floor.edges == null) return;

        foreach (EdgeData e in floor.edges)
        {
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

            bool isVerticalEdge = (ax != bx);   // entre celdas izquierda-derecha
            bool isHorizontalEdge = (ay != by); // entre celdas arriba-abajo

            float cx = ((ax + bx) * 0.5f) * cellSize + cellSize * 0.5f;
            float cz = ((ay + by) * 0.5f) * cellSize + cellSize * 0.5f;
            Vector3 pos = new Vector3(cx, yLevel, cz) + originOffset;

            GameObject prefab = null;

            if (e.type == "wall")
            {
                if (isVerticalEdge && wallVerticalPrefab != null)      prefab = wallVerticalPrefab;
                else if (isHorizontalEdge && wallHorizontalPrefab != null) prefab = wallHorizontalPrefab;
            }
            else if (e.type == "door")
            {
                if (isVerticalEdge && doorVerticalPrefab != null)      prefab = doorVerticalPrefab;
                else if (isHorizontalEdge && doorHorizontalPrefab != null) prefab = doorHorizontalPrefab;
            }

            if (prefab == null) continue;

            Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
        }
    }

    // -------------- HAZARDS --------------
    void BuildHazardsFromArray(HazardData[] hazards)
    {
        // borra los anteriores
        foreach (GameObject go in activeHazards)
            if (go != null) Destroy(go);
        activeHazards.Clear();

        if (hazards == null || hazards.Length == 0)
            return;

        foreach (HazardData h in hazards)
        {
            GameObject prefab = null;
            if (h.kind == "fire" && firePrefab != null)      prefab = firePrefab;
            else if (h.kind == "smoke" && smokePrefab != null) prefab = smokePrefab;

            if (prefab == null) continue;

            int gridX = h.x;
            int gridY = h.y;

            if (mirrorX)
                gridX = (mapWidth - 1) - gridX;
            if (mirrorZ)
                gridY = (mapHeight - 1) - gridY;

            float worldX = gridX * cellSize + cellSize * 0.5f;
            float worldZ = gridY * cellSize + cellSize * 0.5f;
            Vector3 pos = new Vector3(worldX, yLevel, worldZ) + originOffset;

            var instance = Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
            activeHazards.Add(instance);
        }
    }
}