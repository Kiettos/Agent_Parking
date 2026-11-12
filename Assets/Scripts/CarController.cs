using UnityEngine;
public class CarController : MonoBehaviour {
    public WheelCollider wheelFL, wheelFR, wheelRL, wheelRR;
    public float maxMotorTorque = 500f;
    public float maxSteeringAngle = 30f;

    public void Move(float throttle, float steer) {
        wheelFL.steerAngle = steer * maxSteeringAngle;
        wheelFR.steerAngle = steer * maxSteeringAngle;

        float motor = throttle * maxMotorTorque;
        wheelFL.motorTorque = motor; wheelFR.motorTorque = motor;
        wheelRL.motorTorque = motor; wheelRR.motorTorque = motor;
    }
}