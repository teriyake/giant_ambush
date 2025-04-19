using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class NetworkUIHandlerRelay : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField]
    private Button m_hostButton;

    [SerializeField]
    private Button m_clientButton;

    [SerializeField]
    private TMP_InputField m_joinCodeInput;

    [SerializeField]
    private TextMeshProUGUI m_joinCodeText;

    [SerializeField]
    private TextMeshProUGUI m_statusText;

    [Header("References")]
    [SerializeField]
    private PlatformRoleManager m_platformRoleManager;

    [Header("Prefabs")]
    [SerializeField]
    private GameObject m_gameManagerPrefab;

    private string _joinCode;
    private bool _gameManagerSpawned = false;

    async void Start()
    {
        if (m_platformRoleManager == null)
        {
            m_platformRoleManager = FindObjectOfType<PlatformRoleManager>();
            if (m_platformRoleManager == null)
            {
                Debug.LogWarning("NetworkUIHandler: PlatformRoleManager not found in scene!", this);
            }
        }

        await InitializeAndAuthenticateAsync();

        m_hostButton.onClick.AddListener(OnHostButtonClicked);
        m_clientButton.onClick.AddListener(OnClientButtonClicked);

        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        m_joinCodeText.gameObject.SetActive(false);
        SetStatus("Ready. Initialize/Authenticate.");
    }

    private async Task InitializeAndAuthenticateAsync()
    {
        try
        {
            SetStatus("Initializing Unity Services...");
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                SetStatus("Authenticating (Anonymous)...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log(
                    $"Player authenticated with ID: {AuthenticationService.Instance.PlayerId}"
                );
            }
            else
            {
                Debug.Log(
                    $"Player already authenticated with ID: {AuthenticationService.Instance.PlayerId}"
                );
            }
            SetStatus("Ready.");
            m_hostButton.interactable = true;
            m_clientButton.interactable = true;
        }
        catch (System.Exception e)
        {
            SetStatus($"Error initializing/authenticating: {e.Message}");
            Debug.LogError($"Unity Services Initialization or Authentication Failed: {e}");
            m_hostButton.interactable = false;
            m_clientButton.interactable = false;
        }
    }

    private async void OnHostButtonClicked()
    {
        SetStatus("Starting Host via Relay...");
        m_hostButton.interactable = false;
        m_clientButton.interactable = false;

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(
                maxConnections: 1
            );

            _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Host: Relay Join Code: {_joinCode}");
            m_joinCodeText.text = $"Join Code: {_joinCode}";
            m_joinCodeText.gameObject.SetActive(true);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("UnityTransport component not found on NetworkManager!");
                SetStatus("Error: UnityTransport missing.");
                m_hostButton.interactable = true;
                m_clientButton.interactable = true;
                return;
            }
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            if (m_platformRoleManager != null)
                m_platformRoleManager.SetupForRole(true);

            Debug.Log("Starting Host...");
            NetworkManager.Singleton.StartHost();
            HideInputUI();
            SetStatus($"Host started. Join Code: {_joinCode}");
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Failed to start Host via Relay: {e}");
            SetStatus($"Error starting host: {e.Message}");
            m_hostButton.interactable = true;
            m_clientButton.interactable = true;
            m_joinCodeText.gameObject.SetActive(false);
        }
    }

    private async void OnClientButtonClicked()
    {
        string joinCode = m_joinCodeInput.text;
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            SetStatus("Please enter a Join Code.");
            Debug.LogWarning("Join Code is empty.");
            return;
        }

        SetStatus($"Joining Relay with code: {joinCode}...");
        m_hostButton.interactable = false;
        m_clientButton.interactable = false;

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(
                joinCode
            );

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("UnityTransport component not found on NetworkManager!");
                SetStatus("Error: UnityTransport missing.");
                m_hostButton.interactable = true;
                m_clientButton.interactable = true;
                return;
            }

            transport.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            if (m_platformRoleManager != null)
                m_platformRoleManager.SetupForRole(false);

            Debug.Log($"Attempting to connect to Host via Relay Join Code: {joinCode}");
            NetworkManager.Singleton.StartClient();
            HideInputUI();
            SetStatus("Client started, connecting...");
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Failed to join Relay: {e}");
            SetStatus($"Error joining: {e.Message}");
            m_hostButton.interactable = true;
            m_clientButton.interactable = true;
        }
    }

    void HideInputUI()
    {
        m_hostButton.gameObject.SetActive(false);
        m_clientButton.gameObject.SetActive(false);
        m_joinCodeInput.gameObject.SetActive(false);
        if (!NetworkManager.Singleton.IsHost)
        {
            m_joinCodeText.gameObject.SetActive(false);
        }
    }

    private void SetStatus(string message)
    {
        if (m_statusText != null)
        {
            m_statusText.text = $"Status: {message}";
        }
        Debug.Log($"Status Update: {message}");
    }

    void OnServerStarted()
    {
        RoleManager.VRClientId = NetworkManager.Singleton.LocalClientId;
        Debug.Log($"Host started. Assigned VR Giant role to Client ID: {RoleManager.VRClientId}");
        SetStatus($"Host active. Waiting for client... Join Code: {_joinCode}");

        if (!_gameManagerSpawned)
        {
            if (m_gameManagerPrefab != null)
            {
                Debug.Log("Spawning GameManager...");
                GameObject gmInstance = Instantiate(m_gameManagerPrefab);
                NetworkObject networkObject = gmInstance.GetComponent<NetworkObject>();
                if (networkObject != null)
                {
                    networkObject.Spawn(true);
                    _gameManagerSpawned = true;
                    Debug.Log("GameManager spawned successfully.");
                }
                else
                {
                    Debug.LogError(
                        "GameManager prefab is missing NetworkObject component!",
                        m_gameManagerPrefab
                    );
                    Destroy(gmInstance);
                    SetStatus("Error: GameManager prefab invalid.");
                }
            }
            else
            {
                Debug.LogError(
                    "GameManager Prefab is not assigned in NetworkUIHandlerRelay!",
                    this
                );
                SetStatus("Error: GameManager prefab missing.");
            }
        }
        else
        {
            Debug.LogWarning("HandleServerStarted called but GameManager already spawned.");
        }
    }

    void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            if (RoleManager.AntClientId == ulong.MaxValue)
            {
                RoleManager.AntClientId = NetworkManager.Singleton.LocalClientId;
                Debug.Log(
                    $"Client connected to Host. Assigned Ant role to myself (Client ID: {clientId})"
                );
                SetStatus("Connected to host.");
            }
        }
        else
        {
            if (clientId != NetworkManager.Singleton.LocalClientId)
            {
                if (RoleManager.AntClientId == ulong.MaxValue)
                {
                    RoleManager.AntClientId = clientId;
                    Debug.Log($"Client {clientId} connected to Host. Assigned Ant role.");
                    SetStatus($"Client {clientId} connected.");
                }
            }
        }
    }

    void OnClientDisconnected(ulong clientId)
    {
        bool wasVR = false;
        bool wasAnt = false;

        if (clientId == RoleManager.VRClientId)
        {
            RoleManager.VRClientId = ulong.MaxValue;
            Debug.Log($"VR Giant (Client {clientId}) disconnected.");
            wasVR = true;
        }
        if (clientId == RoleManager.AntClientId)
        {
            RoleManager.AntClientId = ulong.MaxValue;
            Debug.Log($"Ant (Client {clientId}) disconnected.");
            wasAnt = true;
        }

        if (NetworkManager.Singleton.IsHost)
        {
            SetStatus($"Client {clientId} disconnected. Waiting for client...");
        }
        else if (clientId == NetworkManager.ServerClientId)
        {
            SetStatus("Disconnected from host.");
            // ShowInputUI();
        }
    }

    void ShowInputUI()
    {
        m_hostButton.gameObject.SetActive(true);
        m_clientButton.gameObject.SetActive(true);
        m_joinCodeInput.gameObject.SetActive(true);
        m_joinCodeText.gameObject.SetActive(false);
        m_hostButton.interactable = AuthenticationService.Instance.IsSignedIn;
        m_clientButton.interactable = AuthenticationService.Instance.IsSignedIn;
        SetStatus("Ready.");
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}