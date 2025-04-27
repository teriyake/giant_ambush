using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public static class RoleManager
{
    public static ulong GetVRClientId()
    {
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.VRClientId.Value;
        }

        Debug.LogWarning(
            "RoleManager: GameManager.Instance is null, cannot get networked VRClientId."
        );
        return ulong.MaxValue;
    }

    public static ulong GetARClientId()
    {
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.ARClientId.Value;
        }

        Debug.LogWarning(
            "RoleManager: GameManager.Instance is null, cannot get networked ARClientId."
        );
        return ulong.MaxValue;
    }

    public static bool IsClientVR(ulong clientId)
    {
        return clientId == GetVRClientId();
    }

    public static bool IsClientAR(ulong clientId)
    {
        return clientId == GetARClientId();
    }
}