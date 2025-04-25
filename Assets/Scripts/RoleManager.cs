using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public static class RoleManager
{
    public static ulong VRClientId = ulong.MaxValue;
    public static ulong ARClientId = ulong.MaxValue;

    public static ulong GetVRClientId()
    {
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.VRClientId.Value;
        }
        return VRClientId;
    }

    public static ulong GetARClientId()
    {
        if (GameManager.Instance != null)
        {
            return GameManager.Instance.ARClientId.Value;
        }
        return ARClientId;
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