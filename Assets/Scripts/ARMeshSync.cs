using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Netcode;

public class ARMeshSync : NetworkBehaviour
{
    [SerializeField] private ARPlaneManager arPlaneManager;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SendEnvironmentToVR()
    {
        foreach (var plane in arPlaneManager.trackables)
        {
            Vector3 center = plane.center;
            Vector3 scale = plane.size;
            Quaternion rotation = plane.transform.rotation;

            SubmitPlaneServerRpc(center, scale, rotation);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitPlaneServerRpc(Vector3 center, Vector3 scale, Quaternion rotation)
    {
        SpawnSyncedPlaneClientRpc(center, scale, rotation);
    }

    [ClientRpc]
    void SpawnSyncedPlaneClientRpc(Vector3 center, Vector3 scale, Quaternion rotation)
    {
        if (IsOwner) return;

        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.transform.position = center;
        plane.transform.localScale = new Vector3(scale.x * 0.1f, 1, scale.y * 0.1f);
        plane.transform.rotation = rotation;
        plane.GetComponent<Renderer>().material.color = Color.blue;
    }
}
