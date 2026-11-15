using UnityEngine;

// Drive the car by hand to test it: W/S or Up/Down = gas/reverse,
// A/D or Left/Right = steer, Space = brake.
// Turn this component OFF when the ML-Agents agent controls the car.
[RequireComponent(typeof(Rigidbody))]
public class ManualDrive : MonoBehaviour
{
    public WheelCollider fl, fr, rl, rr;

    public float motorTorque = 800f;    // per rear wheel
    public float brakeTorque = 2500f;   // Space
    public float idleBrake = 150f;      // small brake when no key is pressed
    public float maxSteerAngle = 30f;   // degrees

    void FixedUpdate()
    {
        float throttle = Input.GetAxis("Vertical");
        float steer = Input.GetAxis("Horizontal");
        bool handbrake = Input.GetKey(KeyCode.Space);

        rl.motorTorque = throttle * motorTorque;
        rr.motorTorque = throttle * motorTorque;

        fl.steerAngle = steer * maxSteerAngle;
        fr.steerAngle = steer * maxSteerAngle;

        float brake = handbrake ? brakeTorque : (Mathf.Abs(throttle) < 0.01f ? idleBrake : 0f);
        fl.brakeTorque = brake;
        fr.brakeTorque = brake;
        rl.brakeTorque = brake;
        rr.brakeTorque = brake;
    }
}
