using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public static class RoleManager
{

    public static ulong VRClientId = ulong.MaxValue; 
    public static ulong AntClientId = ulong.MaxValue;

    public static bool IsClientVR(ulong clientId)
    {
        return clientId == VRClientId;
    }
    
    public static bool IsClientAnt(ulong clientId)
    {
        return clientId == AntClientId;
    }
}
