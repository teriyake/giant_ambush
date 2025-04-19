using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
public class ARLevelSetup : NetworkBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARRaycastManager m_raycastManager;
    [SerializeField] private ARPlaneManager m_planeManager;
    [SerializeField] private ARAnchorManager m_anchorManager;

    [Header("Level Setup")]
    [SerializeField] private GameObject m_levelPlaceholderPrefab;
    [SerializeField] private LayerMask m_levelPieceLayer; 

    private List<ARRaycastHit> m_raycastHits = new List<ARRaycastHit>();
    private bool m_levelSpawned = false;
    private bool m_canAttemptSpawn = false;

    private ulong m_spawnedLevelPieceNetworkId = 0;
    private Pose m_anchorPose;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner || NetworkManager.Singleton.IsHost)
        {
            m_canAttemptSpawn = false;
            enabled = false;
            // Debug.Log($"ARLevelSetup on NetworkObject {NetworkObjectId}: Disabling spawn attempt logic. IsOwner={IsOwner}, IsHost={NetworkManager.Singleton.IsHost}");
        }
        else
        {
            m_canAttemptSpawn = true;
            InitializeARComponents();
            // Debug.Log($"ARLevelSetup on NetworkObject {NetworkObjectId}: Enabling spawn attempt logic for AR Client Owner.");
        }
    }

    private void InitializeARComponents()
    {
        if (m_raycastManager == null) m_raycastManager = FindObjectOfType<ARRaycastManager>();
        if (m_planeManager == null) m_planeManager = FindObjectOfType<ARPlaneManager>();
        if (m_anchorManager == null) m_anchorManager = FindObjectOfType<ARAnchorManager>();

        if (m_raycastManager == null) Debug.LogError("ARLevelSetup: ARRaycastManager not found!", this);
        if (m_anchorManager == null) Debug.LogWarning("ARLevelSetup: ARAnchorManager not found! Anchoring will not work.", this);
        if (m_levelPlaceholderPrefab == null) Debug.LogError("ARLevelSetup: Level Placeholder Prefab not assigned!", this);

        if (m_planeManager != null)
        {
            Debug.Log($"ARLevelSetup: Found ARPlaneManager. Is GameObject active? {m_planeManager.gameObject.activeInHierarchy}. Is Component enabled? {m_planeManager.enabled}", m_planeManager.gameObject);
        // if (!m_planeManager.enabled) {
        //     Debug.LogWarning("ARPlaneManager component was disabled, attempting to enable it.");
        //     m_planeManager.enabled = true;
        // }
        }
        else
        {
            Debug.LogError("ARLevelSetup: ARPlaneManager component STILL not found after rig activation attempt!", this);
        }
        if (m_raycastManager != null)
        {
            Debug.Log($"ARLevelSetup: Found ARRaycastManager. Is GameObject active? {m_raycastManager.gameObject.activeInHierarchy}. Is Component enabled? {m_raycastManager.enabled}", m_raycastManager.gameObject);
        }
        else 
        {
            Debug.LogError("ARLevelSetup: ARRaycastManager component STILL not found!", this);
        }

        Debug.Log("ARLevelSetup initialized for local AR player. Tap on a detected plane to set up the level.");
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!m_canAttemptSpawn || m_levelSpawned || m_raycastManager == null || m_levelPlaceholderPrefab == null)
        {
            return;
        }

        Pointer currentPointer = Pointer.current;
        if (currentPointer == null || !currentPointer.press.wasPressedThisFrame)
        {
            return; 
        }

        Vector2 screenPosition = currentPointer.position.ReadValue();

        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, m_levelPieceLayer)) return;

        if (m_raycastManager.Raycast(screenPosition, m_raycastHits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = m_raycastHits[0].pose;
            ARPlane plane = m_planeManager.GetPlane(m_raycastHits[0].trackableId);
            Vector2 planeSize = Vector2.zero;

            if (plane != null)
            {
                planeSize = plane.size;
                Debug.Log($"ARLevelSetup: AR Plane detected at {hitPose.position}. Pose Rotation: {hitPose.rotation.eulerAngles}");
            }

            const float minPlaneSize = 0.5f;
            if (planeSize.x < minPlaneSize || planeSize.y < minPlaneSize)
            {
                Debug.LogWarning($"ARLevelSetup: Detected plane size ({planeSize.x:F2}m x {planeSize.y:F2}m) is too small or invalid. Ignoring tap.");
                return;
            }

            // LogPlaneDetails(m_raycastHits[0]);

            m_anchorPose = hitPose;

            Debug.Log($"ARLevelSetup: Requesting procedural level spawn with Size: {planeSize} at Pose: {hitPose.position}, {hitPose.rotation.eulerAngles}");
            RequestLevelSpawnServerRpc(hitPose.position, hitPose.rotation, planeSize);

            m_levelSpawned = true;
            // m_canAttemptSpawn = false;

            if (m_planeManager != null)
            {
                foreach (var p in m_planeManager.trackables)
                {
                    p.gameObject.SetActive(false);
                }
                m_planeManager.enabled = false;
            }
        }
        else
        {
            Debug.Log("ARLevelSetup: Tap did not hit a valid AR plane.");
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestLevelSpawnServerRpc(Vector3 position, Quaternion rotation, Vector2 size, ServerRpcParams rpcParams = default)
    {
        if (m_levelPlaceholderPrefab == null) return;

        Debug.Log($"Server received procedural level spawn request from Client {rpcParams.Receive.SenderClientId} at pos {position}, size {size}");

        NetworkedAutoLevelGenerator prefabNalg = m_levelPlaceholderPrefab.GetComponentInChildren<NetworkedAutoLevelGenerator>();
        if (prefabNalg == null) { Debug.LogError("Prefab NALG not found!"); return; }
        float blockSize = prefabNalg.BlockSize;
        int levelHeight = prefabNalg.LevelHeight;
        Vector3Int estimatedLevelSizeBlocks = new Vector3Int(Mathf.CeilToInt(size.x / blockSize) * 10, levelHeight, Mathf.CeilToInt(size.y / blockSize) * 10);
        Vector3 initialCenterOffset = new Vector3(
            -estimatedLevelSizeBlocks.x * blockSize * 0.5f,
            0,
            -estimatedLevelSizeBlocks.z * blockSize * 0.5f
        );

        Vector3 targetWorldPosition = position + rotation * initialCenterOffset;
        Debug.Log($"ServerRpc: Calculated Target World Position: {targetWorldPosition}");

        GameObject roomSpawnerObject = Instantiate(m_levelPlaceholderPrefab, Vector3.zero, Quaternion.identity);
        Debug.Log($"ServerRpc: Instantiated '{roomSpawnerObject.name}' at origin.");

        roomSpawnerObject.transform.SetPositionAndRotation(targetWorldPosition, rotation);
        Debug.Log($"ServerRpc: Set '{roomSpawnerObject.name}' world position to: {roomSpawnerObject.transform.position} | Rotation: {roomSpawnerObject.transform.rotation.eulerAngles}");


        NetworkObject networkObject = roomSpawnerObject.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("ServerRpc (RequestLevelSpawn): Spawned room spawner object is missing NetworkObject component!");
            Destroy(roomSpawnerObject);
            return;
        }

        /*
        var networkTransform = roomSpawnerObject.GetComponent<NetworkTransform>();
        if (networkTransform != null)
        {
            networkTransform.SetState(targetWorldPosition, rotation, Vector3.one);
            Debug.Log($"ServerRpc: Forced NetworkTransform state.");
        }
        */

        networkObject.Spawn(true);
        Debug.Log($"Server spawned Procedural Room Spawner {networkObject.NetworkObjectId} at pos {roomSpawnerObject.transform.position} (Initial Offset applied).");

        NetworkedAutoLevelGenerator autoLevelGenerator = roomSpawnerObject.GetComponent<NetworkedAutoLevelGenerator>();
        if (autoLevelGenerator == null)
        {
            Debug.LogError($"ServerRpc (RequestLevelSpawn): Spawned AutoLevel object {networkObject.NetworkObjectId} is missing NetworkedAutoLevelGenerator component!");
            networkObject.Despawn(true);
            Destroy(roomSpawnerObject);
            return;
        }

        Debug.Log($"Server: Calling GenerateLevel() with size {size} * 7 on {roomSpawnerObject.name} ({networkObject.NetworkObjectId})");
        autoLevelGenerator.GenerateLevel(size * 7);

        GameObject autoLevelRootGO = GameObject.Find("root");
        if (autoLevelRootGO != null && autoLevelRootGO.transform.parent == null)
        {
            Debug.Log($"ServerRpc: Found AutoLevel 'root' GO '{autoLevelRootGO.name}' at scene root.");
            autoLevelRootGO.transform.SetPositionAndRotation(targetWorldPosition, rotation);
            Debug.Log($"ServerRpc: Set '{autoLevelRootGO.name}' position to {autoLevelRootGO.transform.position}");
            autoLevelRootGO.transform.SetParent(roomSpawnerObject.transform, worldPositionStays: true);
            autoLevelRootGO.name = $"AutoLevelContent_{networkObject.NetworkObjectId}";
            Debug.Log($"ServerRpc: Parented '{autoLevelRootGO.name}' under '{roomSpawnerObject.name}'. Parent Pos: {roomSpawnerObject.transform.position}, Child World Pos: {autoLevelRootGO.transform.position}");
        }

        if (networkObject != null && roomSpawnerObject != null)
        {
            Debug.Log($"ServerRpc: Calling AnchorLevelPieceClientRpc for NO:{networkObject.NetworkObjectId} at Position:{targetWorldPosition}");
            AnchorLevelPieceClientRpc(networkObject.NetworkObjectId, targetWorldPosition, rotation);
        }
    }

    private void LogPlaneDetails(ARRaycastHit arHit)
    {
        if (m_planeManager == null) {
             Debug.LogWarning("ARLevelSetup: Cannot log plane details - ARPlaneManager not found or assigned.");
             return;
        }

        ARPlane plane = m_planeManager.GetPlane(arHit.trackableId);

        if (plane != null)
        {
            Debug.Log($"ARLevelSetup: Tapped Plane Info:\n" +
                    //   $"  - Trackable ID: {plane.trackableId}\n" +
                      $"  - Alignment: {plane.alignment}\n" + 
                    //   $"  - Classification: {plane.classification}\n" +
                      $"  - Center (World Space): {plane.center}\n" +
                      $"  - Size (Approx): {plane.size}\n" + 
                    //   $"  - Extents (Half Size): {plane.extents}\n" + 
                    //   $"  - Normal (World Space): {plane.normal}\n" + 
                    //   $"  - Pose Position (Tap Location): {arHit.pose.position}\n" + 
                    //   $"  - Pose Rotation (Surface Orientation at Tap): {arHit.pose.rotation}\n" +
                      $"  - Boundary Vertices Count: {plane.boundary.Length}");
        }
        else
        {
            Debug.LogWarning($"ARLevelSetup: Raycast hit trackable {arHit.trackableId}, but couldn't find corresponding ARPlane via ARPlaneManager.");
        }
    }

    [ClientRpc]
    private void AnchorLevelPieceClientRpc(ulong networkObjectId, Vector3 position, Quaternion rotation)
    {
        Debug.Log($"Client {NetworkManager.Singleton.LocalClientId} received AnchorLevelPieceClientRpc for NetworkObjectId {networkObjectId}");

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject))
        {
            GameObject levelPieceObject = networkObject.gameObject;
            Debug.Log($"Found NetworkObject {networkObjectId} locally: {levelPieceObject.name}");

            if (m_anchorManager != null)
            {
                Debug.Log("ARAnchorManager found, attempting to create ARAnchor.");

                GameObject anchorGO = new GameObject($"Anchor_For_NO_{networkObjectId}");
                anchorGO.transform.position = position;
                anchorGO.transform.rotation = rotation;
                ARAnchor anchor = anchorGO.AddComponent<ARAnchor>();

                if (anchor != null)
                {
                    Debug.Log($"Successfully created ARAnchor: {anchor.name} ({anchor.trackableId}). Adding ARAnchorFollower to {levelPieceObject.name}.");

                    ARAnchorFollower follower = levelPieceObject.AddComponent<ARAnchorFollower>();
                    follower.Initialize(anchor);

                    if (levelPieceObject.transform.parent != null && levelPieceObject.transform.GetComponentInParent<PlayerMovement>() != null)
                    {
                        Debug.LogWarning($"Object {networkObjectId} was parented to player ({levelPieceObject.transform.parent.name}), unparenting before anchor following.");
                        levelPieceObject.transform.SetParent(null, true);
                    }
                }                
                else
                {
                    Debug.LogError($"Failed to create ARAnchor for NetworkObject {networkObjectId} at Pose ({position}, {rotation}). Object might drift.");
                     if (levelPieceObject.transform.parent != null)
                     {
                        levelPieceObject.transform.SetParent(null, true);
                     }
                }
            }
            else
            {
                Debug.Log("ARAnchorManager not found (expected for VR client). Ensuring object is not parented incorrectly.");
                if (levelPieceObject.transform.parent != null)
                {
                    if (levelPieceObject.transform.GetComponentInParent<PlayerMovement>() != null)
                    {
                        levelPieceObject.transform.SetParent(null, true);
                    }
                 }
            }
        }
        else
        {
            Debug.LogError($"Client {NetworkManager.Singleton.LocalClientId} could not find NetworkObject with ID {networkObjectId} to anchor.");
        }
    }
}