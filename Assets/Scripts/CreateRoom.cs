using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions; // Unity's built-in assert library

public class CreateRoom : MonoBehaviour
{
    public GameObject roomRoot;
    public GameObject wallPrefab, cornerPrefab, floorPrefab;

    public GameObject[] roomPrefabs;
    List<Transform> roomCorners = new List<Transform>();
    List<GameObject> roomObjects = new List<GameObject>();
    BoxCollider collider;
    Vector2 roomSize;

    void Start(){
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

    public void ConstructRoom(Vector2 size)
    {
        roomSize = CalculateRoomSize();
        Vector2 rmdr = new Vector2(size.x % roomSize.x, size.y % roomSize.y);

        Debug.Log("Room Size: " + roomSize);
        Debug.Log("Rmdr: " + rmdr); 

        HashSet<GameObject> xSet = new HashSet<GameObject>();
        if (rmdr.x != 0)
        {
            collider.size = new Vector3(rmdr.x * 2, collider.size.y, roomSize.y * 2);
            foreach (GameObject obj in roomObjects)
            {
                if(isInBounds(obj, collider.bounds)){
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
                if(isInBounds(obj, collider.bounds)){
                    ySet.Add(obj);
                }
            }
        }

        HashSet<GameObject> xySet = new HashSet<GameObject>(xSet);
        xySet.IntersectWith(ySet);  

        int xBound = Mathf.CeilToInt(size.x/roomSize.x);
        int yBound = Mathf.CeilToInt(size.y/roomSize.y);
        for(int i=0;i<xBound-1;i++){
            for(int j=0;j<yBound-1;j++){
                if(i == xBound-1 && j == yBound-1){
                    foreach(GameObject obj in xySet){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                } 
                else if(i == xBound-1){
                    foreach(GameObject obj in xSet){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                } 
                else if(j == yBound-1){
                    foreach(GameObject obj in ySet){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x - i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z - j * roomSize.y
                        );
                    }
                } 
                else {
                    foreach(GameObject obj in roomObjects){
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

        for (int i=0;i<=xBound-1;i++) {
            for (int j=0;j<=yBound-1;j++) {
                int x = -(int)Mathf.Min(size.x, (i * roomSize.x));
                int y = -(int)Mathf.Min(size.y, (j * roomSize.y));
                if ((i == 0 || i == xBound) && (j == 0 || j == yBound)) {
                    Debug.Log("Creating corner at: " + x + ", " + y);
                    GameObject corner = Instantiate(cornerPrefab, roomRoot.transform);
                    corner.transform.localPosition = new Vector3(x, 0, y);
                }
                if (i == 0 || i == xBound-1) {
                    Debug.Log("Creating wall at: " + x + ", " + y);
                    GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                    wall.transform.localPosition = new Vector3(x, 0, y);
                    wall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    if(i==xBound)
                        wall.transform.localScale = new Vector3(wall.transform.localScale.x, wall.transform.localScale.y, -wall.transform.localScale.z);
                }
                if (j == 0 || j == yBound-1) {
                    Debug.Log("Creating wall at: " + x + ", " + y);
                    GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                    wall.transform.localPosition = new Vector3(x, 0, y);
                    if(j==yBound)
                        wall.transform.localScale = new Vector3(wall.transform.localScale.x, wall.transform.localScale.y, -wall.transform.localScale.z);
                }
            }
        }

        GameObject floor = Instantiate(floorPrefab, roomRoot.transform);
        floor.transform.localPosition = new Vector3(size.x / 2, 0, size.y / 2);
        floor.transform.localScale = new Vector3(size.x, 1, size.y);
    }

    Vector2 CalculateRoomSize()
    {
        foreach (var corner in roomCorners)
        {
            Debug.Log(corner.name + " at position: " + corner.position);
        }

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
}
