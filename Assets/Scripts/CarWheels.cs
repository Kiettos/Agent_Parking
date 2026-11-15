using UnityEngine;

// Keeps the visible wheel meshes on the WheelColliders (spin, steer, suspension).
// Order in both arrays: FL, FR, RL, RR.
[RequireComponent(typeof(Rigidbody))]
public class CarWheels : MonoBehaviour
{
    public WheelCollider[] colliders = new WheelCollider[4];
    public Transform[] meshes = new Transform[4];

    [Tooltip("Lower centre of mass = car flips less. Local space of CarAgent.")]
    public Vector3 centerOfMass = new Vector3(0f, 0.4f, 0f);

    void Awake()
    {
        GetComponent<Rigidbody>().centerOfMass = centerOfMass;
    }

    void LateUpdate()
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null || meshes[i] == null) continue;
            colliders[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
            meshes[i].SetPositionAndRotation(pos, rot);
        }
    }
}
