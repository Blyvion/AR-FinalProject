using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Buttons : MonoBehaviour {

public Play play;
    public SettingsUI settings_ui;

    bool adjusting_grip;
    bool moving_table;
    Vector3 last_table_move_position;
    Quaternion last_table_move_rotation;

    void Start() {
	
    }

void Update () {
        Hand free_hand = play.free_hand;
        Hand paddle_hand = play.paddle_hand;

if (adjusting_grip) {
	    paddle_hand.freeze_paddle = true;
	    paddle_hand.adjust_paddle_grip();
	    return;
	} else if (paddle_hand.freeze_paddle) {
	    paddle_hand.freeze_paddle = false;
	}

if (moving_table)
	    move_table();
    }

    public void OnRobotServe() {
	if (play.settings.auto_serve.isOn)
	{

	    bool auto_serve = !play.robot.auto_serve;
	    play.robot.auto_serve = auto_serve;
	    if (auto_serve)
		play.robot.serve ();
	}
	else
	    play.robot.serve ();
    }

    public void OnHoldBall() {
	if (!play.free_hand.holding_ball ())
            if (play.playing_game && !play.player_serves ())
                play.robot.serve ();
	    else
		play.hold_ball(play.free_hand);
    }

    public void OnShowSettings() {
        settings_ui.show_ui(! settings_ui.shown());
    }

public void enable_move_table(bool enable)
    {

	string action_map = (enable ? "MoveTableActions" : "PlayActions");
	PlayerInput player_input = GetComponent<PlayerInput>();
	player_input.SwitchCurrentActionMap(action_map);
	if (!enable)
	  moving_table = false;
    }
    public void OnMoveTableStart() {
	moving_table = true;
	last_table_move_position = play.paddle_hand.wand.position();
	last_table_move_rotation = play.paddle_hand.wand.rotation();
    }
    public void OnMoveTableEnd() {
	moving_table = false;
    }
    void move_table() {

	Wand paddle_wand = play.paddle_hand.wand;
	Quaternion paddle_rotation = paddle_wand.rotation();
	Quaternion rotation = paddle_rotation * Quaternion.Inverse(last_table_move_rotation);
	float angle;
	Vector3 axis;
	rotation.ToAngleAxis(out angle, out axis);
	Quaternion y_rotation = Quaternion.AngleAxis(axis.y * angle, Vector3.up);

Vector3 paddle_position = paddle_wand.position();
	Vector3 offset = paddle_position - y_rotation*last_table_move_position;
	offset.y = 0f;

Quaternion c_rotation = Quaternion.Inverse(y_rotation);
	Vector3 c_offset = -(c_rotation * offset);

Transform player_transform = play.vr_camera.transform;
	player_transform.rotation = c_rotation * player_transform.rotation;
	player_transform.position = c_offset + c_rotation * player_transform.position;

last_table_move_position = paddle_wand.position();
	last_table_move_rotation = paddle_wand.rotation();
    }
    
    public void enable_adjust_grip(bool enable)
    {

	string action_map = (enable ? "AdjustGripActions" : "PlayActions");
	PlayerInput player_input = GetComponent<PlayerInput>();
	player_input.SwitchCurrentActionMap(action_map);
    }
    public void OnAdjustGripStart() {
	adjusting_grip = true;
    }
    public void OnAdjustGripEnd() {
	adjusting_grip = false;
    }

    public void OnReportMotion() {
	Vector3 hp, hv, hav, ha;
        Quaternion hr;
        play.paddle_hand.wand.wand_motion (out hp, out hr, out hv, out hav, out ha);
	string msg = ("p " + hp.x + " " + hp.y + " " + hp.z + " " +
		      "v " + hv.x + " " + hv.y + " " + hv.z + " " +
		      "a " + ha.x + " " + ha.y + " " + ha.z + "\n" +
		      "r " + hr.w + " " + hr.x + " " + hr.y + " " + hr.z + " " +
		      "av " + hav.x + " " + hav.y + " " + hav.z + "\n");
	Debug.Log(msg);
    }
}
