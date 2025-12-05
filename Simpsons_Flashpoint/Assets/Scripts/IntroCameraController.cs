using UnityEngine;
using Unity.Cinemachine;   // Cinemachine 3 (Unity 6)
using UnityEngine.Splines;

public class IntroCameraController : MonoBehaviour
{
    [Header("Config")]
    public CinemachineSplineDolly splineDolly;   // Componente de la vcam de intro
    public CinemachineCamera introCam;           // VCam de intro
    public CinemachineCamera gameplayCam;        // VCam de gameplay
    public float duration = 5f;                  // Segundos que dura la animación
    public bool playOnStart = true;

    private float t = 0f;
    private bool playing = false;

    void Awake()
    {
        // Usar posición normalizada (0..1) a lo largo del spline
        if (splineDolly != null)
        {
            splineDolly.PositionUnits = PathIndexUnit.Normalized;
        }
    }

    void Start()
    {
        if (playOnStart)
        {
            StartIntro();
        }
    }

    public void StartIntro()
    {
        t = 0f;
        playing = true;

        // Asegura que la intro tenga mayor prioridad
        if (introCam != null) introCam.Priority = 20;
        if (gameplayCam != null) gameplayCam.Priority = 5;

        // Inicio del recorrido en el spline (0 = principio)
        if (splineDolly != null)
        {
            splineDolly.CameraPosition = 0f;
        }
    }

    void Update()
    {
        if (!playing || splineDolly == null || duration <= 0f)
            return;

        t += Time.deltaTime / duration;
        float clampedT = Mathf.Clamp01(t);

        // Mueve la cámara por el spline (0 → 1)
        splineDolly.CameraPosition = clampedT;

        // Cuando termina la intro
        if (t >= 1f)
        {
            playing = false;

            // Cambiar a cámara de gameplay
            if (introCam != null) introCam.Priority = 5;
            if (gameplayCam != null) gameplayCam.Priority = 20;
        }
    }
}
