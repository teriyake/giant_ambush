using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions; // Unity's built-in assert library
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class CreateRoom : NetworkBehaviour
{
    public GameObject roomRoot;
    public GameObject wallPrefab, cornerPrefab, floorPrefab, ceilingPrefab;

    public GameObject[] roomPrefabs;
    List<Transform> roomCorners = new List<Transform>();
    List<GameObject> roomObjects = new List<GameObject>();
    BoxCollider collider;
    Vector2 roomSize;
    public GameObject[] scatterPrefabs; 
    public int numberOfScatterObjects = 20;

    void Start(){
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

    bool isInBounds(GameObject obj, Bounds bounds){
        Renderer renderer = obj.GetComponent<Renderer>();
        if(renderer){
            if(!(bounds.Contains(renderer.bounds.min) && bounds.Contains(renderer.bounds.max))){
                return false;
            }
        }
        for(int i=0;i< obj.transform.childCount;i++){
            if(!isInBounds(obj.transform.GetChild(i).gameObject, bounds)){
                return false;
            }
        }
        return true;
    }

    public void GenerateRoomForAllClients(Vector2 size)
    {
        if (!IsServer) return;

        ConstructRoomClientRpc(size);


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
        Debug.Log($"========={roomCorners.Count}");
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
                    if (i == xBound)
                        wall.transform.localScale = new Vector3(wall.transform.localScale.x, wall.transform.localScale.y, -wall.transform.localScale.z);
                }
                if (j == 0 || j == yBound)
                {
                    Debug.Log("Creating wall at: " + x + ", " + y);
                    GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                    wall.transform.localPosition = new Vector3(x, 0, y);
                    if (j == yBound)
                        wall.transform.localScale = new Vector3(wall.transform.localScale.x, wall.transform.localScale.y, -wall.transform.localScale.z);
                }
            }
        }

        GameObject floor = Instantiate(floorPrefab, roomRoot.transform);
        floor.transform.localPosition = new Vector3(size.x / 2, 0, size.y / 2);
        floor.transform.localScale = new Vector3(size.x, 1, size.y);

        if (ceilingPrefab != null)
        {
            GameObject ceiling = Instantiate(ceilingPrefab, roomRoot.transform);
            float ceilingHeight = 3f;
            ceiling.transform.localPosition = new Vector3(-size.x / 2, ceilingHeight, -size.y / 2);
            ceiling.transform.localScale = new Vector3(size.x, 1, size.y);
        }
        else
        {
            Debug.LogWarning("Ceiling prefab is not assigned");
        }

        ScatterObjects(size);

        roomRoot.transform.localPosition -= new Vector3(-size.x / 2f, 0, -size.y / 2f);
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

        if (roomRoot == null) return;

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