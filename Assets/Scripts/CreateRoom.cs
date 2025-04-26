using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions; // Unity's built-in assert library

[RequireComponent(typeof(NetworkObject))]
public class CreateRoom : NetworkBehaviour
{
    [System.Serializable]
    public struct ScatteredObjectData : INetworkSerializable
    {
        public int prefabIndex;
        public Vector3 localPosition;
        public Quaternion localRotation;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref prefabIndex);
            serializer.SerializeValue(ref localPosition);
            serializer.SerializeValue(ref localRotation);
        }
    }

    public GameObject roomRoot;
    public GameObject wallPrefab,
        cornerPrefab,
        floorPrefab,
        ceilingPrefab;

    public GameObject[] roomPrefabs;
    List<Transform> roomCorners = new List<Transform>();
    List<GameObject> roomObjects = new List<GameObject>();
    BoxCollider collider;
    Vector2 roomSize;
    public GameObject[] scatterPrefabs;
    public LayerMask scatterOverlapLayerMask;
    int numberOfScatterObjects;

    void Start()
    {
        /*
        GameObject roomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Length)];
        collider = GetComponent<BoxCollider>();
        for (int i=0;i<roomPrefab.transform.childCount;i++){
            GameObject obj = roomPrefab.transform.GetChild(i).gameObject;
            if(obj.name.Contains("Corner")){
                roomCorners.Add(obj.transform);
            }
            else if (!obj.name.Contains("Wall")){
                roomObjects.Add(obj);
            }
        }
        */

        // ConstructRoom(new Vector2(20, 20));
    }

    bool isInBounds(GameObject obj, Bounds bounds)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer)
        {
            if (!(bounds.Contains(renderer.bounds.min) && bounds.Contains(renderer.bounds.max)))
            {
                return false;
            }
        }
        for (int i = 0; i < obj.transform.childCount; i++)
        {
            if (!isInBounds(obj.transform.GetChild(i).gameObject, bounds))
            {
                return false;
            }
        }
        return true;
    }

    public void GenerateRoomForAllClients(Vector2 size)
    {
        if (!IsServer)
            return;

        ConstructRoomClientRpc(size);

        GenerateScatteredObjectsData(size, out List<ScatteredObjectData> scatteredObjectsDataList);

        InstantiateScatteredObjectsClientRpc(scatteredObjectsDataList.ToArray());

        GameManager.Instance?.Server_NotifyLevelReady(roomRoot);
    }

    [ClientRpc]
    public void ConstructRoomClientRpc(Vector2 size)
    {
        GameObject roomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Length)];
        collider = GetComponent<BoxCollider>();
        for (int i = 0; i < roomPrefab.transform.childCount; i++)
        {
            GameObject obj = roomPrefab.transform.GetChild(i).gameObject;
            if (obj.name.Contains("Corner"))
            {
                roomCorners.Add(obj.transform);
            }
            else if (!obj.name.Contains("Wall"))
            {
                roomObjects.Add(obj);
            }
        }

        float x0 = roomCorners[0].position.x;
        float z0 = roomCorners[0].position.z;
        float x1 = roomCorners[1].position.x;
        float z1 = roomCorners[1].position.z;

        float length = Mathf.Sqrt(Mathf.Pow(x1 - x0, 2) + Mathf.Pow(z1 - z0, 2));

        float x2 = roomCorners[2].position.x;
        float z2 = roomCorners[2].position.z;

        float breadth = Mathf.Sqrt(Mathf.Pow(x2 - x1, 2) + Mathf.Pow(z2 - z1, 2));

        roomSize = new Vector2(length, breadth);
        Vector2 rmdr = new Vector2(size.x % roomSize.x, size.y % roomSize.y);

        Debug.Log("Room Size: " + size);
        Debug.Log("Rmdr: " + rmdr);

        HashSet<GameObject> xSet = new HashSet<GameObject>();
        if (rmdr.x != 0)
        {
            collider.size = new Vector3(rmdr.x * 2, collider.size.y, roomSize.y * 2);
            foreach (GameObject obj in roomObjects)
            {
                if (isInBounds(obj, collider.bounds))
                {
                    xSet.Add(obj);
                }
            }
        }

        HashSet<GameObject> ySet = new HashSet<GameObject>();
        if (rmdr.y != 0)
        {
            collider.size = new Vector3(roomSize.x * 2, collider.size.y, rmdr.y * 2);
            foreach (GameObject obj in roomObjects)
            {
                if (isInBounds(obj, collider.bounds))
                {
                    ySet.Add(obj);
                }
            }
        }

        HashSet<GameObject> xySet = new HashSet<GameObject>(xSet);
        xySet.IntersectWith(ySet);

        int xBound = Mathf.CeilToInt(size.x / roomSize.x);
        int yBound = Mathf.CeilToInt(size.y / roomSize.y);
        for (int i = 0; i < xBound; i++)
        {
            for (int j = 0; j < yBound; j++)
            {
                if (i == xBound - 1 && j == yBound - 1)
                {
                    foreach (GameObject obj in xySet)
                    {
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                }
                else if (i == xBound - 1)
                {
                    foreach (GameObject obj in xSet)
                    {
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                }
                else if (j == yBound - 1)
                {
                    foreach (GameObject obj in ySet)
                    {
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                }
                else
                {
                    foreach (GameObject obj in roomObjects)
                    {
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                }
            }
        }

        for (int i = 0; i <= xBound; i++)
        {
            for (int j = 0; j <= yBound; j++)
            {
                int x = -(int)Mathf.Min(size.x, (i * roomSize.x));
                int y = -(int)Mathf.Min(size.y, (j * roomSize.y));
                if ((i == 0 || i == xBound) && (j == 0 || j == yBound))
                {
                    Debug.Log("Creating corner at: " + x + ", " + y);
                    GameObject corner = Instantiate(cornerPrefab, roomRoot.transform);
                    corner.transform.localPosition = new Vector3(x, 0, y);
                }
                if (i == 0 || i == xBound)
                {
                    Debug.Log("Creating wall at: " + x + ", " + y);
                    GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                    wall.transform.localPosition = new Vector3(x, 0, y);
                    wall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    if ((i == 0) && (j == 0))
                        wall.transform.localScale = new Vector3(
                            -wall.transform.localScale.x,
                            wall.transform.localScale.y,
                            wall.transform.localScale.z
                        );
                    if (i == xBound)
                        wall.transform.localScale = new Vector3(
                            wall.transform.localScale.x,
                            wall.transform.localScale.y,
                            -wall.transform.localScale.z
                        );

                    if ((i == xBound) && (j == yBound))
                    {
                        wall.transform.localRotation = Quaternion.Euler(0, 0, 0);
                        wall.transform.localScale = new Vector3(
                            -wall.transform.localScale.x,
                            wall.transform.localScale.y,
                            wall.transform.localScale.z
                        );
                    }
                    if ((i == xBound) && (j == 0))
                    {
                        wall.transform.localRotation = Quaternion.Euler(0, -90, 0);
                        wall.transform.localScale = new Vector3(
                            wall.transform.localScale.x,
                            wall.transform.localScale.y,
                            -wall.transform.localScale.z
                        );
                    }
                }
                if (j == 0 || j == yBound)
                {
                    Debug.Log("Creating wall at: " + x + ", " + y);
                    GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                    wall.transform.localPosition = new Vector3(x, 0, y);
                    if (j == yBound)
                        wall.transform.localScale = new Vector3(
                            wall.transform.localScale.x,
                            wall.transform.localScale.y,
                            -wall.transform.localScale.z
                        );
                    if ((j == yBound) && (i == xBound))
                        wall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    if ((i == xBound) && (j == 0))
                    {
                        wall.transform.localScale = new Vector3(
                            -wall.transform.localScale.x,
                            wall.transform.localScale.y,
                            wall.transform.localScale.z
                        );
                    }
                }
            }
        }

        GameObject floor = Instantiate(floorPrefab, roomRoot.transform);
        floor.transform.localPosition = new Vector3(size.x / 2, 0, size.y / 2);
        floor.transform.localScale = new Vector3(size.x, 1, size.y);

        if (ceilingPrefab != null)
        {
            GameObject ceiling = Instantiate(ceilingPrefab, roomRoot.transform);
            float ceilingHeight = 4f;
            ceiling.transform.localPosition = new Vector3(-size.x / 2, ceilingHeight, -size.y / 2);
            ceiling.transform.localScale = new Vector3(size.x, 1, size.y);
        }
        else
        {
            Debug.LogWarning("Ceiling prefab is not assigned");
        }

        roomRoot.transform.localPosition -= new Vector3(-size.x / 2f, 0, -size.y / 2f);
    }

    void GenerateScatteredObjectsData(Vector2 size, out List<ScatteredObjectData> scatteredObjects)
    {
        scatteredObjects = new List<ScatteredObjectData>();

        if (!IsServer)
        {
            Debug.LogError("GenerateScatteredObjectsData called on a client!");
            return;
        }

        if (scatterPrefabs == null || scatterPrefabs.Length == 0)
        {
            Debug.LogWarning("Scatter prefabs list is empty or not assigned.");
            return;
        }
        if (roomRoot == null)
        {
            Debug.LogError("Room root is not assigned.");
            return;
        }
        if (scatterOverlapLayerMask.value == 0)
        {
            Debug.LogWarning("Scatter Overlap Layer Mask is not set.");
        }

        float minX = -size.x;
        float maxX = 0f;
        float minZ = -size.y;
        float maxZ = 0f;
        float yPos = 0f;

        int numberOfScatterObjects = Mathf.CeilToInt(Mathf.Max(size.x, size.y));
        Debug.Log($"Scattering {numberOfScatterObjects} objects...");

        int maxPlacementAttemptsPerObject = 20;
        Collider[] overlapResults = new Collider[10];

        GameObject tempCheckParent = new GameObject("ScatterCheck_TemporaryColliders");
        tempCheckParent.transform.SetParent(roomRoot.transform, false);
        tempCheckParent.hideFlags = HideFlags.HideAndDontSave;
        List<GameObject> tempInstances = new List<GameObject>();

        for (int i = 0; i < numberOfScatterObjects; i++)
        {
            bool positionFound = false;
            for (int attempt = 0; attempt < maxPlacementAttemptsPerObject; attempt++)
            {
                int prefabIndex = Random.Range(0, scatterPrefabs.Length);
                GameObject prefab = scatterPrefabs[prefabIndex];
                Collider prefabCollider = prefab.GetComponentInChildren<Collider>();

                if (prefabCollider == null)
                {
                    Debug.LogError(
                        $"Scatter prefab '{prefab.name}' or its children are missing a Collider component needed for overlap checks!",
                        prefab
                    );
                    continue;
                }

                Vector3 randomLocalPosition = new Vector3(
                    Random.Range(minX, maxX),
                    yPos,
                    Random.Range(minZ, maxZ)
                );
                Quaternion randomLocalRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                Vector3 checkPosition = roomRoot.transform.TransformPoint(randomLocalPosition);
                Quaternion checkRotation = roomRoot.transform.rotation * randomLocalRotation;

                int hitCount = 0;
                bool overlaps = false;

                if (prefabCollider is BoxCollider box)
                {
                    Vector3 center = box.center;
                    Vector3 sizeScaled = Vector3.Scale(box.size, prefab.transform.lossyScale);
                    Vector3 worldCenter =
                        checkPosition
                        + checkRotation * Vector3.Scale(center, prefab.transform.lossyScale);
                    Vector3 halfExtents = sizeScaled / 2f;

                    hitCount = Physics.OverlapBoxNonAlloc(
                        worldCenter,
                        halfExtents,
                        overlapResults,
                        checkRotation,
                        scatterOverlapLayerMask
                    );
                }
                else if (prefabCollider is SphereCollider sphere)
                {
                    Vector3 center = sphere.center;
                    float maxScale = MaxComponent(prefab.transform.lossyScale);
                    float radiusScaled = sphere.radius * maxScale;
                    Vector3 worldCenter =
                        checkPosition
                        + checkRotation * Vector3.Scale(center, prefab.transform.lossyScale);

                    hitCount = Physics.OverlapSphereNonAlloc(
                        worldCenter,
                        radiusScaled,
                        overlapResults,
                        scatterOverlapLayerMask
                    );
                }
                else if (prefabCollider is CapsuleCollider capsule)
                {
                    Vector3 center = capsule.center;
                    float height = capsule.height * prefab.transform.lossyScale.y;
                    float radius =
                        capsule.radius
                        * Mathf.Max(prefab.transform.lossyScale.x, prefab.transform.lossyScale.z);
                    Vector3 worldCenter =
                        checkPosition
                        + checkRotation * Vector3.Scale(center, prefab.transform.lossyScale);

                    Vector3 halfExtents = new Vector3(radius, height / 2f, radius);
                    Quaternion capsuleWorldRotation =
                        checkRotation
                        * Quaternion.Euler(
                            capsule.direction == 0 ? new Vector3(0, 0, 90)
                            : capsule.direction == 2 ? new Vector3(90, 0, 0)
                            : Vector3.zero
                        );

                    hitCount = Physics.OverlapBoxNonAlloc(
                        worldCenter,
                        halfExtents,
                        overlapResults,
                        capsuleWorldRotation,
                        scatterOverlapLayerMask
                    );
                }
                else if (prefabCollider is MeshCollider meshCollider && meshCollider.convex)
                {
                    Bounds worldBounds = meshCollider.bounds;
                    Vector3 center = meshCollider.bounds.center;
                    Vector3 meshColliderSize = meshCollider.bounds.size;

                    Vector3 centerScaled = Vector3.Scale(center, prefab.transform.lossyScale);
                    Vector3 sizeScaled = Vector3.Scale(
                        meshColliderSize,
                        prefab.transform.lossyScale
                    );
                    Vector3 worldCenter = checkPosition + checkRotation * centerScaled;
                    Vector3 halfExtents = sizeScaled / 2f;

                    hitCount = Physics.OverlapBoxNonAlloc(
                        worldCenter,
                        halfExtents,
                        overlapResults,
                        checkRotation,
                        scatterOverlapLayerMask
                    );

                    // float radius = meshCollider.bounds.extents.magnitude;
                    // hitCount = Physics.OverlapSphereNonAlloc(worldCenter, radius * MaxComponent(prefab.transform.lossyScale), overlapResults, scatterOverlapLayerMask);
                }
                else
                {
                    Debug.LogError(
                        $"Unsupported collider type {prefabCollider.GetType()} on {prefab.name} for overlap check.",
                        prefab
                    );
                    continue;
                }

                if (hitCount > 0)
                {
                    overlaps = true;
                    for (int k = 0; k < hitCount; k++)
                    {
                        Debug.Log($"Overlap detected with: {overlapResults[k].gameObject.name}");
                    }
                }

                if (!overlaps)
                {
                    scatteredObjects.Add(
                        new ScatteredObjectData
                        {
                            prefabIndex = prefabIndex,
                            localPosition = randomLocalPosition,
                            localRotation = randomLocalRotation,
                        }
                    );

                    GameObject tempInstance = Instantiate(prefab, tempCheckParent.transform);
                    tempInstance.transform.localPosition = randomLocalPosition;
                    tempInstance.transform.localRotation = randomLocalRotation;
                    tempInstances.Add(tempInstance);

                    positionFound = true;
                    break;
                }
            }

            if (!positionFound)
            {
                Debug.LogWarning(
                    $"Failed to find a non-overlapping position for scattered object {i + 1} after {maxPlacementAttemptsPerObject} attempts. Room might be too full or colliders too large."
                );
            }
        }

        Destroy(tempCheckParent);

        Debug.Log(
            $"Generated data for {scatteredObjects.Count}/{numberOfScatterObjects} scattered objects."
        );
    }

    float MaxComponent(Vector3 v)
    {
        return Mathf.Max(Mathf.Max(v.x, v.y), v.z);
    }

    [ClientRpc]
    void InstantiateScatteredObjectsClientRpc(ScatteredObjectData[] scatteredObjectsData)
    {
        if (roomRoot == null)
        {
            Debug.LogError(
                "Room root is not assigned when trying to instantiate scattered objects."
            );
            return;
        }

        if (scatterPrefabs == null || scatterPrefabs.Length == 0)
        {
            Debug.LogError("Scatter prefabs list is empty or not assigned on client.");
            return;
        }

        foreach (var objData in scatteredObjectsData)
        {
            if (objData.prefabIndex < 0 || objData.prefabIndex >= scatterPrefabs.Length)
            {
                Debug.LogError($"Invalid prefab index {objData.prefabIndex} received.");
                continue;
            }

            GameObject prefabToScatter = scatterPrefabs[objData.prefabIndex];
            GameObject scatteredObj = Instantiate(prefabToScatter, roomRoot.transform);
            scatteredObj.transform.localPosition = objData.localPosition;
            scatteredObj.transform.localRotation = objData.localRotation;
        }
        Debug.Log($"Instantiated {scatteredObjectsData.Length} scattered objects on client.");
    }

    Vector2 CalculateRoomSize()
    {
        float length = Vector2.Distance(
            new Vector2(roomCorners[0].position.x, roomCorners[0].position.z),
            new Vector2(roomCorners[1].position.x, roomCorners[1].position.z)
        );

        float breadth = Vector2.Distance(
            new Vector2(roomCorners[1].position.x, roomCorners[1].position.z),
            new Vector2(roomCorners[2].position.x, roomCorners[2].position.z)
        );

        return new Vector2(length, breadth);
    }

    void ScatterObjects(Vector2 size)
    {
        if (scatterPrefabs == null || scatterPrefabs.Length == 0)
        {
            Debug.LogWarning("Scatter prefabs list is empty or not assigned.");
            return;
        }

        if (roomRoot == null)
            return;

        float minX = -size.x;
        float maxX = 0f;
        float minZ = -size.y;
        float maxZ = 0f;
        float yPos = 0f;

        for (int i = 0; i < numberOfScatterObjects; i++)
        {
            GameObject prefabToScatter = scatterPrefabs[Random.Range(0, scatterPrefabs.Length)];
            Vector3 randomPosition = new Vector3(
                Random.Range(minX, maxX),
                yPos,
                Random.Range(minZ, maxZ)
            );

            GameObject scatteredObj = Instantiate(prefabToScatter, roomRoot.transform);
            scatteredObj.transform.localPosition = randomPosition;

            scatteredObj.transform.localRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }
    }
}