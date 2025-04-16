using UnityEngine;
using Unity.Netcode;
using AlaslTools;
using AutoLevel;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class NetworkedAutoLevelGenerator : NetworkBehaviour
{
    [Header("AutoLevel Setup")]
    [SerializeField] private BlocksRepo m_blocksRepo;
    [SerializeField] private float m_blockSize = 1.0f;
    [SerializeField] private int m_levelHeight = 5;
    [SerializeField] private Transform m_meshRoot;

    private BlocksRepo.Runtime runtimeRepo;
    private LevelMeshBuilder meshBuilder;
    private LevelSolver serverSolver;
    private BoundsInt m_generationBounds;

    private bool m_isAutoLevelInitialized = false;

    public BoundsInt GenerationBounds => m_generationBounds;
    public float BlockSize => m_blockSize;
    public int LevelHeight => m_levelHeight;

    void Awake()
    {
        if (m_blocksRepo == null) { Debug.LogError("NALG: BlocksRepo null", this); enabled = false; return; }
        if (m_meshRoot == null)
        {
            GameObject rootGO = new GameObject("GeneratedMeshRoot");
            rootGO.transform.SetParent(transform);
            rootGO.transform.localPosition = Vector3.zero;
            rootGO.transform.localRotation = Quaternion.identity;
            m_meshRoot = rootGO.transform;
            Debug.LogWarning($"NALG: Created mesh root '{rootGO.name}'.", this);
        }
        m_meshRoot.localPosition = Vector3.zero;
        m_meshRoot.localRotation = Quaternion.identity;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        InitializeAutoLevelCommon();
    }

    private void InitializeAutoLevelCommon()
    {
        if (m_isAutoLevelInitialized) return;
        if (m_blocksRepo == null) 
        { 
            Debug.LogError($"NALG ({NetworkObjectId}): BlocksRepo null", this); 
            return; 
        }

        Debug.Log($"NALG ({NetworkObjectId} - IsServer: {IsServer}): Initializing AutoLevel Common (Runtime Repo).");
        runtimeRepo = m_blocksRepo.CreateRuntime();
        if (runtimeRepo == null) 
        {
            Debug.LogError($"NALG ({NetworkObjectId}): Failed to create BlocksRepo.Runtime!", this);
            return;
        }

        m_isAutoLevelInitialized = true;
    }

    public void GenerateLevel(Vector2 realWorldSize)
    {
        if (!IsServer) { Debug.LogWarning($"NALG ({NetworkObjectId}): GenerateLevel called on non-server.", this); return; }
        if (!m_isAutoLevelInitialized) InitializeAutoLevelCommon();
        if (runtimeRepo == null) { Debug.LogError($"NALG (Server {NetworkObjectId}): RuntimeRepo null", this); return; }

        int width = Mathf.Max(1, Mathf.CeilToInt(realWorldSize.x / m_blockSize));
        int depth = Mathf.Max(1, Mathf.CeilToInt(realWorldSize.y / m_blockSize));
        int height = Mathf.Max(1, m_levelHeight);
        m_generationBounds = new BoundsInt(0, 0, 0, width, height, depth);

        Debug.Log($"NALG (Server {NetworkObjectId}): Calc Bounds={m_generationBounds}");

        LevelData serverLevelData = new LevelData(m_generationBounds);
        serverSolver = new LevelSolver(m_generationBounds.size);
        serverSolver.SetRepo(runtimeRepo);
        serverSolver.SetlevelData(serverLevelData); 

        serverSolver.SetGroupBoundary(BlocksRepo.SOLID_GROUP, Direction.Down);

        Debug.Log($"NALG (Server {NetworkObjectId}): Starting Solver...");
        int iterations = serverSolver.Solve(m_generationBounds, 10);

        if (iterations > 0)
        {
            Debug.Log($"NALG (Server {NetworkObjectId}): Solver successful ({iterations} iter).");

            int[] blockData = SerializeLevelData(serverLevelData);
            if (blockData == null) { Debug.LogError("NALG (Server): Failed to serialize LevelData!"); return; }

            Debug.Log($"NALG (Server {NetworkObjectId}): Sending BuildLevelClientRpc with Bounds={m_generationBounds} and {blockData.Length} block data points.");
            BuildLevelClientRpc(m_generationBounds.position, m_generationBounds.size, blockData);

            RebuildMeshLocally(serverLevelData);
        }
        else
        {
            Debug.LogError($"NALG (Server {NetworkObjectId}): Solver failed!", this);
        }
    }

    private void RebuildMeshLocally(LevelData localLevelData)
    {
        if (runtimeRepo == null || localLevelData == null)
        {
            Debug.LogError($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Cannot rebuild mesh, RuntimeRepo or LevelData is null.");
            return;
        }

        Transform previousRoot = transform.Find("root");
        if (previousRoot != null)
        {
            Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Destroying previous AutoLevel 'root' GameObject before rebuild.");
            Destroy(previousRoot.gameObject);
        }

        if (meshBuilder != null)
        {
            Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Disposing previous MeshBuilder.");
            meshBuilder.Dispose();
            meshBuilder = null;
        }

        Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Creating new LevelMeshBuilder.");
        meshBuilder = new LevelMeshBuilder(localLevelData, runtimeRepo);

        Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Rebuilding mesh locally for bounds {localLevelData.bounds}...");
        meshBuilder.Rebuild(localLevelData.bounds);

        GameObject autoLevelRootGO = GameObject.Find("root");
        if (autoLevelRootGO != null && autoLevelRootGO.transform.parent == null)
        {
            Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Found AutoLevel 'root' GO '{autoLevelRootGO.name}' at scene root. Repositioning and reparenting...");

            autoLevelRootGO.transform.SetPositionAndRotation(transform.position, transform.rotation);
            autoLevelRootGO.transform.SetParent(transform, worldPositionStays: true);
            autoLevelRootGO.name = $"AutoLevelContent_{NetworkObjectId}";

            Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Positioned '{autoLevelRootGO.name}' at world {autoLevelRootGO.transform.position}, Parented under {transform.name}.");
        }
        else if (autoLevelRootGO != null && autoLevelRootGO.transform.parent != null)
        {
            Debug.LogWarning($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Found a 'root' GO, but it was already parented to {autoLevelRootGO.transform.parent.name}. Assuming it's not the one just created by AutoLevel or was handled differently.", autoLevelRootGO);
        }
        else
        {
            Debug.LogError($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Failed to find AutoLevel's generated 'root' GameObject at scene root after Rebuild! Level geometry will be at the wrong place.");
        }

        // ApplyMeshRootOffset();
        Debug.Log($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Local mesh rebuild complete. Mesh root at {m_meshRoot.localPosition}");
    }

    [ClientRpc]
    private void BuildLevelClientRpc(Vector3Int boundsPosition, Vector3Int boundsSize, int[] receivedBlockData)
    {
        Debug.Log($"NALG (Client {NetworkManager.Singleton.LocalClientId}): Received BuildLevelClientRpc.");

        if (!m_isAutoLevelInitialized) InitializeAutoLevelCommon();
        if (runtimeRepo == null) { Debug.LogError($"NALG (Client {NetworkManager.Singleton.LocalClientId}): RuntimeRepo null", this); return; }

        m_generationBounds = new BoundsInt(boundsPosition, boundsSize);

        LevelData clientLevelData = new LevelData(m_generationBounds);
        if (!DeserializeLevelData(clientLevelData, receivedBlockData))
        {
            Debug.LogError($"NALG (Client {NetworkManager.Singleton.LocalClientId}): Failed to deserialize LevelData!");
            return;
        }
        Debug.Log($"NALG (Client {NetworkManager.Singleton.LocalClientId}): Successfully deserialized LevelData for bounds {m_generationBounds}.");

        RebuildMeshLocally(clientLevelData);
    }

    private void ApplyMeshRootOffset()
    {
        if (m_generationBounds.size == Vector3Int.zero) 
        {
            Debug.LogWarning($"NALG ({NetworkObjectId} - IsServer:{IsServer}): Cannot apply offset, generation bounds size is zero.");
            return;
        }

        Vector3 centerOffset = new Vector3(
            -m_generationBounds.size.x * m_blockSize * 0.5f,
             0,
             -m_generationBounds.size.z * m_blockSize * 0.5f
        );

        if (m_meshRoot != null) 
        {
            m_meshRoot.localPosition = centerOffset;
            Debug.Log($"NALG ({NetworkObjectId} - IsServer: {IsServer}): Applied local offset {centerOffset} to mesh root '{m_meshRoot.name}'.");
        } 
    }

    private int[] SerializeLevelData(LevelData data)
    {
        if (data == null || data.Blocks == null)
        {
            Debug.LogError("NALG Serialize: Input LevelData or its Blocks array is null.");
            return null;
        }

        Array3D<int> blocks = data.Blocks; 
        Vector3Int size = blocks.Size;
        int totalBlocks = size.x * size.y * size.z;

        if (totalBlocks <= 0) return new int[0];

        int[] serializedData = new int[totalBlocks];
        int index1D = 0;

        for (int k = 0; k < size.z; ++k) 
        {
            for (int j = 0; j < size.y; ++j) 
            { 
                for (int i = 0; i < size.x; ++i) 
                { 
                    serializedData[index1D++] = blocks[k, j, i];
                }
            }
        }
        Debug.Log($"NALG Serialize: Serialized {index1D} block hashes.");
        return serializedData;
    }

    private bool DeserializeLevelData(LevelData data, int[] serializedData)
    {
        if (data == null || data.Blocks == null || serializedData == null)
        {
            Debug.LogError("NALG Deserialize: Input LevelData, its Blocks array, or serializedData is null.");
            return false;
        }

        Array3D<int> blocks = data.Blocks; 
        Vector3Int size = blocks.Size;
        int expectedTotal = size.x * size.y * size.z;

        if (serializedData.Length != expectedTotal)
        {
            Debug.LogError($"NALG Deserialize: Data size mismatch! Expected {expectedTotal}, got {serializedData.Length}");
            return false;
        }
        if (expectedTotal == 0) return true;

        int index1D = 0;
        for (int k = 0; k < size.z; ++k) 
        {
            for (int j = 0; j < size.y; ++j) 
            {
                for (int i = 0; i < size.x; ++i) 
                {
                    blocks[k, j, i] = serializedData[index1D++];
                }
            }
        }
        Debug.Log($"NALG Deserialize: Deserialized {index1D} block hashes into LevelData.");
        return true;
    }

    public override void OnNetworkDespawn()
    {
        CleanupAutoLevelRuntime();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        CleanupAutoLevelRuntime();
        base.OnDestroy();
    }

    private void CleanupAutoLevelRuntime()
    {
        string role = IsServer ? "Server" : $"Client {NetworkManager.Singleton?.LocalClientId ?? 0}";

        if (meshBuilder != null)
        {
            Debug.Log($"NALG ({NetworkObjectId} - {role}): Disposing MeshBuilder.");
            try { meshBuilder.Dispose(); } catch (System.Exception e) { Debug.LogException(e, this); }
            meshBuilder = null;
        }

        if (runtimeRepo != null)
        {
            Debug.Log($"NALG ({NetworkObjectId} - {role}): Disposing RuntimeRepo.");
            try { runtimeRepo.Dispose(); } catch (System.Exception e) { Debug.LogException(e, this); }
            runtimeRepo = null;
        }

        serverSolver = null;
        m_isAutoLevelInitialized = false;
        Debug.Log($"NALG ({NetworkObjectId} - {role}): Cleanup complete.");
    }
}
