using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;

public class NetworkUIHandler : MonoBehaviour
{
    [SerializeField] private Button m_hostButton;
    [SerializeField] private Button m_clientButton;
    [SerializeField] private TMP_InputField m_ipAddressInput;
    [SerializeField] private PlatformRoleManager m_platformRoleManager;

    void Awake()
    {
        if (m_platformRoleManager == null)
        {
            m_platformRoleManager = FindObjectOfType<PlatformRoleManager>();
            if (m_platformRoleManager == null)
            {
                Debug.LogWarning("NetworkUIHandler: PlatformRoleManager not found in scene!", this);
            }
        }

        m_hostButton.onClick.AddListener(() =>
        {
            if (m_platformRoleManager != null) m_platformRoleManager.SetupForRole(true);

            Debug.Log("STARTING HOST...");
            NetworkManager.Singleton.StartHost();
            HideButtons();
        });

        m_clientButton.onClick.AddListener(() =>
        {
            string ipAddress = m_ipAddressInput.text;
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                ipAddress = "127.0.0.1";
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            if (transport != null)
            {
                transport.ConnectionData.Address = ipAddress;
                Debug.Log($"Attempting to connect to Host at IP: {ipAddress}");
            }
            else
            {
                Debug.LogError("UnityTransport component not found on NetworkManager!");
                return;
            }

            if (m_platformRoleManager != null) m_platformRoleManager.SetupForRole(false);

            Debug.Log("STARTING CLIENT...");
            NetworkManager.Singleton.StartClient();
            HideButtons();
        });

    }

    void HideButtons()
    {
        m_hostButton.gameObject.SetActive(false);
        m_clientButton.gameObject.SetActive(false);
        m_ipAddressInput.gameObject.SetActive(false);
    }

    // Start is called before the first frame update
    void Start()
    {
        NetworkManager.Singleton.OnServerStarted += () => 
        {
            RoleManager.VRClientId = NetworkManager.Singleton.LocalClientId;
            Debug.Log($"Host started. Assigned VR Giant role to Client ID: {RoleManager.VRClientId}");
        };

        NetworkManager.Singleton.OnClientConnectedCallback += (clientId) => 
        {
            if (!NetworkManager.Singleton.IsHost)
            {
                if(RoleManager.AntClientId == ulong.MaxValue)
                {
                    RoleManager.AntClientId = clientId;
                    Debug.Log($"Client connected. Assigned Ant role to Client ID: {clientId}");
                }
            }
            else if (NetworkManager.Singleton.IsHost && clientId != NetworkManager.Singleton.LocalClientId)
            {  
                if(RoleManager.AntClientId == ulong.MaxValue)
                {
                    RoleManager.AntClientId = clientId;
                    Debug.Log($"Client {clientId} connected to Host. Assigned Ant role.");
                }
            }
        }; 

        NetworkManager.Singleton.OnClientDisconnectCallback += (clientId) => 
        {
            if (clientId == RoleManager.VRClientId)
            {
                RoleManager.VRClientId = ulong.MaxValue;
                Debug.Log($"VR Giant (Client {clientId}) disconnected.");
            }
            if (clientId == RoleManager.AntClientId) {
                RoleManager.AntClientId = ulong.MaxValue;
                Debug.Log($"Ant (Client {clientId}) disconnected.");
            }
        };
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
