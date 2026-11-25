using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using System;

public class UpdateFalcon : MonoBehaviour
{
    // --------------------------------------------------------
    // Native plugin functions
    // --------------------------------------------------------
    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_Init();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_GetDeviceIDs(out int id1, out int id2);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_StartSimulation();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_SetParameters(
        double stiffness,
        double damping,
        double mass
    );

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_SetForceOutputEnabled(int enabled);

    // [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    // private static extern int Falcon_GetRingPosition(
    //     out double x,
    //     out double y,
    //     out double z
    // );

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_GetDev1Position(out double x, out double y, out double z);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_GetDev2Position(out double x, out double y, out double z);

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_StopSimulation();

    [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Falcon_Close();

    // Old API calls.
    // [DllImport("FalconBridge", CallingConvention = CallingConvention.Cdecl)]
    // private static extern int Falcon_GetPosition(
    //     out double x,
    //     out double y,
    //     out double z
    // );

    // --------------------------------------------------------
    // Unity Settings
    // - Initialize the Falcon and configure the virtual mass spring damper parameters.
    // - Start the native haptic thread.
    // - Read the simulated ring position and update its Transform.
    // --------------------------------------------------------
    [Header("Scene References")]
    public Transform ring; // 2 x 2 x 0.3 m
    public Transform cursor1;
    public Transform cursor2;
    Vector3 previousCursor1Pos;
    Vector3 previousCursor2Pos;

    [Header("Simulation Parameters")]
    [Tooltip("Spring stiffness in N/m")]
    public double stiffness = 50.0;

    [Tooltip("Damping coefficient in N/(m/s)")]
    public double damping = 0.5;

    [Tooltip("Virtual ring mass in kg")]
    public double mass = 0.2;
    public bool applyPararmeters = false;


    [Header("Coordinate Mapping")]
    [Tooltip("Unity units per meter")]
    public float unityUnitsPerMeter = 1.0f;
    [Tooltip("Unity position corresponding to the DHD origin")]
    public Transform unityOrigin;

    [Header("Startup")]
    [Tooltip("Enable only when ready to test force output")]
    public bool startSimulationOnPlay = true;
    public bool isClutched = false;
    public Transform clutchRecord;

    // --------------------------------------------------------
    // Internal state
    // --------------------------------------------------------
    // Physical force output remains disabled.
    private bool forceOutputEnabled = false;
    private bool deviceInitialized = false;
    private bool simulationStarted = false;

    void Start()
    {
        if (ring == null)
        {
            Debug.LogError("FalconController: Ring reference is missing.");
            enabled = false;
            return;
        }

        if (unityUnitsPerMeter <= 0.0f)
        {
            Debug.LogError("FalconController: unityUnitsPerMeter must be positive.");
            enabled = false;
            return;
        }

        // 1. Connect the Falcon.
        int initResult = Falcon_Init();
        if (initResult < 0)
        {
            Debug.LogError("Falcon initialization failed.");
            enabled = false;
            return;
        }

        deviceInitialized = true;
        Debug.Log("Falcon initialized successfully.");
        // Falcon_SetForceOutputEnabled(0);
        
        // 2. Configure the virtual ring.
        int parameterResult = Falcon_SetParameters(stiffness, damping, mass);

        if (parameterResult < 0)
        {
            Debug.LogError("Failed to set simulation parameters.");
            Shutdown();
            enabled = false;
            return;
        }

        Debug.Log($"Simulation parameters: k={stiffness} N/m, " + $"b={damping} N/(m/s), m={mass} kg");

        // 3. Start force output only when explicitly enabled.
        if (startSimulationOnPlay)
        {
            StartSimulation();
        }
        else
        {
            Debug.Log("Falcon is initialized, but haptics are not running. " + "Enable Start Simulation On Play when ready.");
        }
        previousCursor1Pos = cursor1.position;
        previousCursor2Pos = cursor2.position;

    }

    void Update()
    {
        if (!deviceInitialized || !simulationStarted) return;
        
        if (Falcon_GetDev1Position(out double hx, out double hy, out double hz) == 0)
        {
            if (cursor1 != null) cursor1.position = DhdToUnity(hx, hy, hz);
        }

        if (Falcon_GetDev2Position(out double x, out double y, out double z) == 0)
        {
            if (cursor2 != null) cursor2.position = DhdToUnity(x, y, z);
        }

        if (!isClutched)
        {
            // Read ring position.
            ring.position += new Vector3(0, ComputeTranslation(cursor1, cursor2).y, 0);
            // ring.position += ComputeTranslation(cursor1, cursor2);
            // ring.rotation *= ComputeRotation(cursor1, cursor2);
            
            // need to consider the correctness of updating previous positions here.
            previousCursor1Pos = cursor1.position;
            previousCursor2Pos = cursor2.position;
        }
        else
        {
            Debug.Log("Clutch is pressed, not updating ring and haptic device positions.");
            clutchRecord.position += new Vector3(0, ComputeTranslation(cursor1, cursor2).y, 0);   
        }

        if (applyPararmeters)
        {
            ApplyParameters();
            applyPararmeters = false;
        }
    }

    public void StartSimulation()
    {
        if (!deviceInitialized || simulationStarted)
            return;

        int result = Falcon_StartSimulation();

        if (result < 0)
        {
            Debug.LogError("Could not start Falcon simulation.");
            return;
        }

        simulationStarted = true;

        Debug.Log("Ring simulation started.");
    }

    private void Shutdown()
    {
        if (!deviceInitialized)
            return;

        Falcon_StopSimulation();
        Falcon_Close();

        simulationStarted = false;
        deviceInitialized = false;
        forceOutputEnabled = false;
    }

    void OnApplicationQuit()
    {
        Shutdown();
    }

    void OnDestroy()
    {
        Shutdown();
    }

    private Vector3 DhdToUnity(double x, double y, double z)
    {
        Vector3 origin = unityOrigin != null ? unityOrigin.position : Vector3.zero;

        return origin + new Vector3((float)y, (float)z, (float)x) * unityUnitsPerMeter;
    }

    public void ApplyParameters()
    {
        if (!deviceInitialized)
            return;

        int result = Falcon_SetParameters(stiffness, damping, mass);
        Debug.Log($"Simulation parameters: k={stiffness} N/m, " + $"b={damping} N/(m/s), m={mass} kg");

        if (result < 0)
        {
            Debug.LogError("Could not update ring parameters.");
        }
    }

    public void EnableForceOutput()
    {
        if (!deviceInitialized)
            return;

        forceOutputEnabled = true;
        Falcon_SetForceOutputEnabled(1);

        Debug.LogWarning("Falcon force output enabled.");
    }

    public void DisableForceOutput()
    {
        if (!deviceInitialized)
            return;

        forceOutputEnabled = false;
        Falcon_SetForceOutputEnabled(0);

        Debug.Log("Falcon force output disabled.");
    }


    public Vector3 ComputeTranslation(Transform m_User1, Transform m_User2)
    {
        // Compute the translation according to the motion of the two controllers
        Vector3 previousCenter = (previousCursor1Pos + previousCursor2Pos) / 2.0f;
        Vector3 currentCenter = (m_User1.position + m_User2.position) / 2.0f;

        return currentCenter - previousCenter;
    }

    public Quaternion ComputeRotation(Transform m_User1, Transform m_User2)
    {
            // Create vector from right to left hand (previousFrame)
            Vector3 previousVector = previousCursor1Pos - previousCursor2Pos;
            previousVector.Normalize();

            // Create vector from right to left hand (currentFrame)
            Vector3 currentVector = m_User1.position - m_User2.position;
            currentVector.Normalize();

            // Compute the cross product to compute the axis of rotation Vector3.Cross
            Vector3 axis = Vector3.Cross(previousVector, currentVector);

            //Compute the angle between previous and the current frame Vector3.Angle
            float angle = Vector3.Angle(previousVector, currentVector);

            //Compute the quaternion that encodes the rotation of “angle” along the “axis”
            return Quaternion.AngleAxis(angle, axis);
    }
}