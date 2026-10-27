using System.Reflection;
using Localizor.LanguageChangeEvent;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;

namespace TheBigBoard;

/// <summary>
/// Clones a vanilla phone-menu button so our mod gets
/// its own icon in the top bar / full menu. No AssetBundles, no Harmony:
/// the icon sprite is generated at runtime with Texture2D.
/// </summary>
public sealed class PhoneButtonInstaller : MonoBehaviour
{
    public const string ButtonName = "The Big Board";
    public const string TitleLocaleKey = "thebigboard_app_title";

    static PhoneButtonInstaller _instance;
    ModContext _context;
    GameObject _button;
    int _frames;
    float _installTime = -1f;
    bool _checked5, _checked15;

    public static void Ensure(ModContext context)
    {
        if (_instance != null) return;
        var go = new GameObject("TheBigBoard_Bootstrap");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PhoneButtonInstaller>();
        _instance._context = context;
        context.Logger.Info("The Big Board bootstrap created, waiting for phone UI...");
    }

    public static void Teardown()
    {
        if (_instance != null)
        {
            if (_instance._button != null) Destroy(_instance._button);
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    /// <summary>Runs a coroutine on the bootstrap object (chunked background work).</summary>
    public static void RunJob(System.Collections.IEnumerator routine)
    {
        if (routine == null) return;
        if (_instance != null) _instance.StartCoroutine(routine);
        else { while (routine.MoveNext()) { } }
    }

    /// <summary>Runs the action after one frame (lets "Searching..." paint first).</summary>
    public static void RunNextFrame(System.Action action)
    {
        if (_instance != null) _instance.StartCoroutine(RunDeferred(action));
        else if (action != null) action();
    }

    static System.Collections.IEnumerator RunDeferred(System.Action action)
    {
        yield return null;
        if (action != null) action();
    }

    int _tickFrames;

    void Update()
    {
        // Freshness watchdog: new day recomputes the factory plan, a new
        // hour re-runs the supply count while SUPPLY is open (~2s cadence).
        // StockHistory snapshots day-boundary stocks for our movement ledger.
        if (++_tickFrames % 120 == 0) { FactoryPanel.Tick(); StockHistory.Tick(); EmployeePanel.Tick(); BuildingsPanel.Tick(); BusinessesPanel.Tick(); }
        // Unity null-check: destroyed button becomes null, reinstall then.
        if (_button == null)
        {
            _installTime = -1f; _checked5 = _checked15 = false;
            if (++_frames % 30 != 0) return;
            TryInstall();
            return;
        }
        // Delayed re-check: does vanilla logic overwrite our icon/title?
        float dt = Time.realtimeSinceStartup - _installTime;
        if (!_checked5 && dt > 5f) { _checked5 = true; DumpState("t+5s"); }
        if (!_checked15 && dt > 15f) { _checked15 = true; DumpState("t+15s"); }
    }

    void DumpState(string tag)
    {
        if (_button == null || _context == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append($"The Big Board [{tag}] clone pos={_button.transform.position} active={_button.activeSelf} ");
        foreach (var img in _button.GetComponentsInChildren<Image>(true))
            sb.Append($"[img:{img.name} sprite={(img.sprite != null ? img.sprite.name : "<null>")} alpha={img.color.a}]");
        foreach (var tmp in _button.GetComponentsInChildren<TMPro.TMP_Text>(true))
            sb.Append($"[text:{tmp.name}='{tmp.text}']");
        _context.Logger.Info(sb.ToString());
    }

    void TryInstall()
    {
        // Full-menu top bar: UI.Smartphone.FullMenu holds a private
        // "appButtonTemplate". Append LAST so vanilla/mod order is untouched.
        var menu = FindObjectOfType<UI.Smartphone.FullMenu>();
        if (menu != null)
        {
            var field = typeof(UI.Smartphone.FullMenu).GetField(
                "appButtonTemplate", BindingFlags.NonPublic | BindingFlags.Instance);
            var template = field?.GetValue(menu) as Transform;
            if (template != null)
            {
                if (TryInstallFrom(template.gameObject, template.parent, "topbar")) return;
            }
            else _context?.Logger.Warn("The Big Board: FullMenu appButtonTemplate not found.");
        }

        // Fallback: clone a live top-bar button.
        if (menu == null) return;
        var buttons = menu.GetComponentsInChildren<FullMenuAppButton>(true);
        if (buttons == null || buttons.Length == 0) return;
        FullMenuAppButton templateBtn = null;
        foreach (var b in buttons)
        {
            if (b == null || b.name == ButtonName) continue;
            templateBtn = b;
            break;
        }
        if (templateBtn == null) return;
        TryInstallFrom(templateBtn.gameObject, templateBtn.transform.parent, "topbar-live");
    }

    bool TryInstallFrom(GameObject template, Transform parent, string kind)
    {
        // Avoid duplicates after scene reloads. Adopt + rewire: the clone may
        // belong to an unloaded copy (same assembly identity shares our
        // statics), so never trust its existing icon/title/listeners.
        var dup = parent != null ? parent.Find(ButtonName) : null;
        if (dup != null)
        {
            _button = dup.gameObject;
            if (!WireClone(_button)) { _button = null; return false; }
            _installTime = Time.realtimeSinceStartup;
            _checked5 = _checked15 = false;
            _context?.Logger.Info("The Big Board phone button re-adopted.");
            return true;
        }

        var clone = Instantiate(template, parent, false);
        clone.name = ButtonName;
        _context?.Logger.Info($"The Big Board: parent={FullPath(parent)} children={parent?.childCount} index={clone.transform.GetSiblingIndex()} active={clone.activeSelf}");
        if (!WireClone(clone)) return false;

        _button = clone;
        _installTime = Time.realtimeSinceStartup;
        _checked5 = _checked15 = false;
        _context?.Logger.Info($"The Big Board phone button installed ({kind}).");
        DumpState("t+0s");
        return true;
    }

    /// <summary>Wires a clone (fresh or adopted) with our icon, title and click handler.</summary>
    bool WireClone(GameObject clone)
    {
        clone.tag = "Untagged"; // templates are tagged UiTemplate; ResetTemplate destroys tagged siblings
        clone.transform.SetAsLastSibling(); // keep vanilla/mod order untouched
        clone.SetActive(true);
        var appButton = clone.GetComponent<AppButton>();
        if (appButton == null) { Destroy(clone); return false; }
        appButton.SetIcon(CreateIcon());
        var full = clone.GetComponent<FullMenuAppButton>();
        if (full != null) full.HideSelectedIcon();

        // Title: use our own locale key so it follows the game language.
        var loc = clone.GetComponentInChildren<TextLocalizationComponent>(true);
        if (loc != null) loc.Key = TitleLocaleKey;
        else
        {
            var tmp = clone.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null) tmp.text = "The Big Board";
        }

        var btn = clone.GetComponent<Button>();
        if (btn == null) btn = clone.AddComponent<Button>();
        btn.onClick.RemoveAllListeners();
        var ctx = _context;
        btn.onClick.AddListener(() =>
        {
            ctx?.Logger.Info("The Big Board phone button clicked!");
            ModPanel.Toggle(FindObjectOfType<UI.Smartphone.FullMenu>(), ctx);
        });
        return true;
    }

    static string FullPath(Transform t)
    {
        if (t == null) return "<null>";
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 6 && t != null; i++, t = t.parent)
            parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>128px rounded teal tile with a white "B" glyph.</summary>
    static Sprite CreateIcon()
    {
        const int S = 128;
        const int R = 30;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var bg = new Color(0.10f, 0.55f, 0.55f, 1f);
        var clear = new Color(0, 0, 0, 0);

        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int cx = Mathf.Min(x, S - 1 - x);
            int cy = Mathf.Min(y, S - 1 - y);
            bool inside = cx >= 0 && cy >= 0 &&
                          (cx >= R || cy >= R || (R - cx) * (R - cx) + (R - cy) * (R - cy) <= R * R);
            tex.SetPixel(x, y, inside ? bg : clear);
        }

        var fg = Color.white;
        FillRect(tex, 34, 36, 14, 56, fg);   // B spine
        FillRect(tex, 34, 36, 60, 12, fg);   // B top bowl
        FillRect(tex, 34, 58, 60, 12, fg);   // B middle bar
        FillRect(tex, 34, 80, 60, 12, fg);   // B bottom bowl
        FillRect(tex, 80, 36, 14, 34, fg);   // B upper right
        FillRect(tex, 80, 58, 14, 34, fg);   // B lower right

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    static void FillRect(Texture2D tex, int x0, int y0, int w, int h, Color c)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
            tex.SetPixel(x, y, c);
    }
}
