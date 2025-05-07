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

        if (slicedPiecePrefab == null)
        {
            Debug.LogError(
                $"SliceThis ({gameObject.name}): 'slicedPiecePrefab' is not assigned!",
                this
            );
            return;
        }
        NetworkObject thisNetObj = GetComponent<NetworkObject>();
        if (thisNetObj == null || !thisNetObj.IsSpawned)
        {
            Debug.LogWarning(
                $"SliceThis ({gameObject.name}): Original object is not spawned or missing NetworkObject. Cannot slice.",
                this
            );
            return;
        }

        Renderer originalRenderer = GetComponent<Renderer>();
        Material[] originalMaterials =
            (originalRenderer != null) ? originalRenderer.materials : new Material[0];

        EzySlice.Plane plane = new EzySlice.Plane(slicePlanePositionInWorld, slicePlaneNormal);

        SlicedHull slicedHull = SlicerExtensions.Slice(gameObject, plane, crossSectionMaterial);

        if (slicedHull != null)
        {
            GameObject upperHullTemp = slicedHull.CreateUpperHull(gameObject, crossSectionMaterial);
            if (upperHullTemp != null)
            {
                SetupSlicedPiece(
                    upperHullTemp,
                    originalMaterials,
                    transform.position,
                    transform.rotation,
                    slicePlaneNormal,
                    sliceStrength
                );
                Destroy(upperHullTemp);
            }

            GameObject lowerHullTemp = slicedHull.CreateLowerHull(gameObject, crossSectionMaterial);
            if (lowerHullTemp != null)
            {
                SetupSlicedPiece(
                    lowerHullTemp,
                    originalMaterials,
                    transform.position,
                    transform.rotation,
                    -slicePlaneNormal,
                    sliceStrength
                );
                Destroy(lowerHullTemp);
            }

            thisNetObj.Despawn(true);
        }
        else
        {
            Debug.LogWarning(
                $"SliceThis ({gameObject.name}): Slice operation returned null.",
                this
            );
        }
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
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * forceMagnitude * 0.05f, ForceMode.Impulse);
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
}