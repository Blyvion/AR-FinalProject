using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class Balls : MonoBehaviour {

	public Play play;
	public Ball ball;   // Original ball for duplicating
	public Material white_ball, orange_striped_ball;
	List<Ball> balls = new List<Ball>();
	int cur_ball;
	public int ball_count = 3;

	// Read-only view of the live ball pool, used by NetworkPowerUp's
	// host-side proximity check.
	public IReadOnlyList<Ball> AllBalls => balls;

	public List<Bouncer> bouncers;

	// ── Multiplayer ────────────────────────────────────────────────────────
	// Assign in Inspector with the ball prefab that has NetworkObject + NetworkedBall.
	// Set by MultiplayerBridge.Activate() once the session is full.
	public NetworkObject network_ball_prefab;
	NetworkRunner _runner;

	public void set_runner(NetworkRunner runner) { _runner = runner; }

	/// <summary>
	/// Switch the ball pool to multiplayer mode. Hides every local-only ball
	/// (the original scene ball, plus any clones Play.Start created before MP
	/// flipped) and clears the pool. After this returns, the only balls that
	/// will appear in the pool are NetworkedBalls that arrive via
	/// register_remote_ball() — i.e. balls Runner.Spawn'd by the host.
	/// </summary>
	public void enter_multiplayer() {
		int hidden = 0;
		foreach (var b in balls) {
			if (b != null && b.gameObject != null) {
				b.gameObject.SetActive(false);
				hidden++;
			}
		}
		balls.Clear();
		cur_ball = 0;
		Debug.Log($"[Balls] enter_multiplayer: hid {hidden} local balls; pool cleared");
	}

	/// <summary>
	/// Called on the HOST after enter_multiplayer() to mint N replicated balls.
	/// Each Runner.Spawn fires NetworkedBall.Spawned on both peers, which calls
	/// register_remote_ball, populating the pool symmetrically.
	/// </summary>
	public void prespawn_networked_balls(int count) {
		if (_runner == null || !_runner.IsServer || network_ball_prefab == null) {
			Debug.LogWarning($"[Balls] prespawn_networked_balls skipped (runner={_runner != null}, " +
			                 $"isServer={_runner?.IsServer}, prefab={network_ball_prefab != null})");
			return;
		}
		for (int i = 0; i < count; i++) {
			NetworkObject no = _runner.Spawn(network_ball_prefab, transform.position, Quaternion.identity);
			Debug.Log($"[Balls] HOST pre-spawned networked ball #{i + 1}/{count} id={no.Id}");
		}
	}

	/// <summary>
	/// Called from NetworkedBall.Spawned() on every peer (host and client) so
	/// the locally-replicated ball joins the rotation pool used by Play.cs.
	///
	/// Important: do NOT touch free_hand.held_ball or RequestStateAuthority here.
	/// Doing so on the client would steal authority from the host for every ball
	/// as it spawns, breaking the host's serve. Authority handoff happens lazily:
	/// Play.hold_ball when the local player is about to serve, and
	/// MultiplayerBridge.RequestAuthorityOnHit when the local paddle strikes.
	/// </summary>
	public void register_remote_ball(Ball b) {
		if (b != null && !balls.Contains(b))
		{
			balls.Add(b);
			Debug.Log($"[Balls] register_remote_ball name={b.name} count={balls.Count}");
		}
	}

	// Use this for initialization
	void Start () {
		balls.Add (ball);
		cur_ball = 0;
	}

	public Ball new_ball () {
		bool mp = (play != null && play.is_multiplayer);

		if (mp) {
			// In multiplayer the pool ONLY contains networked balls (the local
			// scene ball was hidden in enter_multiplayer; the host pre-spawned
			// ball_count NetworkObjects via Runner.Spawn). Just rotate.
			if (balls.Count == 0) {
				Debug.LogWarning("[Balls] new_ball: MP pool is empty — no networked balls available yet");
				return null;
			}
			cur_ball = (cur_ball + 1) % balls.Count;
			Debug.Log($"[Balls] MP rotated cur_ball={cur_ball} count={balls.Count}");
		}
		else if (balls.Count < ball_count) {
			GameObject bo = Object.Instantiate (ball.gameObject, transform);
			Ball bc = bo.GetComponent<Ball> ();
			balls.Add (bc);
			cur_ball = balls.Count - 1;
		} else {
			cur_ball = (cur_ball + 1) % balls.Count;
		}
		Ball b = balls [cur_ball];
		if (play.ball_held (b))
			return new_ball ();
		b.gameObject.SetActive (true);
		// Only unfreeze if this peer owns physics for the ball. Unfreezing a
		// proxy ball lets Balls.move_balls integrate phantom rebounds against
		// it before the next Fusion tick re-asserts _netPos.
		var no = b.GetComponent<NetworkObject>();
		bool weOwnIt = (no == null || !no.IsValid || no.HasStateAuthority);
		if (weOwnIt)
			b.freeze = false;
		return b;
	}

	public void move_balls(float delta_t) {
		// Keep every Bouncer's edge-hit tolerance in sync with the actual
		// frame interval so it stays accurate at 90 Hz, 120 Hz, or any
		// other refresh rate Quest chooses.
		foreach (Bouncer bn in bouncers)
			bn.time_step = delta_t;

		foreach (Ball b in balls) {
			// Skip balls this peer is only a proxy of — their position is
			// authoritative on whichever peer holds StateAuthority and is
			// applied locally via NetworkedBall.Render's interpolation. Running
			// local physics on a proxy produces phantom bounces that fight the
			// network state and visually "lose" the ball.
			var no = b.GetComponent<NetworkObject>();
			if (no != null && no.IsValid && !no.HasStateAuthority)
				continue;

			BallState bs1 = b.motion;
			BallState bs2 = b.move_ball (delta_t);
			if (bs2 != null) {
				BallState bsf = compute_rebounds (bs1, bs2, b);
				b.set_motion (bsf);
			}
		}
	}

	BallState compute_rebounds(BallState bs1, BallState bs2, Ball b) {
		// Adjust ball position and velocity for bouncing off paddle, table, floor, net.
		int max_rebounds = 2;
		for (int r = 0; r < max_rebounds; ++r) {
			Rebound first_rb = null;
			foreach (Bouncer bn in bouncers) {
				Rebound rb = bn.check_for_bounce (bs1, bs2, b.radius, b.inertia_coef);
				if (rb != null && first_rb != null) {
					Debug.Log ("Hit " + rb.bouncer.gameObject.name + " time " + rb.contact.time.ToString("F7") +
						" and " + first_rb.bouncer.gameObject.name + " time " + first_rb.contact.time.ToString("F7"));
				}

				if (rb != null && (first_rb == null || rb.contact.time < first_rb.contact.time))
					first_rb = rb;
			}
			if (first_rb == null)
				break;
			first_rb.set_ball (b);
			play.ball_bounced (b, first_rb.bouncer);
			bs1 = first_rb.contact;
			bs2 = first_rb.final;
		}
		return bs2;
	}

	public void set_ball_coloring(string name)
	{
		Material m = white_ball;
		if (name == "orange striped")
			m = orange_striped_ball;
		foreach (Ball b in balls) {
			MeshRenderer r = b.GetComponent<MeshRenderer>();
			r.material = m;
		}
	}
}

