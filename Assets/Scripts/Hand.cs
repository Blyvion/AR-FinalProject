using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class Hand : MonoBehaviour {

    public Wand wand;

    public Ball held_ball;
    Vector3 ball_hold_position = new Vector3(-0.01f,0.04f,0.01f);
    float vmin_ball_release = 1.8f;
    public Toss last_toss;

    public Paddle held_paddle;
    public Grip grip;
    public bool freeze_paddle = false;
    float max_hand_accel = 0f;

    void Start () {
    }

void Update () {

    }

public void set_hand_wand(Wand w) {
        wand = w;
    }

    public void adjust_paddle_grip() {
        grip.adjust_paddle_grip(wand, held_paddle.transform);
    }

public void update_paddle_position(float delta_t) {
        if (held_paddle && !freeze_paddle)
            align_paddle_to_hand ();
    }

    void align_paddle_to_hand() {

Vector3 hp, hv, hav, ha;
        Quaternion hr;
        wand.predict_hand_motion (out hp, out hr, out hv, out hav, out ha);

max_hand_accel =  Mathf.Max (max_hand_accel, ha.magnitude);

Vector3 paddle_position, paddle_velocity, paddle_angular_velocity;
        Quaternion paddle_rotation;
        grip.hand_to_paddle_motion (hp, hr, hv, hav, wand.left,
            out paddle_position, out paddle_rotation,
            out paddle_velocity, out paddle_angular_velocity);

held_paddle.move(paddle_position, paddle_rotation, paddle_velocity, paddle_angular_velocity);
    }

    public bool move_held_ball() {

        if (held_ball == null)
            return false;
        Vector3 p, v, a;
        Quaternion r;
        wand.object_motion (ball_hold_position, out p, out r, out v, out a);

held_ball.set_motion (new BallState (held_ball, 0f, p, v, Vector3.zero));
        held_ball.transform.rotation = r;

if (Vector3.Dot (v, a) < 0
            && v.magnitude >= vmin_ball_release
            && v.y > 0f) {
            held_ball.freeze = false;
            held_ball = null;
            last_toss = new Toss(p, v, 0);
	    return true;
        }

	return false;
    }

    public bool holding_ball () {
        return held_ball != null;
    }

    public void hold_ball(Ball b) {
        held_ball = b;
        held_ball.freeze = true;
        Vector3 p, v, a;
        Quaternion r;
        wand.object_motion (ball_hold_position, out p, out r, out v, out a);
        held_ball.set_motion(new BallState(held_ball, 0, p, v, Vector3.zero));
    }
}

[System.Serializable]
public class Grip {

public string grip_name;
    public string hand_controller;
    public Vector3 paddle_grip_position;
    public Quaternion paddle_grip_rotation;

    public Grip(string name, string hand_controller, Vector3 position, Quaternion rotation) {
           this.grip_name = name;
           this.hand_controller = hand_controller;
           this.paddle_grip_position = position;
           this.paddle_grip_rotation = rotation;
    }

public void hand_to_paddle_motion(Vector3 hp, Quaternion hr, Vector3 hv, Vector3 hav,
            bool left_handed,
        out Vector3 paddle_position, out Quaternion paddle_rotation,
        out Vector3 paddle_velocity, out Vector3 paddle_angular_velocity) {

        Vector3 gp = paddle_grip_position;
        Quaternion gr = paddle_grip_rotation;
        if (left_handed) {

           gp = new Vector3(-gp.x, gp.y, gp.z);

           gr = new Quaternion(gr.x, -gr.y, -gr.z, gr.w);
        }

        Vector3 paddle_offset = hr * gp;
        paddle_position = hp + paddle_offset;
        paddle_rotation = hr * gr;

paddle_velocity = hv + Vector3.Cross (hav, paddle_offset);
        paddle_angular_velocity = hav;
    }

    public void adjust_paddle_grip(Wand wand, Transform paddle_transform) {
        Vector3 hp, hv, hav, ha;
        Quaternion hr;
        wand.wand_motion (out hp, out hr, out hv, out hav, out ha);

        Vector3 paddle_position = paddle_transform.position;
        Quaternion paddle_rotation = paddle_transform.rotation;

        Quaternion hri = Quaternion.Inverse(hr);
        paddle_grip_position = hri * (paddle_position - hp);
        paddle_grip_rotation = hri * paddle_rotation;
    }
}

[System.Serializable]
public class Grips {
        public List<Grip> grips;
    
    public Grips() {
        grips = new List<Grip>();
    }

    public void initialize_grips() {
        if (! load_grips_file())
            add_standard_grips();
    }
    
    public void add_standard_grips() {

string grips_json = "{'grips':[{'grip_name':'shake hands','hand_controller':'oculus rift','paddle_grip_position':{'x':0.007991436868906021,'y':0.04362906515598297,'z':0.03974873572587967},'paddle_grip_rotation':{'x':0.29819056391716006,'y':0.49500420689582827,'z':0.21315982937812806,'w':0.7877920269966126}},{'grip_name':'pen hold','hand_controller':'oculus rift','paddle_grip_position':{'x':-0.007771163247525692,'y':0.011335986666381359,'z':0.06225813180208206},'paddle_grip_rotation':{'x':-0.05083705484867096,'y':0.8304163217544556,'z':0.5415476560592651,'w':0.1206250786781311}},{'grip_name':'custom','hand_controller':'oculus rift','paddle_grip_position':{'x':0.0,'y':0.0,'z':0.0},'paddle_grip_rotation':{'x':0.0,'y':0.0,'z':0.0,'w':1.0}}]}".Replace("'", "\"");

        JsonUtility.FromJsonOverwrite(grips_json, this);
    }

    public Grip find_grip(string grip_name) {
        foreach (Grip g in grips) {
            if (g.grip_name == grip_name)
               return g;
                }
        return null;
    }    

    public void save_grips()
    {
        string path = Path.Combine(Application.persistentDataPath,
				   "grips.json");
        string grips_data = JsonUtility.ToJson(this);
        File.WriteAllText(path, grips_data);
        Debug.Log("Wrote grips to " + path);
    }

    bool load_grips_file()
    {
        string path = Path.Combine(Application.persistentDataPath,
				   "grips.json");
        if (!File.Exists(path))
	{
	    Debug.Log("Could not read grips from " + path);
            return false;
	}

        string grips_data = File.ReadAllText(path);
        JsonUtility.FromJsonOverwrite(grips_data, this);
        Debug.Log("Read grips from " + path);
        return true;
    }
}
