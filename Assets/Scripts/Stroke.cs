using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Stroke : PaddleMotion {
	public float contact_time;
	public float pre_swing_duration, post_swing_duration;
	public Vector3 position, normal, velocity;
	float elbow_distance = 0.45f;
	Vector3 elbow_position;
	Vector3 elbow_rotation_axis;
	float pre_rotation_angle, post_rotation_angle;
	Quaternion paddle_rotation;
	bool backhand;

	public Stroke(float time, Vector3 position,
					Vector3 normal, Vector3 velocity,
					float pre_swing_duration, float post_swing_duration,
					bool backhand = false) {
		this.contact_time = time;
		this.position = position;
		this.normal = normal;
		this.velocity = velocity;
		this.pre_swing_duration = pre_swing_duration;
		this.post_swing_duration = post_swing_duration;
		this.backhand = backhand;

		compute_elbow ();
		compute_swing ();
	}

	void compute_swing() {
		float stime = contact_time - pre_swing_duration;
		float etime = contact_time + post_swing_duration;
		Quaternion sr = Quaternion.AngleAxis (-pre_rotation_angle * Mathf.Rad2Deg, elbow_rotation_axis);
		Quaternion er = Quaternion.AngleAxis (post_rotation_angle * Mathf.Rad2Deg, elbow_rotation_axis);
		Vector3 spos = elbow_position + sr * (position - elbow_position);
		Vector3 epos = elbow_position + er * (position - elbow_position);
		Quaternion srot = sr * paddle_rotation;
		Quaternion erot = er * paddle_rotation;
		set_motion(stime, spos, srot, etime, epos, erot);
	}

	void compute_elbow() {

Vector3 elbow_direction = Vector3.Cross (velocity, normal).normalized;
		if (elbow_direction.magnitude == 0)
			elbow_direction = new Vector3(1,0,0);
		if ((backhand && elbow_direction.x > 0) ||
			(!backhand && elbow_direction.x < 0))
			elbow_direction = -elbow_direction;

elbow_position = position + elbow_distance * elbow_direction;
		elbow_rotation_axis = Vector3.Cross(velocity, elbow_direction).normalized;
		float ave_angle_vel = 0.5f * velocity.magnitude / elbow_distance;
		pre_rotation_angle =  pre_swing_duration * ave_angle_vel;
		post_rotation_angle = post_swing_duration * ave_angle_vel;
		paddle_rotation = (backhand ?
			Quaternion.LookRotation (normal, -elbow_direction) :
			Quaternion.LookRotation (-normal, -elbow_direction));
		
	}

	public float pre_swing_acceleration() {
		return (Mathf.PI / 2f) * velocity.magnitude / pre_swing_duration;
	}

	public void change_pre_swing_duration(float pre_swing_duration) {
		this.pre_swing_duration = pre_swing_duration;
		compute_swing ();
	}

	public void jump_to_start(Paddle p) {

		move (0f, p);

		foreach (Bouncer bc in p.gameObject.GetComponentsInChildren<Bouncer> ())
			bc.jump_wall ();
	}

	public override bool move(float time, Paddle paddle) {
		float t = time;
		float f;
		if (t <= start_time)
			f = 0;
		else if (t >= end_time)
			f = 1;
		else if (t <= contact_time)
			f = 0.5f * (t - start_time) / pre_swing_duration;
		else
			f = 0.5f + 0.5f * (t - contact_time) / post_swing_duration;

float pi2 = 2*Mathf.PI;
		float af = 2f * ((f-0.5f) - Mathf.Sin(f*pi2) / pi2);
		float vf = 0.5f*(1f - Mathf.Cos(f*pi2));

		float a = af * (f <= 0.5 ? pre_rotation_angle : post_rotation_angle);
		Quaternion r = Quaternion.AngleAxis (a * Mathf.Rad2Deg, elbow_rotation_axis);
		Vector3 p = elbow_position + r * (position - elbow_position);
		Quaternion rp = r * paddle_rotation;
		Vector3 vp = ((f > 0 && f < 1) ? vf * (r * velocity) : Vector3.zero);
		Vector3 vap = ((f > 0 && f < 1) ? elbow_rotation_axis * (vf / elbow_distance) : Vector3.zero);

paddle.move(p, rp, vp, vap);

		bool done = (f >= 1);
		return done;
	}
}
