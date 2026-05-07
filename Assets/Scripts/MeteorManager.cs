using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class MeteorManager : MonoBehaviour
{

    [Tooltip("Deterministic RNG seed — must be identical on every peer (leave at 42).")]
    [SerializeField] int     _seed             = 42;

    [Tooltip("Seconds between waves at game start.")]
    [SerializeField] float   _baseInterval     = 4f;

    [Tooltip("Minimum seconds between waves regardless of progression.")]
    [SerializeField] float   _minInterval      = 0.8f;

    [Tooltip("Interval shrinks by this much per successive wave (linear ramp).")]
    [SerializeField] float   _decayPerStep     = 0.06f;

    [Tooltip("Max simultaneous meteors in early-game waves.")]
    [SerializeField] int     _startMaxPerWave  = 2;

    [Tooltip("Max simultaneous meteors in late-game waves (ramps up over time).")]
    [SerializeField] int     _endMaxPerWave    = 5;

    [Tooltip("Number of waves before max-per-wave reaches its peak.")]
    [SerializeField] int     _waveRampOver     = 60;

    [Tooltip("Max seconds of random stagger between meteors inside one wave.")]
    [SerializeField] float   _intraWaveStagger = 0.35f;

    [Tooltip("Meteor travel speed range (m/s).")]
    [SerializeField] Vector2 _speedRange       = new Vector2(5f, 13f);

    [Tooltip("Meteor visual/collision radius range (m).")]
    [SerializeField] Vector2 _radiusRange      = new Vector2(0.055f, 0.14f);

    [Tooltip("How many waves to pre-generate.")]
    [SerializeField] int     _pregenWaves      = 200;

    [Tooltip("Trail sample count — higher = longer glowing tail.")]
    [SerializeField] int     _trailLength      = 16;

const float kHalfW     = 0.85f;
    const float kHalfL     = 1.45f;
    const float kLaunchPad = 1.4f;

const float kHitCooldown = 0.35f;

struct MeteorDef
    {
        public float   spawnOffset;
        public Vector3 startPos;
        public Vector3 velocity;
        public float   radius;
        public float   lifetime;
    }

class ActiveMeteor
    {
        public MeteorDef    def;
        public float        spawnTime;
        public GameObject   go;
        public Light        glow;
        public LineRenderer trail;
        public float        lastHitTime = -99f;
    }

MeteorDef[]        _defs;
    List<ActiveMeteor> _active    = new List<ActiveMeteor>();
    float              _gameStart = -1f;
    int                _nextDef   = 0;
    bool               _running   = false;
    Material           _meteorMat;

[HideInInspector] public Balls balls;

public void StartMeteors()
    {
        Pregenerate();
        _gameStart = Time.time;
        _nextDef   = 0;
        _running   = true;
        ClearActive();
    }

public void StopMeteors()
    {
        _running = false;
        ClearActive();
    }

void Awake()
    {

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");
        _meteorMat = new Material(shader);

        Color neon = new Color(1f, 0.40f, 0.05f, 1f);
        if (_meteorMat.HasProperty("_BaseColor"))
            _meteorMat.SetColor("_BaseColor", neon);
        else
            _meteorMat.color = neon;

        if (_meteorMat.HasProperty("_EmissionColor"))
        {
            _meteorMat.SetColor("_EmissionColor", neon * 6f);
            _meteorMat.EnableKeyword("_EMISSION");
        }
    }

    void Update()
    {
        if (!_running || _gameStart < 0f || balls == null) return;

        float now     = Time.time;
        float elapsed = now - _gameStart;

while (_nextDef < _defs.Length && _defs[_nextDef].spawnOffset <= elapsed)
        {
            SpawnMeteor(_defs[_nextDef]);
            _nextDef++;
        }

for (int i = _active.Count - 1; i >= 0; i--)
        {
            ActiveMeteor m   = _active[i];
            float        age = now - m.spawnTime;

            if (age >= m.def.lifetime)
            {
                DestroyMeteor(m);
                _active.RemoveAt(i);
                continue;
            }

            Vector3 pos = m.def.startPos + m.def.velocity * age;
            m.go.transform.position = pos;

if (m.glow != null)
                m.glow.intensity = 2.2f + Mathf.Sin(now * 15f + i * 1.3f) * 0.7f;

UpdateTrail(m, pos);

if (now - m.lastHitTime < kHitCooldown) continue;

            foreach (var b in balls.AllBalls)
            {
                if (b == null || !b.gameObject.activeSelf || b.freeze) continue;

var  nb          = b.GetComponent<NetworkedBall>();
                bool isAuthority = (nb == null)
                                || (nb.Object != null && nb.Object.HasStateAuthority);
                if (!isAuthority) continue;

                float   reach = m.def.radius + b.radius;
                Vector3 delta = b.motion.position - pos;
                if (delta.sqrMagnitude > reach * reach) continue;

Vector3 normal = delta.sqrMagnitude > 1e-6f
                    ? delta.normalized
                    : Vector3.up;
                ApplyDeflection(b, m.def.velocity, normal);
                m.lastHitTime = now;
                break;
            }
        }
    }

    void OnDestroy()
    {
        ClearActive();
        if (_meteorMat != null) Destroy(_meteorMat);
    }

void SpawnMeteor(MeteorDef def)
    {

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Meteor";
        go.GetComponent<Renderer>().sharedMaterial = _meteorMat;
        float d = def.radius * 2f;
        go.transform.localScale = new Vector3(d, d, d);
        go.transform.position   = def.startPos;

        Destroy(go.GetComponent<Collider>());

var lightGO = new GameObject("MeteorGlow");
        lightGO.transform.SetParent(go.transform, false);
        var lt        = lightGO.AddComponent<Light>();
        lt.type       = LightType.Point;
        lt.color      = new Color(1f, 0.55f, 0.12f);
        lt.intensity  = 2.5f;
        lt.range      = def.radius * 14f;

var lr          = go.AddComponent<LineRenderer>();
        lr.material     = _meteorMat;
        lr.startWidth   = def.radius * 0.7f;
        lr.endWidth     = 0f;
        lr.positionCount = _trailLength;
        lr.useWorldSpace = true;
        for (int i = 0; i < _trailLength; i++)
            lr.SetPosition(i, def.startPos);

        _active.Add(new ActiveMeteor
        {
            def       = def,
            spawnTime = Time.time,
            go        = go,
            glow      = lt,
            trail     = lr,
        });
    }

void UpdateTrail(ActiveMeteor m, Vector3 currentPos)
    {
        if (m.trail == null) return;
        int n = m.trail.positionCount;
        for (int i = n - 1; i > 0; i--)
            m.trail.SetPosition(i, m.trail.GetPosition(i - 1));
        m.trail.SetPosition(0, currentPos);
    }

    void DestroyMeteor(ActiveMeteor m)
    {
        if (m.go != null) Destroy(m.go);
    }

    void ClearActive()
    {
        foreach (var m in _active) DestroyMeteor(m);
        _active.Clear();
    }

void ApplyDeflection(Ball b, Vector3 meteorVelocity, Vector3 normalAwayFromMeteor)
    {

const float restitution  = 0.75f;
        const float meteorMassFactor = 60f;

        Vector3 relVel   = b.motion.velocity - meteorVelocity;
        float   relAlongN = Vector3.Dot(relVel, normalAwayFromMeteor);

if (relAlongN >= 0f) return;

        float   impulseScale = -(1f + restitution) * relAlongN
                               * (b.mass * meteorMassFactor / (b.mass + meteorMassFactor));
        Vector3 newVel       = b.motion.velocity + impulseScale * normalAwayFromMeteor;

BallState bs = b.motion;
        b.set_motion(new BallState(b, bs.time, bs.position, newVel, bs.angular_velocity));
    }

void Pregenerate()
    {
        var rng = new System.Random(_seed);

var list = new System.Collections.Generic.List<MeteorDef>();
        float waveTime = 0f;

        Vector3 boxHalf = new Vector3(kHalfW, 0.9f, kHalfL);
        Vector3 ellipsoidRadii = new Vector3(
            boxHalf.x + kLaunchPad,
            boxHalf.y + kLaunchPad,
            boxHalf.z + kLaunchPad
        );

        for (int w = 0; w < _pregenWaves; w++)
        {
            float interval = Mathf.Max(_minInterval, _baseInterval - w * _decayPerStep);
            waveTime += interval;

float rampFrac  = Mathf.Clamp01((float)w / _waveRampOver);
            int   maxInWave = Mathf.RoundToInt(Mathf.Lerp(_startMaxPerWave, _endMaxPerWave, rampFrac));

float skew      = RandRange(rng, rampFrac * 0.5f, 1f);
            int   count     = Mathf.Max(1, Mathf.RoundToInt(skew * maxInWave));

            for (int i = 0; i < count; i++)
            {

                float stagger = RandRange(rng, 0f, _intraWaveStagger);

float theta = RandRange(rng, 0f,              Mathf.PI * 2f);
                float phi   = RandRange(rng, -80f * Mathf.Deg2Rad, 80f * Mathf.Deg2Rad);

                Vector3 dir = new Vector3(
                    Mathf.Cos(phi) * Mathf.Cos(theta),
                    Mathf.Sin(phi),
                    Mathf.Cos(phi) * Mathf.Sin(theta)
                ).normalized;

Vector3 target = new Vector3(
                    RandRange(rng, -boxHalf.x, boxHalf.x),
                    RandRange(rng,  0.05f,     boxHalf.y),
                    RandRange(rng, -boxHalf.z, boxHalf.z)
                );

float a  = (dir.x * dir.x) / (ellipsoidRadii.x * ellipsoidRadii.x)
                         + (dir.y * dir.y) / (ellipsoidRadii.y * ellipsoidRadii.y)
                         + (dir.z * dir.z) / (ellipsoidRadii.z * ellipsoidRadii.z);
                float b2 = (target.x * dir.x) / (ellipsoidRadii.x * ellipsoidRadii.x)
                         + (target.y * dir.y) / (ellipsoidRadii.y * ellipsoidRadii.y)
                         + (target.z * dir.z) / (ellipsoidRadii.z * ellipsoidRadii.z);
                float c2 = (target.x * target.x) / (ellipsoidRadii.x * ellipsoidRadii.x)
                         + (target.y * target.y) / (ellipsoidRadii.y * ellipsoidRadii.y)
                         + (target.z * target.z) / (ellipsoidRadii.z * ellipsoidRadii.z) - 1f;
                float disc = b2 * b2 - a * c2;
                float s    = disc >= 0f
                    ? (b2 + Mathf.Sqrt(Mathf.Max(0f, disc))) / a
                    : kLaunchPad * 2f;

                Vector3 start  = target - dir * s;
                float   speed  = RandRange(rng, _speedRange.x, _speedRange.y);
                float   radius = RandRange(rng, _radiusRange.x, _radiusRange.y);

                list.Add(new MeteorDef
                {
                    spawnOffset = waveTime + stagger,
                    startPos    = start,
                    velocity    = dir * speed,
                    radius      = radius,
                    lifetime    = (s * 2f) / speed + 0.4f,
                });
            }
        }

list.Sort((a, b) => a.spawnOffset.CompareTo(b.spawnOffset));
        _defs = list.ToArray();
    }

    static float RandRange(System.Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}
