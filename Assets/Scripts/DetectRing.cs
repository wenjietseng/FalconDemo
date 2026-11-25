using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectRing : MonoBehaviour
{
    public Material _materialWire;
    public Material _materialCrossing;

    private void OnTriggerEnter(Collider other)
    {
                    Debug.Log("1");
        if (other.transform.CompareTag("Ring"))
        {
            Debug.Log("cross the wire");
            this.GetComponent<Renderer>().material = _materialCrossing;
        }
    }

    private void OnTriggerExit(Collider other)
    {
                            Debug.Log("2");
        if (other.transform.CompareTag("Ring"))
        {
            Debug.Log("leave the wire");
            this.GetComponent<Renderer>().material = _materialWire;
        }
    }
}
