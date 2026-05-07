using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Play : MonoBehaviour {
    public Hand paddle_hand;
    public Hand free_hand;
    public Grips paddle_grips;
    public Wand left_wand, right_wand, tracker_wand;
    public float haptics_duration = 0.01f;
    public float haptics_strength = 1.0f;
    public Robot robot;
    public Balls balls;
    public Ball ball_in_play;
    public BallTracking ball_tracking;
    PlayState play_state, player_to_serve, robot_to_serve;
    public NeonScoreUI score_display;
    MeteorManager _meteors;
    public List<Bouncer> marked_surfaces;
    public bool playing_game = false;
    int score_player = 0, score_robot = 0;

[HideInInspector] public bool is_multiplayer = false;
    [HideInInspector] public bool is_host = false;

[HideInInspector] public System.Action<Ball, string> on_ball_hit;

[HideInInspector] public System.Action<int, int> on_score_updated;
    public SettingsUI settings;
    public GameObject vr_camera;
    public OVRPassthroughLayer pass_through_video;
    public Renderer floor;
    public Camera passthrough_camera;
    public Material skybox_material;
    public PathTracer path_tracer;
    int track_countdown;
    
    Play() {
        create_play_states ();
    }

    void create_play_states() {
        PlayState pts = new PlayState("player to serve");
        PlayState psp = new PlayState("player serve paddle", "player_paddle");
        PlayState pst = new PlayState("player serve table", "table_near");
        PlayState rt = new PlayState ("robot table", "table_far");
        PlayState rh = new PlayState ("robot hit", "robot_paddle");
        PlayState pt = new PlayState ("player table", "table_near");
        PlayState ph = new PlayState ("player hit", "player_paddle");
        PlayState rts = new PlayState("robot to serve");
        PlayState rsp = new PlayState("robot serve paddle", "robot_paddle");
        PlayState rst = new PlayState("robot serve table", "table_far");
        PlayState nt = new PlayState("net touch", "net");
        pts.next_state = psp;
        psp.next_state = pst;
        pst.next_state = rt;
        rt.next_state = rh;
        rh.next_state = pt;
        rh.optional_next_state = nt;
        pt.next_state = ph;
        ph.next_state = rt;
        ph.optional_next_state = nt;
        rts.next_state = rsp;
        rsp.next_state = rst;
        rst.next_state = pt;
        player_to_serve = pts;
        robot_to_serve = rts;
    }
    void Awake() {

paddle_grips = new Grips();
        paddle_grips.initialize_grips();
    }
    
    void Start() {

Unity.XR.Oculus.Performance.TrySetDisplayRefreshRate(120f);

enable_show_room(false);

if (score_display == null)
        {
            var go = new GameObject("NeonScore");
            go.transform.position = new Vector3(0f, 1.55f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            score_display = go.AddComponent<NeonScoreUI>();
        }

if (_meteors == null)
        {
            var go = new GameObject("MeteorManager");
            _meteors = go.AddComponent<MeteorManager>();
        }
        _meteors.balls = balls;

start_game();

        hold_ball (free_hand);
    }
    
    void Update() {
        float delta_t = Time.deltaTime;
        paddle_hand.update_paddle_position (delta_t);

robot.move_paddle (delta_t);

float ball_delta_t = is_multiplayer ? delta_t * 0.5f : delta_t;
        balls.move_balls (ball_delta_t);
	bool tossed = free_hand.move_held_ball();

	record_tracks(tossed);
    }

    void record_tracks(bool start) {

	if (start)
	{
	    path_tracer.start_tracking();
	    track_countdown = 30;
	}
	else if (play_state == null)
	{
	    if (track_countdown > 0)
		track_countdown -= 1;
	    else if (path_tracer.is_tracking())
	    {
		path_tracer.end_tracking();
		path_tracer.show_tracks(settings.show_ball_tracks.isOn,
					settings.show_paddle_tracks.isOn);
	    }
	}
	path_tracer.new_position(paddle_hand.held_paddle, ball_in_play);
    }

    public void ball_bounced(Ball b, Bouncer bn) {
        if (b == ball_in_play && play_state != null) {
            PlayState nps = play_state.next_state;
	    PlayState onps = play_state.optional_next_state;
            if (bn.tag == nps.bouncer_tag) {
                play_state = nps;

		if (play_state.name == "robot hit")
		    path_tracer.add_segment();
            } else if (onps != null && bn.tag == onps.bouncer_tag) {
                ;
            } else {
                keep_score (play_state);
                play_state = null;
		if (!is_multiplayer && robot.auto_serve)
		    robot.serve();
		else if (is_multiplayer)
		    StartCoroutine(restart_mp_rally());
            }
        }
        if (bn.tag == "player_paddle") {
            if (haptics_duration > 0 && haptics_strength > 0)
                paddle_hand.wand.haptic_pulse (haptics_duration, haptics_strength);

            Stroke s = (!is_multiplayer) ? return_ball (b) : null;
            enable_markers();
            on_ball_hit?.Invoke(b, bn.tag);

        }
        if (bn.tag == "robot_paddle") {
            disable_markers();
            on_ball_hit?.Invoke(b, bn.tag);
            
        }
    }

IEnumerator restart_mp_rally() {
        yield return new WaitForSeconds(2f);
        if (is_host)
            hold_ball(free_hand);

    }

    Stroke return_ball(Ball b) {

        bool player_serve = (play_state != null &&
                          play_state.name == "player serve paddle");
        BallTrack bt = ball_tracking.predict_ball_flight (b, player_serve);

if (!bt.in_play)
            return null;

        Stroke s = robot.return_ball (bt);
        
        if (player_serve)
        {
            Serve serve = new Serve(free_hand.last_toss, bt);
            robot.serves.add_serve(serve);
        }
        
        return s;
    }
    
    public void hold_ball(Hand hand) {
        Ball b = balls.new_ball ();
        if (b == null) return;

if (is_multiplayer) {
            var nb = b.GetComponent<NetworkedBall>();
            if (nb != null && nb.Object != null && nb.Object.IsValid) {
                if (!nb.Object.HasStateAuthority)
                    nb.Object.RequestStateAuthority();

nb.SetSizeMultiplier(1f);
                nb.SetSpeedMultiplier(1f);
            }
        }
        hand.hold_ball (b);
        ball_in_play = b;
        play_state = player_to_serve;
        robot.new_rally();
	path_tracer.clear_tracking();
    }

    public bool ball_held(Ball b) {
        return (free_hand.held_ball == b || paddle_hand.held_ball == b);
    }

    public Ball start_robot_serve() {
        if (is_multiplayer) {
            Debug.Log("[Play] start_robot_serve blocked — multiplayer mode");
            return null;
        }
        play_state = robot_to_serve;
        Ball ball = balls.new_ball ();
        ball_in_play = ball;
	record_tracks(true);
        return ball;
    }

    public void start_game() {
        playing_game = true;
        score_player = 0;
        score_robot = 0;
        report_score ();
        if (_meteors != null) _meteors.StartMeteors();
    }

    void keep_score(PlayState last_state) {
        if (playing_game) {
            string sname = last_state.name;
            bool player_scored;
            if (sname.StartsWith ("robot")) {
                score_player += 1;
                player_scored = true;
            } else if (sname != "player to serve") {
                score_robot += 1;
                player_scored = false;
            } else {
                return;
            }
            report_score ();
            if (is_multiplayer) {

bool host_scored = is_host ? player_scored : !player_scored;
                on_score_updated?.Invoke(host_scored ? 1 : 0, 0);
            }
        }
    }

public void receive_mp_point(bool host_scored) {

if (is_host == host_scored)
            score_player += 1;
        else
            score_robot  += 1;
        report_score();
    }

    public bool player_serves() {
        int total_score = score_player + score_robot;
        bool ps = (
            total_score < 20 ?
            (((total_score / 2) % 2) == 0) :
            ((total_score % 2) == 0));
        return ps;
    }

    void report_score() {
        if (score_display == null) return;
        string p_label = is_multiplayer ? "YOU"      : "PLAYER";
        string r_label = is_multiplayer ? "OPP"      : "ROBOT";
        string status;
        if (score_player >= 11 && score_player >= score_robot + 2) {
            status = "YOU WIN!";
            playing_game = false;
            if (_meteors != null) _meteors.StopMeteors();
        } else if (score_robot >= 11 && score_robot >= score_player + 2) {
            status = r_label + " WINS";
            playing_game = false;
            if (_meteors != null) _meteors.StopMeteors();
        } else {
            string serve = player_serves() ? "YOU SERVE" : r_label + " SERVES";
            string deuce = score_player + score_robot >= 20 ? "  ·  WIN BY 2" : "";
            status = serve + deuce;
        }
        score_display.UpdateScore(score_player, score_robot, status, p_label, r_label);
    }

    void enable_markers() {
        foreach (Bouncer bn in marked_surfaces)
            bn.mark = true;
    }

    void disable_markers() {
        foreach (Bouncer bn in marked_surfaces)
            bn.mark = false;
    }
    void report_imu_state() {
	
    }
    
    public void enable_show_room(bool show_room)
    {
        if (pass_through_video != null) pass_through_video.enabled = false;
        if (floor != null) floor.enabled = false;

if (skybox_material != null)
            RenderSettings.skybox = skybox_material;

        if (vr_camera != null)
        {
            foreach (var cam in vr_camera.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags      = CameraClearFlags.Skybox;
                cam.backgroundColor = new Color(0f, 0f, 0.05f, 1f);
            }
        }
    }
}

public class PlayState {
    public string name;
    public PlayState next_state, optional_next_state;
    public string bouncer_tag;
    public PlayState(string name, string bouncer_tag ="") {
        this.name = name;
        this.next_state = null;
        this.bouncer_tag = bouncer_tag;
    }
}
