
using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class RingSpringDamper : MonoBehaviour
{
    [Header("Scene References")]
    public Transform ring;
    public Transform cursor1;
    public Transform cursor2;
    public Transform contactPoint1;
    public Transform contactPoint2;

    [Header("Spring-Damper Parameters")]
    [Tooltip("Spring stiffness in N/m")]
    public float stiffness = 50f;

    [Tooltip("Damping coefficient in Ns/m")]
    public float damping = 0.5f;

    [Header("Ring Dynamics")]
    [Tooltip("Ring mass in kg")]
    public float ringMass = 0.2f;

    [Header("Grasp Thresholds")]
    [Tooltip("Both participants must exceed this force to grasp (N)")]
    public float graspThreshold = 1.0f;

    [Tooltip("Either participant falling below this force releases (N)")]
    public float releaseThreshold = 0.5f;

    [Header("Visual Feedback")]
    public Material graspedColorRed;
    public Material releasedColorGreen;
    public Material releasedColorPurple;    

    [Header("Coordinate Scaling")]
    [Tooltip("Unity units corresponding to one meter")]
    public float unityUnitsPerMeter = 1f;

    [Header("Diagnostics")]
    public bool logForces = true;
    public float logInterval = 0.5f;

    private Vector3 previousCursor1;
    private Vector3 previousCursor2;
    private Vector3 previousContact1;
    private Vector3 previousContact2;

    private Vector3 force1;
    private Vector3 force2;
    private Vector3 ringVelocity;

    private Vector3 initialRingPosition;
    private Quaternion initialRingRotation;
    private float initialRingZ;

    private bool isGrasped;
    private float logTimer;
    private bool initialized;
    private MaterialPropertyBlock materialProperties;
    private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");

    void Start()
    {
        if (cursor1 == null || cursor2 == null || contactPoint1 == null || contactPoint2 == null)
        {
            Debug.LogError("RingSpringDamper: Assign all four Transform references.");
            enabled = false;
            return;
        }

        if (unityUnitsPerMeter <= 0f || ringMass <= 0f || stiffness < 0f || damping < 0f || releaseThreshold < 0f || graspThreshold <= releaseThreshold)
        {
            Debug.LogError("Invalid parameters. Check scale, mass, stiffness, " + "damping, and grasp/release thresholds.");
            enabled = false;
            return;
        }

        // Save the ring's initial pose.
        initialRingPosition = ring.position;
        initialRingRotation = ring.rotation;
        initialRingZ = ring.position.z;

        // Initialize position history.
        previousCursor1 = cursor1.position;
        previousCursor2 = cursor2.position;
        previousContact1 = contactPoint1.position;
        previousContact2 = contactPoint2.position;

        ringVelocity = Vector3.zero;
        isGrasped = false;

        materialProperties = new MaterialPropertyBlock();

        initialized = true;
        contactPoint1.GetComponent<Renderer>().material = releasedColorGreen;
        contactPoint2.GetComponent<Renderer>().material = releasedColorPurple;

        Debug.Log("RingSpringDamper initialized.");
    }

    void LateUpdate()
    {
        if (!initialized) return;

        float dt = Time.deltaTime;

        if (dt <= 0f) return;

        // Calculate cursor and contact-point velocities.
        Vector3 cursor1Velocity = (cursor1.position - previousCursor1) / (dt * unityUnitsPerMeter);

        Vector3 cursor2Velocity = (cursor2.position - previousCursor2) / (dt * unityUnitsPerMeter);

        Vector3 contact1Velocity = (contactPoint1.position - previousContact1) / (dt * unityUnitsPerMeter);

        Vector3 contact2Velocity = (contactPoint2.position - previousContact2) / (dt * unityUnitsPerMeter);

        // Calculate spring displacement in meters.
        Vector3 displacement1 = (cursor1.position - contactPoint1.position) / unityUnitsPerMeter;

        Vector3 displacement2 = (cursor2.position - contactPoint2.position) / unityUnitsPerMeter;

        // Relative velocity in meters per second.
        Vector3 relativeVelocity1 = cursor1Velocity - contact1Velocity;

        Vector3 relativeVelocity2 = cursor2Velocity - contact2Velocity;

        // Force exerted by each participant on the ring.
        force1 = stiffness * displacement1 + damping * relativeVelocity1;
        force2 = stiffness * displacement2 + damping * relativeVelocity2;

        // Save positions for the next frame.
        previousCursor1 = cursor1.position;
        previousCursor2 = cursor2.position;
        previousContact1 = contactPoint1.position;
        previousContact2 = contactPoint2.position;

        // Draw the virtual spring connections.
        float pressingForce1 = force1.z;
        float pressingForce2 = force2.z;
        bool wasGrasped = isGrasped;
        if (!isGrasped)
        {
            UpdateContactPointColors(pressingForce1, pressingForce2);
            // Both participants must reach the ON threshold.
            if (pressingForce1 >= graspThreshold && pressingForce2 >= graspThreshold)
            {
                isGrasped = true;
                ringVelocity = Vector3.zero;
                Debug.Log("RING GRASPED");
            }
        }
        else
        {
            // Either participant falling below the OFF threshold releases the ring.
            if (pressingForce1 < releaseThreshold || pressingForce2 < releaseThreshold)
            {
                isGrasped = false;
                ringVelocity = Vector3.zero;
                Debug.Log("RING RELEASED");
            }
        }
        
        // if (wasGrasped != isGrasped)
        // {
        //     // UpdateRingColor();
        // }

        if (isGrasped)
        {
            Vector3 lateralForce = new Vector3(force1.x + force2.x, force1.y + force2.y, 0f);

            // Newton's second law: acceleration in m/s^2.
            Vector3 acceleration = lateralForce / ringMass;

            // Semi-implicit Euler integration.
            ringVelocity += acceleration * dt;

            // Prevent any unintended Z velocity.
            ringVelocity.z = 0f;

            // Convert displacement from meters to Unity units.
            Vector3 displacementUnity = ringVelocity * dt * unityUnitsPerMeter;

            ring.position += displacementUnity;
        }
        else
        {
            ringVelocity = Vector3.zero;
        }

        // Enforce fixed Z position and fixed rotation.
        Vector3 ringPosition = ring.position;
        ringPosition.z = initialRingZ;
        ring.position = ringPosition;

        ring.rotation = initialRingRotation;
        previousCursor1 = cursor1.position;
        previousCursor2 = cursor2.position;
        previousContact1 = contactPoint1.position;
        previousContact2 = contactPoint2.position;

        Debug.DrawLine(cursor1.position, contactPoint1.position, Color.green);
        Debug.DrawLine(cursor2.position, contactPoint2.position, Color.red);

        // Log forces at a controlled rate.
        if (logForces)
        {
            logTimer += dt;

            if (logTimer >= logInterval)
            {
                logTimer = 0f;

                // Debug.Log($"Participant 1 force: {force1:F3} N | " + $"Magnitude: {force1.magnitude:F3} N");
                // Debug.Log($"Participant 2 force: {force2:F3} N | " + $"Magnitude: {force2.magnitude:F3} N");
                Debug.LogWarning($"Grasped: {isGrasped} | " + $"F1: {force1:F2} N | " + $"F2: {force2:F2} N | " + $"Ring velocity: {ringVelocity:F3} m/s");
            }
        }
    }

    // private void UpdateRingColor()
    // {
    //     if (isG)
    //     materialProperties.SetColor(ColorProperty, isGrasped ? graspedColor : releasedColor);

    //     ringRenderer.SetPropertyBlock(materialProperties);
    // }
    private void UpdateContactPointColors(float pressingForce1, float pressingForce2)
    {            
        if (pressingForce1 > graspThreshold)
        {
            contactPoint1.GetComponent<Renderer>().material = graspedColorRed;
        }
        else if (pressingForce1 < releaseThreshold)
        {
            contactPoint1.GetComponent<Renderer>().material = releasedColorGreen;
        }
        
        if (pressingForce2 > graspThreshold)
        {
            contactPoint2.GetComponent<Renderer>().material = graspedColorRed;
        }
        else if (pressingForce2 < releaseThreshold)
        {
            contactPoint2.GetComponent<Renderer>().material = releasedColorPurple;
        }
    }


    public Vector3 GetForce1()
    {
        return force1;
    }

    public Vector3 GetForce2()
    {
        return force2;
    }

    public bool IsGrasped()
    {
        return isGrasped;
    }

    public Vector3 GetRingVelocity()
    {
        return ringVelocity;
    }

    public void ResetRing()
    {
        if (ring == null) return;

        ring.position = initialRingPosition;
        ring.rotation = initialRingRotation;

        ringVelocity = Vector3.zero;
        isGrasped = false;
    }

}
