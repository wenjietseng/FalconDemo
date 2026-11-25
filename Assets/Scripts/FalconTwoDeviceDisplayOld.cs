using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class FalconTwoDeviceDisplayOld : MonoBehaviour
{
    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_Init();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_GetDeviceIDs(out int id1, out int id2);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_SetDeviceFrame(int deviceIndex, double tx, double ty, double tz, double qx, double qy, double qz, double qw);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_SetSimulationParameters(double stiffness, double damping, double mass, double graspThreshold, double releaseThreshold);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_SetInitialRingState(double rx, double ry, double rz, double c1x, double c1y, double c1z, double c2x, double c2y, double c2z);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_ResetRing();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_SetForceOutputEnabled(int enabled);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_StartSimulation();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_GetSimulationState(
        out double c1x, out double c1y, out double c1z,
        out double c2x, out double c2y, out double c2z,
        out double f1x, out double f1y, out double f1z,
        out double f2x, out double f2y, out double f2z,
        out double rx, out double ry, out double rz,
        out double vx, out double vy, out double vz,
        out int pressing1,
        out int pressing2,
        out int grasped);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Falcon_SetDeviceForceEnabled(int deviceIndex, int enabled);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Falcon_GetForceStatus(
        out int enabled1,
        out int enabled2,
        out int deadman1,
        out int deadman2,
        out int active1,
        out int active2,
        out int error1,
        out int error2,
        out int fault
    );

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_StopSimulation();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_Close();


    // [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    // private static extern int Falcon_GetDev1Position(out double x, out double y, out double z);

    // [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    // private static extern int Falcon_GetDev2Position(out double x, out double y, out double z);

    [Header("Scene references")]
    public Transform ring;
    public Transform cursor1;
    public Transform cursor2;
    public Transform contactPoint1;
    public Transform contactPoint2;

    [Header("Device coordinate frames")]
    [Tooltip("Position and rotation calibration for Falcon 1.")]
    public Transform device1Frame;

    [Tooltip("Position and rotation calibration for Falcon 2.")]
    public Transform device2Frame;

    [Header("Visual Feedback Materials")]
    public Material graspedColorRed;
    public Material releasedColorGreen;
    public Material releasedColorPurple;

    [Header("Simulation Parameters")]
    public float stiffness = 50f;
    public float damping = 0.5f;
    public float ringMass = 0.2f;
    public float graspThreshold = 1.0f;
    public float releaseThreshold = 0.5f;

    [Header("Coordinate mapping")]
    [Tooltip("Unity units per meter.")]
    public float unityUnitsPerMeter = 1.0f;

    [Header("Diagnostics")]
    public bool logPositions = true;
    public bool logForces = true;
    public float logIntervalSeconds = 0.5f;

    private bool deviceInitialized;
    private bool simulationStarted;
    private float nextLogTime;

    private Vector3 initialRingPosition;
    private Quaternion initialRingRotation;

    private Vector3 force1;
    private Vector3 force2;
    private Vector3 ringVelocity;

    private bool participant1Pressing;
    private bool participant2Pressing;
    private bool ringGrasped;

    private Renderer contactRenderer1;
    private Renderer contactRenderer2;
    private bool device1ForceArmed = false;
    private bool device2ForceArmed = false;

    private int enabled1, enabled2;
    private int deadman1, deadman2;
    private int active1, active2;
    private int error1, error2;
    private int forceFault;

private float nextStatusRead;

    private Vector3 device1Position;
    private Vector3 device2Position;



    void Start()
    {
        if (!ValidateScene())
        {
            enabled = false;
            return;
        }

        initialRingPosition = ring.position;
        initialRingRotation = ring.rotation;

        contactRenderer1 = contactPoint1.GetComponent<Renderer>();
        contactRenderer2 = contactPoint2.GetComponent<Renderer>();

        if (contactRenderer1 == null || contactRenderer2 == null)
        {
            Debug.LogError("Both contact points need Renderer components.");
            enabled = false;
            return;
        }

        if (releasedColorGreen != null) contactRenderer1.material = releasedColorGreen;

        if (releasedColorPurple != null) contactRenderer2.material = releasedColorPurple;

        int result = Falcon_Init();

        if (result < 0)
        {
            Debug.LogError($"Falcon_Init failed: {result}");
            enabled = false;
            return;
        }

        deviceInitialized = true;

        if (Falcon_GetDeviceIDs(out int id1, out int id2) != 0)
        {
            Debug.LogError("Could not retrieve Falcon device IDs.");
            Shutdown();
            enabled = false;
            return;
        }

        if (id1 == id2)
        {
            Debug.LogError("Falcon device IDs are not distinct.");
            Shutdown();
            enabled = false;
            return;
        }

        Debug.Log($"Falcon devices opened: ID 1 = {id1}, ID 2 = {id2}");

        // Force output is explicitly disabled.
        if (Falcon_SetForceOutputEnabled(0) != 0)
        {
            Debug.LogError("Could not confirm disabled force output.");
            Shutdown();
            enabled = false;
            return;
        }

        // Configure both device frames in the common task frame.
        if (!ConfigureDeviceFrame(1, device1Frame) ||
            !ConfigureDeviceFrame(2, device2Frame))
        {
            Shutdown();
            enabled = false;
            return;
        }

        // Configure simulation parameters.
        result = Falcon_SetSimulationParameters(stiffness, damping, ringMass, graspThreshold, releaseThreshold);

        if (result != 0)
        {
            Debug.LogError($"Falcon_SetSimulationParameters failed: {result}");
            Shutdown();
            enabled = false;
            return;
        }

        // Convert ring centre and contact offsets to metres.
        Vector3 ringMetres = ring.position / unityUnitsPerMeter;

        Vector3 contactOffset1Metres = (contactPoint1.position - ring.position) / unityUnitsPerMeter;

        Vector3 contactOffset2Metres = (contactPoint2.position - ring.position) / unityUnitsPerMeter;

        result = Falcon_SetInitialRingState(
            ringMetres.x, ringMetres.y, ringMetres.z,
            contactOffset1Metres.x,
            contactOffset1Metres.y,
            contactOffset1Metres.z,
            contactOffset2Metres.x,
            contactOffset2Metres.y,
            contactOffset2Metres.z);

        if (result != 0)
        {
            Debug.LogError($"Falcon_SetInitialRingState failed: {result}");
            Shutdown();
            enabled = false;
            return;
        }

        // Start native simulation only after all configuration is set.
        result = Falcon_StartSimulation();

        if (result != 0)
        {
            Debug.LogError($"Falcon_StartSimulation failed: {result}");
            Shutdown();
            enabled = false;
            return;
        }

        simulationStarted = true;
        nextLogTime = Time.unscaledTime + logIntervalSeconds;

        Debug.Log("Native two-Falcon ring simulation started. " + "Physical force output remains disabled.");
    }

    void Update()
    {
        if (!deviceInitialized || !simulationStarted) return;

        int result = Falcon_GetSimulationState(
            out double c1x, out double c1y, out double c1z,
            out double c2x, out double c2y, out double c2z,
            out double f1x, out double f1y, out double f1z,
            out double f2x, out double f2y, out double f2z,
            out double rx, out double ry, out double rz,
            out double vx, out double vy, out double vz,
            out int pressing1,
            out int pressing2,
            out int grasped);

        if (result != 0)
        {
            Debug.LogWarning($"Falcon_GetSimulationState failed: {result}");
            return;
        }

        // Native positions are already in the common task frame.
        // Do NOT apply DHD axis mapping or device transforms again.
        cursor1.position = new Vector3((float)c1x, (float)c1y, (float)c1z) * unityUnitsPerMeter;

        cursor2.position = new Vector3((float)c2x, (float)c2y, (float)c2z) * unityUnitsPerMeter;

        ring.position = new Vector3((float)rx, (float)ry, (float)rz) * unityUnitsPerMeter;

        ring.rotation = initialRingRotation;

        // Contact points follow the ring.
        // contactPoint1.position = ring.position + initialRingRotation * (contactPoint1.localPosition);
        // contactPoint2.position = ring.position + initialRingRotation * (contactPoint2.localPosition);

        force1 = new Vector3((float)f1x, (float)f1y, (float)f1z);

        force2 = new Vector3((float)f2x, (float)f2y, (float)f2z);

        ringVelocity = new Vector3((float)vx, (float)vy, (float)vz);

        participant1Pressing = pressing1 != 0;
        participant2Pressing = pressing2 != 0;
        ringGrasped = grasped != 0;

        UpdateContactPointColors();

        Debug.DrawLine(cursor1.position, contactPoint1.position, Color.green);
        Debug.DrawLine(cursor2.position, contactPoint2.position, Color.red);

        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + logIntervalSeconds;

            if (logPositions)
            {
                Debug.Log($"Cursor 1: {cursor1.position:F3} | " + $"Cursor 2: {cursor2.position:F3}");
            }

            if (logForces)
            {
                Debug.Log($"F1: {force1:F3} N | F2: {force2:F3} N | " + $"Grasped: {ringGrasped} | " + $"Ring velocity: {ringVelocity:F3} m/s");
            }
        }
    }

    private bool ValidateScene()
    {
        if (ring == null ||
            cursor1 == null ||
            cursor2 == null ||
            contactPoint1 == null ||
            contactPoint2 == null ||
            device1Frame == null ||
            device2Frame == null)
        {
            Debug.LogError("Assign ring, both cursors, both contact points, " + "and both device frames.");
            return false;
        }

        if (unityUnitsPerMeter <= 0f ||
            ringMass <= 0f ||
            stiffness < 0f ||
            damping < 0f ||
            releaseThreshold < 0f ||
            graspThreshold <= releaseThreshold)
        {
            Debug.LogError("Invalid simulation parameters.");
            return false;
        }

        return true;
    }

    private bool ConfigureDeviceFrame(int deviceIndex, Transform frame)
    {
        Vector3 positionMetres = frame.position / unityUnitsPerMeter;
        Quaternion rotation = frame.rotation;

        int result = Falcon_SetDeviceFrame(
            deviceIndex,
            positionMetres.x,
            positionMetres.y,
            positionMetres.z,
            rotation.x,
            rotation.y,
            rotation.z,
            rotation.w);

        if (result != 0)
        {
            Debug.LogError($"Falcon_SetDeviceFrame({deviceIndex}) failed: {result}"); 
            return false;
        }

        return true;
    }

    private void UpdateContactPointColors()
    {
        if (ringGrasped)
        {
            if (graspedColorRed != null)
            {
                contactRenderer1.material = graspedColorRed;
                contactRenderer2.material = graspedColorRed;
            }

            return;
        }

        // Preserve the previous material in the hysteresis band.
        if (force1.z > graspThreshold)
        {
            if (graspedColorRed != null) contactRenderer1.material = graspedColorRed;
        }
        else if (force1.z < releaseThreshold)
        {
            if (releasedColorGreen != null) contactRenderer1.material = releasedColorGreen;
        }

        if (force2.z > graspThreshold)
        {
            if (graspedColorRed != null) contactRenderer2.material = graspedColorRed;
        }
        else if (force2.z < releaseThreshold)
        {
            if (releasedColorPurple != null) contactRenderer2.material = releasedColorPurple;
        }
    }

    private void UpdateForceStatus()
    {
        // Status polling is for the Unity display only.
        // It does not control the haptic-loop frequency.
        if (Time.unscaledTime < nextStatusRead)
            return;

        nextStatusRead = Time.unscaledTime + 0.05f;

        Falcon_GetForceStatus(
            out enabled1,
            out enabled2,
            out deadman1,
            out deadman2,
            out active1,
            out active2,
            out error1,
            out error2,
            out forceFault
        );

        // Reflect the native state in the Unity controls.
        device1ForceArmed = enabled1 != 0;
        device2ForceArmed = enabled2 != 0;
    }

    // private Vector3 DhdToUnity(double x, double y, double z, Transform deviceFrame)
    // {
    //     // Quesion to think: do we need two haptic devices to keep their coordinate aligned in Unity? ... I think it does not need because the interaction space can be adapted using clutching...
    //     // Initial DHD-to-Unity axis permutation:
    //     // Unity X = DHD Y
    //     // Unity Y = DHD Z
    //     // Unity Z = DHD X
    //     Vector3 mapped = new Vector3((float)y, (float)z, (float)x) * unityUnitsPerMeter;
    //     if (deviceFrame == null) return mapped;
    //     return deviceFrame.position + deviceFrame.rotation * mapped;
    // }

    public void ResetRing()
    {
        if (!simulationStarted)
            return;

        int result = Falcon_ResetRing();

        if (result != 0)
        {
            Debug.LogWarning($"Falcon_ResetRing failed: {result}");
            return;
        }

        ring.position = initialRingPosition;
        ring.rotation = initialRingRotation;

        ringVelocity = Vector3.zero;
        ringGrasped = false;
    }

    private void Shutdown()
    {
        if (!deviceInitialized)
            return;

        Falcon_SetForceOutputEnabled(0);
        Falcon_StopSimulation();
        Falcon_Close();

        simulationStarted = false;
        deviceInitialized = false;
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        Shutdown();
    }

private void OnGUI()
{
    GUILayout.BeginArea(
        new Rect(15, 15, 340, 330),
        GUI.skin.box
    );

    GUILayout.Label("FALCON FORCE CONTROL");

    GUILayout.Space(8);

    GUI.enabled = forceFault == 0;

    bool newDevice1 = GUILayout.Toggle(
        device1ForceArmed,
        " Arm Device 1 force output"
    );

    if (newDevice1 != device1ForceArmed)
    {
        Falcon_SetDeviceForceEnabled(
            0,
            newDevice1 ? 1 : 0
        );

        device1ForceArmed = newDevice1;
    }

    bool newDevice2 = GUILayout.Toggle(
        device2ForceArmed,
        " Arm Device 2 force output"
    );

    if (newDevice2 != device2ForceArmed)
    {
        Falcon_SetDeviceForceEnabled(
            1,
            newDevice2 ? 1 : 0
        );

        device2ForceArmed = newDevice2;
    }

    GUI.enabled = true;

    GUILayout.Space(12);
    GUILayout.Label("DEVICE STATUS");

    DrawDeviceStatus(
        "Device 1",
        enabled1,
        deadman1,
        active1,
        error1
    );

    GUILayout.Space(6);

    DrawDeviceStatus(
        "Device 2",
        enabled2,
        deadman2,
        active2,
        error2
    );

    GUILayout.Space(12);

    if (forceFault != 0)
    {
        GUI.color = Color.red;
        GUILayout.Label("FORCE FAULT: OUTPUT DISABLED");
        GUI.color = Color.white;
        GUILayout.Label("Restart the bridge after investigating.");
    }
    else
    {
        GUILayout.Label("Maximum force: 2.0 N per device");
        GUILayout.Label("Physical dead-man button required");
    }

    GUILayout.EndArea();
}

private void DrawDeviceStatus(
    string deviceName,
    int enabled,
    int deadman,
    int active,
    int error)
{
    GUILayout.Label(deviceName);

    GUILayout.Label(
        "  Unity arm: " + (enabled != 0 ? "ON" : "OFF")
    );

    GUILayout.Label(
        "  Physical button: " + (deadman != 0 ? "HELD" : "RELEASED")
    );

    GUILayout.Label(
        "  Force command: " + (active != 0 ? "ACTIVE" : "ZERO")
    );

    GUILayout.Label(
        "  Error: " + GetForceErrorText(error)
    );
}

private string GetForceErrorText(int error)
{
    switch (error)
    {
        case 0: return "None";
        case 1: return "Position read failed";
        case 2: return "Button read failed";
        case 3: return "Force command failed";
        case 4: return "Invalid data";
        default: return "Unknown error";
    }
}


}
