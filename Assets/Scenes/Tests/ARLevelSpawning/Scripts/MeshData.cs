using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class MeshData : INetworkSerializable
{
    public Vector3[] vertices;
    public int[] triangles;
    public string meshId;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref meshId);

        int vertexCount = 0;
        if (!serializer.IsReader)
        {
            vertexCount = vertices?.Length ?? 0; 
        }
        serializer.SerializeValue(ref vertexCount);

        if (serializer.IsReader)
        {
            vertices = new Vector3[vertexCount];
        }

        if (vertices != null) 
        {
            for (int i = 0; i < vertexCount; ++i)
            {
                serializer.SerializeValue(ref vertices[i]);
            }
        }

        int triangleCount = 0;
        if (!serializer.IsReader)
        {
            triangleCount = triangles?.Length ?? 0; 
        }
        serializer.SerializeValue(ref triangleCount);

        if (serializer.IsReader)
        {
            triangles = new int[triangleCount];
        }

        if (triangles != null) 
        {
            for (int i = 0; i < triangleCount; ++i)
            {
                serializer.SerializeValue(ref triangles[i]);
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
