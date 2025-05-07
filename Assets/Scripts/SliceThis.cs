using System.Collections.Generic;
using EzySlice;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class SliceThis : NetworkBehaviour
{
    [Tooltip(
        "Material for the newly cut faces (cross-section). If null, original material is used."
    )]
    public Material crossSectionMaterial;

    [Tooltip(
        "Prefab for the sliced pieces. Must have NetworkObject, Rigidbody, MeshFilter, MeshRenderer, MeshCollider."
    )]
    public GameObject slicedPiecePrefab;

    [ServerRpc(RequireOwnership = false)]
    public void SliceObjectServerRpc(
        Vector3 slicePlaneNormal,
        Vector3 slicePlanePositionInWorld,
        float sliceStrength
    )
    {
        if (!IsServer)
            return;
        Debug.LogError("SliceThis: ServerRpc entered.");
        if (slicedPiecePrefab == null)
        {
            Debug.LogError(
                $"SliceThis ({gameObject.name}): 'slicedPiecePrefab' is not assigned!",
                this
            );
            return;
        }
        BreakObj(this.gameObject, sliceStrength * 0.4f);

        Debug.LogError("SliceThis: ServerRpc returned.");
    }

    private void SetupSlicedPiece(
        GameObject tempPieceMeshProvider,
        Material[] originalMaterials,
        Vector3 originalPosition,
        Quaternion originalRotation,
        Vector3 forceDirection,
        float forceMagnitude
    )
    {
        GameObject spawnedPiece = Instantiate(
            slicedPiecePrefab,
            originalPosition,
            originalRotation
        );

        MeshFilter tempMeshFilter = tempPieceMeshProvider.GetComponent<MeshFilter>();
        Renderer tempRenderer = tempPieceMeshProvider.GetComponent<Renderer>();

        if (tempMeshFilter != null)
        {
            spawnedPiece.GetComponent<MeshFilter>().mesh = tempMeshFilter.mesh;
        }
        if (tempRenderer != null)
        {
            spawnedPiece.GetComponent<Renderer>().materials = tempRenderer.materials;
        }
        else if (originalMaterials.Length > 0)
        {
            spawnedPiece.GetComponent<Renderer>().materials = originalMaterials;
        }

        Rigidbody rb = spawnedPiece.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            float forceMultiplier = UnityEngine.Random.Range(0.8f, 1.5f);
            rb.AddForce(
                forceDirection * forceMagnitude * forceMultiplier * 0.1f,
                ForceMode.Impulse
            );
            rb.AddTorque(
                UnityEngine.Random.insideUnitSphere * forceMagnitude * 0.05f,
                ForceMode.Impulse
            );
        }

        MeshCollider mc = spawnedPiece.GetComponent<MeshCollider>();
        if (mc != null)
        {
            mc.sharedMesh = spawnedPiece.GetComponent<MeshFilter>().mesh;
            mc.convex = true;
        }

        NetworkObject pieceNetObj = spawnedPiece.GetComponent<NetworkObject>();
        if (pieceNetObj != null)
        {
            pieceNetObj.Spawn(true);
        }
        else
        {
            Debug.LogError(
                $"SliceThis: Sliced piece prefab '{slicedPiecePrefab.name}' is missing a NetworkObject component!",
                spawnedPiece
            );
            Destroy(spawnedPiece);
        }
    }

    private int maxIter = 3;
    private Material[] mats;

    public void BreakObj(GameObject obj, float sliceStrength)
    {
        Vector3 randomNormal = Random.onUnitSphere;
        mats = gameObject.GetComponent<MeshRenderer>().materials;
        List<(GameObject, Vector3)> slices = SliceGameObject(
            obj,
            new EzySlice.Plane(Vector3.zero, randomNormal),
            0
        );
        foreach ((GameObject, Vector3) tuple in slices)
        {
            GameObject o = tuple.Item1;
            Vector3 normal = tuple.Item2;
            NetworkObject thisNetObj = GetComponent<NetworkObject>();
            if (thisNetObj == null || !thisNetObj.IsSpawned)
            {
                Debug.LogWarning(
                    $"SliceThis ({gameObject.name}): Original object is not spawned or missing NetworkObject. Cannot slice.",
                    this
                );
                return;
            }

            SetupSlicedPiece(o, mats, o.transform.position, o.transform.rotation, normal, sliceStrength);
            Destroy(o);
        }
    }

    List<(GameObject, Vector3)> SliceGameObject(
        GameObject objToSlice,
        EzySlice.Plane slicingPlane,
        int call
    )
    {
        SlicedHull slicedHull = SlicerExtensions.Slice(objToSlice, slicingPlane, mats[0]);
        call++;

        List<(GameObject, Vector3)> slicedObjects = new List<(GameObject, Vector3)>();

        if (slicedHull != null)
        {
            GameObject upperHull = slicedHull.CreateUpperHull(objToSlice, null);
            GameObject lowerHull = slicedHull.CreateLowerHull(objToSlice, null);
            objToSlice.SetActive(false); // Hide the original object
            Destroy(objToSlice); // Destroy the original object

            if (call < maxIter)
            {
                slicedObjects.AddRange(
                    SliceGameObject(
                        upperHull,
                        new EzySlice.Plane(Vector3.zero, Random.onUnitSphere),
                        call
                    )
                );
                slicedObjects.AddRange(
                    SliceGameObject(
                        lowerHull,
                        new EzySlice.Plane(Vector3.zero, Random.onUnitSphere),
                        call
                    )
                );
            }
            else
            {
                slicedObjects.Add((upperHull, slicingPlane.GetNormal()));
                slicedObjects.Add((lowerHull, -slicingPlane.GetNormal()));
            }
        }
        else
        {
            slicedObjects.Add((objToSlice, slicingPlane.GetNormal()));
        }
        return slicedObjects;
    }
}