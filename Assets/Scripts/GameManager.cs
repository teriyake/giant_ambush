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

    [SerializeField]
    private float captureDistance = 2.0f;

    // [SerializeField] private float captureHoldTime = 3.0f;

    [Header("References")]
    [SerializeField]
    private GameObject playerPrefab;

    public NetworkVariable<GamePhase> CurrentPhase = new NetworkVariable<GamePhase>(
        GamePhase.WaitingForPlayers
    );
    public NetworkVariable<float> RoundTimer = new NetworkVariable<float>(0f);
    public NetworkVariable<ulong> WinnerClientId = new NetworkVariable<ulong>(ulong.MaxValue);
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
        _vrPlayerSpawnPoint.rotation = generatorTransform.rotation;

        Debug.Log(
            $"GameManager: Calculated VR Spawn Point at world coordinates: {worldSpawnPosition} relative to Generator {generator.NetworkObjectId}"
        );
    }

    private void MoveVRPlayerToSpawn()
    {
        if (!IsServer || _vrPlayerSpawnPoint == null)
            return;

        if (
            RoleManager.VRClientId != ulong.MaxValue
            && NetworkManager.Singleton.ConnectedClients.TryGetValue(
                RoleManager.VRClientId,
                out NetworkClient vrClient
            )
            && vrClient.PlayerObject != null
        )
        {
            Debug.Log(
                $"GameManager: Moving VR Player (Client {RoleManager.VRClientId}, GO: {vrClient.PlayerObject.name}) to spawn point."
            );
            vrClient.PlayerObject.transform.position = _vrPlayerSpawnPoint.position;
            vrClient.PlayerObject.transform.rotation = _vrPlayerSpawnPoint.rotation;
        }
        else
        {
            Debug.LogError("GameManager: Could not find VR player object to move to spawn point!");
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

                CheckForCapture();

                if (RoundTimer.Value <= 0)
                {
                    Debug.Log("GameManager: Timer expired. Ant wins.");
                    EndGame(RoleManager.AntClientId);
                }
                break;
            case GamePhase.GameOver:
                break;
        }
    }

    private void CheckForCapture()
    {
        if (!IsServer || CurrentPhase.Value != GamePhase.Playing)
            return;

        if (RoleManager.VRClientId == ulong.MaxValue || RoleManager.AntClientId == ulong.MaxValue)
            return;

        if (
            NetworkManager.Singleton.ConnectedClients.TryGetValue(
                RoleManager.VRClientId,
                out NetworkClient vrClient
            )
            && NetworkManager.Singleton.ConnectedClients.TryGetValue(
                RoleManager.AntClientId,
                out NetworkClient antClient
            )
            && vrClient.PlayerObject != null
            && antClient.PlayerObject != null
        )
        {
            float distance = Vector3.Distance(
                vrClient.PlayerObject.transform.position,
                antClient.PlayerObject.transform.position
            );

            if (distance <= captureDistance)
            {
                Debug.Log($"GameManager: Capture condition met! Distance: {distance}. Giant wins.");
                EndGame(RoleManager.VRClientId);
            }
        }
        else
        {
            Debug.LogWarning("GameManager: Could not find player objects for capture check.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestCaptureAttemptServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;
        if (requestingClientId == RoleManager.VRClientId)
        {
            Debug.Log(
                $"GameManager: Received capture attempt request from Giant (Client {requestingClientId}). Proximity check runs in Update."
            );
        }
        else
        {
            Debug.LogWarning(
                $"GameManager: Received capture attempt from non-Giant client {requestingClientId}. Ignoring."
            );
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
        Debug.Log($"Client received phase change: {previous} -> {current}");
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdatePhase(current);
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
}