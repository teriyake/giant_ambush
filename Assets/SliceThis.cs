using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EzySlice;

public class SliceThis : MonoBehaviour
{
    // Start is called before the first frame update
    int maxIter = 3;
    void Start()
    {
        Vector3 randomNormal = Random.onUnitSphere;
        SliceGameObject(gameObject, new EzySlice.Plane(Vector3.zero, randomNormal), 0);
    }

    void SliceGameObject(GameObject objToSlice, EzySlice.Plane slicingPlane, int call)
    {
        SlicedHull slicedHull = SlicerExtensions.Slice(objToSlice, slicingPlane);
        call++;

        if (slicedHull != null && call < maxIter) {
            Debug.Log("Sliced Hull Created!");
            GameObject upperHull = slicedHull.CreateUpperHull(objToSlice, null);
            GameObject lowerHull = slicedHull.CreateLowerHull(objToSlice, null);
            objToSlice.SetActive(false); // Hide the original object
            Destroy(objToSlice); // Destroy the original object

            SliceGameObject(upperHull, new EzySlice.Plane(Vector3.zero, Random.onUnitSphere), call);
            SliceGameObject(lowerHull, new EzySlice.Plane(Vector3.zero, Random.onUnitSphere), call);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
