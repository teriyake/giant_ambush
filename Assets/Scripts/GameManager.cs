using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public enum GamePhase
{
    WaitingForPlayers,
    Setup,
    LevelReady,
    Countdown,
    Playing,
    GameOver,
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    [SerializeField]
    private float roundDuration = 180f;

    [SerializeField]
    private float countdownDuration = 5f;

    // [SerializeField] private float captureHoldTime = 3.0f;

    [Header("References")]
    [SerializeField]
    private GameObject playerPrefab;

    private GameObject VROriginGO;

    public NetworkVariable<GamePhase> CurrentPhase = new NetworkVariable<GamePhase>(
        GamePhase.WaitingForPlayers
    );
    public NetworkVariable<float> RoundTimer = new NetworkVariable<float>(0f);
    public NetworkVariable<ulong> WinnerClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<ulong> VRClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<ulong> ARClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    private NetworkedAutoLevelGenerator _levelGeneratorInstance;
    private Transform _vrPlayerSpawnPoint;
    private bool _levelGenerated = false;
    private CreateRoom _activeRoomInstance = null;
    private Vector3 m_lastArTapPosition = Vector3.zero;

    [Header("VR Spawn Settings")]
    [SerializeField]
    private LayerMask vrSpawnObstructionLayers;

    [SerializeField]
    private float vrSpawnCheckRadius = 0.5f;

    [SerializeField]
    private float vrSpawnHeightOffset = 0.1f;

    [SerializeField]
    private int maxSpawnPlacementAttempts = 20;

    [SerializeField]
    private float minDistanceFromTap = 3.0f;

    [SerializeField]
    private float furnitureCheckHeight = 1.5f;

    [SerializeField]
    private float furnitureSpawnUnderOffset = 0.5f;

    [Header("Attack Settings")]
    [SerializeField]
    GameObject attackProjectilePrefab;

    [Header("Wind Settings")]
    [SerializeField]
    private float windEffectRadius = 2.5f;

    [SerializeField]
    private float windMaxDistance = 8f;

    [SerializeField]
    private float windForceFactor = 60f;

    [SerializeField]
    private LayerMask windAffectedLayers;

    [SerializeField]
    private float minStrengthToSlice = 7f;

    [SerializeField]
    private float objectPushConeAngle = 45f;

    [Header("World Wind VFX (for VR Player)")]
    [SerializeField]
    private GameObject worldWindVFXPrefab;

    [Tooltip("How long the world wind VFX object stays alive before being despawned.")]
    [SerializeField]
    private float worldWindVFXLifetime = 2.5f;

    private static readonly int WorldWindSpeedID = Shader.PropertyToID("WindSpeed");
    private static readonly int WorldParticleCountID = Shader.PropertyToID("ParticleCount");
    private static readonly string PlayWorldWindEventName = "OnBlowWind";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentPhase.Value = GamePhase.WaitingForPlayers;
            RoundTimer.Value = roundDuration;
            WinnerClientId.Value = ulong.MaxValue;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

            // StartCoroutine(FindLevelGenerator());
        }

        CurrentPhase.OnValueChanged += OnPhaseChanged;
        RoundTimer.OnValueChanged += OnTimerChanged;
        WinnerClientId.OnValueChanged += OnWinnerDetermined;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdatePhase(CurrentPhase.Value);
            UIManager.Instance.UpdateTimer(RoundTimer.Value);
            UIManager.Instance.UpdateWinnerText(WinnerClientId.Value);
        }

        if (PlatformRoleManager.Instance != null)
        {
            // Debug.Log(
            //     $"PlayerMovement (Owner: {OwnerClientId}): Platform not ready yet. Subscribing to OnPlatformReady event."
            // );
            // PlatformRoleManager.Instance.OnPlatformReady += ConfigureARCameraCulling;
        }
        else
        {
            Debug.LogError(
                $"PlayerMovement (Owner: {OwnerClientId}): PlatformRoleManager Instance not found on spawn!",
                this
            );
        }

        Debug.Log($"GameManager spawned. IsServer: {IsServer}, IsClient: {IsClient}");
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        CurrentPhase.OnValueChanged -= OnPhaseChanged;
        RoundTimer.OnValueChanged -= OnTimerChanged;
        WinnerClientId.OnValueChanged -= OnWinnerDetermined;

        if (Instance == this)
        {
            Instance = null;
        }
        base.OnNetworkDespawn();
    }

    public void Server_SetLastARTapPosition(Vector3 tapPosition)
    {
        if (!IsServer)
            return;
        m_lastArTapPosition = tapPosition;
        Debug.Log($"GameManager [Server]: Stored last AR tap position: {m_lastArTapPosition}");
    }

    public void Server_NotifyLevelReady(
        CreateRoom levelInstance,
        List<CreateRoom.ScatterPointInfo> scatterInfo
    )
    {
        if (!IsServer)
            return;

        Debug.Log($"GameManager [Server]: Received Level Ready notification from level generator.");
        _levelGenerated = true;
        _activeRoomInstance = levelInstance;

        Vector3 safeSpawnPoint;
        Quaternion spawnRotation = Quaternion.identity;

        if (
            CalculateSafeVRSpawnPoint(
                levelInstance,
                m_lastArTapPosition,
                scatterInfo,
                out safeSpawnPoint
            )
        )
        {
            Debug.Log($"GameManager [Server]: Calculated safe VR spawn point: {safeSpawnPoint}");
            MoveVRPlayerToSpawn(safeSpawnPoint, spawnRotation);
        }
        else
        {
            Debug.LogError(
                "GameManager [Server]: Failed to find a safe spawn point for the VR player after multiple attempts!"
            );
        }

        if (CurrentPhase.Value == GamePhase.Setup)
        {
            Debug.Log(
                "GameManager [Server]: Level Ready notification received while in Setup phase. Proceeding to LevelReady and starting countdown."
            );
            CurrentPhase.Value = GamePhase.LevelReady;
            StartCoroutine(StartGameCountdown());
        }
        else
        {
            Debug.LogWarning(
                $"GameManager [Server]: Level Ready notification received but CurrentPhase is {CurrentPhase.Value}. Not starting countdown yet (will start when both players connect)."
            );
        }
    }

    private bool CalculateSafeVRSpawnPoint(
        CreateRoom level,
        Vector3 arTapPosition,
        List<CreateRoom.ScatterPointInfo> scatterInfo,
        out Vector3 spawnPoint
    )
    {
        spawnPoint = Vector3.zero;
        if (level == null)
            return false;

        Bounds levelBounds = level.GetWorldBounds();
        List<Vector3> potentialFurnitureSpots = new List<Vector3>();

        Debug.Log(
            $"[SpawnCalc] Checking {scatterInfo?.Count ?? 0} scatter points for hiding spots."
        );
        if (scatterInfo != null)
        {
            foreach (var item in scatterInfo)
            {
                Debug.Log($"Checking furniture with bounds = {item.WorldBounds}...");
                if (
                    item.WorldBounds.size.y > 0.2f
                    && item.WorldBounds.size.y < furnitureCheckHeight
                    && item.WorldBounds.size.x > vrSpawnCheckRadius * 2f
                    && item.WorldBounds.size.z > vrSpawnCheckRadius * 2f
                )
                {
                    Vector3 pointUnder =
                        item.WorldPosition - (Vector3.up * furnitureSpawnUnderOffset);
                    pointUnder.y = levelBounds.min.y + vrSpawnHeightOffset;

                    if (
                        levelBounds.Contains(pointUnder)
                        && !Physics.CheckSphere(
                            pointUnder,
                            vrSpawnCheckRadius,
                            vrSpawnObstructionLayers
                        )
                    )
                    {
                        potentialFurnitureSpots.Add(pointUnder);
                        Debug.Log(
                            $"[SpawnCalc] Found potential safe spot under furniture at {pointUnder}"
                        );
                    }
                    // else { Debug.Log($"[SpawnCalc] Spot under {item.PrefabIndex} at {pointUnder} failed check (InBounds: {levelBounds.Contains(pointUnder)}, SphereCheck: {!Physics.CheckSphere(pointUnder, vrSpawnCheckRadius, vrSpawnObstructionLayers)})"); }
                }
                // else { Debug.Log($"[SpawnCalc] Furniture {item.PrefabIndex} skipped (Size: {item.WorldBounds.size})"); }
            }
        }

        if (potentialFurnitureSpots.Count > 0)
        {
            float maxDistSq = -1f;
            Vector3 bestSpot = potentialFurnitureSpots[0];

            foreach (var spot in potentialFurnitureSpots)
            {
                float distSq = (spot - arTapPosition).sqrMagnitude;
                if (distSq > maxDistSq)
                {
                    maxDistSq = distSq;
                    bestSpot = spot;
                }
            }
            spawnPoint = bestSpot;
            Debug.Log(
                $"[SpawnCalc] Selected furniture spot furthest from tap: {spawnPoint} (DistSq: {maxDistSq})"
            );
            return true;
        }

        spawnPoint = levelBounds.center;
        spawnPoint.y = levelBounds.min.y + vrSpawnHeightOffset;
        if (Physics.CheckSphere(spawnPoint, vrSpawnCheckRadius, vrSpawnObstructionLayers))
        {
            Debug.LogError("[SpawnCalc] Couldn't fins a suitable spawn location.");
        }
        return true;
    }

    public void RegisterLevelGenerator(NetworkedAutoLevelGenerator generator)
    {
        if (!IsServer || generator == null)
            return;

        Debug.Log(
            $"GameManager [Server]: NetworkedAutoLevelGenerator ({generator.NetworkObjectId}) registered."
        );

        Action completionHandler = null;
        Action destructionHandler = null;

        completionHandler = () =>
        {
            Debug.Log(
                $"GameManager [Server]: Received Level Generation Complete signal from Generator {generator.NetworkObjectId}."
            );
            _levelGenerated = true;
            CalculateAndSetVRSpawnPoint(generator);
            // MoveVRPlayerToSpawn();

            if (CurrentPhase.Value == GamePhase.Setup || CurrentPhase.Value == GamePhase.LevelReady)
            {
                Debug.Log(
                    "GameManager [Server]: Setting Phase to LevelReady and starting Countdown Coroutine."
                );
                CurrentPhase.Value = GamePhase.LevelReady;
                StartCoroutine(StartGameCountdown());
            }
            else
            {
                Debug.LogWarning(
                    $"GameManager [Server]: Level Gen Complete signal received but CurrentPhase is {CurrentPhase.Value}. Not starting countdown."
                );
            }

            if (generator != null)
            {
                generator.OnGenerationCompleteServer -= completionHandler;
                generator.OnDestroyedEvent -= destructionHandler;
            }
        };

        destructionHandler = () =>
        {
            Debug.Log(
                $"GameManager [Server]: Received Level Generator ({generator?.NetworkObjectId ?? 0}) Destroyed signal."
            );
            _levelGenerated = false;
            if (generator != null)
            {
                generator.OnGenerationCompleteServer -= completionHandler;
                generator.OnDestroyedEvent -= destructionHandler;
            }
            if (
                CurrentPhase.Value != GamePhase.GameOver
                && CurrentPhase.Value != GamePhase.WaitingForPlayers
            )
            {
                Debug.LogWarning(
                    "GameManager [Server]: Level Generator destroyed unexpectedly. Resetting phase."
                );
                CurrentPhase.Value = GamePhase.WaitingForPlayers;
            }
        };

        generator.OnGenerationCompleteServer += completionHandler;
        generator.OnDestroyedEvent += destructionHandler;
    }

    private void HandleLevelGeneratorDestroyed()
    {
        if (!IsServer)
            return;
        Debug.Log("GameManager [Server]: Received Level Generator Destroyed signal.");
        if (_levelGeneratorInstance != null)
        {
            _levelGeneratorInstance.OnDestroyedEvent -= HandleLevelGeneratorDestroyed;
        }
        _levelGeneratorInstance = null;
        _levelGenerated = false;

        if (
            CurrentPhase.Value != GamePhase.GameOver
            && CurrentPhase.Value != GamePhase.WaitingForPlayers
        )
        {
            Debug.LogWarning(
                "GameManager [Server]: Level Generator destroyed unexpectedly. Resetting phase."
            );
            CurrentPhase.Value = GamePhase.WaitingForPlayers;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer)
            return;
        Debug.Log(
            $"GameManager: Client {clientId} connected. Total clients: {NetworkManager.Singleton.ConnectedClients.Count}"
        );

        if (NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            Debug.Log("GameManager: Both players connected. Transitioning to Setup phase.");
            CurrentPhase.Value = GamePhase.Setup;

            //
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer)
            return;
        Debug.Log($"GameManager: Client {clientId} disconnected.");
        if (
            CurrentPhase.Value != GamePhase.WaitingForPlayers
            && CurrentPhase.Value != GamePhase.GameOver
        )
        {
            Debug.Log(
                "GameManager: A player disconnected during the game. Resetting to WaitingForPlayers."
            );
            CurrentPhase.Value = GamePhase.WaitingForPlayers;
            WinnerClientId.Value = ulong.MaxValue;
            _levelGenerated = false;
        }
    }

    private void CalculateAndSetVRSpawnPoint(NetworkedAutoLevelGenerator generator)
    {
        if (!IsServer || generator == null)
            return;

        BoundsInt bounds = generator.GenerationBounds;
        float blockSize = generator.BlockSize;
        Transform generatorTransform = generator.transform;

        Vector3 localSpawnOffset = new Vector3(
            (bounds.min.x + 1.5f) * blockSize,
            bounds.min.y * blockSize + 0.1f,
            (bounds.min.z + 1.5f) * blockSize
        );

        Vector3 worldSpawnPosition =
            generatorTransform.position + generatorTransform.rotation * localSpawnOffset;

        if (_vrPlayerSpawnPoint == null)
        {
            _vrPlayerSpawnPoint = new GameObject("VRSpawnPoint_Helper").transform;
        }
        _vrPlayerSpawnPoint.position = worldSpawnPosition;
        _vrPlayerSpawnPoint.position += new Vector3(0.0f, 0.5f, 0.0f);
        _vrPlayerSpawnPoint.rotation = generatorTransform.rotation;

        Debug.Log(
            $"GameManager: Calculated VR Spawn Point at world coordinates: {worldSpawnPosition} relative to Generator {generator.NetworkObjectId}"
        );
    }

    private void MoveVRPlayerToSpawn(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
            return;

        ulong vrClientId = VRClientId.Value;

        if (vrClientId != ulong.MaxValue)
        {
            ClientRpcParams vrClientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { vrClientId } },
            };
            TeleportVRPlayerClientRpc(position, rotation, vrClientRpcParams);
            Debug.Log(
                $"GameManager [Server]: Sent TeleportVRPlayerClientRpc to Client {vrClientId} -> Pos: {position}"
            );
        }
        else
        {
            Debug.LogError(
                $"GameManager [Server]: VR Client ID is not set ({vrClientId}). Cannot teleport VR player!"
            );
        }
    }

    [ClientRpc]
    private void TeleportVRPlayerClientRpc(
        Vector3 targetPosition,
        Quaternion targetRotation,
        ClientRpcParams rpcParams = default
    )
    {
        Debug.Log(
            $"GameManager [Client {NetworkManager.Singleton.LocalClientId}]: Received TeleportVRPlayerClientRpc. Target: {targetPosition}"
        );

        if (!IsOwner && NetworkManager.Singleton.LocalClientId != VRClientId.Value)
        {
            Debug.LogWarning(
                $"GameManager [Client {NetworkManager.Singleton.LocalClientId}]: Ignoring TeleportVRPlayerClientRpc as this is not the designated VR client ({VRClientId.Value})."
            );
            return;
        }

        GameObject vrOriginGO = null;
        var vrOrigins = GameObject.FindGameObjectsWithTag("VROrigin");
        if (vrOrigins.Length > 0)
        {
            vrOriginGO = vrOrigins[0];
        }

        if (vrOriginGO != null)
        {
            CharacterController cc = vrOriginGO.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                vrOriginGO.transform.position = targetPosition;
                vrOriginGO.transform.rotation = targetRotation;
                cc.enabled = true;
            }
            else
            {
                vrOriginGO.transform.position = targetPosition;
                vrOriginGO.transform.rotation = targetRotation;
            }
        }
        else
        {
            Debug.LogError(
                $"GameManager [Client {NetworkManager.Singleton.LocalClientId}]: Could not find GameObject with tag 'VROrigin' to teleport!"
            );
        }
    }

    IEnumerator StartGameCountdown()
    {
        if (!IsServer)
            yield break;
        Debug.Log("GameManager: Starting Countdown phase.");
        CurrentPhase.Value = GamePhase.Countdown;
        RoundTimer.Value = countdownDuration;

        while (RoundTimer.Value > 0)
        {
            yield return null;
        }

        Debug.Log("GameManager: Countdown finished. Starting Playing phase.");
        CurrentPhase.Value = GamePhase.Playing;
        RoundTimer.Value = roundDuration;
    }

    void Update()
    {
        if (!IsServer)
            return;

        switch (CurrentPhase.Value)
        {
            case GamePhase.WaitingForPlayers:
                break;
            case GamePhase.Setup:
                break;
            case GamePhase.LevelReady:
                break;
            case GamePhase.Countdown:
                RoundTimer.Value -= Time.deltaTime;
                if (RoundTimer.Value <= 0) { }
                break;
            case GamePhase.Playing:
                RoundTimer.Value -= Time.deltaTime;

                if (RoundTimer.Value <= 0)
                {
                    Debug.Log("GameManager: Timer expired. Ant wins.");
                    EndGame(RoleManager.GetVRClientId());
                }
                break;
            case GamePhase.GameOver:
                break;
        }
    }

    private void CheckForCapture() { }

    [ServerRpc(RequireOwnership = false)]
    public void RequestCaptureAttemptServerRpc(
        ulong targetNetworkObjectId,
        ServerRpcParams rpcParams = default
    )
    {
        Debug.Log(
            $"GameManager: RequestCaptureAttemptServerRpc called by Client {rpcParams.Receive.SenderClientId} for target {targetNetworkObjectId}"
        );
        ulong requestingClientId = rpcParams.Receive.SenderClientId;

        Debug.Log(
            $"GameManager: RequestCaptureAttemptServerRpc - Phase: {CurrentPhase.Value}, RequestingClient: {requestingClientId}, ARClientId: {ARClientId.Value}"
        );
        if (CurrentPhase.Value != GamePhase.Playing || requestingClientId != ARClientId.Value)
        {
            Debug.LogWarning(
                $"GameManager: Received invalid capture attempt from Client {requestingClientId} during phase {CurrentPhase.Value}. Ignoring."
            );
            return;
        }

        ClientRpcParams vrClientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { VRClientId.Value } },
        };

        if (
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                targetNetworkObjectId,
                out NetworkObject targetObject
            )
        )
        {
            NotifyCaptureAttemptClientRpc(targetObject.transform.position, vrClientRpcParams);
        }
        else
        {
            Debug.LogWarning(
                $"GameManager: Could not find target object with ID {targetNetworkObjectId} to get position for capture attempt VFX."
            );
        }

        NetworkObject captorObject = null;
        if (
            NetworkManager.Singleton.ConnectedClients.TryGetValue(
                requestingClientId,
                out NetworkClient captorClient
            )
            && captorClient.PlayerObject != null
        )
        {
            captorObject = captorClient.PlayerObject;
        }

        if (
            VRClientId.Value != ulong.MaxValue
            && NetworkManager.Singleton.ConnectedClients.TryGetValue(
                VRClientId.Value,
                out NetworkClient vrClient
            )
            && vrClient.PlayerObject != null
        )
        {
            ulong vrPlayerNetworkId = vrClient.PlayerObject.NetworkObjectId;

            if (targetNetworkObjectId == vrPlayerNetworkId)
            {
                Debug.Log(
                    $"GameManager: Successful capture attempt by Giant (Client {requestingClientId}) on Ant (Object ID {targetNetworkObjectId}). Giant wins."
                );
                EndGame(ARClientId.Value);
                if (captorObject != null)
                {
                    NotifyCaptureSuccessClientRpc(
                        requestingClientId,
                        captorObject.NetworkObjectId,
                        targetNetworkObjectId
                    );
                }
                else
                {
                    Debug.LogError(
                        $"GameManager: Could not find captor's NetworkObject for client {requestingClientId} to send CaptureSuccess RPC!"
                    );
                }
            }
            else
            {
                Debug.Log(
                    $"GameManager: Failed capture attempt by Giant (Client {requestingClientId}). Target ID {targetNetworkObjectId} is not the Ant (ID {vrPlayerNetworkId})."
                );
                NotifyCaptureFailClientRpc(requestingClientId, targetNetworkObjectId);
            }
        }
        else
        {
            Debug.LogError("GameManager: Could not find VR player object for capture check.");
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { requestingClientId },
                },
            };
            NotifyCaptureFailClientRpc(requestingClientId, targetNetworkObjectId, clientRpcParams);
        }
    }

    private void EndGame(ulong winnerId)
    {
        if (!IsServer || CurrentPhase.Value == GamePhase.GameOver)
            return;

        CurrentPhase.Value = GamePhase.GameOver;
        WinnerClientId.Value = winnerId;
        Debug.Log($"GameManager: Game Over! Winner: Client {winnerId}");

        // StartCoroutine(GameOverSequence());
    }

    private void OnPhaseChanged(GamePhase previous, GamePhase current)
    {
        Debug.Log(
            $"[{(IsServer ? "Server" : "Client")} {NetworkManager.Singleton?.LocalClientId ?? 0}] OnPhaseChanged: {previous} -> {current}"
        );

        if (UIManager.Instance != null)
        {
            Debug.Log(
                $"[{(IsServer ? "Server" : "Client")} {NetworkManager.Singleton?.LocalClientId ?? 0}] UIManager.Instance found. Calling UpdatePhase({current})."
            );
            UIManager.Instance.UpdatePhase(current);
        }
        else
        {
            Debug.LogError(
                $"[{(IsServer ? "Server" : "Client")} {NetworkManager.Singleton?.LocalClientId ?? 0}] UIManager.Instance is NULL when trying to update phase to {current}!"
            );
        }
    }

    private void OnTimerChanged(float previous, float current)
    {
        if (UIManager.Instance != null)
            UIManager.Instance.UpdateTimer(current);
    }

    private void OnWinnerDetermined(ulong previous, ulong current)
    {
        Debug.Log($"Client received winner update: Client {current}");
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateWinnerText(current);
        }
    }

    [ClientRpc]
    private void NotifyCaptureSuccessClientRpc(
        ulong captorClientId,
        ulong captorObjectId,
        ulong capturedObjectId,
        ClientRpcParams clientRpcParams = default
    )
    {
        Debug.Log(
            $"Client {NetworkManager.Singleton.LocalClientId}: Received successful capture notification. Captor Client: {captorClientId}, Captor Object: {captorObjectId}, Captured Object: {capturedObjectId}"
        );

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogError(
                $"[Client {NetworkManager.Singleton?.LocalClientId}] NotifyCaptureSuccessClientRpc: NetworkManager or SpawnManager is null!"
            );
            return;
        }

        if (
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                captorObjectId,
                out NetworkObject captorObject
            )
        )
        {
            PlayerFeedback captorFeedback = captorObject.GetComponent<PlayerFeedback>();
            if (captorFeedback != null)
            {
                captorFeedback.PlayCaptureSuccessEffect();
                Debug.Log(
                    $"[Client {NetworkManager.Singleton.LocalClientId}] Played capture success effect for Captor Object {captorObjectId}"
                );
            }
            else
            {
                Debug.LogWarning(
                    $"[Client {NetworkManager.Singleton.LocalClientId}] Captor Object {captorObjectId} has no PlayerFeedback component."
                );
            }
        }
        else
        {
            Debug.LogWarning(
                $"[Client {NetworkManager.Singleton.LocalClientId}] Could not find Captor NetworkObject with ID {captorObjectId} to play success feedback."
            );
        }

        if (
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                capturedObjectId,
                out NetworkObject capturedObject
            )
        )
        {
            PlayerFeedback capturedFeedback = capturedObject.GetComponent<PlayerFeedback>();
            if (capturedFeedback != null)
            {
                capturedFeedback.PlayCapturedEffect();
                Debug.Log(
                    $"[Client {NetworkManager.Singleton.LocalClientId}] Played captured effect for Captured Object {capturedObjectId}"
                );
            }
            else
            {
                Debug.LogWarning(
                    $"[Client {NetworkManager.Singleton.LocalClientId}] Captured Object {capturedObjectId} has no PlayerFeedback component."
                );
            }
        }
        else
        {
            Debug.LogWarning(
                $"[Client {NetworkManager.Singleton.LocalClientId}] Could not find Captured NetworkObject with ID {capturedObjectId} to play captured feedback."
            );
        }
    }

    [ClientRpc]
    private void NotifyCaptureAttemptClientRpc(
        Vector3 targetPosition,
        ClientRpcParams clientRpcParams = default
    )
    {
        Debug.Log(
            $"Client {NetworkManager.Singleton.LocalClientId}: Received capture attempt notification at position {targetPosition}."
        );

        if (
            VRClientId.Value != ulong.MaxValue
            && NetworkManager.Singleton.ConnectedClients.TryGetValue(
                VRClientId.Value,
                out NetworkClient vrClient
            )
            && vrClient.PlayerObject != null
        )
        {
            PlayerFeedback vrPlayerFeedback = vrClient.PlayerObject.GetComponent<PlayerFeedback>();
            vrPlayerFeedback?.PlayCaptureAttemptEffect(targetPosition);
        }
        else
        {
            Debug.LogWarning(
                $"GameManager Client: Could not find VR PlayerObject to play capture attempt feedback."
            );
        }
    }

    [ClientRpc]
    private void NotifyCaptureFailClientRpc(
        ulong attackerClientId,
        ulong targetObjectId,
        ClientRpcParams clientRpcParams = default
    )
    {
        Debug.Log(
            $"Client {NetworkManager.Singleton.LocalClientId}: Received failed capture notification. Attacker: {attackerClientId}, Target: {targetObjectId}"
        );

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            return;

        if (
            NetworkManager.Singleton.ConnectedClients.TryGetValue(
                attackerClientId,
                out NetworkClient attackerClient
            )
            && attackerClient.PlayerObject != null
        )
        {
            PlayerFeedback attackerFeedback =
                attackerClient.PlayerObject.GetComponent<PlayerFeedback>();
            attackerFeedback?.PlayCaptureFailEffect();
        }
        else
        {
            Debug.LogWarning(
                $"GameManager Client: Could not find PlayerObject for attacker client {attackerClientId} to play fail feedback."
            );
        }

        // if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetObjectId, out NetworkObject targetObject))
        // {
        //     PlayerFeedback targetFeedback = targetObject.GetComponent<PlayerFeedback>();
        //     targetFeedback?.PlayMissedEffect();
        // }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestAttackServerRpc(
        Vector3 origin,
        Vector3 direction,
        float projectileSpeed,
        ServerRpcParams rpcParams = default
    )
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;

        if (CurrentPhase.Value != GamePhase.Playing)
        {
            Debug.LogWarning(
                $"Attack requested by {requestingClientId} but game phase is {CurrentPhase.Value}. Ignoring."
            );
            return;
        }
        if (requestingClientId != ARClientId.Value)
        {
            Debug.LogWarning(
                $"Attack requested by {requestingClientId} but the AR Giant is {ARClientId.Value}. Ignoring."
            );
            return;
        }
        if (attackProjectilePrefab == null)
        {
            Debug.LogError("AttackProjectilePrefab is not assigned in GameManager!");
            return;
        }

        Debug.Log(
            $"Server received attack request from AR Client {requestingClientId}. Spawning projectile."
        );

        GameObject projectileGO = Instantiate(
            attackProjectilePrefab,
            origin,
            Quaternion.LookRotation(direction)
        );

        NetworkObject projectileNO = projectileGO.GetComponent<NetworkObject>();
        if (projectileNO != null)
        {
            projectileNO.Spawn(true);

            NetworkedProjectile projectileScript = projectileGO.GetComponent<NetworkedProjectile>();
            if (projectileScript != null)
            {
                projectileScript.Initialize(origin, direction, projectileSpeed, requestingClientId);
            }
            else
            {
                Debug.LogError("Spawned projectile is missing NetworkedProjectile script!");
            }
        }
        else
        {
            Debug.LogError("AttackProjectilePrefab is missing NetworkObject component!");
            Destroy(projectileGO);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReportProjectileHitServerRpc(
        ulong targetClientId,
        ServerRpcParams rpcParams = default
    )
    {
        Debug.Log($"[Server] Received projectile hit report targeting Client ID: {targetClientId}");

        if (CurrentPhase.Value == GamePhase.Playing && targetClientId == VRClientId.Value)
        {
            NetworkObject arPlayerObject = null;
            if (
                NetworkManager.Singleton.ConnectedClients.TryGetValue(
                    ARClientId.Value,
                    out NetworkClient arClient
                )
                && arClient.PlayerObject != null
            )
            {
                arPlayerObject = arClient.PlayerObject;
            }

            NetworkObject vrPlayerObject = null;
            if (
                NetworkManager.Singleton.ConnectedClients.TryGetValue(
                    VRClientId.Value,
                    out NetworkClient vrClient
                )
                && vrClient.PlayerObject != null
            )
            {
                vrPlayerObject = vrClient.PlayerObject;
            }

            if (arPlayerObject != null && vrPlayerObject != null)
            {
                Debug.Log(
                    $"[Server] Confirmed hit on VR Player (Object {vrPlayerObject.NetworkObjectId}, Client {targetClientId}). AR Player (Object {arPlayerObject.NetworkObjectId}, Client {ARClientId.Value}) wins!"
                );
                EndGame(ARClientId.Value);

                NotifyCaptureSuccessClientRpc(
                    ARClientId.Value,
                    arPlayerObject.NetworkObjectId,
                    vrPlayerObject.NetworkObjectId
                );
            }
            else
            {
                Debug.LogError(
                    $"[Server] Could not find NetworkObject for AR Player ({ARClientId.Value}) or VR Player ({VRClientId.Value}) to process projectile hit!"
                );
            }
        }
        else
        {
            Debug.LogWarning(
                $"[Server] Projectile hit report for Client {targetClientId} ignored. Target is not VR player ({VRClientId.Value}) or game phase is not Playing ({CurrentPhase.Value})."
            );
        }
    }

    private void ConfigureARCameraCulling()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            return;
        }

        if (RoleManager.GetARClientId() == ulong.MaxValue)
        {
            Debug.LogWarning(
                "GameManager: AR Client ID not yet assigned in RoleManager. Cannot configure camera culling."
            );
            return;
        }

        if (RoleManager.IsClientAR(NetworkManager.Singleton.LocalClientId))
        {
            Debug.Log(
                $"GameManager: Local client {NetworkManager.Singleton.LocalClientId} is the AR player. Configuring camera culling."
            );
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                int secretLayer = LayerMask.NameToLayer("Wall");
                if (secretLayer != -1)
                {
                    mainCamera.cullingMask &= ~(1 << secretLayer);
                    Debug.Log($"GameManager: Hid layer 'Wall' ({secretLayer}) from AR camera.");
                }
                else
                {
                    Debug.LogWarning("GameManager: Layer 'Wall' not found!");
                }
            }
            else
            {
                Debug.LogError("GameManager: Main camera not found!");
            }
        }
        else
        {
            Debug.Log(
                $"GameManager: Local client {NetworkManager.Singleton.LocalClientId} is not the AR player. No camera culling needed."
            );
        }
    }

    public void HandleWindEffect(
        Vector3 origin,
        Vector3 direction,
        float strength,
        ulong instigatorClientId
    )
    {
        if (!IsServer)
            return;
        if (CurrentPhase.Value != GamePhase.Playing)
            return;
        if (instigatorClientId != ARClientId.Value)
        {
            Debug.LogWarning(
                $"[Server] Wind effect triggered by non-AR client {instigatorClientId}. Ignoring."
            );
            return;
        }

        Debug.Log(
            $"[Server] Handling wind. Origin: {origin}, Dir: {direction}, Strength: {strength}"
        );

        if (worldWindVFXPrefab != null)
        {
            GameObject vfxInstance = Instantiate(
                worldWindVFXPrefab,
                origin,
                Quaternion.LookRotation(direction)
            );
            NetworkObject vfxNetObj = vfxInstance.GetComponent<NetworkObject>();
            if (vfxNetObj != null)
            {
                vfxNetObj.Spawn(true);

                VisualEffect worldVFX = vfxInstance.GetComponent<VisualEffect>();
                if (worldVFX != null)
                {
                    float vfxSpeed = 5f + (strength * 0.5f);
                    int vfxCount = 100 + (int)(strength * 10f);

                    worldVFX.SetFloat(WorldWindSpeedID, vfxSpeed);
                    worldVFX.SetInt(WorldParticleCountID, vfxCount);
                    TriggerWorldWindVFXClientRpc(vfxNetObj.NetworkObjectId, vfxSpeed, vfxCount);
                }
                else
                {
                    Debug.LogError(
                        "[Server] Spawned worldWindVFXPrefab is missing VisualEffect component!"
                    );
                }

                StartCoroutine(DespawnNetworkedObject(vfxInstance, worldWindVFXLifetime));
            }
            else
            {
                Debug.LogError(
                    "[Server] worldWindVFXPrefab is missing NetworkObject component! Cannot spawn.",
                    worldWindVFXPrefab
                );
                Destroy(vfxInstance);
            }
        }

        Vector3 checkCenter = origin + direction * (windMaxDistance * 0.5f);
        Collider[] hitColliders = Physics.OverlapSphere(
            checkCenter,
            windEffectRadius,
            windAffectedLayers
        );

        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.gameObject.transform.root == transform)
                continue;

            Vector3 objectDirection = (hitCollider.transform.position - origin).normalized;
            float angleToObject = Vector3.Angle(direction, objectDirection);

            if (angleToObject > objectPushConeAngle)
            {
                continue;
            }

            float distance = Vector3.Distance(origin, hitCollider.transform.position);
            float distanceFactor = Mathf.Clamp01(1f - (distance / windMaxDistance));
            float effectiveStrength = strength * distanceFactor;

            Rigidbody rb = hitCollider.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.AddForce(direction * effectiveStrength * windForceFactor, ForceMode.Impulse);

                Debug.Log($"[Server] Applied wind force to {hitCollider.name}");
            }
            else
            {
                Debug.LogError(
                    $"[Server] Cannot apply wind force to {hitCollider.name}. Make sure it has a Rigidbody and is not kinematic!"
                );
            }

            SliceThis slicer = hitCollider.GetComponentInParent<SliceThis>();
            if (slicer != null && effectiveStrength >= minStrengthToSlice)
            {
                Vector3 slicePosition = hitCollider.bounds.center;
                Vector3 sliceNormal = (
                    direction + UnityEngine.Random.insideUnitSphere * 0.2f
                ).normalized;

                slicer.SliceObjectServerRpc(sliceNormal, slicePosition, effectiveStrength);

                Debug.Log($"[Server] Requested slice for {hitCollider.name}");
            }
        }
    }

    [ClientRpc]
    private void TriggerWorldWindVFXClientRpc(ulong vfxNetworkId, float speed, int count)
    {
        if (
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                vfxNetworkId,
                out NetworkObject netObj
            )
        )
        {
            VisualEffect vfx = netObj.GetComponent<VisualEffect>();
            if (vfx != null)
            {
                vfx.SetFloat(WorldWindSpeedID, speed);
                vfx.SetInt(WorldParticleCountID, count);
                vfx.SendEvent(PlayWorldWindEventName);

                Debug.Log(
                    $"Client {NetworkManager.Singleton.LocalClientId} triggered world wind VFX ({vfxNetworkId})"
                );
            }
        }
    }
}