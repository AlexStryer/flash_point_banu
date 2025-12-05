"""
Aviso de AI

Parte del desarrollo de este proyecto fue asistida por herramientas de inteligencia artificial, específicamente ChatGPT (modelo GPT-5, OpenAI, 2025).

La IA fue especialmente útil para apoyar en la construcción del sistema de conexión entre Unity y la API del servidor, resolver errores en la comunicación HTTP, y orientar en la organización del flujo de actualización del mapa (pisos, paredes, agentes, víctimas y hazards). También brindó ayuda puntual para ajustar detalles de sincronización y depuración durante el desarrollo.

Todo el contenido fue posteriormente probado, corregido y adaptado manualmente para asegurar su correcto funcionamiento dentro de Unity.

OpenAI. (2025). ChatGPT (versión GPT-5). Recuperado de https://chat.openai.com/ 
"""

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;   // New Input System

// ----------------- DATA CLASSES -----------------
// Clases simples para mapear el JSON que viene del servidor Python.

// Representa una celda del piso con tipo (inside, outside, kitchen, etc.)
[Serializable]
public class TileData
{
    public int x;
    public int y;
    public string type; // inside, outside, kitchen, garage, safe, spawn...
}

// Representa un borde entre dos celdas (pared o puerta)
[Serializable]
public class EdgeData
{
    public int ax;
    public int ay;
    public int bx;
    public int by;
    public string type; // "wall", "door_closed", "door_open"
}

// Representa un hazard (fuego o humo) en cierta celda
[Serializable]
public class HazardData
{
    public int x;
    public int y;
    public string kind; // "fire" o "smoke"
}

// Representa un agente/bombero en el mapa
[Serializable]
public class AgentData
{
    public int id;
    public int x;
    public int y;
    public string role;
}

// Representa una víctima en el mapa
[Serializable]
public class VictimData
{
    public int x;
    public int y;
}

// --- Stats por episodio (un solo juego) ---
[Serializable]
public class EpisodeStats
{
    public int victims_rescued;
    public int victims_picked;
    public int fires_extinguished;
    public int smokes_extinguished;
    public int doors_opened;
    public int action_points;
}

// --- Stats totales acumuladas de todos los episodios ---
[Serializable]
public class TotalStats
{
    public int victims_rescued;
    public int victims_picked;
    public int fires_extinguished;
    public int smokes_extinguished;
    public int doors_opened;
    public int action_points;
}

// ----------------- RESPUESTAS HTTP -----------------
// Estas clases representan la estructura completa del JSON que responde el servidor.

// Respuesta inicial de /floor (incluye tiles + edges + estado inicial y stats)
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

    // Campos extra para episodios múltiples y estadísticas
    public int episode;
    public int wins;
    public int losses;
    public int others;
    public int current_seed;
    public int max_episodes;
    public bool simulation_done;
    public bool episode_finished;
    public EpisodeStats episode_stats;
    public TotalStats total_stats;
}

// Respuesta de /step (un paso de simulación, sin tiles porque ya no cambian)
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

    // Campos de episodios múltiples y estadísticas
    public int episode;
    public int wins;
    public int losses;
    public int others;
    public int current_seed;
    public int max_episodes;
    public bool simulation_done;
    public bool episode_finished;
    public EpisodeStats episode_stats;
    public TotalStats total_stats;
}

// ----------------- FLOOR LOADER -----------------
// Este componente se encarga de:
// - Pedir el layout inicial a /floor
// - Construir el mapa en Unity (pisos, paredes, puertas)
// - En cada /step actualizar hazards, agentes y víctimas
// - Llevar control de la simulación (terminada o no)

public class FloorLoader : MonoBehaviour
{
    [Header("API")]
    public string url = "http://localhost:8585/floor";   // Endpoint para inicializar mapa
    public string stepUrl = "http://localhost:8585/step"; // Endpoint para avanzar un paso

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
    // Offsets para posicionar las partículas/objetos de fuego y humo
    public float fireXOffset = 0f;
    public float fireYOffset = 0.05f;
    public float fireZOffset = 0f;
    public float smokeXOffset = 0f;
    public float smokeYOffset = 0.05f;
    public float smokeZOffset = 0f;

    [Header("Prefabs - Bomberos (multi-modelo)")]
    public GameObject firefighterPrefab;         // fallback si no hay lista
    public GameObject[] firefighterPrefabs;      // lista de modelos distintos para agentes

    [Header("Prefabs - Víctimas (multi-skin)")]
    public GameObject victimPrefab;              // fallback si no hay lista
    public GameObject[] victimPrefabs;           // lista de modelos distintos para víctimas

    [Header("Grid config")]
    public float cellSize = 1f;                  // tamaño de cada celda en Unity
    public float yLevel = 0f;                    // altura base del mapa
    public Vector3 originOffset = Vector3.zero;  // offset global del mapa en la escena

    [Header("Walls config")]
    public float wallHeight = 2f;                // altura visual de la pared
    public float doorHeight = 2f;                // altura visual de la puerta

    [Header("Walls offset (extra)")]
    // Offsets extra para ajustar la posición de paredes
    public float wallXOffset = 0f;
    public float wallYOffset = 0f;
    public float wallZOffset = 0f;

    [Header("Doors offset (extra)")]
    // Offsets extra para ajustar la posición de puertas
    public float doorXOffset = 0f;
    public float doorYOffset = 0f;
    public float doorZOffset = 0f;

    [Header("Orientation")]
    // Permite espejear el mapa horizontal o verticalmente
    public bool mirrorX = true;
    public bool mirrorZ = false;

    // --- estado interno ---
    int mapWidth;
    int mapHeight;

    // Lista de instancias activas de hazards (para borrarlas en cada step)
    List<GameObject> activeHazards = new List<GameObject>();

    // Mapeo ID de agente → GameObject de bombero
    Dictionary<int, GameObject> agentObjects = new Dictionary<int, GameObject>();

    // Mapeo "x_y" → GameObject de víctima (para saber cuáles borrar/crear)
    Dictionary<string, GameObject> victimObjects = new Dictionary<string, GameObject>(); // key = "x_y"

    // Lista de paredes/puertas activas en escena
    List<GameObject> activeEdges = new List<GameObject>();

    // Flag para dejar de pedir /step cuando ya terminó toda la simulación
    bool simulationFinished = false;

    void Start()
    {
        // Pide el layout inicial con una seed fija (se puede parametrizar)
        StartCoroutine(RequestFloorLayout(897));
    }

    void Update()
    {
        // Si ya terminó la simulación (N episodios), no hacemos nada más
        if (simulationFinished)
            return;

        // Cada vez que se presiona Space, pedimos un nuevo /step
        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            StartCoroutine(RequestStep());
        }
    }

    // ----------------- /floor -----------------
    // Llama a la API Python con un POST a /floor para crear nuevo modelo
    IEnumerator RequestFloorLayout(int seed)
    {
        // JSON simple con la seed: { "seed": <valor> }
        string bodyJson = "{\"seed\": " + seed + "}";

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(bodyJson);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            // Espera la respuesta del servidor
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error al pedir /floor: " + www.error);
                yield break;
            }

            string json = www.downloadHandler.text;
            Debug.Log("Respuesta /floor: " + json);

            // Parseamos el JSON a FloorResponse (con tiles, edges, etc.)
            FloorResponse floor = JsonUtility.FromJson<FloorResponse>(json);
            if (floor == null || floor.tiles == null)
            {
                Debug.LogError("No se pudo parsear el JSON de /floor");
                yield break;
            }

            // Checamos si la simulación ya está marcada como terminada
            simulationFinished = floor.simulation_done;

            Debug.Log($"[FLOOR] Episodio {floor.episode}/{floor.max_episodes} - " +
                      $"Wins: {floor.wins}, Losses: {floor.losses}, Others: {floor.others}");

            // Log de stats totales acumuladas (puede ser útil para debug)
            if (floor.total_stats != null)
            {
                Debug.Log($"[FLOOR] Totales: " +
                          $"VR={floor.total_stats.victims_rescued}, " +
                          $"VP={floor.total_stats.victims_picked}, " +
                          $"F={floor.total_stats.fires_extinguished}, " +
                          $"S={floor.total_stats.smokes_extinguished}, " +
                          $"D={floor.total_stats.doors_opened}, " +
                          $"AP={floor.total_stats.action_points}");
            }

            // Construimos el mapa completo a partir de la respuesta inicial
            BuildFromResponse(floor);
        }
    }

    // ----------------- /step -----------------
    // Llama a la API Python con un POST a /step para avanzar la simulación
    IEnumerator RequestStep()
    {
        using (UnityWebRequest www = new UnityWebRequest(stepUrl, "POST"))
        {
            // Body vacío "{}" porque el servidor no espera parámetros
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes("{}");
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            // Espera la respuesta del servidor
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error al pedir /step: " + www.error);
                yield break;
            }

            string json = www.downloadHandler.text;
            Debug.Log("Respuesta /step: " + json);

            // Parseamos JSON a StepResponse (hazards, agentes, víctimas, edges)
            StepResponse step = JsonUtility.FromJson<StepResponse>(json);
            if (step == null || step.hazards == null)
            {
                Debug.LogError("No se pudo parsear el JSON de /step");
                yield break;
            }

            mapWidth = step.width;
            mapHeight = step.height;

            // Actualizamos paredes/puertas, hazards, agentes y víctimas
            BuildEdges(step.width, step.height, step.edges);
            BuildHazardsFromArray(step.hazards);
            UpdateAgents(step.agents, clearExisting: false);
            RebuildVictims(step.victims);

            Debug.Log($"[STEP] Episodio {step.episode}/{step.max_episodes} - " +
                      $"Wins: {step.wins}, Losses: {step.losses}, Others: {step.others}");

            // Si el episodio terminó, mostramos stats de ese episodio
            if (step.episode_finished && step.episode_stats != null)
            {
                Debug.Log($"[STEP] Episodio terminado. Stats: " +
                          $"VR={step.episode_stats.victims_rescued}, " +
                          $"VP={step.episode_stats.victims_picked}, " +
                          $"F={step.episode_stats.fires_extinguished}, " +
                          $"S={step.episode_stats.smokes_extinguished}, " +
                          $"D={step.episode_stats.doors_opened}, " +
                          $"AP={step.episode_stats.action_points}");
            }

            // Stats acumuladas de todos los episodios hasta el momento
            if (step.total_stats != null)
            {
                Debug.Log($"[STEP] Totales acumulados: " +
                          $"VR={step.total_stats.victims_rescued}, " +
                          $"VP={step.total_stats.victims_picked}, " +
                          $"F={step.total_stats.fires_extinguished}, " +
                          $"S={step.total_stats.smokes_extinguished}, " +
                          $"D={step.total_stats.doors_opened}, " +
                          $"AP={step.total_stats.action_points}");
            }

            // Si ya no hay más episodios que correr, marcamos como terminado
            if (step.simulation_done)
            {
                simulationFinished = true;
                Debug.Log("[STEP] Simulación COMPLETA, ya no se piden más pasos.");
            }

            // Mensaje final si el juego terminó en este episodio
            if (step.game_over)
            {
                Debug.Log($"GAME OVER (episodio {step.episode}): {step.result}");
            }
        }
    }

    // -------- build inicial ----------
    // Construye TODO el mapa desde cero a partir de la respuesta de /floor
    void BuildFromResponse(FloorResponse floor)
    {
        mapWidth = floor.width;
        mapHeight = floor.height;

        // Borra todos los hijos actuales de este GameObject (limpia el mapa)
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        // Limpia estructuras internas
        activeHazards.Clear();
        agentObjects.Clear();
        victimObjects.Clear();   // solo limpiamos el diccionario, los GOs ya se destruyeron
        activeEdges.Clear();

        // Reconstruye pisos, edges, hazards, agentes y víctimas iniciales
        BuildTiles(floor);
        BuildEdges(floor.width, floor.height, floor.edges);
        BuildHazardsFromArray(floor.hazards);
        UpdateAgents(floor.agents, clearExisting: true);
        RebuildVictims(floor.victims);
    }

    // ----------------- PISOS -----------------
    // Instancia un prefab de piso por cada tile recibido
    void BuildTiles(FloorResponse floor)
    {
        foreach (TileData tile in floor.tiles)
        {
            GameObject prefab = ChooseFloorPrefab(tile.type);
            if (prefab == null) continue;

            int gridX = tile.x;
            int gridY = tile.y;

            // Aplica espejado en X/Z si está activado
            if (mirrorX) gridX = (floor.width - 1) - gridX;
            if (mirrorZ) gridY = (floor.height - 1) - gridY;

            Vector3 pos = GridToWorld(gridX, gridY);
            Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
        }
    }

    // Selecciona el prefab correcto según el tipo de tile
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
        // Si no hay tipo específico, usamos default
        return defaultFloorPrefab;
    }

    // ----------------- PAREDES / PUERTAS -----------------
    // Reconstruye las paredes/puertas a partir de la lista de edges
    void BuildEdges(int width, int height, EdgeData[] edges)
    {
        // Destruye las instancias anteriores
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

            // Aplica espejado si corresponde
            if (mirrorX)
            {
                ax = (width - 1) - ax;
                bx = (width - 1) - bx;
            }
            if (mirrorZ)
            {
                ay = (height - 1) - ay;
                by = (height - 1) - by;
            }

            // Convierte coordenadas de grid a mundo para los dos puntos
            Vector3 a = GridToWorld(ax, ay);
            Vector3 b = GridToWorld(bx, by);
            Vector3 pos = (a + b) * 0.5f; // posición en medio del borde

            // Determina si el borde es vertical u horizontal
            bool isVerticalEdge = (ax != bx);
            bool isHorizontalEdge = (ay != by);

            GameObject prefab = null;

            float xOff = 0f;
            float yOff = 0f;
            float zOff = 0f;

            // Selecciona prefab y offsets según el tipo de edge
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

            // Aplica offset extra y crea la instancia en la escena
            pos += new Vector3(xOff, yOff, zOff);

            GameObject inst = Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
            activeEdges.Add(inst);
        }
    }

    // ----------------- HAZARDS -----------------
    // Reconstruye fuego y humo según el array de hazards
    void BuildHazardsFromArray(HazardData[] hazards)
    {
        // Borra hazards anteriores
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

            // Seleccionamos prefab y offsets según tipo (fire/smoke)
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

            // Aplica espejado según la configuración
            if (mirrorX) gx = (mapWidth - 1) - gx;
            if (mirrorZ) gy = (mapHeight - 1) - gy;

            Vector3 pos = GridToWorld(gx, gy) + new Vector3(xOffset, yOffset, zOffset);

            GameObject inst = Instantiate(prefab, pos, prefab.transform.rotation, this.transform);
            activeHazards.Add(inst);
        }
    }

    // ----------------- HELPERS PREFABS -----------------
    // Elige un prefab de bombero en base al id del agente (para variar skins)
    GameObject GetFirefighterPrefabForAgent(AgentData agent)
    {
        if (firefighterPrefabs != null && firefighterPrefabs.Length > 0)
        {
            int idx = Mathf.Abs(agent.id) % firefighterPrefabs.Length;
            return firefighterPrefabs[idx];
        }
        return firefighterPrefab;
    }

    // Elige un prefab de víctima basado en la key (para variar modelos)
    GameObject GetVictimPrefabForKey(string key)
    {
        if (victimPrefabs != null && victimPrefabs.Length > 0)
        {
            int idx = Mathf.Abs(key.GetHashCode()) % victimPrefabs.Length;
            return victimPrefabs[idx];
        }
        return victimPrefab;
    }

    // ----------------- AGENTES -----------------
    // Crea o actualiza la posición de los bomberos en la escena
    void UpdateAgents(AgentData[] agents, bool clearExisting)
    {
        // Si clearExisting es true, borramos todos los agentes previos
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
            // Si ya existe un GameObject para este id, solo movemos
            if (!agentObjects.TryGetValue(a.id, out go))
            {
                // Si no existe todavía, instanciamos uno nuevo
                GameObject prefabToUse = GetFirefighterPrefabForAgent(a);
                if (prefabToUse == null) continue;

                go = Instantiate(prefabToUse, pos, prefabToUse.transform.rotation, this.transform);
                agentObjects[a.id] = go;
            }
            else
            {
                go.transform.position = pos;
            }
        }
    }

    // ----------------- VÍCTIMAS -----------------
    // Sincroniza las víctimas de la escena con la lista que llega del servidor
    void RebuildVictims(VictimData[] victims)
    {
        // Conjunto de keys que existen en ESTE step
        HashSet<string> newKeys = new HashSet<string>();

        if (victims != null)
        {
            foreach (var v in victims)
            {
                // Key única para la celda de la víctima
                string key = v.x.ToString() + "_" + v.y.ToString();
                newKeys.Add(key);

                // Posición en grid (aplicando espejado)
                int gx = v.x;
                int gy = v.y;
                if (mirrorX) gx = (mapWidth - 1) - gx;
                if (mirrorZ) gy = (mapHeight - 1) - gy;

                Vector3 pos = GridToWorld(gx, gy) + new Vector3(0, 0.05f, 0);

                GameObject go;
                // Si no existía esa víctima, se instancia nueva
                if (!victimObjects.TryGetValue(key, out go))
                {
                    // Víctima nueva → elegimos skin solo UNA vez, basada en la key
                    GameObject prefabToUse = GetVictimPrefabForKey(key);
                    if (prefabToUse == null) continue;

                    go = Instantiate(prefabToUse, pos, prefabToUse.transform.rotation, this.transform);
                    victimObjects[key] = go;
                }
                else
                {
                    // Si ya existía, solo se actualiza la posición
                    go.transform.position = pos;
                }
            }
        }

        // Cualquier víctima que ya no aparezca en newKeys se elimina (muerta/rescatada)
        List<string> toRemove = new List<string>();
        foreach (var kv in victimObjects)
        {
            if (!newKeys.Contains(kv.Key))
            {
                if (kv.Value != null) Destroy(kv.Value);
                toRemove.Add(kv.Key);
            }
        }
        // Limpiamos el diccionario de entradas que ya no existen
        foreach (var k in toRemove)
        {
            victimObjects.Remove(k);
        }
    }

    // ----------------- Utils -----------------
    // Convierte coordenadas de grid (x,y) a coordenadas del mundo 3D de Unity
    Vector3 GridToWorld(int gx, int gy)
    {
        float worldX = gx * cellSize + cellSize * 0.5f;
        float worldZ = gy * cellSize + cellSize * 0.5f;
        return new Vector3(worldX, yLevel, worldZ) + originOffset;
    }
}
