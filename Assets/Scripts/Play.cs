using System;  // use IntPtr
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using System.Runtime.InteropServices;  // Use Marshal

public class Play : MonoBehaviour {
    public Hand paddle_hand;
    public Hand free_hand;
    public Grips paddle_grips;
    public Wand left_wand, right_wand, tracker_wand;
    public float haptics_duration = 0.01f;  // Seconds for hand pulse.
    public float haptics_strength = 1.0f;   // Strength of hand pulse, 0-1.
    public Robot robot;
    public Balls balls;
    public Ball ball_in_play;
    public BallTracking ball_tracking;
    PlayState play_state, player_to_serve, robot_to_serve;
    public NeonScoreUI score_display;
    public List<Bouncer> marked_surfaces;
    public bool playing_game = false;
    int score_player = 0, score_robot = 0;

    // ── Multiplayer (set at runtime by MultiplayerBridge / PlayerSpawner) ────
    [HideInInspector] public bool is_multiplayer = false;
    [HideInInspector] public bool is_host = false;  // true when this peer is the Fusion server/host
    /// <summary>
    /// Fired whenever the ball bounces off any surface.
    /// MultiplayerBridge subscribes to this to route Fusion authority calls
    /// without requiring Fusion types inside this script.
    /// args: (Ball that bounced, Bouncer tag of the surface hit)
    /// </summary>
    [HideInInspector] public System.Action<Ball, string> on_ball_hit;
    public SettingsUI settings;
    public GameObject vr_camera;
    public OVRPassthroughLayer pass_through_video;
    public Renderer floor;  // Used to hide floor with pass-through video
    public Camera passthrough_camera;   // Used to hide skybox with pass-through video
    public Material skybox_material;    // Drag StarSkyboxMat here in the Inspector
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
	// Initialize grips before SettingsUI.Start() initialize
	// settings so initial grip can be set.
        paddle_grips = new Grips();
        paddle_grips.initialize_grips();
    }
    
    void Start() {
    	// Request 120 Hz refresh rate on Quest 3 for the crispest feel.
	// Verified: the previous 90 Hz hard-coded timesteps in Wand.cs and
	// Bouncer.cs have been replaced with Time.deltaTime / dynamic values
	// so physics stays accurate at any refresh rate.
        Unity.XR.Oculus.Performance.TrySetDisplayRefreshRate(120f);

        // Force skybox mode from the start (passthrough disabled).
        enable_show_room(false);

        // Start scoring immediately for single player.
        // In multiplayer this is called again by NetworkConnectionManager
        // once both players have joined, resetting scores to 0.
        start_game();

        // Create neon scoreboard above the net if not already wired up in the Inspector.
        if (score_display == null)
        {
            var go = new GameObject("NeonScore");
            go.transform.position = new Vector3(0f, 1.55f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // faces the host side
            score_display = go.AddComponent<NeonScoreUI>();
        }

        hold_ball (free_hand);
    }
    
    void Update() {
        float delta_t = Time.deltaTime;
        paddle_hand.update_paddle_position (delta_t);
        // Robot paddle motion must be before ball motion to keep
        // ball and paddle exactly in sync during robot serve.
        robot.move_paddle (delta_t);
        // In multiplayer, run ball physics at half speed so rallies are
        // more manageable over a network.  The state authority applies this
        // scaled step and replicates the result, so both peers see the same
        // slower ball without any extra networking changes.
        float ball_delta_t = is_multiplayer ? delta_t * 0.5f : delta_t;
        balls.move_balls (ball_delta_t);
	bool tossed = free_hand.move_held_ball();

	record_tracks(tossed);
    }

    void record_tracks(bool start) {
	// Record paddle and ball tracks.
	if (start)
	{
	    path_tracer.start_tracking();
	    track_countdown = 30;  // Keep this many frames after play ends
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
                // Debug.Log ("Hit " + bn.tag + ", next player state " + play_state.name);
		if (play_state.name == "robot hit")
		    path_tracer.add_segment();
            } else if (onps != null && bn.tag == onps.bouncer_tag) {
                ; // net ball, play continues.
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
            // In multiplayer the bridge handles ball authority; in singleplayer the robot returns.
            Stroke s = (!is_multiplayer) ? return_ball (b) : null;
            enable_markers();
            on_ball_hit?.Invoke(b, bn.tag);   // notify networking layer
//	    report_imu_state();  // Only for SteamVR
        }
        if (bn.tag == "robot_paddle") {
            disable_markers();
            on_ball_hit?.Invoke(b, bn.tag);   // notify networking layer
            /*
            Debug.Log("Robot paddle hit time " + b.motion.time
            + " hit height " + b.motion.position.y
            + " pos " + b.motion.position
            + " vel " + b.motion.velocity
            + " ang vel " + b.motion.angular_velocity);
            */
        }
    }

    // After a multiplayer point, pause then give the host the next ball to serve.
    IEnumerator restart_mp_rally() {
        yield return new WaitForSeconds(2f);
        if (is_host)
            hold_ball(free_hand);
        // Client: host will spawn and replicate the new ball automatically.
    }

    Stroke return_ball(Ball b) {

        bool player_serve = (play_state != null &&
                          play_state.name == "player serve paddle");
        BallTrack bt = ball_tracking.predict_ball_flight (b, player_serve);
        //Debug.Log ("Ball " + (bt.in_play ? "in play" : "out of play")
        //    + " over net " + bt.over_net.Count + " after bounce " + bt.final.Count);

        if (!bt.in_play)
            return null;

        Stroke s = robot.return_ball (bt);
        /*
        if (player_serve && s != null)
            serves.set_last_player_serve(b);
        */
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
        // The peer that is about to serve must own StateAuthority of the ball,
        // otherwise their hand position cannot drive _netPos and the other peer
        // will see the ball stuck wherever the previous authority left it.
        if (is_multiplayer) {
            var nb = b.GetComponent<NetworkedBall>();
            if (nb != null && nb.Object != null && nb.Object.IsValid) {
                if (!nb.Object.HasStateAuthority)
                    nb.Object.RequestStateAuthority();
                // Reset any leftover power-up state from the previous rally.
                // The pool recycles the same N NetworkedBalls, so a ball that
                // grew via the BigBall power-up keeps SizeMultiplier=10 across
                // serves unless we clear it here. SetSizeMultiplier queues the
                // write if authority hasn't fully transferred yet.
                nb.SetSizeMultiplier(1f);
                nb.SetSpeedMultiplier(1f);   // also clears any active SlowBall timer
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
    }

    void keep_score(PlayState last_state) {
        if (playing_game) {
            string sname = last_state.name;
            if (sname.StartsWith ("robot"))
                score_player += 1;
            else if (sname != "player to serve")
                score_robot += 1;
            report_score ();
        }
    }

    public bool player_serves() {
        int total_score = score_player + score_robot;
        bool ps = (
            total_score < 20 ?
            (((total_score / 2) % 2) == 0) :  // Have not reached deuce.
            ((total_score % 2) == 0));        // Have reached deuce.
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
        } else if (score_robot >= 11 && score_robot >= score_player + 2) {
            status = r_label + " WINS";
            playing_game = false;
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
	/*
	double time = 0;
	Vector3 accel = new Vector3(0,0,0);
	Vector3 rot = new Vector3(0,0,0);
	int out_of_range = 0;
	if (paddle_hand.wand.imu_state(ref time, ref accel, ref rot,
				       ref out_of_range))
	{
	    Debug.Log("IMU sample: time " + time.ToString("F3") +
		      " accel " + accel.x.ToString("F") + "," +
		      accel.y.ToString("F") + "," + accel.z.ToString("F") +
		      " rotation " + rot.x.ToString("F") + "," +
		      rot.y.ToString("F") + "," + rot.z.ToString("F"));
	    if (out_of_range != 0)
		Debug.Log("IMU out of range " + out_of_range);
	}
	else
	    Debug.Log("Failed to get IMU state.");
	*/
    }
    
    public void enable_show_room(bool show_room)
    {
        if (pass_through_video != null) pass_through_video.enabled = false;
        if (floor != null) floor.enabled = false;  // hide renderer; collider stays active

        // Assign the star skybox material at runtime so it works on-device.
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
    public string bouncer_tag;       // Surface to hit to reach this state.
    public PlayState(string name, string bouncer_tag ="") {
        this.name = name;
        this.next_state = null;
        this.bouncer_tag = bouncer_tag;
    }
}
