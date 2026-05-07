using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

public class Wand : MonoBehaviour {

public bool right, left;
    public Transform tracking_space;
    public float latency = 0f;

    public Warning warning;
    float last_hs = 0f;
    float last_ac = 0f;

Vector3 prev_hv;
    Vector3 prev_pos = new Vector3();
    Quaternion prev_rot = new Quaternion();

    InputDevice device;
    
    public int device_index() {

return -1;
    }

    bool have_device() {
	var devices = new List<InputDevice>();
	XRNode hand = (left? XRNode.LeftHand : XRNode.RightHand);
	InputDevices.GetDevicesAtXRNode(hand, devices);

	if(devices.Count == 1)
	{

	    device = devices[0];
	    return true;
	}
	else if (devices.Count > 1)
	{
	    Debug.Log("Found more than one XR input device!");
	}
	return false;
    }

public void wand_motion(out Vector3 position, out Quaternion rotation,
        out Vector3 velocity, out Vector3 angular_velocity,
        out Vector3 acceleration) {

	position = velocity = angular_velocity = acceleration = Vector3.zero;
	rotation = Quaternion.identity;

	if (!have_device())
	    return;

	Vector3 p, v, a, av;
	Quaternion r;
	if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out p))
	    return;
	if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out r))
	    return;
	if (!device.TryGetFeatureValue(CommonUsages.deviceVelocity, out v))
	    return;
	if (!device.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out av))
	    return;
	if (!device.TryGetFeatureValue(CommonUsages.deviceAcceleration, out a))
	    return;
	
#if old_steamvr
        int i = device_index();
        var compositor = OpenVR.Compositor;
        if (i == -1 || compositor == null) {
            position = velocity = angular_velocity = acceleration = Vector3.zero;
            rotation = Quaternion.identity;

            return;
        }

compositor.GetLastPoses(render_poses, game_poses);

var pose = game_poses [i];

        var t = new SteamVR_Utils.RigidTransform(pose.mDeviceToAbsoluteTracking);
        Vector3 hp = t.pos;
        Quaternion hr = t.rot;

Vector3 hv = new Vector3(pose.vVelocity.v0, pose.vVelocity.v1, -pose.vVelocity.v2);
#endif
	
        Vector3 hp = p;
        Quaternion hr = r;

Vector3 hv = v;

float time_step = Time.deltaTime > 0f ? Time.deltaTime : (1f / 90f);
	if (hv.magnitude > 2f)

	    hv = (hp - prev_pos) / time_step;

Vector3 hav = Quaternion.AngleAxis(135.0f, new Vector3(0,1,0)) * av;
	
	if (av.magnitude > 12f && false)
	{
	    Quaternion rdelta = r * Quaternion.Inverse(prev_rot);
	    float angle = 0.0f;
	    Vector3 axis = Vector3.zero;
	    rdelta.ToAngleAxis(out angle, out axis);
	    float fps = 90.0f;
	    float s = fps * angle * Mathf.PI / 180.0f;
	    Vector3 nav = Quaternion.AngleAxis(135.0f, new Vector3(0,1,0)) * av;
	    Debug.Log("Angular vel " + nav.x + ", " + nav.y + ", " + nav.z +
		      " last rot " + s*axis.x + ", " + s*axis.y + ", " + s*axis.z);
	    
	}
	prev_rot = r;

Vector3 ha = (hv - prev_hv) / time_step;
        prev_hv = hv;

	Vector3 vpred = (hp - prev_pos)/time_step;
	prev_pos = hp;

position = tracking_space.TransformPoint (hp);
        rotation = tracking_space.rotation * hr;
        velocity = tracking_space.TransformVector (hv);
        angular_velocity = tracking_space.TransformVector (hav);
        acceleration = tracking_space.TransformVector (ha);
    }

    public Vector3 position() {
        Vector3 p, v, av, a;
        Quaternion r;
        wand_motion(out p, out r, out v, out av, out a);
	return p;
    }

    public Quaternion rotation() {
        Vector3 p, v, av, a;
        Quaternion r;
        wand_motion(out p, out r, out v, out av, out a);
	return r;
    }

public void object_motion(Vector3 object_position, out Vector3 position, out Quaternion rotation,
                                out Vector3 velocity, out Vector3 acceleration) {

Vector3 p, va;
        Quaternion r;
        wand_motion(out p, out r, out velocity, out va, out acceleration);
        position = p + r * object_position;
        rotation = r;
    }

public void predict_hand_motion(out Vector3 hp, out Quaternion hr,
                                    out Vector3 hv, out Vector3 hav, out Vector3 ha) {
        wand_motion (out hp, out hr, out hv, out hav, out ha);

if (latency != 0) {

            float t = latency;
            hp += t * hv + (0.5f * t * t) * ha;
            float ad = Mathf.Rad2Deg * t * hav.magnitude;
            Vector3 axis = hav.normalized;
            hr = Quaternion.AngleAxis (ad, axis) * hr;

        }
    }

void check_imu_out_of_range(Vector3 hv, Vector3 ha) {
        if (hv.magnitude == 0 && last_hs >= 5) {
            warning.warn ("Acceleration out of range, last hand speed " + last_hs + " and accel " + last_ac);
        }
        last_hs = hv.magnitude;
        last_ac = ha.magnitude;
    }

public void haptic_pulse(float duration, float strength) {
    }
    
}
