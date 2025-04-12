using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions; // Unity's built-in assert library

public class CreateRoom : MonoBehaviour
{
    public GameObject roomRoot;
    public GameObject wallPrefab, cornerPrefab, floorPrefab;

    public GameObject roomPrefab;
    public Transform[] roomCorners;
    public List<GameObject> roomObjects = new List<GameObject>();
    BoxCollider collider;
    Vector2 roomSize;

    void Start(){
        collider = GetComponent<BoxCollider>();
        for (int i=0;i<roomPrefab.transform.childCount;i++){
            GameObject obj = roomPrefab.transform.GetChild(i).gameObject;
            if (!obj.name.Contains("Wall"))
                roomObjects.Add(obj);
        }
    }

    public void ConstructRoom(Vector2 size)
    {
        roomSize = CalculateRoomSize();
        Vector2 rmdr = new Vector2(size.x % roomSize.x, size.y % roomSize.y);

        HashSet<GameObject> xSet = new HashSet<GameObject>();
        if (rmdr.x != 0)
        {
            collider.size = new Vector3(rmdr.x * 2, collider.size.y, roomSize.y * 2);
            foreach (GameObject obj in roomObjects)
            {
                Vector3[] vertices = obj.GetComponent<MeshFilter>().mesh.vertices;
                bool isContained = true;
                foreach (Vector3 vertex in vertices)
                {
                    if (!collider.bounds.Contains(vertex))
                    {
                        isContained = false;
                        break;
                    }
                }
                if (isContained)
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
                Vector3[] vertices = obj.GetComponent<MeshFilter>().mesh.vertices;
                bool isContained = true;
                foreach (Vector3 vertex in vertices)
                {
                    if (!collider.bounds.Contains(vertex))
                    {
                        isContained = false;
                        break;
                    }
                }
                if (isContained)
                {
                    ySet.Add(obj);
                }
            }
        }

        HashSet<GameObject> xySet = new HashSet<GameObject>(xSet);
        xySet.IntersectWith(ySet);  

        int xBound = Mathf.CeilToInt(size.x/roomSize.x);
        int yBound = Mathf.CeilToInt(size.y/roomSize.y);
        for(int i=1;i<=xBound;i++){
            for(int j=1;j<=yBound;j++){
                if(i == xBound && j == yBound){
                    foreach(GameObject obj in xySet){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x + i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z + j * roomSize.y
                        );
                    }
                } 
                else if(i == xBound){
                    foreach(GameObject obj in xSet){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x + i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z + j * roomSize.y
                        );
                    }
                } 
                else if(j == yBound){
                    foreach(GameObject obj in ySet){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x + i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z + j * roomSize.y
                        );
                    }
                } 
                else {
                    foreach(GameObject obj in roomObjects){
                        GameObject newObj = Instantiate(obj, roomRoot.transform);
                        newObj.transform.localPosition = new Vector3(
                            obj.transform.localPosition.x + i * roomSize.x,
                            obj.transform.localPosition.y,
                            obj.transform.localPosition.z + j * roomSize.y
                        );
                    }
                }
            }
        }

        for (float x = 0; x <= size.x; x += size.x) {
            for (float y = 0; y <= size.y; y += size.y){
                if ((x == 0 || x == size.x) && (y == 0 || y == size.y)) {
                    GameObject corner = Instantiate(cornerPrefab, roomRoot.transform);
                    corner.transform.localPosition = new Vector3(x, 0, y);
                }
                else if (x == 0 || x == size.x) {
                    GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                    wall.transform.localPosition = new Vector3(x, 0, y);
                    wall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                }
                else if (y == 0 || y == size.y) {
                GameObject wall = Instantiate(wallPrefab, roomRoot.transform);
                wall.transform.localPosition = new Vector3(x, 0, y);
            }
            }
        }

        GameObject floor = Instantiate(floorPrefab, roomRoot.transform);
        floor.transform.localPosition = new Vector3(size.x / 2, 0, size.y / 2);
        floor.transform.localScale = new Vector3(size.x, 1, size.y);
    }

    Vector2 CalculateRoomSize()
    {
        float length = Vector2.Distance(
            new Vector2(roomCorners[0].position.x, roomCorners[0].position.y),
            new Vector2(roomCorners[1].position.x, roomCorners[1].position.y)
        );

        float breadth = Vector2.Distance(
            new Vector2(roomCorners[1].position.x, roomCorners[1].position.y),
            new Vector2(roomCorners[2].position.x, roomCorners[2].position.y)
        );

        return new Vector2(length, breadth);
    }
}
