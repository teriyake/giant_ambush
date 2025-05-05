using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EzySlice;

public class SliceThis : MonoBehaviour
{
    Material mat;

    // Start is called before the first frame update
    int maxIter = 3;
    public void BreakObj(GameObject obj)
    {
        Vector3 randomNormal = Random.onUnitSphere;
        mat = gameObject.GetComponent<MeshRenderer>().material;
        List<(GameObject, Vector3)> slices = SliceGameObject(obj, new EzySlice.Plane(Vector3.zero, randomNormal), 0);
        foreach((GameObject, Vector3) tuple in slices){
            GameObject o = tuple.Item1;
            Vector3 normal = tuple.Item2;
            Rigidbody rb = o.AddComponent<Rigidbody>();
            rb.AddForce(normal * Random.Range(5f, 10f), ForceMode.Impulse);
        }
    }

    List<(GameObject, Vector3)> SliceGameObject(GameObject objToSlice, EzySlice.Plane slicingPlane, int call)
    {
        SlicedHull slicedHull = SlicerExtensions.Slice(objToSlice, slicingPlane, mat);
        call++;

        List<(GameObject, Vector3)> slicedObjects = new List<(GameObject, Vector3)>();

        if (slicedHull != null) {
            GameObject upperHull = slicedHull.CreateUpperHull(objToSlice, null);
            GameObject lowerHull = slicedHull.CreateLowerHull(objToSlice, null);
            objToSlice.SetActive(false); // Hide the original object
            Destroy(objToSlice); // Destroy the original object

            if(call < maxIter) {
                slicedObjects.AddRange(SliceGameObject(upperHull, new EzySlice.Plane(Vector3.zero, Random.onUnitSphere), call));
                slicedObjects.AddRange(SliceGameObject(lowerHull, new EzySlice.Plane(Vector3.zero, Random.onUnitSphere), call));
            } else {
                slicedObjects.Add((upperHull, slicingPlane.GetNormal()));
                slicedObjects.Add((lowerHull, -slicingPlane.GetNormal()));
            }
        } else {
            slicedObjects.Add((objToSlice, slicingPlane.GetNormal())); 
        }
        return slicedObjects;
    }

    // Update is called once per frame
    void Start()
    {
        BreakObj(gameObject);
    }
}
