using System;
using System.Collections;
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

        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdatePhase(CurrentPhase.Value);
            GameHUD.Instance.UpdateTimer(RoundTimer.Value);
            GameHUD.Instance.UpdateWinnerText(WinnerClientId.Value);
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

    public void Server_NotifyLevelReady(GameObject levelRootObject)
    {
        if (!IsServer) return;

        Debug.Log($"GameManager [Server]: Received Level Ready notification from level generator.");
        _levelGenerated = true;

       
        Debug.LogWarning("GameManager [Server]: TODO: Calculate VR Spawn point CreateRoom.");
        
       
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
            MoveVRPlayerToSpawn();

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

    private void MoveVRPlayerToSpawn()
    {
        if (!IsServer || _vrPlayerSpawnPoint == null)
            return;

        ulong vrClientId = GameManager.Instance.VRClientId.Value;

        if (
            vrClientId != ulong.MaxValue
            && NetworkManager.Singleton.ConnectedClients.TryGetValue(
                vrClientId,
                out NetworkClient vrClient
            )
            && vrClient.PlayerObject != null
        )
        {
            GameObject VROriginGO = GameObject.FindGameObjectsWithTag("VROrigin")[0];
            if (VROriginGO != null)
            {
                Debug.Log(
                    $"GameManager: Moving VR Player (Client {RoleManager.VRClientId}, VR Origin: {VROriginGO.name}) to spawn point."
                );
                VROriginGO.transform.position = _vrPlayerSpawnPoint.position;
                // VROriginGO.transform.rotation = _vrPlayerSpawnPoint.rotation;
            }
            else
            {
                Debug.LogError("GameManager: Could not find VR Origin!");
            }
        }
        else
        {
            Debug.LogError(
                $"GameManager: Could not find VR player object (Client ID: {vrClientId}) or PlayerObject is null to move to spawn point!"
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
                    EndGame(RoleManager.ARClientId);
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
                NotifyCaptureSuccessClientRpc(requestingClientId, targetNetworkObjectId);
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
        Debug.Log($"[{ (IsServer ? "Server" : "Client") } { NetworkManager.Singleton?.LocalClientId ?? 0 }] OnPhaseChanged: {previous} -> {current}");

        if (GameHUD.Instance != null)
        {
            Debug.Log($"[{ (IsServer ? "Server" : "Client") } { NetworkManager.Singleton?.LocalClientId ?? 0 }] GameHUD.Instance found. Calling UpdatePhase({current}).");
            GameHUD.Instance.UpdatePhase(current);
        }
        else
        {
            Debug.LogError($"[{ (IsServer ? "Server" : "Client") } { NetworkManager.Singleton?.LocalClientId ?? 0 }] GameHUD.Instance is NULL when trying to update phase to {current}!");
        }
    }

    private void OnTimerChanged(float previous, float current)
    {
        if (GameHUD.Instance != null)
            GameHUD.Instance.UpdateTimer(current);
    }

    private void OnWinnerDetermined(ulong previous, ulong current)
    {
        Debug.Log($"Client received winner update: Client {current}");
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdateWinnerText(current);
        }
    }

    [ClientRpc]
    private void NotifyCaptureSuccessClientRpc(
        ulong captorClientId,
        ulong capturedObjectId,
        ClientRpcParams clientRpcParams = default
    )
    {
        Debug.Log(
            $"Client {NetworkManager.Singleton.LocalClientId}: Received successful capture notification. Captor: {captorClientId}, Captured: {capturedObjectId}"
        );

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            return;

        if (
            NetworkManager.Singleton.ConnectedClients.TryGetValue(
                captorClientId,
                out NetworkClient captorClient
            )
            && captorClient.PlayerObject != null
        )
        {
            PlayerFeedback captorFeedback =
                captorClient.PlayerObject.GetComponent<PlayerFeedback>();
            captorFeedback?.PlayCaptureSuccessEffect();
        }
        else
        {
            Debug.LogWarning(
                $"GameManager Client: Could not find PlayerObject for captor client {captorClientId} to play success feedback."
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
            capturedFeedback?.PlayCapturedEffect();
        }
        else
        {
            Debug.LogWarning(
                $"GameManager Client: Could not find NetworkObject with ID {capturedObjectId} to play captured feedback."
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
}