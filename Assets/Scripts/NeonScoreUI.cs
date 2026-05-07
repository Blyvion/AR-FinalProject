using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NeonScoreUI : MonoBehaviour
{

    static readonly Color k_Cyan    = new Color(0.00f, 1.00f, 1.00f, 1f);
    static readonly Color k_Magenta = new Color(1.00f, 0.10f, 0.85f, 1f);
    static readonly Color k_Yellow  = new Color(1.00f, 0.95f, 0.00f, 1f);
    static readonly Color k_BgColor = new Color(0.00f, 0.00f, 0.05f, 0.75f);
    static readonly Color k_Divider = new Color(0.40f, 0.40f, 0.40f, 0.80f);

TMP_Text _you_label_text, _you_score_text;
    TMP_Text _opp_label_text, _opp_score_text;
    TMP_Text _status_text;

    string _you_label = "YOU", _opp_label = "OPP";

const float k_W     = 1000f;
    const float k_H     = 450f;
    const float k_Scale = 0.001f;

    void Awake() => BuildCanvas();

    void BuildCanvas()
    {

        var canvas        = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        gameObject.AddComponent<GraphicRaycaster>();

        var rt         = (RectTransform)canvas.transform;
        rt.sizeDelta   = new Vector2(k_W, k_H);
        rt.localScale  = Vector3.one * k_Scale;

var bg    = MakeChild<Image>("BG", rt);
        bg.color  = k_BgColor;
        var bgRt  = (RectTransform)bg.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

var div   = MakeChild<Image>("Divider", rt);
        div.color = k_Divider;
        SetRect(div.transform, new Vector2(0f, 30f), new Vector2(3f, 300f));

_you_label_text = MakeTMP("YouLabel", rt, new Vector2(-250f,  160f), 42f,  k_Cyan,    new Vector2(400f, 55f));
        _you_score_text = MakeTMP("YouScore", rt, new Vector2(-250f,   30f), 160f, k_Cyan,    new Vector2(400f, 210f));

_opp_label_text = MakeTMP("OppLabel", rt, new Vector2( 250f,  160f), 42f,  k_Magenta, new Vector2(400f, 55f));
        _opp_score_text = MakeTMP("OppScore", rt, new Vector2( 250f,   30f), 160f, k_Magenta, new Vector2(400f, 210f));

_status_text = MakeTMP("Status", rt, new Vector2(0f, -165f), 46f, k_Yellow, new Vector2(900f, 70f));

        UpdateScore(0, 0, "");
    }

static T MakeChild<T>(string name, Transform parent) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    static void SetRect(Transform t, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)t;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
    }

    TMP_Text MakeTMP(string name, Transform parent, Vector2 pos, float fontSize,
                     Color color, Vector2 size)
    {
        var t           = MakeChild<TextMeshProUGUI>(name, parent);
        t.alignment     = TextAlignmentOptions.Center;
        t.fontSize      = fontSize;
        t.color         = color;
        t.fontStyle     = FontStyles.Bold;
        ApplyNeonGlow(t, color);
        SetRect(t.transform, pos, size);
        return t;
    }

    static void ApplyNeonGlow(TMP_Text t, Color neon)
    {
        var mat = t.fontMaterial;
        if (mat == null) return;

        mat.EnableKeyword("GLOW_ON");
        mat.SetColor(ShaderUtilities.ID_GlowColor,    new Color(neon.r, neon.g, neon.b, 0.7f));
        mat.SetFloat(ShaderUtilities.ID_GlowPower,    0.45f);
        mat.SetFloat(ShaderUtilities.ID_GlowOuter,    0.35f);
        mat.SetFloat(ShaderUtilities.ID_GlowInner,    0.05f);
        mat.SetFloat(ShaderUtilities.ID_GlowOffset,   0f);

        mat.EnableKeyword("OUTLINE_ON");
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.08f);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.white * 0.6f);
    }

public void UpdateScore(int you, int opp, string status,
                            string you_label = null, string opp_label = null)
    {
        if (you_label != null) _you_label = you_label;
        if (opp_label != null) _opp_label = opp_label;

        if (_you_label_text != null) _you_label_text.text = _you_label;
        if (_opp_label_text != null) _opp_label_text.text = _opp_label;
        if (_you_score_text != null) _you_score_text.text = you.ToString();
        if (_opp_score_text != null) _opp_score_text.text = opp.ToString();
        if (_status_text    != null) _status_text.text    = status;
    }
}
