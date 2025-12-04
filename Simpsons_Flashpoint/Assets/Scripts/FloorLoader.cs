using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;   // New Input System

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
    public string type; // "wall", "door_closed", "door_open"
}

[Serializable]
public class HazardData
{
    public int x;
    public int y;
    public string kind; // "fire" o "smoke"
}

[Serializable]
public class AgentData
{
    public int id;
    public int x;
    public int y;
}

[Serializable]
public class VictimData
{
    public int x;
    public int y;
}

[Serializable]
public class FloorResponse
{
    public int width;
    public int height;
    public TileData[] tiles;
    public EdgeData[] edges;
    public HazardData[] hazards;
    public AgentData[] agents;
    public VictimData[] victims;
    public bool game_over;
    public string result;
}

[Serializable]
public class StepResponse
{
    public int width;
    public int height;
    public EdgeData[] edges;
    public HazardData[] hazards;
    public AgentData[] agents;
    public VictimData[] victims;
    public bool game_over;
    public string result;
}

public class FloorLoader : MonoBehaviour
{
    [Header("API")]
    public string url = "http://localhost:8585/floor";
    public string stepUrl = "http://localhost:8585/step";

    [Header("Prefabs - Piso")]
    public GameObject defaultFloorPrefab;
    public GameObject insideFloorPrefab;
    public GameObject outsideFloorPrefab;
    public GameObject kitchenFloorPrefab;
    public GameObject garageFloorPrefab;
    public GameObject safeFloorPrefab;
    public GameObject spawnFloorPrefab;

    [Header("Prefabs - Paredes")]
    public GameObject wallVerticalPrefab;
    public GameObject wallHorizontalPrefab;

    [Header("Prefabs - Puertas cerradas")]
    public GameObject doorClosedVerticalPrefab;
    public GameObject doorClosedHorizontalPrefab;

    [Header("Prefabs - Puertas abiertas")]
    public GameObject doorOpenVerticalPrefab;
    public GameObject doorOpenHorizontalPrefab;

    [Header("Prefabs - Hazards")]
    public GameObject firePrefab;
    public GameObject smokePrefab;

    [Header("Hazards config")]
    public float fireXOffset = 0f;
    public float fireYOffset = 0.05f;
    public float fireZOffset = 0f;
    public float smokeXOffset = 0f;
    public float smokeYOffset = 0.05f;
    public float smokeZOffset = 0f;

    [Header("Prefabs - Entidades")]
    public GameObject firefighterPrefab;
    public GameObject victimPrefab;

    [Header("Grid config")]
    public float cellSize = 1f;
    public float yLevel = 0f;
    public Vector3 originOffset = Vector3.zero;

    [Header("Walls config")]
    public float wallHeight = 2f;   // altura visual de pared
    public float doorHeight = 2f;   // altura visual de puerta

    [Header("Walls offset (extra)")]
    public float wallXOffset = 0f;
    public float wallYOffset = 0f;
    public float wallZOffset = 0f;

    [Header("Doors offset (extra)")]
    public float doorXOffset = 0f;
    public float doorYOffset = 0f;
    public float doorZOffset = 0f;

    [Header("Orientation")]
    public bool mirrorX = true;
    public bool mirrorZ = false;

    // --- estado interno ---
    int mapWidth;
    int mapHeight;

    List<GameObject> activeHazards = new List<GameObject>();
    Dictionary<int, GameObject> agentObjects = new Dictionary<int, GameObject>();
    List<GameObject> victimObjects = new List<GameObject>();
    List<GameObject> activeEdges = new List<GameObject>();

    void Start()
    {
        StartCoroutine(RequestFloorLayout(897));
    }

    void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            StartCoroutine(RequestStep());
        }
    }

    // ----------------- /floor -----------------
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

    // ----------------- /step -----------------
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

            BuildEdges(step.width, step.height, step.edges);
            BuildHazardsFromArray(step.hazards);
            UpdateAgents(step.agents, clearExisting: false);
            RebuildVictims(step.victims);

            if (step.game_over)
            {
                Debug.Log($"GAME OVER: {step.result}");
            }
        }
    }

    // -------- build inicial ----------
    void BuildFromResponse(FloorResponse floor)
    {
        mapWidth = floor.width;
        mapHeight = floor.height;

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        activeHazards.Clear();
        agentObjects.Clear();
        victimObjects.Clear();
        activeEdges.Clear();

        BuildTiles(floor);
        BuildEdges(floor.width, floor.height, floor.edges);
        BuildHazardsFromArray(floor.hazards);
        UpdateAgents(floor.agents, clearExisting: true);
        RebuildVictims(floor.victims);
    }

    // ----------------- PISOS -----------------
    void BuildTiles(FloorResponse floor)
    {
        foreach (TileData tile in floor.tiles)
        {
            GameObject prefab = ChooseFloorPrefab(tile.type);
            if (prefab == null) continue;

            int gridX = tile.x;
            int gridY = tile.y;

            if (mirrorX) gridX = (floor.width - 1) - gridX;
            if (mirrorZ) gridY = (floor.height - 1) - gridY;

            Vector3 pos = GridToWorld(gridX, gridY);
            Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
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

    // ----------------- PAREDES / PUERTAS -----------------
    // ----------------- PAREDES / PUERTAS -----------------
void BuildEdges(int width, int height, EdgeData[] edges)
    {
        foreach (GameObject go in activeEdges)
            if (go != null) Destroy(go);
        activeEdges.Clear();

        if (edges == null) return;

        foreach (EdgeData e in edges)
        {
            int ax = e.ax;
            int ay = e.ay;
            int bx = e.bx;
            int by = e.by;

            if (mirrorX)
            {
                ax = (width  - 1) - ax;
                bx = (width  - 1) - bx;
            }
            if (mirrorZ)
            {
                ay = (height - 1) - ay;
                by = (height - 1) - by;
            }

            Vector3 a = GridToWorld(ax, ay);
            Vector3 b = GridToWorld(bx, by);
            Vector3 pos = (a + b) * 0.5f;

            bool isVerticalEdge   = (ax != bx);
            bool isHorizontalEdge = (ay != by);

            GameObject prefab = null;

            float xOff = 0f;
            float yOff = 0f;
            float zOff = 0f;

            // --- PARED ---
            if (e.type == "wall")
            {
                if (isVerticalEdge && wallVerticalPrefab != null)
                    prefab = wallVerticalPrefab;
                else if (isHorizontalEdge && wallHorizontalPrefab != null)
                    prefab = wallHorizontalPrefab;

                yOff = wallHeight / 2f + wallYOffset;
                xOff = wallXOffset;
                zOff = wallZOffset;
            }
            // --- PUERTA CERRADA (también acepta "door" viejo) ---
            else if (e.type == "door" || e.type == "door_closed")
            {
                if (isVerticalEdge && doorClosedVerticalPrefab != null)
                    prefab = doorClosedVerticalPrefab;
                else if (isHorizontalEdge && doorClosedHorizontalPrefab != null)
                    prefab = doorClosedHorizontalPrefab;

                yOff = doorHeight / 2f + doorYOffset;
                xOff = doorXOffset;
                zOff = doorZOffset;
            }
            // --- PUERTA ABIERTA ---
            else if (e.type == "door_open")
            {
                if (isVerticalEdge && doorOpenVerticalPrefab != null)
                    prefab = doorOpenVerticalPrefab;
                else if (isHorizontalEdge && doorOpenHorizontalPrefab != null)
                    prefab = doorOpenHorizontalPrefab;

                yOff = doorHeight / 2f + doorYOffset;
                xOff = doorXOffset;
                zOff = doorZOffset;
            }

            if (prefab == null) continue;

            pos += new Vector3(xOff, yOff, zOff);

            GameObject inst = Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
            activeEdges.Add(inst);
        }
    }


    // ----------------- HAZARDS -----------------
    void BuildHazardsFromArray(HazardData[] hazards)
    {
        foreach (GameObject go in activeHazards)
            if (go != null) Destroy(go);
        activeHazards.Clear();

        if (hazards == null) return;

        foreach (HazardData h in hazards)
        {
            GameObject prefab = null;
            float xOffset = 0f;
            float yOffset = 0f;
            float zOffset = 0f;

            if (h.kind == "fire" && firePrefab != null)
            {
                prefab = firePrefab;
                xOffset = fireXOffset;
                yOffset = fireYOffset;
                zOffset = fireZOffset;
            }
            else if (h.kind == "smoke" && smokePrefab != null)
            {
                prefab = smokePrefab;
                xOffset = smokeXOffset;
                yOffset = smokeYOffset;
                zOffset = smokeZOffset;
            }

            if (prefab == null) continue;

            int gx = h.x;
            int gy = h.y;

            if (mirrorX) gx = (mapWidth - 1) - gx;
            if (mirrorZ) gy = (mapHeight - 1) - gy;

            Vector3 pos = GridToWorld(gx, gy) + new Vector3(xOffset, yOffset, zOffset);

            GameObject inst = Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
            activeHazards.Add(inst);
        }
    }

    // ----------------- AGENTES -----------------
    void UpdateAgents(AgentData[] agents, bool clearExisting)
    {
        if (clearExisting)
        {
            foreach (var kv in agentObjects)
                if (kv.Value != null) Destroy(kv.Value);
            agentObjects.Clear();
        }

        if (agents == null) return;

        foreach (AgentData a in agents)
        {
            int gx = a.x;
            int gy = a.y;
            if (mirrorX) gx = (mapWidth - 1) - gx;
            if (mirrorZ) gy = (mapHeight - 1) - gy;

            Vector3 pos = GridToWorld(gx, gy) + new Vector3(0, 0.05f, 0);

            GameObject go;
            if (!agentObjects.TryGetValue(a.id, out go))
            {
                if (firefighterPrefab == null) continue;
                go = Instantiate(firefighterPrefab, pos, firefighterPrefab.transform.rotation, this.transform);
                agentObjects[a.id] = go;
            }
            else
            {
                go.transform.position = pos;
            }
        }
    }

    // ----------------- VÍCTIMAS -----------------
    void RebuildVictims(VictimData[] victims)
    {
        foreach (GameObject v in victimObjects)
            if (v != null) Destroy(v);
        victimObjects.Clear();

        if (victims == null) return;

        foreach (VictimData v in victims)
        {
            if (victimPrefab == null) continue;

            int gx = v.x;
            int gy = v.y;
            if (mirrorX) gx = (mapWidth - 1) - gx;
            if (mirrorZ) gy = (mapHeight - 1) - gy;

            Vector3 pos = GridToWorld(gx, gy) + new Vector3(0, 0.05f, 0);
            GameObject inst = Instantiate(victimPrefab, pos, victimPrefab.transform.rotation, this.transform);
            victimObjects.Add(inst);
        }
    }

    // ----------------- Utils -----------------
    Vector3 GridToWorld(int gx, int gy)
    {
        float worldX = gx * cellSize + cellSize * 0.5f;
        float worldZ = gy * cellSize + cellSize * 0.5f;
        return new Vector3(worldX, yLevel, worldZ) + originOffset;
    }
}
