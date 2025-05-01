using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerAppearance : NetworkBehaviour
{
    [Header("VR Host (Ant)")]
    [SerializeField]
    private Material m_hostMaterial;

    [SerializeField]
    private Mesh m_hostMesh;

    [Header("AR Client (Giant)")]
    [SerializeField]
    private Material m_clientMaterial;

    [SerializeField]
    private Mesh m_clientMesh;

    private NetworkVariable<bool> m_useHostAppearance = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Renderer m_objectRenderer;
    private MeshFilter m_meshFilter;
    private bool m_isGloballyVisible = false;

    void Awake()
    {
        m_objectRenderer = GetComponentInChildren<Renderer>();
        if (m_objectRenderer == null)
        {
            Debug.LogError(
                "PlayerAppearance: No Renderer found on this object or its children!",
                this
            );
        }

        m_meshFilter = GetComponentInChildren<MeshFilter>();
        if (m_meshFilter == null)
        {
            Debug.LogError(
                "PlayerAppearance: No MeshFilter found on this object or its children!",
                this
            );
        }
    }

    public override void OnNetworkSpawn()
    {
        m_useHostAppearance.OnValueChanged += OnAppearanceChanged;

        if (m_objectRenderer == null || m_meshFilter == null)
            return;

        if (IsServer)
        {
            bool isHostOwned = (OwnerClientId == NetworkManager.ServerClientId);
            m_useHostAppearance.Value = isHostOwned;
            Debug.Log(
                $"Server setting appearance for NetworkObject {NetworkObjectId} (Owned by {OwnerClientId}). IsHostAppearance: {isHostOwned}"
            );
        }

        if (IsOwner)
        {
            m_objectRenderer.enabled = false;
        }

        ApplyAppearance(m_useHostAppearance.Value);

        SetVisibility(false);
        if (GameManager.Instance != null)
        {
            HandlePhaseChange(GamePhase.WaitingForPlayers, GameManager.Instance.CurrentPhase.Value);
            GameManager.Instance.CurrentPhase.OnValueChanged += HandlePhaseChange;
        }
        else
        {
            StartCoroutine(WaitForGameManagerAndSubscribe());
        }
    }

    public override void OnNetworkDespawn()
    {
        m_useHostAppearance.OnValueChanged -= OnAppearanceChanged;
    }

    private IEnumerator WaitForGameManagerAndSubscribe()
    {
        while (GameManager.Instance == null)
        {
            yield return null;
        }
        HandlePhaseChange(GamePhase.WaitingForPlayers, GameManager.Instance.CurrentPhase.Value);
        GameManager.Instance.CurrentPhase.OnValueChanged += HandlePhaseChange;
    }

    private void HandlePhaseChange(GamePhase previousPhase, GamePhase currentPhase)
    {
        bool shouldBeVisible = currentPhase == GamePhase.LevelReady ||
                               currentPhase == GamePhase.Countdown ||
                               currentPhase == GamePhase.Playing;

        Debug.Log($"PlayerAppearance ({NetworkObjectId}) HandlePhaseChange: {currentPhase}. ShouldBeVisible: {shouldBeVisible}");
        SetVisibility(shouldBeVisible);
    }

    public void SetVisibility(bool visible)
    {
        m_isGloballyVisible = visible;

        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (m_objectRenderer == null) return;

        if (IsOwner)
        {
            m_objectRenderer.enabled = false;
        }
        else
        {
            m_objectRenderer.enabled = m_isGloballyVisible;
        }
    }


    private void OnAppearanceChanged(bool previousValue, bool newValue)
    {
        Debug.Log(
            $"OnAppearanceChanged for {NetworkObjectId} on client {NetworkManager.LocalClientId}. New Value: {newValue}"
        );
        ApplyAppearance(newValue);
    }

    private void ApplyAppearance(bool useHostAppearance)
    {
        if (m_objectRenderer == null || m_meshFilter == null)
            return;

        Material materialToApply = useHostAppearance ? m_hostMaterial : m_clientMaterial;
        Mesh meshToApply = useHostAppearance ? m_hostMesh : m_clientMesh;

        if (materialToApply != null)
        {
            m_objectRenderer.material = materialToApply;
        }
        else
        {
            Debug.LogWarning(
                $"Material is not assigned for appearance type (IsHost: {useHostAppearance}).",
                this
            );
        }

        if (meshToApply != null)
        {
            m_meshFilter.sharedMesh = meshToApply;
            transform.rotation = Quaternion.Euler(-90, 0, 0);
        }
        else
        {
            Debug.LogWarning(
                $"Mesh is not assigned for appearance type (IsHost: {useHostAppearance}).",
                this
            );
        }

        ApplyVisibility();
    }
}
