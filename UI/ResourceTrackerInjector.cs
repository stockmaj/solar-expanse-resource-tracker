#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.ObjectInfoElements;
using Language;
using Manager;
using ScriptableObjectScripts;
using SolarExpanseResourceTracker.Core;
using SolarExpanseResourceTracker.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SolarExpanseResourceTracker.UI
{
    // ── Persisted configuration ───────────────────────────────────────────────

    internal class ResourceTrackerConfig
    {
        private readonly ConfigEntry<string> _hidden;
        private readonly ConfigEntry<string> _active;
        private readonly ConfigEntry<string> _depQty;
        private readonly ConfigEntry<string> _depQual;
        private readonly ConfigEntry<string> _stockSort;
        private readonly ConfigEntry<string> _depSort;
        private readonly ConfigEntry<bool>   _stockTab;

        internal ResourceTrackerConfig(ConfigFile cfg)
        {
            _hidden    = cfg.Bind("ResourceTracker", "HiddenResources",  "", "Resource IDs hidden from rows (comma-separated)");
            _active    = cfg.Bind("ResourceTracker", "ActiveResources",  "", "Resource IDs toggled for combo filter (comma-separated)");
            _depQty    = cfg.Bind("ResourceTracker", "DepositMinQty",    "", "Per-resource minimum qty thresholds (id=value;...)");
            _depQual   = cfg.Bind("ResourceTracker", "DepositMinQual",   "", "Per-resource minimum quality thresholds (id=value;...)");
            _stockSort = cfg.Bind("ResourceTracker", "StockpileSort",    "res=;qual=qty",   "Stockpile sort state");
            _depSort   = cfg.Bind("ResourceTracker", "DepositSort",      "res=;qual=total", "Deposit sort state");
            _stockTab  = cfg.Bind("ResourceTracker", "StockpilesTab",    true, "Which tab was last active");
        }

        internal HashSet<string>          LoadHidden()       => ParseSet(_hidden.Value);
        internal HashSet<string>          LoadActive()       => ParseSet(_active.Value);
        internal Dictionary<string,float> LoadDepositQty()   => ParseFloatDict(_depQty.Value);
        internal Dictionary<string,float> LoadDepositQual()  => ParseFloatDict(_depQual.Value);
        internal (string res, string qual) LoadStockSort()   => ParseSort(_stockSort.Value, "qty");
        internal (string res, string qual) LoadDepositSort() => ParseSort(_depSort.Value,   "total");
        internal bool                     LoadStockTab()     => _stockTab.Value;

        internal void SaveHidden(HashSet<string> v)                  { _hidden.Value    = string.Join(",", v); }
        internal void SaveActive(HashSet<string> v)                  { _active.Value    = string.Join(",", v); }
        internal void SaveDepositQty(Dictionary<string,float> v)     { _depQty.Value    = SerializeFloatDict(v); }
        internal void SaveDepositQual(Dictionary<string,float> v)    { _depQual.Value   = SerializeFloatDict(v); }
        internal void SaveStockSort(string res, string qual)         { _stockSort.Value = $"res={res};qual={qual}"; }
        internal void SaveDepositSort(string res, string qual)       { _depSort.Value   = $"res={res};qual={qual}"; }
        internal void SaveStockTab(bool v)                           { _stockTab.Value  = v; }

        static HashSet<string> ParseSet(string s)
        {
            var r = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(s)) return r;
            foreach (var p in s.Split(','))
                if (!string.IsNullOrWhiteSpace(p)) r.Add(p.Trim());
            return r;
        }

        static Dictionary<string,float> ParseFloatDict(string s)
        {
            var r = new Dictionary<string,float>();
            if (string.IsNullOrWhiteSpace(s)) return r;
            foreach (var pair in s.Split(';'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float v))
                    r[kv[0].Trim()] = v;
            }
            return r;
        }

        static string SerializeFloatDict(Dictionary<string,float> d)
        {
            var parts = new List<string>();
            foreach (var kv in d)
                parts.Add($"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}");
            return string.Join(";", parts);
        }

        static (string res, string qual) ParseSort(string s, string defaultQual)
        {
            string res = "", qual = defaultQual;
            if (string.IsNullOrWhiteSpace(s)) return (res, qual);
            foreach (var part in s.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2)
                {
                    if (kv[0] == "res")  res  = kv[1];
                    if (kv[0] == "qual") qual = kv[1];
                }
            }
            return (res, qual);
        }
    }

    // ── Row caches (hold live TMP refs so we update in-place) ────────────────

    internal sealed class StockpileRowCache
    {
        public GameObject GO;
        public TextMeshProUGUI BodyTMP, ResTMP, QtyTMP, StateTMP, InTMP, OutTMP, NetTMP, LastsTMP;
        public Image BgImage;
        public Image  BodySprite;
        public Button BodyBtn;
    }

    internal sealed class DepositRowCache
    {
        public GameObject GO;
        public TextMeshProUGUI BodyTMP, ResTMP, TotalTMP, EffTMP, TimeTMP;
        public GameObject BadgesGO;
        public Image BgImage;
        public string BadgeSig;
        public Image  BodySprite;
        public Button BodyBtn;
    }

    // ── Injector (called from Harmony patch) ─────────────────────────────────

    internal static class ResourceTrackerInjector
    {
        private static readonly FieldInfo FieldShowBtn =
            typeof(NotificationManager).GetField("showNotificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FieldHistoryGO =
            typeof(NotificationManager).GetField("notificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static TMP_FontAsset FontAsset;
        internal static ManualLogSource Log;

        internal static void Inject(NotificationManager nm, ManualLogSource log, ResourceTrackerConfig config)
        {
            Log = log;
            try
            {
                Button showBtn = FieldShowBtn?.GetValue(nm) as Button;
                if (showBtn == null) { log.LogError("[RT] showNotificationHistory not found"); return; }

                GameObject historyGO = FieldHistoryGO?.GetValue(nm) as GameObject;
                if (historyGO == null) { log.LogError("[RT] notificationHistory not found"); return; }

                Canvas canvas = showBtn.GetComponentInParent<Canvas>();
                if (canvas == null) { log.LogError("[RT] Canvas not found"); return; }

                FontAsset = historyGO.GetComponentInChildren<TextMeshProUGUI>(true)?.font;

                // ── Panel ─────────────────────────────────────────────────────
                GameObject panelGO = UnityEngine.Object.Instantiate(historyGO, canvas.transform);
                panelGO.name = "modResourceTrackerPanel";
                panelGO.transform.SetSiblingIndex(historyGO.transform.GetSiblingIndex());

                for (int i = panelGO.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(panelGO.transform.GetChild(i).gameObject);
                foreach (var sr in panelGO.GetComponents<ScrollRect>())   UnityEngine.Object.DestroyImmediate(sr);
                foreach (var lg in panelGO.GetComponents<LayoutGroup>())  UnityEngine.Object.DestroyImmediate(lg);
                var csf = panelGO.GetComponent<ContentSizeFitter>();
                if (csf != null) UnityEngine.Object.DestroyImmediate(csf);

                Image panelBg = panelGO.GetComponent<Image>() ?? panelGO.AddComponent<Image>();
                Image bgSrc = historyGO.GetComponent<Image>();
                if (bgSrc?.sprite != null)
                { panelBg.sprite = bgSrc.sprite; panelBg.color = bgSrc.color; panelBg.type = bgSrc.type; panelBg.material = bgSrc.material; }
                else panelBg.color = new Color(0.07f, 0.08f, 0.10f, 0.96f);
                panelBg.raycastTarget = true;

                foreach (var cg in panelGO.GetComponents<CanvasGroup>())
                { cg.interactable = true; cg.blocksRaycasts = true; }

                panelGO.AddComponent<LayoutElement>().ignoreLayout = true;

                RectTransform panelRT = panelGO.GetComponent<RectTransform>();
                panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
                panelRT.pivot     = new Vector2(0f, 1f);
                panelRT.sizeDelta = new Vector2(920f, 390f);
                panelRT.anchoredPosition = new Vector2(-9999f, -9999f);
                panelGO.SetActive(false);

                // ── Header bar (absolute, top — no outer VLG like Power/Fleet Tracker) ──
                const float HeaderH = 108f;
                var headerGO = new GameObject("HeaderBar", typeof(RectTransform));
                headerGO.transform.SetParent(panelGO.transform, false);
                var headerRT = headerGO.GetComponent<RectTransform>();
                headerRT.anchorMin        = new Vector2(0f, 1f);
                headerRT.anchorMax        = new Vector2(1f, 1f);
                headerRT.pivot            = new Vector2(0.5f, 1f);
                headerRT.sizeDelta        = new Vector2(-16f, HeaderH);
                headerRT.anchoredPosition = new Vector2(0f, -8f);
                var headerVLG = headerGO.AddComponent<VerticalLayoutGroup>();
                headerVLG.childControlHeight     = true;
                headerVLG.childControlWidth      = true;
                headerVLG.childForceExpandHeight = false;
                headerVLG.childForceExpandWidth  = true;
                headerVLG.spacing = 2f;
                headerVLG.padding = new RectOffset(2, 2, 4, 2);

                // ── Scroll viewport (absolute, fills below header — matches Power Tracker) ─
                var viewportGO = new GameObject("ScrollViewport", typeof(RectTransform));
                viewportGO.transform.SetParent(panelGO.transform, false);
                viewportGO.transform.SetAsLastSibling();
                var viewportRT = viewportGO.GetComponent<RectTransform>();
                viewportRT.anchorMin = Vector2.zero;
                viewportRT.anchorMax = Vector2.one;
                viewportRT.pivot     = new Vector2(0.5f, 0.5f);
                viewportRT.offsetMin = new Vector2(8f, 8f);
                viewportRT.offsetMax = new Vector2(-22f, -(8f + HeaderH + 4f));
                viewportGO.AddComponent<RectMask2D>();

                // ── Scroll content ────────────────────────────────────────────
                var contentGO = new GameObject("ScrollContent", typeof(RectTransform));
                contentGO.transform.SetParent(viewportGO.transform, false);
                var contentRT = contentGO.GetComponent<RectTransform>();
                contentRT.anchorMin = new Vector2(0f, 1f);
                contentRT.anchorMax = new Vector2(1f, 1f);
                contentRT.pivot     = new Vector2(0.5f, 1f);
                contentRT.sizeDelta = Vector2.zero;
                var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
                contentVLG.childControlHeight     = true;
                contentVLG.childControlWidth      = true;
                contentVLG.childForceExpandHeight = false;
                contentVLG.childForceExpandWidth  = true;
                contentVLG.spacing  = 1f;
                contentVLG.padding  = new RectOffset(4, 4, 2, 2);
                var contentCSF = contentGO.AddComponent<ContentSizeFitter>();
                contentCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
                contentCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                // ── Vertical scrollbar (matches Power Tracker) ────────────────
                var scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform));
                scrollbarGO.transform.SetParent(panelGO.transform, false);
                var scrollbarRT = scrollbarGO.GetComponent<RectTransform>();
                scrollbarRT.anchorMin        = new Vector2(1f, 0f);
                scrollbarRT.anchorMax        = new Vector2(1f, 1f);
                scrollbarRT.pivot            = new Vector2(1f, 0.5f);
                scrollbarRT.sizeDelta        = new Vector2(6f, -(8f + HeaderH + 4f + 8f));
                scrollbarRT.anchoredPosition = new Vector2(-8f, -(8f + HeaderH + 4f - 8f) / 2f);
                var scrollbarBg  = scrollbarGO.AddComponent<Image>();
                var scrollbar    = scrollbarGO.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;

                var slidingAreaGO = new GameObject("SlidingArea", typeof(RectTransform));
                slidingAreaGO.transform.SetParent(scrollbarGO.transform, false);
                var slidingAreaRT = slidingAreaGO.GetComponent<RectTransform>();
                slidingAreaRT.anchorMin = Vector2.zero; slidingAreaRT.anchorMax = Vector2.one;
                slidingAreaRT.sizeDelta = Vector2.zero; slidingAreaRT.anchoredPosition = Vector2.zero;

                var handleGO = new GameObject("Handle", typeof(RectTransform));
                handleGO.transform.SetParent(slidingAreaGO.transform, false);
                var handleRT = handleGO.GetComponent<RectTransform>();
                handleRT.anchorMin = Vector2.zero; handleRT.anchorMax = Vector2.one;
                handleRT.sizeDelta = Vector2.zero;
                var handleImg = handleGO.AddComponent<Image>();
                scrollbar.handleRect    = handleRT;
                scrollbar.targetGraphic = handleImg;
                CopyGameScrollbarStyle(scrollbarBg, handleImg);

                // ── ScrollRect directly on panelGO (matches all three reference trackers) ─
                var scrollRect = panelGO.AddComponent<ScrollRect>();
                scrollRect.viewport                    = viewportRT;
                scrollRect.content                     = contentRT;
                scrollRect.verticalScrollbar           = scrollbar;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                scrollRect.horizontal        = false;
                scrollRect.vertical          = true;
                scrollRect.scrollSensitivity = 30f;
                scrollRect.movementType      = ScrollRect.MovementType.Clamped;

                // ── Resize handle ─────────────────────────────────────────────
                var resizeHandleGO = new GameObject("ResizeHandle", typeof(RectTransform));
                resizeHandleGO.transform.SetParent(panelGO.transform, false);
                var resizeRT = resizeHandleGO.GetComponent<RectTransform>();
                resizeRT.anchorMin = new Vector2(0f, 0f); resizeRT.anchorMax = new Vector2(1f, 0f);
                resizeRT.pivot = new Vector2(0.5f, 1f);
                resizeRT.sizeDelta = new Vector2(0f, 10f); resizeRT.anchoredPosition = Vector2.zero;
                resizeHandleGO.AddComponent<Image>().color = Color.clear;
                resizeHandleGO.AddComponent<ResizeHandle>().PanelRT = panelRT;

                // Table header: fixed area between main header and scrollable viewport
                var tableHeaderGO = new GameObject("TableHeader", typeof(RectTransform));
                tableHeaderGO.transform.SetParent(panelGO.transform, false);
                var tableHeaderRT = tableHeaderGO.GetComponent<RectTransform>();
                // exact position set by ApplyHeaderHeight; leave at default for now
                var tableHeaderVLG = tableHeaderGO.AddComponent<VerticalLayoutGroup>();
                tableHeaderVLG.childControlHeight     = true;
                tableHeaderVLG.childControlWidth      = true;
                tableHeaderVLG.childForceExpandHeight = false;
                tableHeaderVLG.childForceExpandWidth  = true;
                tableHeaderVLG.spacing  = 0f;
                tableHeaderVLG.padding  = new RectOffset(0, 0, 0, 0);

                var panel = panelGO.AddComponent<ResourceTrackerPanel>();
                panel.Init(panelGO, panelRT, headerGO.transform, contentGO.transform, scrollRect, config,
                           headerRT, viewportRT, scrollbarRT,
                           tableHeaderGO.transform, tableHeaderRT);

                PauseScreenEscPatch.PanelGO = panelGO;

                // ── Indicator button (matches Launch Windows style) ───────────
                GameObject btnGO = new GameObject("modResourceTrackerButton", typeof(RectTransform));
                btnGO.transform.SetParent(canvas.transform, false);
                btnGO.transform.SetAsLastSibling();
                btnGO.AddComponent<LayoutElement>().ignoreLayout = true;

                RectTransform btnRT = btnGO.GetComponent<RectTransform>();
                btnRT.anchorMin = btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot     = new Vector2(0f, 1f);
                btnRT.sizeDelta = new Vector2(90f, 22f);
                btnRT.anchoredPosition = new Vector2(-9999f, -9999f);

                Image btnImg = btnGO.AddComponent<Image>();
                var origBtnImg = showBtn.GetComponent<Image>();
                if (origBtnImg != null)
                { btnImg.sprite = origBtnImg.sprite; btnImg.type = origBtnImg.type; btnImg.color = origBtnImg.color; btnImg.material = origBtnImg.material; }
                else btnImg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
                btnImg.raycastTarget = true;

                var btnTMP = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                btnTMP.transform.SetParent(btnGO.transform, false);
                var btnTMPRT = btnTMP.GetComponent<RectTransform>();
                btnTMPRT.anchorMin = Vector2.zero; btnTMPRT.anchorMax = Vector2.one;
                btnTMPRT.sizeDelta = Vector2.zero;
                btnTMP.font = FontAsset;
                btnTMP.text = "RESOURCES";
                btnTMP.fontSize = 9f;
                btnTMP.alignment = TextAlignmentOptions.Center;
                btnTMP.color = Color.white;
                btnTMP.raycastTarget = false;

                Button btn = btnGO.AddComponent<Button>();
                var btnColors = btn.colors;
                btnColors.normalColor      = new Color(1f, 1f, 1f, 1f);
                btnColors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
                btnColors.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
                btn.colors = btnColors;
                var mover = btnGO.AddComponent<DraggableMover>();
                mover.PanelGO  = panelGO;
                mover.PanelRT  = panelRT;
                mover.ShowBtnRT = showBtn.GetComponent<RectTransform>();

                btn.onClick.AddListener(() =>
                {
                    bool show = !panelGO.activeSelf;
                    panelGO.SetActive(show);
                    if (show) { mover.PlacePanelUnderButton(); panel.RefreshRows(force: true); }
                });

                var updater = btnGO.AddComponent<TrackerUpdater>();
                updater.Panel = panel;

                log.LogInfo("[RT] Injection complete");
            }
            catch (Exception ex)
            {
                log.LogError($"[RT] Inject failed: {ex}");
            }
        }

        private static void CopyGameScrollbarStyle(Image track, Image handle)
        {
            Scrollbar src = Resources.FindObjectsOfTypeAll<Scrollbar>()
                .FirstOrDefault(sb => sb.handleRect != null && sb.GetComponent<Image>() != null);
            if (src == null)
            {
                track.color  = new Color(0.06f, 0.12f, 0.14f, 0.9f);
                handle.color = new Color(0.05f, 0.62f, 0.68f, 0.9f);
                return;
            }
            Image srcTrack  = src.GetComponent<Image>();
            Image srcHandle = src.handleRect.GetComponent<Image>();
            if (srcTrack  != null) { track.sprite  = srcTrack.sprite;  track.color  = srcTrack.color;  track.type  = srcTrack.type; }
            if (srcHandle != null) { handle.sprite = srcHandle.sprite; handle.color = srcHandle.color; handle.type = srcHandle.type; }
        }
    }

    // ── Draggable button mover ────────────────────────────────────────────────

    internal sealed class DraggableMover : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        internal GameObject PanelGO;
        internal RectTransform PanelRT;
        internal RectTransform ShowBtnRT;

        private RectTransform _rt;
        private Canvas _canvas;
        private RectTransform _canvasRT;
        private Vector2 _dragOffset;
        private Vector2 _normalizedPos;
        private bool _normalizedPosSet;
        private Vector2 _lastCanvasSize;
        private bool _dragging;

        void Awake()
        {
            _rt       = GetComponent<RectTransform>();
            _canvas   = GetComponentInParent<Canvas>();
            _canvasRT = _canvas?.GetComponent<RectTransform>();
        }

        IEnumerator Start()
        {
            // Wait up to 3 seconds for all mod buttons to settle
            yield return new WaitForSeconds(3f);
            PositionButton();
        }

        void Update()
        {
            if (_canvasRT == null) return;
            Vector2 sz = _canvasRT.rect.size;
            if (sz != _lastCanvasSize) { _lastCanvasSize = sz; RestoreFromNormalized(); RepositionPanel(); }
            PauseScreenEscPatch.LateUpdateTick();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_rt == null || _canvas == null) return;
            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, e.position, cam, out var local);
            _dragOffset = _rt.anchoredPosition - local;
            _dragging = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || _rt == null || _canvas == null) return;
            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, e.position, cam, out var local);
            _rt.anchoredPosition = local + _dragOffset;
            Clamp();
            StoreNormalized();
            RepositionPanel();
        }

        public void OnEndDrag(PointerEventData e) => _dragging = false;

        void PositionButton()
        {
            if (_canvas == null || _canvasRT == null || _rt == null) return;

            // Find the best already-positioned reference: prefer Launch Windows, then indicators.
            var refRT = FindPositionedReference() ?? ShowBtnRT;
            if (refRT == null) return;

            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            var corners = new Vector3[4];
            refRT.GetWorldCorners(corners);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRT, new Vector2(corners[1].x, corners[1].y), cam, out var topLeft)) return;

            _rt.anchoredPosition = new Vector2(topLeft.x - 4f - _rt.sizeDelta.x, topLeft.y);
            Clamp();
            StoreNormalized();
            RepositionPanel();
            ResourceTrackerInjector.Log?.LogInfo($"[RT] indicator at {_rt.anchoredPosition} (ref={refRT.gameObject.name})");
        }

        // Returns the best reference that has actually been placed on-screen (world x within bounds).
        RectTransform FindPositionedReference()
        {
            if (_canvas == null) return null;
            RectTransform launchWindows = null;
            RectTransform indicator     = null;
            var tmp = new Vector3[4];
            foreach (RectTransform rt in _canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == _rt || rt.GetComponent<Image>() == null) continue;
                string n = rt.gameObject.name ?? "";
                rt.GetWorldCorners(tmp);
                bool valid = Mathf.Abs(tmp[1].x) < 5000f;  // not at sentinel -9999
                if (!valid) continue;
                if (n.Equals("modLaunchWindowsButton", StringComparison.OrdinalIgnoreCase))
                    launchWindows = rt;
                else if (n.Equals("modFleetTrackerButton",  StringComparison.OrdinalIgnoreCase) ||
                         n.Equals("modPowerTrackerButton",  StringComparison.OrdinalIgnoreCase) ||
                         n.Equals("modLifeSupportButton",   StringComparison.OrdinalIgnoreCase))
                    indicator = indicator ?? rt;
            }
            return launchWindows ?? indicator;
        }

        void StoreNormalized()
        {
            if (_canvasRT == null) return;
            Rect cr = _canvasRT.rect;
            if (cr.xMax <= 0f || cr.yMax <= 0f) return;
            _normalizedPos = new Vector2(_rt.anchoredPosition.x / cr.xMax, _rt.anchoredPosition.y / cr.yMax);
            _normalizedPosSet = true;
        }

        void RestoreFromNormalized()
        {
            if (!_normalizedPosSet || _canvasRT == null) return;
            Rect cr = _canvasRT.rect;
            _rt.anchoredPosition = new Vector2(_normalizedPos.x * cr.xMax, _normalizedPos.y * cr.yMax);
            Clamp();
        }

        void Clamp()
        {
            if (_canvasRT == null || _rt == null) return;
            Rect cr = _canvasRT.rect; Vector2 s = _rt.sizeDelta, p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, cr.xMin, cr.xMax - s.x);
            p.y = Mathf.Clamp(p.y, cr.yMin + s.y, cr.yMax);
            _rt.anchoredPosition = p;
        }

        internal void PlacePanelUnderButton() => RepositionPanel();

        void RepositionPanel()
        {
            if (PanelRT == null || PanelGO == null || !PanelGO.activeSelf || _rt == null) return;
            Vector2 p = new Vector2(_rt.anchoredPosition.x, _rt.anchoredPosition.y - _rt.sizeDelta.y - 4f);
            if (_canvasRT != null)
            {
                Rect cr = _canvasRT.rect; Vector2 s = PanelRT.sizeDelta;
                p.x = Mathf.Clamp(p.x, cr.xMin, cr.xMax - s.x);
                p.y = Mathf.Clamp(p.y, cr.yMin + s.y, cr.yMax);
            }
            PanelRT.anchoredPosition = p;
        }
    }

    // ── Periodic updater ──────────────────────────────────────────────────────

    internal sealed class TrackerUpdater : MonoBehaviour
    {
        internal ResourceTrackerPanel Panel;
        private float _timer;
        private const float Interval = 5f;

        void Update()
        {
            if (Panel == null || !Panel.PanelGO.activeSelf) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer >= Interval) { _timer = 0f; Panel.RefreshRows(); }
        }
    }

    // ── Main panel ────────────────────────────────────────────────────────────

    internal sealed class ResourceTrackerPanel : MonoBehaviour
    {
        internal GameObject PanelGO;
        private RectTransform _panelRT;
        private RTTooltipTrigger _filterByTrigger;
        private RectTransform _headerRT;
        private RectTransform _viewportRT;
        private RectTransform _scrollbarRT;
        private Transform _tableHeaderParent;
        private RectTransform _tableHeaderRT;
        private const float TableHeaderH = 17f; // 16px header row + 1px separator
        private LayoutElement _stripLE;

        // Tab state
        private bool _stockpilesTab = true;

        // Filter state
        private readonly HashSet<string> _activeResources = new HashSet<string>();
        private readonly Dictionary<string, float> _depositMinQty  = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _depositMinQual = new Dictionary<string, float>();
        private readonly Dictionary<string, (TMP_InputField qty, TMP_InputField qual)> _depositFilterInputs =
            new Dictionary<string, (TMP_InputField, TMP_InputField)>();

        // Persistence
        private ResourceTrackerConfig _config;

        // UI references
        private Transform _contentParent;
        private ScrollRect _scrollRect;
        private GameObject _filterInputsGO;     // deposits-only filter inputs (inline in strip)

        // Tab button refs for highlighting
        private Button _tabStock;
        private Button _tabDepo;
        private Image  _tabStockBg;
        private Image  _tabDepoBg;

        // Toggle button refs keyed by resource ID
        private readonly Dictionary<string, Button> _toggleBtns = new Dictionary<string, Button>();
        private Button _clearFilterBtn;

        // All normal resources (populated at Init)
        private List<ResourceDefinition> _allResources;

        // Signature caching
        private string _lastSig;
        private bool   _forceRefresh = true;

        // Row caches: key = "bodyName\x1fresourceId"
        private readonly Dictionary<string, StockpileRowCache> _stockpileCache =
            new Dictionary<string, StockpileRowCache>();
        private readonly Dictionary<string, DepositRowCache> _depositCache =
            new Dictionary<string, DepositRowCache>();
        private readonly Dictionary<string, Sprite>     _bodySprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Game.Info.ObjectInfo> _bodyOIs = new Dictionary<string, Game.Info.ObjectInfo>();
        // Separator GOs between body groups: key = body name
        private readonly Dictionary<string, GameObject> _separatorCache =
            new Dictionary<string, GameObject>();
        private GameObject _emptyRowGO;
        private bool _tabChanged = true;   // true → rebuild header on next refresh
        private int  _headerChildCount;    // content children that are static (header + sep line)

        // Row-visibility filter: resource IDs that are hidden from the table
        private readonly HashSet<string> _hiddenResources = new HashSet<string>();
        // Per-resource TMP refs in the col-filter panel (for updating checkbox text in-place)
        private readonly Dictionary<string, TextMeshProUGUI> _colFilterTMPs =
            new Dictionary<string, TextMeshProUGUI>();
        private GameObject _colFilterPanel;
        private TextMeshProUGUI _colFilterAllTMP;

        // Sort state — each tab tracks its own resource + qualifier independently
        private string _depositSortRes  = "";       // "" = sort by body name
        private string _depositSortQual = "total";  // total|eff|bestquality|lasts
        private string _stockSortRes    = "";
        private string _stockSortQual   = "qty";    // qty|net|lasts

        // Sort row UI refs
        private TextMeshProUGUI _sortResTMP;
        private TextMeshProUGUI _sortQualTMP;
        private Button          _sortQualBtn;
        private GameObject      _sortDropPanel; // one floating option panel at a time

        private static readonly Color ColActive   = new Color(0.40f, 0.85f, 0.40f, 1f);
        private static readonly Color ColInactive = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color ColHeader   = new Color(0.65f, 0.65f, 0.65f, 1f);
        private static readonly Color ColBody     = new Color(0.90f, 0.90f, 0.90f, 1f);
        private static readonly Color ColDim      = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color ColGreen    = new Color(0.35f, 0.85f, 0.45f, 1f);
        private static readonly Color ColRed      = new Color(0.90f, 0.30f, 0.25f, 1f);
        private static readonly Color ColYellow   = new Color(0.90f, 0.80f, 0.25f, 1f);
        private static readonly Color ColGold     = new Color(0.92f, 0.75f, 0.20f, 1f);

        internal void Init(GameObject panelGO, RectTransform panelRT,
                           Transform headerParent, Transform contentParent,
                           ScrollRect scrollRect, ResourceTrackerConfig config,
                           RectTransform headerRT, RectTransform viewportRT, RectTransform scrollbarRT,
                           Transform tableHeaderParent, RectTransform tableHeaderRT)
        {
            PanelGO        = panelGO;
            _panelRT       = panelRT;
            _contentParent = contentParent;
            _scrollRect    = scrollRect;
            _config        = config;
            _headerRT      = headerRT;
            _viewportRT    = viewportRT;
            _scrollbarRT   = scrollbarRT;
            _tableHeaderParent = tableHeaderParent;
            _tableHeaderRT     = tableHeaderRT;

            _allResources = GetAllResources();
            LoadConfig();
            BuildChrome(headerParent);
            ApplyTabVisuals(_stockpilesTab);  // visual-only init; no prefs save, scroll reset, or refresh
        }

        void LoadConfig()
        {
            if (_config == null) return;
            foreach (var id in _config.LoadHidden())  _hiddenResources.Add(id);
            foreach (var id in _config.LoadActive())  _activeResources.Add(id);
            foreach (var kv in _config.LoadDepositQty())  _depositMinQty[kv.Key]  = kv.Value;
            foreach (var kv in _config.LoadDepositQual()) _depositMinQual[kv.Key] = kv.Value;
            var stockSort  = _config.LoadStockSort();   _stockSortRes  = stockSort.res;  _stockSortQual  = stockSort.qual;
            var depositSort = _config.LoadDepositSort(); _depositSortRes = depositSort.res; _depositSortQual = depositSort.qual;
            _stockpilesTab = _config.LoadStockTab();
        }

        // ── UI Construction ───────────────────────────────────────────────────

        void BuildChrome(Transform headerParent)
        {
            var font = ResourceTrackerInjector.FontAsset;
            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 1-tabRow");
            // Row 1: tabs + close — child of headerParent (absolute header bar)
            var tabRow = MakeHRow("TabRow", headerParent, 22f, 0f);
            _tabStock = MakeTabButton("TabStock", tabRow.transform, font, "STOCKPILES", () => SwitchTab(true));
            _tabStockBg = _tabStock.GetComponent<Image>();
            _tabDepo  = MakeTabButton("TabDepo",  tabRow.transform, font, "DEPOSITS",   () => SwitchTab(false));
            _tabDepoBg = _tabDepo.GetComponent<Image>();
            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(tabRow.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var colsBtn = MakeSmallButton("ColsBtn", tabRow.transform, font, "Show Resources", 100f,
                new Color(0.10f, 0.14f, 0.20f, 0.5f), new Color(0.20f, 0.30f, 0.45f, 0.8f));
            colsBtn.onClick.AddListener(ToggleColFilter);
            var helpSpacer = new GameObject("HelpSp", typeof(RectTransform));
            helpSpacer.transform.SetParent(tabRow.transform, false);
            helpSpacer.AddComponent<LayoutElement>().preferredWidth = 8f;
            var helpBtn = MakeSmallButton("HelpBtn", tabRow.transform, font, "?", 20f,
                new Color(0.08f, 0.18f, 0.10f, 0.5f), new Color(0.12f, 0.40f, 0.20f, 0.8f));
            const string HelpBase = "https://github.com/stockmaj/solar-expanse-resource-tracker#";
            var helpLbl = helpBtn.GetComponentInChildren<TextMeshProUGUI>();
            helpBtn.onClick.AddListener(() =>
            {
                string url = HelpBase + (_stockpilesTab ? "stockpiles-tab" : "deposits-tab");
                UnityEngine.Application.OpenURL(url);
                if (helpLbl != null) StartCoroutine(FlashText(helpLbl, "?", "✓", 2f));
            });
            // Overlay child for tooltip (Image + TMP must not share a GO)
            {
                var hov = new GameObject("TTOverlay", typeof(RectTransform));
                hov.transform.SetParent(helpBtn.gameObject.transform, false);
                var hovRT = hov.GetComponent<RectTransform>();
                hovRT.anchorMin = Vector2.zero; hovRT.anchorMax = Vector2.one;
                hovRT.offsetMin = hovRT.offsetMax = Vector2.zero;
                hov.AddComponent<Image>().color = Color.clear;
                hov.GetComponent<Image>().raycastTarget = true;
                hov.AddComponent<RTTooltipTrigger>().Text =
                    "Open README for the active tab";
            }
            var closeBtn = MakeSmallButton("Close", tabRow.transform, font, "×", 20f,
                new Color(0.20f, 0.05f, 0.05f, 0f), new Color(0.55f, 0.10f, 0.10f, 0.8f));
            closeBtn.onClick.AddListener(() => { CloseColFilter(); PanelGO.SetActive(false); });

            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 2-strip");
            // Row 2: toggle strip — outer HLG splits into iconsGO (fixed) + filterInputsGO (flex fill)
            var stripContainer = MakeHRow("ToggleStrip", headerParent, 28f, 2f);
            var stripLE2 = stripContainer.GetComponent<LayoutElement>() ?? stripContainer.AddComponent<LayoutElement>();
            stripLE2.preferredHeight = 28f;
            _stripLE = stripLE2;
            var stripHlg = stripContainer.GetComponent<HorizontalLayoutGroup>();
            stripHlg.childControlWidth      = true;
            stripHlg.childForceExpandWidth  = false;
            stripHlg.childControlHeight     = true;
            stripHlg.childForceExpandHeight = false;  // icons stay 28px even when filter rows grow
            stripHlg.spacing  = 0f;   // no gap between iconsGO and filterInputsGO
            stripHlg.padding  = new RectOffset(0, 0, 0, 0);

            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 3-iconsGO");
            // Left sub-group: "Bodies with:" label + icon buttons + clear button (fixed natural width)
            var iconsGO = new GameObject("IconGroup", typeof(RectTransform));
            iconsGO.transform.SetParent(stripContainer.transform, false);
            var iconsLE = iconsGO.AddComponent<LayoutElement>();
            iconsLE.flexibleWidth   = 0f;   // never expands horizontally
            iconsLE.preferredHeight = 28f;  // never stretches vertically when filter rows grow
            var iconsHLG = iconsGO.AddComponent<HorizontalLayoutGroup>();
            iconsHLG.childControlWidth      = true;
            iconsHLG.childForceExpandWidth  = false;
            iconsHLG.childControlHeight     = true;
            iconsHLG.childForceExpandHeight = false;
            iconsHLG.spacing  = 3f;
            iconsHLG.padding  = new RectOffset(2, 2, 2, 2);

            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 4-filterByLabel");
            // Wrapper holds Image (raycast target) + tooltip; TMP is a child so the two
            // MaskableGraphic types (Image, TextMeshProUGUI) live on separate GameObjects.
            var filterByWrap = new GameObject("FilterByWrap", typeof(RectTransform));
            filterByWrap.transform.SetParent(iconsGO.transform, false);
            filterByWrap.AddComponent<LayoutElement>().preferredWidth = 65f;
            var filterByImg = filterByWrap.AddComponent<Image>();
            filterByImg.color = Color.clear;
            filterByImg.raycastTarget = true;
            filterByWrap.AddComponent<RTTooltipTrigger>().Text =
                "Show only bodies that have all selected resources stockpiled";
            _filterByTrigger = filterByWrap.GetComponent<RTTooltipTrigger>();
            // TMP label as a stretch-fill child of the wrapper
            var filterByLblGO = new GameObject("Lbl", typeof(RectTransform));
            filterByLblGO.transform.SetParent(filterByWrap.transform, false);
            var filterByLblRT = filterByLblGO.GetComponent<RectTransform>();
            filterByLblRT.anchorMin = Vector2.zero; filterByLblRT.anchorMax = Vector2.one;
            filterByLblRT.offsetMin = filterByLblRT.offsetMax = Vector2.zero;
            var filterByTMP = filterByLblGO.AddComponent<TextMeshProUGUI>();
            if (font != null) filterByTMP.font = font;
            filterByTMP.text = "Bodies with:"; filterByTMP.fontSize = 8f;
            filterByTMP.alignment = TextAlignmentOptions.MidlineRight;
            filterByTMP.color = ColDim; filterByTMP.enableWordWrapping = false;
            filterByTMP.raycastTarget = false;

            ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildChrome: 5-iconLoop ({_allResources.Count})");
            foreach (var rd in _allResources)
            {
                if (rd == null) continue;
                string rdId = rd.ID;
                string tooltip = GetResourceName(rd);
                string iconStr = rd.IconString ?? "";
                var btn = MakeSpriteToggleButton(rdId, iconsGO.transform, font, iconStr);
                btn.onClick.AddListener(() => ToggleResource(rdId));
                _toggleBtns[rdId] = btn;
                var trigger = btn.gameObject.AddComponent<RTTooltipTrigger>();
                trigger.Text = tooltip;
            }

            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 6-clearBtn");
            // Clear filter button — visible only when ≥1 resource is toggled
            _clearFilterBtn = MakeSpriteToggleButton("ClearFilter", iconsGO.transform, font, "×");
            _clearFilterBtn.onClick.AddListener(ClearActiveFilter);
            var clearTMP = _clearFilterBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (clearTMP != null) clearTMP.color = ColRed;
            var clearBtnImg = _clearFilterBtn.GetComponent<Image>();
            if (clearBtnImg != null) clearBtnImg.color = new Color(0.25f, 0.08f, 0.08f, 0.75f);
            _clearFilterBtn.gameObject.AddComponent<RTTooltipTrigger>().Text = "Clear all active resource filters";
            _clearFilterBtn.gameObject.SetActive(false);

            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 7-filterInputs");
            // Right group: filter inputs fill all remaining strip space (VLG for multi-row support)
            _filterInputsGO = new GameObject("FilterInputs", typeof(RectTransform));
            _filterInputsGO.transform.SetParent(stripContainer.transform, false);
            _filterInputsGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var filterInputsVLG = _filterInputsGO.AddComponent<VerticalLayoutGroup>();
            filterInputsVLG.childControlWidth      = true;
            filterInputsVLG.childForceExpandWidth  = true;
            filterInputsVLG.childControlHeight     = true;
            filterInputsVLG.childForceExpandHeight = false;
            filterInputsVLG.spacing  = 2f;
            filterInputsVLG.padding  = new RectOffset(4, 4, 2, 2);
            _filterInputsGO.SetActive(false);

            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 8-sortRow");
            // Row 3 (was Row 4): sort row
            BuildSortRow(font, headerParent);

            // Scroll content parent is set from Inject() via Init()
            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: 9-highlights");
            UpdateTabHighlights();
            UpdateToggleHighlights();
            ResourceTrackerInjector.Log?.LogInfo("[RT] BuildChrome: done");
        }

        // Pure layout/visual setup — safe to call from both Init and user-initiated SwitchTab.
        // No config saves (except the active-resource cleanup), no scroll reset, no data refresh.
        void ApplyTabVisuals(bool stockpiles)
        {
            if (_filterInputsGO != null) _filterInputsGO.SetActive(!stockpiles);
            if (stockpiles && _stripLE != null) { _stripLE.preferredHeight = 28f; ApplyHeaderHeight(); }
            if (_filterByTrigger != null)
                _filterByTrigger.Text = stockpiles
                    ? "Show only bodies that have all selected resources stockpiled"
                    : "Show only bodies that have all selected resources as explored deposits";
            foreach (var kv in _toggleBtns)
            {
                bool showOnThisTab = stockpiles || IsDepositResource(kv.Key);
                kv.Value.gameObject.SetActive(showOnThisTab);
                if (!showOnThisTab) _activeResources.Remove(kv.Key);
            }
            if (!stockpiles) _config?.SaveActive(_activeResources);
            if (!stockpiles) RebuildDepositFilterRow();
            UpdateTabHighlights();
            UpdateToggleHighlights();
        }

        void SwitchTab(bool stockpiles)
        {
            _stockpilesTab = stockpiles;
            _config?.SaveStockTab(stockpiles);
            ApplyTabVisuals(stockpiles);
            UpdateSortUI();
            if (_sortDropPanel != null) { UnityEngine.Object.Destroy(_sortDropPanel); _sortDropPanel = null; }
            _tabChanged   = true;
            _lastSig      = null;
            _forceRefresh = true;
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
            RefreshRows(force: true);
        }

        void RebuildDepositFilterRow()
        {
            if (_filterInputsGO == null) return;
            for (int i = _filterInputsGO.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_filterInputsGO.transform.GetChild(i).gameObject);
            _depositFilterInputs.Clear();

            var font = ResourceTrackerInjector.FontAsset;

            var depositIds = _allResources
                .Select(r => r.ID)
                .Where(id => _activeResources.Contains(id) && IsDepositResource(id))
                .ToList();

            if (depositIds.Count == 0)
            {
                MakeLabel("FilterHint", _filterInputsGO.transform, font,
                    "Select a resource above to filter deposits", 8f, 0f,
                    TextAlignmentOptions.MidlineLeft, ColDim, flexibleWidth: 1f);
                SetStripHeight(28f);
                return;
            }

            const float CellH = 34f;    // 12px header row + 2px gap + 20px data row
            const float ColQty  = 38f;  // qty input width
            const float ColQual = 36f;  // qual input width
            const float ColUnit = 16f;  // "KT" label width

            int numRows = (depositIds.Count + 1) / 2;

            for (int rowIdx = 0; rowIdx < numRows; rowIdx++)
            {
                // Outer HLG row containing 2 cells (or 1 cell + spacer)
                var rowGO = MakeHRow($"FRow{rowIdx}", _filterInputsGO.transform, CellH, 4f);

                for (int colIdx = 0; colIdx < 2; colIdx++)
                {
                    int itemIdx = rowIdx * 2 + colIdx;

                    if (itemIdx >= depositIds.Count)
                    {
                        var sp = new GameObject("FSpacer", typeof(RectTransform));
                        sp.transform.SetParent(rowGO.transform, false);
                        sp.AddComponent<LayoutElement>().flexibleWidth = 1f;
                        break;
                    }

                    string rdId  = depositIds[itemIdx];
                    var rd       = _allResources.Find(r => r.ID == rdId);
                    string name  = rd != null ? GetResourceName(rd) : rdId;
                    string icon  = rd?.IconString ?? "";
                    string capturedId = rdId;
                    float initQty  = _depositMinQty.TryGetValue(rdId,  out float q)  ? q  : 0f;
                    float initQual = _depositMinQual.TryGetValue(rdId, out float ql) ? ql : 0f;

                    // Cell: VLG — header sub-row on top, data sub-row below
                    var cell    = new GameObject($"FCell_{rdId}", typeof(RectTransform));
                    cell.transform.SetParent(rowGO.transform, false);
                    var cellLE = cell.AddComponent<LayoutElement>();
                    cellLE.flexibleWidth  = 1f;
                    cellLE.preferredWidth = 1f;  // override content preferred so both cols get equal width
                    var cellVLG = cell.AddComponent<VerticalLayoutGroup>();
                    cellVLG.childControlHeight     = true;
                    cellVLG.childControlWidth      = true;
                    cellVLG.childForceExpandHeight = false;
                    cellVLG.childForceExpandWidth  = true;
                    cellVLG.spacing = 2f;
                    cellVLG.padding = new RectOffset(0, 0, 0, 0);

                    // Header sub-row: labels above qty | KT-spacer | qual
                    var hdrRow = MakeHRow($"FCH_{rdId}", cell.transform, 14f, 0f);
                    MakeLabel($"FHL_{rdId}",   hdrRow.transform, font, "", 8.5f, 0f,
                        TextAlignmentOptions.MidlineRight, ColDim, flexibleWidth: 1f);
                    MakeLabel($"FHQTY_{rdId}", hdrRow.transform, font, "min qty", 8.5f, ColQty,
                        TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f, 1f));
                    MakeLabel($"FHUN_{rdId}",  hdrRow.transform, font, "", 8.5f, ColUnit,
                        TextAlignmentOptions.Center, ColDim);
                    MakeLabel($"FHQL_{rdId}",  hdrRow.transform, font, "min qual", 8.5f, ColQual,
                        TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f, 1f));

                    // Data sub-row: resource label | qty | KT | qual
                    var dataRow = MakeHRow($"FCD_{rdId}", cell.transform, 20f, 3f);
                    MakeLabel($"RL_{rdId}", dataRow.transform, font,
                        (icon.Length > 0 ? icon + " " : "") + name + ":",
                        8f, 0f, TextAlignmentOptions.MidlineRight, ColDim, flexibleWidth: 1f);

                    TMP_InputField qtyInputRef = null;
                    var qtyInput  = MakeInputField($"Q_{rdId}",  dataRow.transform, font,
                        initQty  > 0 ? initQty.ToString("F2")  : "0.00", ColQty,
                        s => {
                            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                            {
                                v = UnityEngine.Mathf.Max(v, 0f);
                                _depositMinQty[capturedId] = v;
                                if (qtyInputRef != null) qtyInputRef.text = v.ToString("F2");
                            }
                            else _depositMinQty.Remove(capturedId);
                            _config?.SaveDepositQty(_depositMinQty); _forceRefresh = true; RefreshRows(force: true);
                        });
                    qtyInputRef = qtyInput;

                    MakeLabel($"QtyUnit_{rdId}", dataRow.transform, font, "KT", 8f, ColUnit,
                        TextAlignmentOptions.MidlineLeft, ColDim);

                    // Qual: clamp 0–1.0, 1 decimal place, always shown
                    TMP_InputField qualInputRef = null;
                    var qualInput = MakeInputField($"QI_{rdId}", dataRow.transform, font,
                        initQual > 0 ? initQual.ToString("F1") : "0.0", ColQual,
                        s => {
                            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                            {
                                v = UnityEngine.Mathf.Clamp(v, 0f, 1f);
                                v = UnityEngine.Mathf.Round(v * 10f) / 10f;
                                _depositMinQual[capturedId] = v;
                                if (qualInputRef != null) qualInputRef.text = v.ToString("F1");
                            }
                            else _depositMinQual.Remove(capturedId);
                            _config?.SaveDepositQual(_depositMinQual);
                            _forceRefresh = true; RefreshRows(force: true);
                        });
                    qualInputRef = qualInput;

                    _depositFilterInputs[rdId] = (qtyInput, qualInput);
                }
            }

            // Resize strip to accommodate the filter rows
            float filterH = numRows * CellH + (numRows - 1) * 2f;  // rows × height + gaps
            SetStripHeight(filterH + 6f);  // +6 for filterInputsVLG padding (2+2) + breathing room
        }

        void UpdateTabHighlights()
        {
            SetTabHighlight(_tabStock, _stockpilesTab);
            SetTabHighlight(_tabDepo,  !_stockpilesTab);
        }

        void ApplyHeaderHeight(float filterRowH = 0f)
        {
            float stripH  = _stripLE != null ? _stripLE.preferredHeight : 28f;
            float h       = 54f + stripH;  // 4+22+2+stripH+2+22+2 = all header rows
            float topGap  = 8f + h + 4f;                    // space from panel top to content area
            float vpTop   = topGap + TableHeaderH;          // viewport starts below table header
            if (_headerRT  != null) _headerRT.sizeDelta        = new Vector2(-16f, h);
            if (_tableHeaderRT != null)
            {
                // Same anchor convention as headerGO: top-anchored, measure downward
                _tableHeaderRT.anchorMin        = new Vector2(0f, 1f);
                _tableHeaderRT.anchorMax        = new Vector2(1f, 1f);
                _tableHeaderRT.pivot            = new Vector2(0.5f, 1f);
                _tableHeaderRT.sizeDelta        = new Vector2(-30f, TableHeaderH);
                _tableHeaderRT.anchoredPosition = new Vector2(0f, -topGap);
            }
            if (_viewportRT != null) _viewportRT.offsetMax      = new Vector2(-22f, -vpTop);
            if (_scrollbarRT != null)
            {
                _scrollbarRT.sizeDelta        = new Vector2(6f,  -(vpTop + 8f));
                _scrollbarRT.anchoredPosition = new Vector2(-8f, -(vpTop - 8f) / 2f);
            }
        }

        void SetStripHeight(float h)
        {
            if (_stripLE != null) _stripLE.preferredHeight = h;
            ApplyHeaderHeight();
        }

        System.Collections.IEnumerator FlashText(TextMeshProUGUI tmp, string restore, string flash, float seconds)
        {
            tmp.text = flash;
            yield return new WaitForSeconds(seconds);
            if (tmp != null) tmp.text = restore;
        }

        private static readonly Color TabBgActive   = new Color(0.08f, 0.28f, 0.32f, 1.00f);  // Fleet Tracker teal
        private static readonly Color TabBgInactive = new Color(0.12f, 0.14f, 0.16f, 0.94f); // Fleet Tracker dark

        void SetTabHighlight(Button btn, bool active)
        {
            if (btn == null) return;
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.color = active ? ColActive : ColInactive;
            var bg = btn.GetComponent<Image>();
            if (bg != null) bg.color = active ? TabBgActive : TabBgInactive;
        }

        // ── Sort row ──────────────────────────────────────────────────────────

        void BuildSortRow(TMP_FontAsset font, Transform parent)
        {
            var row = MakeHRow("SortRow", parent, 22f, 4f);
            MakeLabel("SortLbl", row.transform, font, "Sort:", 9f, 30f,
                TextAlignmentOptions.MidlineRight, ColDim);

            // Resource dropdown button
            var resBtnGO = new GameObject("SortResBtn", typeof(RectTransform));
            resBtnGO.transform.SetParent(row.transform, false);
            resBtnGO.AddComponent<LayoutElement>().preferredWidth = 130f;
            var resBg = resBtnGO.AddComponent<Image>();
            resBg.color = new Color(0.10f, 0.14f, 0.20f, 0.6f);
            var resBtn = resBtnGO.AddComponent<Button>();
            SetBtnColors(resBtn);
            _sortResTMP = MakeBtnLabel(resBtnGO, font, SortResLabel());
            resBtn.onClick.AddListener(OpenResourceSortDropdown);

            // Qualifier dropdown button
            var qualBtnGO = new GameObject("SortQualBtn", typeof(RectTransform));
            qualBtnGO.transform.SetParent(row.transform, false);
            qualBtnGO.AddComponent<LayoutElement>().preferredWidth = 100f;
            var qualBg = qualBtnGO.AddComponent<Image>();
            qualBg.color = new Color(0.10f, 0.14f, 0.20f, 0.6f);
            _sortQualBtn = qualBtnGO.AddComponent<Button>();
            SetBtnColors(_sortQualBtn);
            _sortQualTMP = MakeBtnLabel(qualBtnGO, font, SortQualLabel());
            _sortQualBtn.onClick.AddListener(OpenQualifierSortDropdown);
            _sortQualBtn.interactable = ActiveSortRes() != "";

            // spacer
            var sp = new GameObject("SSp", typeof(RectTransform));
            sp.transform.SetParent(row.transform, false);
            sp.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        string ActiveSortRes()  => _stockpilesTab ? _stockSortRes  : _depositSortRes;
        string ActiveSortQual() => _stockpilesTab ? _stockSortQual : _depositSortQual;

        string SortResLabel()
        {
            string res = ActiveSortRes();
            if (res == "") return "Body name ▼";
            var rd = _allResources.Find(r => r.ID == res);
            return (rd != null ? GetResourceName(rd) : res) + " ▼";
        }

        string SortQualLabel()
        {
            string res = ActiveSortRes();
            if (res == "") return "A→Z";
            string q = ActiveSortQual();
            if (_stockpilesTab)
            {
                switch (q)
                {
                    case "qty":   return "Quantity ▼";
                    case "net":   return "Net/day ▼";
                    case "lasts": return "Lasts ▼";
                    default:      return "Quantity ▼";
                }
            }
            else
            {
                switch (q)
                {
                    case "total":      return "Total ▼";
                    case "eff":        return "Effective ▼";
                    case "bestquality":return "Best quality ▼";
                    case "lasts":      return "Lasts ▼";
                    default:           return "Total ▼";
                }
            }
        }

        void OpenResourceSortDropdown()
        {
            CloseColFilter();
            var options = new List<(string label, string value)>();
            options.Add(("Body name", ""));
            foreach (var rd in _allResources)
                options.Add((GetResourceName(rd), rd.ID));

            OpenSortDropdown(options, val =>
            {
                if (_stockpilesTab) _stockSortRes  = val;
                else                _depositSortRes = val;
                if (val == "") { _stockSortQual = "qty"; _depositSortQual = "total"; }
                _config?.SaveStockSort(_stockSortRes, _stockSortQual);
                _config?.SaveDepositSort(_depositSortRes, _depositSortQual);
                UpdateSortUI();
                _forceRefresh = true;
                RefreshRows(force: true);
            });
        }

        void OpenQualifierSortDropdown()
        {
            CloseColFilter();
            List<(string label, string value)> options;
            if (_stockpilesTab)
                options = new List<(string, string)>
                {
                    ("Quantity",  "qty"),
                    ("Net/day",   "net"),
                    ("Lasts",     "lasts"),
                };
            else
                options = new List<(string, string)>
                {
                    ("Total",        "total"),
                    ("Effective",    "eff"),
                    ("Best quality", "bestquality"),
                    ("Lasts",        "lasts"),
                };

            OpenSortDropdown(options, val =>
            {
                if (_stockpilesTab) _stockSortQual  = val;
                else                _depositSortQual = val;
                _config?.SaveStockSort(_stockSortRes, _stockSortQual);
                _config?.SaveDepositSort(_depositSortRes, _depositSortQual);
                UpdateSortUI();
                _forceRefresh = true;
                RefreshRows(force: true);
            });
        }

        void OpenSortDropdown(List<(string label, string value)> options, Action<string> onSelect)
        {
            if (_sortDropPanel != null) { UnityEngine.Object.Destroy(_sortDropPanel); _sortDropPanel = null; }

            var font = ResourceTrackerInjector.FontAsset;
            _sortDropPanel = new GameObject("SortDrop", typeof(RectTransform));
            _sortDropPanel.transform.SetParent(PanelGO.transform, false);
            _sortDropPanel.AddComponent<LayoutElement>().ignoreLayout = true;

            var rt = _sortDropPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            float itemH  = 18f;
            rt.sizeDelta = new Vector2(145f, options.Count * itemH + 4f);
            rt.anchoredPosition = new Vector2(34f, -66f); // below sort row

            _sortDropPanel.AddComponent<Image>().color = new Color(0.06f, 0.09f, 0.14f, 0.98f);

            var vlg = _sortDropPanel.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            vlg.spacing = 0f; vlg.padding = new RectOffset(2, 2, 2, 2);

            foreach (var opt in options)
            {
                string label = opt.label;
                string capturedValue = opt.value;
                var itemGO = new GameObject("Item", typeof(RectTransform));
                itemGO.transform.SetParent(_sortDropPanel.transform, false);
                itemGO.AddComponent<LayoutElement>().preferredHeight = itemH;
                var itemImg = itemGO.AddComponent<Image>();
                itemImg.color = Color.clear;
                var itemBtn = itemGO.AddComponent<Button>();
                var colors  = itemBtn.colors;
                colors.normalColor      = Color.white;
                colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                itemBtn.colors          = colors;
                var tmp = new GameObject("L", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                tmp.transform.SetParent(itemGO.transform, false);
                var tRT = tmp.GetComponent<RectTransform>();
                tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
                tRT.offsetMin = new Vector2(6, 0); tRT.offsetMax = Vector2.zero;
                if (font != null) tmp.font = font;
                tmp.text = label; tmp.fontSize = 9f; tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.color = ColBody; tmp.raycastTarget = false;
                itemBtn.onClick.AddListener(() =>
                {
                    onSelect(capturedValue);
                    if (_sortDropPanel != null) { UnityEngine.Object.Destroy(_sortDropPanel); _sortDropPanel = null; }
                });
            }
        }

        void UpdateSortUI()
        {
            if (_sortResTMP  != null) _sortResTMP.text  = SortResLabel();
            if (_sortQualTMP != null) _sortQualTMP.text = SortQualLabel();
            if (_sortQualBtn != null) _sortQualBtn.interactable = ActiveSortRes() != "";
        }

        static void SetBtnColors(Button btn)
        {
            var c = btn.colors;
            c.normalColor      = new Color(1f, 1f, 1f, 1f);
            c.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            c.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = c;
        }

        static TextMeshProUGUI MakeBtnLabel(GameObject parent, TMP_FontAsset font, string text)
        {
            var tmp = new GameObject("Lbl", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            tmp.transform.SetParent(parent.transform, false);
            var rt = tmp.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 0); rt.offsetMax = new Vector2(-4, 0);
            if (font != null) tmp.font = font;
            tmp.text = text; tmp.fontSize = 9f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = ColBody; tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ── Col-filter panel (show/hide rows by resource) ─────────────────────

        void ToggleColFilter()
        {
            if (_sortDropPanel != null) { UnityEngine.Object.Destroy(_sortDropPanel); _sortDropPanel = null; }
            if (_colFilterPanel == null) BuildColFilterPanel();
            bool show = !_colFilterPanel.activeSelf;
            _colFilterPanel.SetActive(show);
        }

        void CloseColFilter()
        {
            if (_colFilterPanel != null) _colFilterPanel.SetActive(false);
        }

        void BuildColFilterPanel()
        {
            ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildColFilterPanel: _allResources.Count={_allResources?.Count ?? -1}");
            var font = ResourceTrackerInjector.FontAsset;

            // Float above the content area, anchored top-right of the main panel
            _colFilterPanel = new GameObject("ColFilterPanel", typeof(RectTransform));
            _colFilterPanel.transform.SetParent(PanelGO.transform, false);
            _colFilterPanel.AddComponent<LayoutElement>().ignoreLayout = true;

            var rt = _colFilterPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(330f, 0f);   // 2×160 cells + spacing + padding; height auto
            rt.anchoredPosition = new Vector2(0f, -22f);  // below the tab row

            var panelCSF = _colFilterPanel.AddComponent<ContentSizeFitter>();
            panelCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            panelCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var panelMinLE = _colFilterPanel.GetComponent<LayoutElement>() ?? _colFilterPanel.AddComponent<LayoutElement>();
            panelMinLE.ignoreLayout = true;   // already set above, just ensure it's still set
            panelMinLE.minHeight    = 60f;

            var bg = _colFilterPanel.AddComponent<Image>();
            bg.color        = new Color(0.13f, 0.17f, 0.24f, 0.97f);  // lighter than main panel
            bg.raycastTarget = true;

            var vlg = _colFilterPanel.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight     = true;
            vlg.childControlWidth      = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.spacing = 0f;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            // ── Header ────────────────────────────────────────────────────────
            var hdrRow = MakeHRow("CFHdr", _colFilterPanel.transform, 18f, 0f);
            var hdrLE  = hdrRow.GetComponent<LayoutElement>();
            hdrLE.flexibleWidth = 1f;
            var hdrHLG = hdrRow.GetComponent<HorizontalLayoutGroup>();
            hdrHLG.childForceExpandWidth = true;
            MakeLabel("CFTitle", hdrRow.transform, font, "SHOW / HIDE ROWS",
                8f, 0f, TextAlignmentOptions.MidlineLeft, ColHeader, flexibleWidth: 1f);
            var cfCloseBtn = MakeSmallButton("CFClose", hdrRow.transform, font, "×", 18f,
                new Color(0.18f, 0.08f, 0.08f, 0.75f), new Color(0.55f, 0.10f, 0.10f, 0.9f));
            cfCloseBtn.onClick.AddListener(CloseColFilter);

            MakeSeparator(_colFilterPanel.transform);

            // ── Show All / Hide All ───────────────────────────────────────────
            var allRow = MakeHRow("CFAll", _colFilterPanel.transform, 20f, 0f);
            var allBtn = MakeColFilterButton("CFAllBtn", allRow.transform, font, out _colFilterAllTMP);
            UpdateColFilterAllTMP();
            allBtn.onClick.AddListener(ToggleAllResources);

            MakeSeparator(_colFilterPanel.transform);

            // ── 2-column resource grid ────────────────────────────────────────
            var gridGO = new GameObject("CFGrid", typeof(RectTransform));
            gridGO.transform.SetParent(_colFilterPanel.transform, false);
            var gridLE = gridGO.AddComponent<LayoutElement>();
            gridLE.flexibleWidth = 1f;
            var gridCSF = gridGO.AddComponent<ContentSizeFitter>();
            gridCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            gridCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(160f, 20f);
            grid.spacing         = new Vector2(2f, 1f);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment  = TextAnchor.UpperLeft;
            grid.padding         = new RectOffset(2, 2, 2, 2);

            int cfRowIdx = 0;
            foreach (var rd in _allResources)
            {
                string rdId   = rd.ID;
                string rdName = GetResourceName(rd);
                ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildColFilterPanel: adding cell {cfRowIdx} id={rdId}");
                var cellGO  = new GameObject($"CF_{rdId}", typeof(RectTransform));
                cellGO.transform.SetParent(gridGO.transform, false);
                var cellImg = cellGO.AddComponent<Image>();
                cellImg.color = Color.clear;
                var cellBtn = cellGO.AddComponent<Button>();
                SetBtnColors(cellBtn);
                var cellTMP = new GameObject("Lbl", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                cellTMP.transform.SetParent(cellGO.transform, false);
                var cellRT = cellTMP.GetComponent<RectTransform>();
                cellRT.anchorMin = Vector2.zero; cellRT.anchorMax = Vector2.one;
                cellRT.offsetMin = new Vector2(4, 0); cellRT.offsetMax = Vector2.zero;
                if (font != null) cellTMP.font = font;
                cellTMP.fontSize           = 9f;
                cellTMP.alignment          = TextAlignmentOptions.MidlineLeft;
                cellTMP.color              = Color.white;
                cellTMP.enableWordWrapping = false;
                cellTMP.overflowMode       = TextOverflowModes.Ellipsis;
                cellTMP.raycastTarget      = false;
                cellTMP.richText           = true;
                cellTMP.text = ColFilterRowText(rdId, rdName, rd.IconString);
                _colFilterTMPs[rdId] = cellTMP;
                cellBtn.onClick.AddListener(() => ToggleResourceVisibility(rdId));
                cfRowIdx++;
            }
            ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildColFilterPanel: done, added {cfRowIdx} cells");

            _colFilterPanel.SetActive(false);
        }

        static Button MakeColFilterButton(string name, Transform parent, TMP_FontAsset font,
            out TextMeshProUGUI tmp)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var img = go.AddComponent<Image>();
            img.color = Color.clear;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
            colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            tmp = new GameObject("Lbl", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            tmp.transform.SetParent(go.transform, false);
            var rt = tmp.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 0); rt.offsetMax = Vector2.zero;
            if (font != null) tmp.font = font;
            tmp.fontSize          = 9f;
            tmp.alignment         = TextAlignmentOptions.MidlineLeft;
            tmp.color             = Color.white;
            tmp.enableWordWrapping = false;
            tmp.overflowMode      = TextOverflowModes.Ellipsis;
            tmp.raycastTarget     = false;
            tmp.richText          = true;
            return btn;
        }

        string ColFilterRowText(string rdId, string name, string iconString = "")
        {
            bool hidden = _hiddenResources.Contains(rdId);
            // ■ (solid) = visible, □ (outlined) = hidden — both in the same TMP font
            string box   = hidden ? "<color=#444444>□</color>" : "<color=#4CAF50>■</color>";
            string icon  = !string.IsNullOrEmpty(iconString) ? iconString + " " : "";
            string label = hidden ? $"<color=#555555>{name}</color>" : name;
            return $"{box} {icon}{label}";
        }

        void UpdateColFilterAllTMP()
        {
            if (_colFilterAllTMP == null) return;
            bool allHidden = _allResources.Count > 0 &&
                             _allResources.All(r => _hiddenResources.Contains(r.ID));
            bool allShown  = _hiddenResources.Count == 0;
            if (allShown)
                _colFilterAllTMP.text = "<color=#4CAF50>☑</color>  Show All";
            else if (allHidden)
                _colFilterAllTMP.text = "<color=#444444>☐</color>  Hide All";
            else
                _colFilterAllTMP.text = "<color=#FFC107>◧</color>  Show All / Hide All";
        }

        void ToggleResourceVisibility(string rdId)
        {
            if (_hiddenResources.Contains(rdId)) _hiddenResources.Remove(rdId);
            else _hiddenResources.Add(rdId);

            var rdDef = _allResources.Find(r => r.ID == rdId);
            if (_colFilterTMPs.TryGetValue(rdId, out var tmp))
                tmp.text = ColFilterRowText(rdId, GetResourceName(rdDef), rdDef?.IconString ?? "");
            UpdateColFilterAllTMP();
            _config?.SaveHidden(_hiddenResources);

            _forceRefresh = true;
            RefreshRows(force: true);
        }

        void ToggleAllResources()
        {
            bool anyShown = _hiddenResources.Count < _allResources.Count;
            if (anyShown)
                foreach (var rd in _allResources) _hiddenResources.Add(rd.ID);
            else
                _hiddenResources.Clear();
            foreach (var rd in _allResources)
                if (_colFilterTMPs.TryGetValue(rd.ID, out var t))
                    t.text = ColFilterRowText(rd.ID, GetResourceName(rd), rd.IconString);
            UpdateColFilterAllTMP();
            _config?.SaveHidden(_hiddenResources);

            _forceRefresh = true;
            RefreshRows(force: true);
        }

        // Applies _hiddenResources: removes hidden rows and drops empty bodies
        List<BodyStockpileGroup> FilterHiddenStockpiles(List<BodyStockpileGroup> bodies)
            => ResourceTrackerFilter.FilterHidden(bodies, _hiddenResources);

        List<BodyDepositGroup> FilterHiddenDeposits(List<BodyDepositGroup> bodies)
            => ResourceTrackerFilter.FilterHiddenDeposits(bodies, _hiddenResources);

        void ToggleResource(string rdId)
        {
            if (_activeResources.Contains(rdId))
            {
                _activeResources.Remove(rdId);
                _depositMinQty.Remove(rdId);
                _depositMinQual.Remove(rdId);
            }
            else
            {
                _activeResources.Add(rdId);
            }
            UpdateToggleHighlights();
            _config?.SaveActive(_activeResources);
            _config?.SaveDepositQty(_depositMinQty);
            _config?.SaveDepositQual(_depositMinQual);
            if (!_stockpilesTab) RebuildDepositFilterRow();
            _forceRefresh = true;
            RefreshRows(force: true);
        }

        void UpdateToggleHighlights()
        {
            foreach (var kv in _toggleBtns)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img != null)
                    img.color = _activeResources.Contains(kv.Key)
                        ? new Color(0.10f, 0.55f, 0.65f, 1.00f)   // selected: vivid teal
                        : new Color(0.12f, 0.14f, 0.18f, 0.70f);  // TabBgInactive dark
            }
            if (_clearFilterBtn != null)
                _clearFilterBtn.gameObject.SetActive(_activeResources.Count > 0);
        }

        void ClearActiveFilter()
        {
            _activeResources.Clear();
            _depositMinQty.Clear();
            _depositMinQual.Clear();
            _config?.SaveActive(_activeResources);
            _config?.SaveDepositQty(_depositMinQty);
            _config?.SaveDepositQual(_depositMinQual);
            UpdateToggleHighlights();
            if (!_stockpilesTab) RebuildDepositFilterRow();
            _forceRefresh = true;
            RefreshRows(force: true);
        }

        // ── Data refresh ──────────────────────────────────────────────────────

        internal void RefreshRows(bool force = false)
        {
            try
            {
                var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
                if (player == null) return;

                if (_stockpilesTab)
                    RefreshStockpiles(player, force);
                else
                    RefreshDeposits(player, force);
            }
            catch (Exception ex)
            {
                ResourceTrackerInjector.Log?.LogError($"[RT] RefreshRows: {ex}");
            }
        }

        void RefreshStockpiles(Company player, bool force)
        {
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshStockpiles: force={force} forceRefresh={_forceRefresh} tabChanged={_tabChanged}");

            var bodies = BuildStockpiles(player);
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshStockpiles: after BuildStockpiles bodies={bodies.Count}");

            bodies = FilterStockpiles(bodies);
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshStockpiles: after FilterStockpiles (activeRes={_activeResources.Count}) bodies={bodies.Count}");

            bodies = FilterHiddenStockpiles(bodies);
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshStockpiles: after FilterHidden (hidden={_hiddenResources.Count}) bodies={bodies.Count}");

            SortStockpiles(bodies);

            string sig = StockpileSignature(bodies);
            if (!force && !_forceRefresh && !_tabChanged && sig == _lastSig)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] RefreshStockpiles: sig unchanged, skipping sync");
                return;
            }
            _lastSig      = sig;
            _forceRefresh = false;

            if (_tabChanged)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] RefreshStockpiles: tab changed — clearing content, rebuilding header");
                ClearAllContent();
                BuildStockpilesHeader();
                _headerChildCount = 0;
                ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildStockpilesHeader: done, contentParent.childCount={_contentParent.childCount}");
                _tabChanged = false;
            }

            ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncStockpileRows: bodies={bodies.Count} emptyRowGO={(_emptyRowGO != null ? "exists" : "null")} headerChildren={_headerChildCount} contentChildren={_contentParent.childCount}");
            SyncStockpileRows(bodies);
        }

        void RefreshDeposits(Company player, bool force)
        {
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshDeposits: force={force} forceRefresh={_forceRefresh} tabChanged={_tabChanged}");

            var bodies = BuildDeposits(player);
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshDeposits: after BuildDeposits bodies={bodies.Count}");

            bodies = FilterDeposits(bodies);
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshDeposits: after FilterDeposits (activeRes={_activeResources.Count} perResFilters={_depositMinQty.Count}) bodies={bodies.Count}");

            bodies = FilterHiddenDeposits(bodies);
            ResourceTrackerInjector.Log?.LogInfo($"[RT] RefreshDeposits: after FilterHidden (hidden={_hiddenResources.Count}) bodies={bodies.Count}");

            SortDeposits(bodies);

            string sig = DepositSignature(bodies);
            if (!force && !_forceRefresh && !_tabChanged && sig == _lastSig)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] RefreshDeposits: sig unchanged, skipping sync");
                return;
            }
            _lastSig      = sig;
            _forceRefresh = false;

            if (_tabChanged)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] RefreshDeposits: tab changed — clearing content, rebuilding header");
                ClearAllContent();
                BuildDepositsHeader();
                _headerChildCount = 0;
                ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildDepositsHeader: done, contentParent.childCount={_contentParent.childCount}");
                _tabChanged = false;
            }

            ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncDepositRows: bodies={bodies.Count} emptyRowGO={(_emptyRowGO != null ? "exists" : "null")} headerChildren={_headerChildCount} contentChildren={_contentParent.childCount}");
            SyncDepositRows(bodies);
        }

        void ClearAllContent()
        {
            if (_tableHeaderParent != null)
                for (int i = _tableHeaderParent.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(_tableHeaderParent.GetChild(i).gameObject);
            // DestroyImmediate so childCount is accurate immediately when BuildXxxHeader runs next
            for (int i = _contentParent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_contentParent.GetChild(i).gameObject);
            _stockpileCache.Clear();
            _depositCache.Clear();
            _separatorCache.Clear();
            _emptyRowGO = null;
        }

        // ── Stockpiles data ───────────────────────────────────────────────────

        List<BodyStockpileGroup> BuildStockpiles(Company player)
        {
            var result = new List<BodyStockpileGroup>();
            var mgr = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
            if (mgr?.allObjectInfos == null)
            {
                ResourceTrackerInjector.Log?.LogWarning("[RT] BuildStockpiles: ObjectInfoManager or allObjectInfos is null");
                return result;
            }

            int totalBodies = 0, bodiesWithData = 0, bodiesWithRows = 0;
            foreach (var oi in mgr.allObjectInfos)
            {
                if (oi == null || oi.IsInGameDestroy) continue;
                totalBodies++;
                ObjectInfoData data = null;
                try { data = oi.GetObjectInfoData(player); }
                catch (Exception ex) { ResourceTrackerInjector.Log?.LogWarning($"[RT] BuildStockpiles: GetObjectInfoData({oi.ObjectName}) threw: {ex.Message}"); continue; }
                if (data == null) continue;
                bodiesWithData++;

                _bodySprites[oi.ObjectName] = oi.ImagePlanetUI;
                _bodyOIs[oi.ObjectName]     = oi;

                var rows = new List<StockpileRow>();
                List<RowResourcesData> stockpiles = null;
                try { stockpiles = data.ListRowResourcesData; }
                catch (Exception ex) { ResourceTrackerInjector.Log?.LogWarning($"[RT] BuildStockpiles: ListRowResourcesData({oi.ObjectName}) threw: {ex.Message}"); }
                if (stockpiles == null) continue;

                foreach (var row in stockpiles)
                {
                    if (row?.ResourcesType == null) continue;
                    if (!IsTrackableResource(row.ResourcesType)) continue;
                    if (row.Value <= 0 && row.InTake <= 0 && row.OutTake <= 0) continue;

                    double net = row.Balance;
                    double? daysLeft = null;
                    if (net < -1e-9 && row.Value > 0)
                        daysLeft = row.Value / (-net);

                    var rd = row.ResourcesType;
                    rows.Add(new StockpileRow
                    {
                        RdId      = rd.ID,
                        RdName    = GetResourceName(rd),
                        Qty       = row.Value,
                        State     = (ResourceState)(int)row.ResourceState,
                        InPerDay  = row.InTake,
                        OutPerDay = row.OutTake,
                        NetPerDay = net,
                        DaysLeft  = daysLeft,
                    });
                }

                if (rows.Count == 0) continue;
                bodiesWithRows++;
                result.Add(new BodyStockpileGroup { Name = oi.ObjectName, Rows = rows });
            }
            ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildStockpiles: totalBodies={totalBodies} withData={bodiesWithData} withStockpileRows={bodiesWithRows} result={result.Count}");
            return result;
        }

        List<BodyStockpileGroup> FilterStockpiles(List<BodyStockpileGroup> bodies)
            => ResourceTrackerFilter.FilterCombo(bodies, _activeResources);

        void SortStockpiles(List<BodyStockpileGroup> bodies)
            => ResourceTrackerSort.SortStockpiles(bodies, _stockSortRes, _stockSortQual);

        string StockpileSignature(List<BodyStockpileGroup> bodies)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in bodies)
            {
                sb.Append(b.Name).Append('|');
                foreach (var r in b.Rows)
                    sb.Append(r.RdId).Append(':').Append(r.Qty.ToString("F0")).Append(',');
            }
            return sb.ToString();
        }

        // ── Deposits data ─────────────────────────────────────────────────────

        List<BodyDepositGroup> BuildDeposits(Company player)
        {
            var result = new List<BodyDepositGroup>();
            var mgr = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
            if (mgr?.allObjectInfos == null)
            {
                ResourceTrackerInjector.Log?.LogWarning("[RT] BuildDeposits: ObjectInfoManager or allObjectInfos is null");
                return result;
            }

            int totalBodies = 0, bodiesWithDeposits = 0, bodiesAdded = 0;
            foreach (var oi in mgr.allObjectInfos)
            {
                if (oi == null || oi.IsInGameDestroy) continue;
                totalBodies++;
                ObjectInfoData data = null;
                try { data = oi.GetObjectInfoData(player); }
                catch (Exception ex) { ResourceTrackerInjector.Log?.LogWarning($"[RT] BuildDeposits: GetObjectInfoData({oi.ObjectName}) threw: {ex.Message}"); continue; }
                if (data?.listExploredResourcesRows == null) continue;
                bodiesWithDeposits++;

                _bodySprites[oi.ObjectName] = oi.ImagePlanetUI;
                _bodyOIs[oi.ObjectName]     = oi;

                // Group explored deposits by resource type
                var byRd = new Dictionary<string, List<RowExploredResourcesData>>();
                foreach (var dep in data.listExploredResourcesRows)
                {
                    if (dep?.ResourceType == null) continue;
                    if (!dep.ExploredInAnyCapacity) continue;
                    if (dep.ObservedData == null) continue;
                    if (dep.ObservedData.Value <= 0) continue;
                    string key = dep.ResourceType.ID;
                    if (!byRd.ContainsKey(key)) byRd[key] = new List<RowExploredResourcesData>();
                    byRd[key].Add(dep);
                }

                if (byRd.Count == 0) continue;

                var groups = new List<DepositGroup>();
                foreach (var kv in byRd)
                {
                    var gameRd = kv.Value[0].ResourceType;
                    var badges = new List<DepositBadge>();
                    double total = 0, eff = 0;

                    foreach (var dep in kv.Value)
                    {
                        double size      = dep.ObservedData.Value;
                        float  factor    = Mathf.Max(dep.ObservedData.MiningFactor ?? 1f, 0.01f);
                        double outTake   = dep.ObservedData.OutTake;
                        total += size;
                        eff   += size * factor;
                        badges.Add(new DepositBadge
                        {
                            Size          = size,
                            Factor        = factor,
                            State         = (ResourceState)(int)dep.ObservedData.ResourceState,
                            OutTakePerDay = outTake,
                        });
                    }

                    // Sort badges: actively-mined first, then by factor desc
                    badges.Sort((a, b) =>
                    {
                        int activeA = a.OutTakePerDay > 1e-9 ? 0 : 1;
                        int activeB = b.OutTakePerDay > 1e-9 ? 0 : 1;
                        if (activeA != activeB) return activeA.CompareTo(activeB);
                        return b.Factor.CompareTo(a.Factor);
                    });

                    // Derive the mine's base rate from the badge with the highest OutTake.
                    // ratePerUnit = outTake / factor → rate any deposit would see = ratePerUnit × factor.
                    double ratePerUnit = 0;
                    foreach (var badge in badges)
                        if (badge.OutTakePerDay > 1e-9)
                        {
                            double r = badge.OutTakePerDay / badge.Factor;
                            if (r > ratePerUnit) ratePerUnit = r;
                        }

                    // Compute estimated days for each badge and the sequential total.
                    double? totalEstDays = null;
                    if (ratePerUnit > 1e-9)
                    {
                        double sum = 0;
                        foreach (var badge in badges)
                        {
                            double rate = ratePerUnit * badge.Factor;
                            badge.EstDays = badge.Size / rate;
                            sum += badge.EstDays.Value;
                        }
                        totalEstDays = sum;
                    }

                    groups.Add(new DepositGroup
                    {
                        RdId         = gameRd.ID,
                        RdName       = GetResourceName(gameRd),
                        TotalSize    = total,
                        EffScore     = eff,
                        RatePerUnit  = ratePerUnit,
                        TotalEstDays = totalEstDays,
                        Badges       = badges,
                    });
                }

                double totalEff = groups.Sum(g => g.EffScore);
                bodiesAdded++;
                result.Add(new BodyDepositGroup { Name = oi.ObjectName, Groups = groups, TotalEff = totalEff });
            }
            ResourceTrackerInjector.Log?.LogInfo($"[RT] BuildDeposits: totalBodies={totalBodies} withDepositRows={bodiesWithDeposits} added={bodiesAdded} result={result.Count}");
            return result;
        }

        List<BodyDepositGroup> FilterDeposits(List<BodyDepositGroup> bodies)
            => ResourceTrackerFilter.FilterComboDeposits(bodies, _activeResources, _depositMinQty, _depositMinQual);

        void SortDeposits(List<BodyDepositGroup> bodies)
            => ResourceTrackerSort.SortDeposits(bodies, _depositSortRes, _depositSortQual);

        string DepositSignature(List<BodyDepositGroup> bodies)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in bodies)
            {
                sb.Append(b.Name).Append('|');
                foreach (var g in b.Groups)
                    sb.Append(g.RdId).Append(':').Append(g.TotalSize.ToString("F0"))
                      .Append('@').Append(g.EffScore.ToString("F1"))
                      .Append('#').Append(g.RatePerUnit.ToString("F2")).Append(',');
            }
            return sb.ToString();
        }

        // ── UI: stockpiles ────────────────────────────────────────────────────

        // Column widths (stockpiles)
        private const float SW_Body  = 118f;
        private const float SW_Res   = 90f;
        private const float SW_Qty   = 68f;
        private const float SW_State = 38f;
        private const float SW_In    = 58f;
        private const float SW_Out   = 58f;
        private const float SW_Net   = 62f;
        // LASTS: flex

        void BuildStockpilesHeader()
        {
            var hdr = MakeHRow("SHdr", _tableHeaderParent, 16f, 0f);
            AddHeaderCol(hdr.transform, SW_Body,  0f, TextAlignmentOptions.MidlineLeft,  "BODY")
                .margin = new Vector4(17f, 0f, 0f, 0f); // indent to align with sprite+text in data rows
            AddHeaderCol(hdr.transform, SW_Res,   0f, TextAlignmentOptions.MidlineLeft,  "RESOURCE");
            AddHeaderCol(hdr.transform, SW_Qty,   0f, TextAlignmentOptions.MidlineRight, "QTY");
            AddHeaderCol(hdr.transform, SW_State, 0f, TextAlignmentOptions.Midline,"STATE");
            AddHeaderCol(hdr.transform, SW_In,    0f, TextAlignmentOptions.MidlineRight, "IN/DAY");
            AddHeaderCol(hdr.transform, SW_Out,   0f, TextAlignmentOptions.MidlineRight, "OUT/DAY");
            AddHeaderCol(hdr.transform, SW_Net,   0f, TextAlignmentOptions.MidlineRight, "NET/DAY");
            AddHeaderCol(hdr.transform, 65f,      0f, TextAlignmentOptions.MidlineRight, "LASTS");
            AddHeaderCol(hdr.transform, 0f,       1f, TextAlignmentOptions.Midline,      "");
            MakeSeparator(_tableHeaderParent);
        }

        void SyncStockpileRows(List<BodyStockpileGroup> bodies)
        {
            ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncStockpileRows: bodies={bodies.Count} cachedRows={_stockpileCache.Count} emptyRowGO={(_emptyRowGO != null ? "exists" : "null")}");

            // ── Handle empty state ────────────────────────────────────────────
            if (bodies.Count == 0)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] SyncStockpileRows: no bodies — showing empty row");
                PurgeStockpileCache(new HashSet<string>());
                PurgeSeparatorCache(new HashSet<string>());
                if (_emptyRowGO == null)
                {
                    string emptyMsg = _activeResources.Count > 0
                        ? "No locations match the resource filter."
                        : "No resources found.";
                    ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncStockpileRows: creating emptyRowGO msg='{emptyMsg}'");
                    _emptyRowGO = MakeHRow("Empty", _contentParent, 20f, 0f);
                    AddCol(_emptyRowGO.transform, 0f, 1f, TextAlignmentOptions.Midline,
                        emptyMsg, ColDim, 0f);
                }
                return;
            }
            if (_emptyRowGO != null)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] SyncStockpileRows: destroying stale emptyRowGO");
                UnityEngine.Object.Destroy(_emptyRowGO);
                _emptyRowGO = null;
            }

            // ── Compute active key sets ───────────────────────────────────────
            var activeRowKeys  = new HashSet<string>();
            var activeBodyKeys = new HashSet<string>();
            foreach (var body in bodies)
            {
                activeBodyKeys.Add(body.Name);
                foreach (var row in body.Rows)
                    activeRowKeys.Add(ResourceTrackerFormat.RowKey(body.Name, row.RdId));
            }

            PurgeStockpileCache(activeRowKeys);
            PurgeSeparatorCache(activeBodyKeys);

            // ── Create / update rows, then reorder ────────────────────────────
            int sibIdx = _headerChildCount;
            bool alt   = false;
            foreach (var body in bodies)
            {
                bool firstRow = true;
                foreach (var row in body.Rows)
                {
                    string key = ResourceTrackerFormat.RowKey(body.Name, row.RdId);
                    StockpileRowCache cache;
                    if (!_stockpileCache.TryGetValue(key, out cache))
                    {
                        cache = CreateStockpileRow(key);
                        _stockpileCache[key] = cache;
                    }
                    UpdateStockpileRow(cache, body.Name, row, firstRow, alt);
                    cache.GO.transform.SetSiblingIndex(sibIdx++);
                    firstRow = false;
                    alt      = !alt;
                }
                if (!_separatorCache.ContainsKey(body.Name))
                    _separatorCache[body.Name] = MakeThinSeparator(_contentParent);
                _separatorCache[body.Name].transform.SetSiblingIndex(sibIdx++);
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_contentParent.GetComponent<RectTransform>());
        }

        StockpileRowCache CreateStockpileRow(string key)
        {
            var go  = MakeHRow(key, _contentParent, 19f, 0f);
            var bg  = go.AddComponent<Image>();
            bg.raycastTarget = false;
            var c   = new StockpileRowCache { GO = go, BgImage = bg };
            // Body column: [sprite] [name text] — sprite and TMP on separate GOs (both MaskableGraphic)
            var bodyCell = new GameObject("BodyCol", typeof(RectTransform));
            bodyCell.transform.SetParent(go.transform, false);
            var bodyCellLE  = bodyCell.AddComponent<LayoutElement>();
            bodyCellLE.preferredWidth = SW_Body;
            var bodyCellHLG = bodyCell.AddComponent<HorizontalLayoutGroup>();
            bodyCellHLG.childControlWidth = true; bodyCellHLG.childForceExpandWidth = false;
            bodyCellHLG.childControlHeight = true; bodyCellHLG.childForceExpandHeight = true;
            bodyCellHLG.spacing = 3f; bodyCellHLG.padding = new RectOffset(0, 0, 0, 0);
            var bodyCellBtn = bodyCell.AddComponent<Button>();
            var bodyCellBtnColors = bodyCellBtn.colors;
            bodyCellBtnColors.normalColor      = new Color(1f, 1f, 1f, 1f);
            bodyCellBtnColors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            bodyCellBtnColors.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
            bodyCellBtn.colors = bodyCellBtnColors;
            c.BodyBtn = bodyCellBtn;
            // Sprite child
            var bodySpriteGO = new GameObject("Sprite", typeof(RectTransform));
            bodySpriteGO.transform.SetParent(bodyCell.transform, false);
            bodySpriteGO.AddComponent<LayoutElement>().preferredWidth = 14f;
            c.BodySprite = bodySpriteGO.AddComponent<Image>();
            c.BodySprite.preserveAspect = true;
            c.BodySprite.raycastTarget  = false;
            // Text child
            var bodyTxtGO = new GameObject("Txt", typeof(RectTransform));
            bodyTxtGO.transform.SetParent(bodyCell.transform, false);
            bodyTxtGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var bodyTMP = bodyTxtGO.AddComponent<TextMeshProUGUI>();
            if (ResourceTrackerInjector.FontAsset != null) bodyTMP.font = ResourceTrackerInjector.FontAsset;
            bodyTMP.fontSize = 10f; bodyTMP.alignment = TextAlignmentOptions.MidlineLeft;
            bodyTMP.color = ColBody; bodyTMP.enableWordWrapping = false;
            bodyTMP.overflowMode = TextOverflowModes.Ellipsis; bodyTMP.raycastTarget = false;
            c.BodyTMP = bodyTMP;
            // Invisible full-stretch overlay on the text GO provides the click target
            // (Image and TMP must not share a GO — overlay is a child of bodyTxtGO)
            var bodyOverlay = new GameObject("BtnOv", typeof(RectTransform));
            bodyOverlay.transform.SetParent(bodyTxtGO.transform, false);
            bodyOverlay.AddComponent<LayoutElement>().ignoreLayout = true;
            var bodyOvRT = bodyOverlay.GetComponent<RectTransform>();
            bodyOvRT.anchorMin = Vector2.zero; bodyOvRT.anchorMax = Vector2.one;
            bodyOvRT.offsetMin = bodyOvRT.offsetMax = Vector2.zero;
            var bodyOvImg = bodyOverlay.AddComponent<Image>();
            bodyOvImg.color = Color.clear;
            bodyOvImg.raycastTarget = true;
            bodyCellBtn.targetGraphic = bodyOvImg;
            c.ResTMP   = AddCol(go.transform, SW_Res,   0f, TextAlignmentOptions.MidlineLeft,  "", ColBody, 0f);
            c.QtyTMP   = AddCol(go.transform, SW_Qty,   0f, TextAlignmentOptions.MidlineRight, "", ColBody, 0f);
            c.StateTMP = AddCol(go.transform, SW_State, 0f, TextAlignmentOptions.Midline,      "", ColDim,  0f);
            c.InTMP    = AddCol(go.transform, SW_In,    0f, TextAlignmentOptions.MidlineRight, "", ColGreen,0f);
            c.OutTMP   = AddCol(go.transform, SW_Out,   0f, TextAlignmentOptions.MidlineRight, "", ColDim,  0f);
            c.NetTMP   = AddCol(go.transform, SW_Net,   0f, TextAlignmentOptions.MidlineRight, "", ColDim,  0f);
            c.LastsTMP = AddCol(go.transform, 65f,      0f, TextAlignmentOptions.MidlineRight, "", ColDim, 0f);
            AddCol(go.transform, 0f, 1f, TextAlignmentOptions.Midline, "", ColDim, 0f);
            return c;
        }

        void UpdateStockpileRow(StockpileRowCache c, string bodyName, StockpileRow row,
            bool showBody, bool alt)
        {
            c.BgImage.color = alt ? new Color(1f, 1f, 1f, 0.03f) : Color.clear;

            c.BodyTMP.text = showBody ? bodyName : "";
            if (c.BodySprite != null)
            {
                Sprite sp = showBody && _bodySprites.TryGetValue(bodyName, out Sprite s) ? s : null;
                c.BodySprite.sprite = sp;
                c.BodySprite.color  = sp != null ? Color.white : Color.clear;
            }
            if (c.BodyBtn != null)
            {
                c.BodyBtn.onClick.RemoveAllListeners();
                if (showBody && _bodyOIs.TryGetValue(bodyName, out var oi))
                {
                    var capturedOI = oi;
                    c.BodyBtn.onClick.AddListener(() => capturedOI.MyOnMouseUpAsButton2());
                }
            }
            string resIcon = _allResources.Find(r => r.ID == row.RdId)?.IconString ?? "";
            c.ResTMP.text = (resIcon.Length > 0 ? resIcon + " " : "") + row.RdName;
            c.QtyTMP.text   = ResourceTrackerFormat.FormatQty(row.Qty);
            c.StateTMP.text = ResourceTrackerFormat.FormatState(row.State);

            if (row.InPerDay > 1e-6)  { c.InTMP.text  = $"+{ResourceTrackerFormat.FormatFlow(row.InPerDay)}";  c.InTMP.color  = ColGreen; }
            else                      { c.InTMP.text  = "—";                                                    c.InTMP.color  = ColDim;   }

            if (row.OutPerDay > 1e-6) { c.OutTMP.text = $"-{ResourceTrackerFormat.FormatFlow(row.OutPerDay)}"; c.OutTMP.color = ColDim;   }
            else                      { c.OutTMP.text = "—";                                                    c.OutTMP.color = ColDim;   }

            double net = row.NetPerDay;
            if      (net >  1e-6) { c.NetTMP.text = $"+{ResourceTrackerFormat.FormatFlow(net)}";  c.NetTMP.color = ColGreen; }
            else if (net < -1e-6) { c.NetTMP.text = $"-{ResourceTrackerFormat.FormatFlow(-net)}"; c.NetTMP.color = ColRed;   }
            else                  { c.NetTMP.text = "—";                                            c.NetTMP.color = ColDim;   }

            if (row.DaysLeft.HasValue)
            {
                c.LastsTMP.text  = ResourceTrackerFormat.FormatDays(row.DaysLeft.Value);
                c.LastsTMP.color = row.DaysLeft.Value < 30 ? ColRed :
                                   row.DaysLeft.Value < 90 ? ColYellow : ColDim;
            }
            else { c.LastsTMP.text = ""; c.LastsTMP.color = ColDim; }
        }

        void PurgeStockpileCache(HashSet<string> activeKeys)
        {
            var stale = new List<string>();
            foreach (var key in _stockpileCache.Keys)
                if (!activeKeys.Contains(key)) stale.Add(key);
            foreach (var key in stale)
            {
                UnityEngine.Object.Destroy(_stockpileCache[key].GO);
                _stockpileCache.Remove(key);
            }
        }

        void PurgeSeparatorCache(HashSet<string> activeBodyKeys)
        {
            var stale = new List<string>();
            foreach (var key in _separatorCache.Keys)
                if (!activeBodyKeys.Contains(key)) stale.Add(key);
            foreach (var key in stale)
            {
                UnityEngine.Object.Destroy(_separatorCache[key]);
                _separatorCache.Remove(key);
            }
        }

        // ── UI: deposits ──────────────────────────────────────────────────────

        // Column widths (deposits)
        private const float DW_Body  = 118f;
        private const float DW_Res   = 90f;
        private const float DW_Total = 65f;
        private const float DW_Eff   = 65f;
        private const float DW_Time  = 58f;
        // DEPOSITS: flex

        void BuildDepositsHeader()
        {
            var hdr = MakeHRow("DHdr", _tableHeaderParent, 16f, 0f);
            AddHeaderCol(hdr.transform, DW_Body,  0f, TextAlignmentOptions.MidlineLeft,  "BODY")
                .margin = new Vector4(17f, 0f, 0f, 0f);
            AddHeaderCol(hdr.transform, DW_Res,   0f, TextAlignmentOptions.MidlineLeft,  "RESOURCE");
            AddHeaderCol(hdr.transform, DW_Total, 0f, TextAlignmentOptions.MidlineRight, "TOTAL");
            var effTMP = AddHeaderCol(hdr.transform, DW_Eff,   0f, TextAlignmentOptions.MidlineRight, "EFF");
            // Overlay child: Image + trigger on a child GO so Image and TMP don't share a GO
            {
                var ov = new GameObject("TTOverlay", typeof(RectTransform));
                ov.transform.SetParent(effTMP.gameObject.transform, false);
                var ovRT = ov.GetComponent<RectTransform>();
                ovRT.anchorMin = Vector2.zero; ovRT.anchorMax = Vector2.one;
                ovRT.offsetMin = ovRT.offsetMax = Vector2.zero;
                ov.AddComponent<Image>().color = Color.clear;
                ov.GetComponent<Image>().raycastTarget = true;
                ov.AddComponent<RTTooltipTrigger>().Text =
                    "Effective quantity = sum(deposit size × quality factor).\n" +
                    "Quality factor (0–1) is the extraction efficiency —\n" +
                    "higher means more yield per unit of deposit size.";
            }
            AddHeaderCol(hdr.transform, DW_Time,  0f, TextAlignmentOptions.Midline, "LASTS");
            AddHeaderCol(hdr.transform, 0f,       1f, TextAlignmentOptions.MidlineLeft,  "DEPOSITS");
            MakeSeparator(_tableHeaderParent);
        }

        void SyncDepositRows(List<BodyDepositGroup> bodies)
        {
            ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncDepositRows: bodies={bodies.Count} cachedRows={_depositCache.Count} emptyRowGO={(_emptyRowGO != null ? "exists" : "null")}");

            // ── Handle empty state ────────────────────────────────────────────
            if (bodies.Count == 0)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] SyncDepositRows: no bodies — showing empty row");
                PurgeDepositCache(new HashSet<string>());
                PurgeSeparatorCache(new HashSet<string>());
                if (_emptyRowGO == null)
                {
                    string emptyMsg = _activeResources.Count > 0
                        ? "No locations match the filter."
                        : "No deposits found.";
                    ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncDepositRows: creating emptyRowGO msg='{emptyMsg}'");
                    _emptyRowGO = MakeHRow("Empty", _contentParent, 20f, 0f);
                    AddCol(_emptyRowGO.transform, 0f, 1f, TextAlignmentOptions.Midline,
                        emptyMsg, ColDim, 0f);
                }
                return;
            }
            if (_emptyRowGO != null)
            {
                ResourceTrackerInjector.Log?.LogInfo("[RT] SyncDepositRows: destroying stale emptyRowGO");
                UnityEngine.Object.Destroy(_emptyRowGO);
                _emptyRowGO = null;
            }

            // ── Compute active key sets ───────────────────────────────────────
            var activeRowKeys  = new HashSet<string>();
            var activeBodyKeys = new HashSet<string>();
            foreach (var body in bodies)
            {
                activeBodyKeys.Add(body.Name);
                foreach (var grp in body.Groups)
                    activeRowKeys.Add(ResourceTrackerFormat.RowKey(body.Name, grp.RdId));
            }

            PurgeDepositCache(activeRowKeys);
            PurgeSeparatorCache(activeBodyKeys);

            // ── Create / update rows, then reorder ────────────────────────────
            int sibIdx = _headerChildCount;
            bool alt   = false;
            foreach (var body in bodies)
            {
                bool firstRow = true;
                foreach (var grp in body.Groups)
                {
                    string key = ResourceTrackerFormat.RowKey(body.Name, grp.RdId);
                    DepositRowCache cache;
                    if (!_depositCache.TryGetValue(key, out cache))
                    {
                        cache = CreateDepositRow(key);
                        _depositCache[key] = cache;
                    }
                    UpdateDepositRow(cache, body.Name, grp, firstRow, alt);
                    cache.GO.transform.SetSiblingIndex(sibIdx++);
                    firstRow = false;
                    alt      = !alt;
                }
                if (!_separatorCache.ContainsKey(body.Name))
                    _separatorCache[body.Name] = MakeThinSeparator(_contentParent);
                _separatorCache[body.Name].transform.SetSiblingIndex(sibIdx++);
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_contentParent.GetComponent<RectTransform>());
            ResourceTrackerInjector.Log?.LogInfo($"[RT] SyncDepositRows END: cache={_depositCache.Count} seps={_separatorCache.Count} contentChildren={_contentParent.childCount}");
        }

        DepositRowCache CreateDepositRow(string key)
        {
            var go  = MakeHRow(key, _contentParent, 44f, 0f);
            var bg  = go.AddComponent<Image>();
            bg.raycastTarget = false;
            var c   = new DepositRowCache { GO = go, BgImage = bg };
            var bodyCell = new GameObject("BodyCol", typeof(RectTransform));
            bodyCell.transform.SetParent(go.transform, false);
            var bodyCellLE  = bodyCell.AddComponent<LayoutElement>();
            bodyCellLE.preferredWidth = DW_Body;
            var bodyCellHLG = bodyCell.AddComponent<HorizontalLayoutGroup>();
            bodyCellHLG.childControlWidth = true; bodyCellHLG.childForceExpandWidth = false;
            bodyCellHLG.childControlHeight = true; bodyCellHLG.childForceExpandHeight = true;
            bodyCellHLG.spacing = 3f; bodyCellHLG.padding = new RectOffset(0, 0, 0, 0);
            var bodyCellBtn = bodyCell.AddComponent<Button>();
            var bodyCellBtnColors = bodyCellBtn.colors;
            bodyCellBtnColors.normalColor      = new Color(1f, 1f, 1f, 1f);
            bodyCellBtnColors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            bodyCellBtnColors.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
            bodyCellBtn.colors = bodyCellBtnColors;
            c.BodyBtn = bodyCellBtn;
            var bodySpriteGO = new GameObject("Sprite", typeof(RectTransform));
            bodySpriteGO.transform.SetParent(bodyCell.transform, false);
            bodySpriteGO.AddComponent<LayoutElement>().preferredWidth = 14f;
            c.BodySprite = bodySpriteGO.AddComponent<Image>();
            c.BodySprite.preserveAspect = true;
            c.BodySprite.raycastTarget  = false;
            var bodyTxtGO = new GameObject("Txt", typeof(RectTransform));
            bodyTxtGO.transform.SetParent(bodyCell.transform, false);
            bodyTxtGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var bodyTMP = bodyTxtGO.AddComponent<TextMeshProUGUI>();
            if (ResourceTrackerInjector.FontAsset != null) bodyTMP.font = ResourceTrackerInjector.FontAsset;
            bodyTMP.fontSize = 10f; bodyTMP.alignment = TextAlignmentOptions.MidlineLeft;
            bodyTMP.color = ColBody; bodyTMP.enableWordWrapping = false;
            bodyTMP.overflowMode = TextOverflowModes.Ellipsis; bodyTMP.raycastTarget = false;
            c.BodyTMP = bodyTMP;
            var bodyOverlay2 = new GameObject("BtnOv", typeof(RectTransform));
            bodyOverlay2.transform.SetParent(bodyTxtGO.transform, false);
            bodyOverlay2.AddComponent<LayoutElement>().ignoreLayout = true;
            var bodyOv2RT = bodyOverlay2.GetComponent<RectTransform>();
            bodyOv2RT.anchorMin = Vector2.zero; bodyOv2RT.anchorMax = Vector2.one;
            bodyOv2RT.offsetMin = bodyOv2RT.offsetMax = Vector2.zero;
            var bodyOv2Img = bodyOverlay2.AddComponent<Image>();
            bodyOv2Img.color = Color.clear;
            bodyOv2Img.raycastTarget = true;
            bodyCellBtn.targetGraphic = bodyOv2Img;
            c.ResTMP   = AddCol(go.transform, DW_Res,   0f, TextAlignmentOptions.MidlineLeft,   "", ColBody, 0f);
            c.TotalTMP = AddCol(go.transform, DW_Total, 0f, TextAlignmentOptions.MidlineRight,  "", ColDim,  0f);
            c.EffTMP   = AddCol(go.transform, DW_Eff,   0f, TextAlignmentOptions.MidlineRight,  "", ColGold, 0f);
            c.TimeTMP  = AddCol(go.transform, DW_Time,  0f, TextAlignmentOptions.Midline, "", ColDim,  0f);

            // Badges container: HLG of per-deposit tiles
            var badgesGO = new GameObject("Badges", typeof(RectTransform));
            badgesGO.transform.SetParent(go.transform, false);
            var badgesLE = badgesGO.AddComponent<LayoutElement>();
            badgesLE.flexibleWidth = 1f;
            var badgesGLG = badgesGO.AddComponent<GridLayoutGroup>();
            badgesGLG.cellSize       = new Vector2(52f, 40f);
            badgesGLG.spacing        = new Vector2(3f, 3f);
            badgesGLG.padding        = new RectOffset(4, 4, 2, 2);
            badgesGLG.constraint     = GridLayoutGroup.Constraint.Flexible;
            badgesGLG.childAlignment = TextAnchor.UpperLeft;
            badgesGLG.startCorner    = GridLayoutGroup.Corner.UpperLeft;
            badgesGLG.startAxis      = GridLayoutGroup.Axis.Horizontal;
            c.BadgesGO = badgesGO;
            return c;
        }

        void UpdateDepositRow(DepositRowCache c, string bodyName, DepositGroup grp,
            bool showBody, bool alt)
        {
            c.BgImage.color = alt ? new Color(1f, 1f, 1f, 0.03f) : Color.clear;

            c.BodyTMP.text = showBody ? bodyName : "";
            if (c.BodySprite != null)
            {
                Sprite sp = showBody && _bodySprites.TryGetValue(bodyName, out Sprite s) ? s : null;
                c.BodySprite.sprite = sp;
                c.BodySprite.color  = sp != null ? Color.white : Color.clear;
            }
            if (c.BodyBtn != null)
            {
                c.BodyBtn.onClick.RemoveAllListeners();
                if (showBody && _bodyOIs.TryGetValue(bodyName, out var oi))
                {
                    var capturedOI = oi;
                    c.BodyBtn.onClick.AddListener(() => capturedOI.MyOnMouseUpAsButton2());
                }
            }
            string resIcon = _allResources.Find(r => r.ID == grp.RdId)?.IconString ?? "";
            c.ResTMP.text = (resIcon.Length > 0 ? resIcon + " " : "") + grp.RdName;
            c.TotalTMP.text = ResourceTrackerFormat.FormatKT(grp.TotalSize);
            c.EffTMP.text   = ResourceTrackerFormat.FormatKT(grp.EffScore);

            if (grp.TotalEstDays.HasValue)
            {
                c.TimeTMP.text  = ResourceTrackerFormat.FormatDays(grp.TotalEstDays.Value);
                c.TimeTMP.color = grp.TotalEstDays.Value < 30  ? ColRed :
                                  grp.TotalEstDays.Value < 90  ? ColYellow : ColGreen;
            }
            else { c.TimeTMP.text = "—"; c.TimeTMP.color = ColDim; }

            string newBadgeSig = ComputeBadgeSig(grp.Badges);
            if (newBadgeSig != c.BadgeSig)
            {
                c.BadgeSig = newBadgeSig;
                RebuildDepositTiles(c.BadgesGO.transform, grp.Badges, resIcon);
            }
        }

        static string ComputeBadgeSig(List<DepositBadge> badges)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in badges)
                sb.Append(b.Size.ToString("F0")).Append(':')
                  .Append(b.Factor.ToString("F2")).Append(':')
                  .Append((int)b.State).Append(':')
                  .Append(b.OutTakePerDay.ToString("F0")).Append(';');
            return sb.ToString();
        }

        void RebuildDepositTiles(Transform container, List<DepositBadge> badges, string resIcon)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(container.GetChild(i).gameObject);

            var font = ResourceTrackerInjector.FontAsset;
            foreach (var badge in badges)
            {
                var tile = new GameObject("T", typeof(RectTransform));
                tile.transform.SetParent(container, false);
                // LayoutElement is ignored by GridLayoutGroup (which enforces cellSize directly)
                tile.AddComponent<LayoutElement>();

                var bg = tile.AddComponent<Image>();
                bg.color = new Color(0.10f, 0.12f, 0.15f, 0.85f);
                bg.raycastTarget = false;

                var vlg = tile.AddComponent<VerticalLayoutGroup>();
                vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
                vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
                vlg.spacing = 1f;
                vlg.padding = new RectOffset(2, 2, 3, 2);

                // Row 1: small icon + state — context at the top
                var r1 = new GameObject("Meta", typeof(RectTransform));
                r1.transform.SetParent(tile.transform, false);
                r1.AddComponent<LayoutElement>().preferredHeight = 10f;
                var t1 = r1.AddComponent<TextMeshProUGUI>();
                if (font != null) t1.font = font;
                t1.text = (resIcon.Length > 0 ? resIcon + " " : "") +
                          ResourceTrackerFormat.FormatState(badge.State);
                t1.fontSize = 7f;
                t1.color = new Color(0.50f, 0.50f, 0.50f, 1f);
                t1.alignment = TextAlignmentOptions.Center;
                t1.enableWordWrapping = false;
                t1.raycastTarget = false;

                // Row 2: size — prominent
                var r2 = new GameObject("Size", typeof(RectTransform));
                r2.transform.SetParent(tile.transform, false);
                r2.AddComponent<LayoutElement>().preferredHeight = 14f;
                var t2 = r2.AddComponent<TextMeshProUGUI>();
                if (font != null) t2.font = font;
                t2.text = ResourceTrackerFormat.FormatKT(badge.Size);
                t2.fontSize = 11f;
                t2.color = new Color(0.90f, 0.90f, 0.90f, 1f);
                t2.alignment = TextAlignmentOptions.Center;
                t2.enableWordWrapping = false;
                t2.raycastTarget = false;

                // Row 3: quality factor (colored) — prominent
                string qualHex  = ResourceTrackerFormat.QualityColorHex(badge.Factor);
                string factorStr = badge.Factor < 0.015f ? "<0.01" : badge.Factor.ToString("F2");
                var r3 = new GameObject("Qual", typeof(RectTransform));
                r3.transform.SetParent(tile.transform, false);
                r3.AddComponent<LayoutElement>().preferredHeight = 13f;
                var t3 = r3.AddComponent<TextMeshProUGUI>();
                if (font != null) t3.font = font;
                t3.text = $"<color={qualHex}>{factorStr}</color>";
                t3.fontSize = 10f;
                t3.alignment = TextAlignmentOptions.Center;
                t3.enableWordWrapping = false;
                t3.raycastTarget = false;
            }
        }

        void PurgeDepositCache(HashSet<string> activeKeys)
        {
            var stale = new List<string>();
            foreach (var key in _depositCache.Keys)
                if (!activeKeys.Contains(key)) stale.Add(key);
            foreach (var key in stale)
            {
                UnityEngine.Object.Destroy(_depositCache[key].GO);
                _depositCache.Remove(key);
            }
        }

        // ── Game-type helpers ─────────────────────────────────────────────────

        static string GetResourceName(ResourceDefinition rd)
        {
            try { return LEManager.Get(rd.ID) ?? rd.ID; }
            catch (Exception ex)
            {
                ResourceTrackerInjector.Log?.LogWarning($"[RT] GetResourceName({rd?.ID}): {ex.Message}");
                return rd?.ID ?? "?";
            }
        }

        static bool IsTrackableResource(ResourceDefinition rd)
        {
            // Exclude Energy (type=1) and Human (type=2) — use ID as a proxy
            return rd.ID != "id_resource_energy" && rd.ID != "id_resource_human"
                   && rd.ID != "id_resource_empty";
        }

        private static readonly HashSet<string> NonDepositIds = new HashSet<string>
        {
            "id_resource_alloy", "id_resource_chips", "id_resource_glass",
            "id_resource_plastic", "id_resource_steel", "id_resource_supply",
            "id_resource_consumergoods", "id_resource_antimatter"
        };

        static bool IsDepositResource(string rdId) => !NonDepositIds.Contains(rdId);

        List<ResourceDefinition> GetAllResources()
        {
            try
            {
                var all = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance
                    ?.AllResourceDefinitions?.ListResourceDefinitionTakeAsCargo;
                int count = all?.Count ?? 0;
                ResourceTrackerInjector.Log?.LogInfo($"[RT] GetAllResources: found {count} resources");
                return all ?? new List<ResourceDefinition>();
            }
            catch (Exception ex)
            {
                ResourceTrackerInjector.Log?.LogWarning($"[RT] GetAllResources: threw {ex.Message}");
                return new List<ResourceDefinition>();
            }
        }

        // ── UI primitive builders ─────────────────────────────────────────────

        static GameObject MakeHRow(string name, Transform parent, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth      = true;
            hlg.childForceExpandWidth  = false;
            hlg.childControlHeight     = true;
            hlg.childForceExpandHeight = true;
            hlg.spacing = spacing;
            hlg.padding = new RectOffset(2, 2, 0, 0);
            return go;
        }

        TextMeshProUGUI AddHeaderCol(Transform parent, float width, float flex, TextAlignmentOptions align, string text)
        {
            var tmp = AddCol(parent, width, flex, align, text, ColHeader, 0f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize  = 9f;
            return tmp;
        }

        TextMeshProUGUI AddCol(Transform parent, float width, float flex, TextAlignmentOptions align,
            string text, Color color, float minWidth)
        {
            var go = new GameObject("Col", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth  = flex;
            if (minWidth > 0) le.minWidth = minWidth;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (ResourceTrackerInjector.FontAsset != null) tmp.font = ResourceTrackerInjector.FontAsset;
            tmp.text              = text;
            tmp.fontSize          = 10f;
            tmp.alignment         = align;
            tmp.color             = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode      = TextOverflowModes.Ellipsis;
            tmp.raycastTarget     = false;
            return tmp;
        }

        static GameObject MakeLabel(string name, Transform parent, TMP_FontAsset font,
            string text, float fontSize, float width, TextAlignmentOptions align, Color color,
            float flexibleWidth = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            if (flexibleWidth > 0f)
                le.flexibleWidth = flexibleWidth;
            else
                le.preferredWidth = width;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text               = text;
            tmp.fontSize           = fontSize;
            tmp.alignment          = align;
            tmp.color              = color;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget      = false;
            return go;
        }

        static void MakeSeparator(Transform parent)
        {
            var go = new GameObject("Sep", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);
            img.raycastTarget = false;
        }

        static GameObject MakeThinSeparator(Transform parent)
        {
            var go = new GameObject("TSep", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.04f);
            img.raycastTarget = false;
            return go;
        }

        static Button MakeTabButton(string name, Transform parent, TMP_FontAsset font, string text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = 90f;
            le.preferredHeight = 24f;  // explicit — prevents HLG from inflating the row
            var img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.14f, 0.16f, 0.94f);  // Fleet Tracker normal colour
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            var tmp = new GameObject("Lbl", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            tmp.transform.SetParent(go.transform, false);
            var rt = tmp.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (font != null) tmp.font = font;
            tmp.text      = text;
            tmp.fontSize  = 9f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = ColInactive;
            tmp.raycastTarget = false;
            return btn;
        }

        static Button MakeSpriteToggleButton(string name, Transform parent, TMP_FontAsset font, string iconStr)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = 24f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.14f, 0.18f, 0.7f);
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            var tmp = new GameObject("Lbl", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            tmp.transform.SetParent(go.transform, false);
            var rt = tmp.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (font != null) tmp.font = font;
            tmp.text              = iconStr;
            tmp.fontSize          = 14f;
            tmp.alignment         = TextAlignmentOptions.Center;
            tmp.color             = new Color(0.7f, 0.7f, 0.7f, 1f);
            tmp.enableWordWrapping = false;
            tmp.raycastTarget     = false;
            return btn;
        }

        static Button MakeSmallButton(string name, Transform parent, TMP_FontAsset font,
            string text, float width, Color bgNormal, Color bgHover)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = 24f;  // match tab button height
            var img = go.AddComponent<Image>();
            img.color = bgNormal;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            btn.colors = colors;
            var tmp = new GameObject("Lbl", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            tmp.transform.SetParent(go.transform, false);
            var rt = tmp.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (font != null) tmp.font = font;
            tmp.text      = text;
            tmp.fontSize  = 11f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = new Color(0.75f, 0.75f, 0.75f, 1f);
            tmp.raycastTarget = false;
            return btn;
        }

        static TMP_InputField MakeInputField(string name, Transform parent, TMP_FontAsset font,
            string placeholder, float width, UnityEngine.Events.UnityAction<string> onEnd)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.20f, 0.32f, 1.0f);  // visible dark-blue box

            var field = go.AddComponent<TMP_InputField>();
            field.contentType = TMP_InputField.ContentType.DecimalNumber;

            // Text area
            var textAreaGO = new GameObject("TextArea", typeof(RectTransform));
            textAreaGO.transform.SetParent(go.transform, false);
            var taRT = textAreaGO.GetComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(3, 1); taRT.offsetMax = new Vector2(-3, -1);
            textAreaGO.AddComponent<RectMask2D>();

            // Placeholder component
            var phGO = new GameObject("Placeholder", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            phGO.transform.SetParent(textAreaGO.transform, false);
            var phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = phRT.offsetMax = Vector2.zero;
            if (font != null) phGO.font = font;
            phGO.fontSize          = 9f;
            phGO.alignment         = TextAlignmentOptions.MidlineLeft;
            phGO.color             = new Color(0.4f, 0.4f, 0.4f, 1f);
            phGO.text              = "0";
            phGO.enableWordWrapping = false;
            phGO.raycastTarget     = false;

            // Text component
            var textGO = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            textGO.transform.SetParent(textAreaGO.transform, false);
            var tRT = textGO.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.offsetMin = tRT.offsetMax = Vector2.zero;
            if (font != null) textGO.font = font;
            textGO.fontSize   = 9f;
            textGO.alignment  = TextAlignmentOptions.MidlineLeft;
            textGO.color      = new Color(0.9f, 0.9f, 0.9f, 1f);
            textGO.enableWordWrapping = false;

            field.textComponent    = textGO;
            field.textViewport     = taRT;
            field.placeholder      = phGO;
            field.text             = placeholder;
            field.onEndEdit.AddListener(onEnd);

            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = Color.white;
            field.selectionColor = new Color(0.2f, 0.5f, 0.9f, 0.55f);
            var normalBg = img.color;
            var focusBg  = new Color(0.20f, 0.40f, 0.65f, 1.0f);
            field.onSelect.AddListener(val =>
            {
                img.color = focusBg;
                field.selectionStringAnchorPosition = 0;
                field.selectionStringFocusPosition  = field.text != null ? field.text.Length : 0;
            });
            field.onDeselect.AddListener(val => img.color = normalBg);

            return field;
        }
    }

    // ── Resize handle (copied from Fleet/Power/Life Support Tracker) ─────────

    internal class ResizeHandle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IDragHandler
    {
        internal RectTransform PanelRT;
        private const float MinHeight = 180f;
        private const float MinWidth  = 500f;
        private Canvas _canvas;
        private bool   _dragging;
        private Vector2 _dragStartScreen;
        private Vector2 _dragStartSize;

        private void Awake() { _canvas = GetComponentInParent<Canvas>(); }

        public void OnPointerEnter(PointerEventData e) { }
        public void OnPointerExit(PointerEventData e)  { }
        public void OnPointerDown(PointerEventData e)
        {
            _dragging        = true;
            _dragStartScreen = e.position;
            _dragStartSize   = PanelRT.sizeDelta;
        }
        public void OnPointerUp(PointerEventData e) { _dragging = false; }
        public void OnDrag(PointerEventData e)
        {
            float scale  = _canvas != null ? _canvas.scaleFactor : 1f;
            Vector2 delta = (e.position - _dragStartScreen) / scale;
            float width  = Mathf.Max(MinWidth,  _dragStartSize.x + delta.x);
            float height = Mathf.Max(MinHeight, _dragStartSize.y - delta.y);
            PanelRT.sizeDelta = new Vector2(width, height);
        }
    }

    // ── Tooltip (pattern from LaunchWindows) ─────────────────────────────────

    internal class RTTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Text;
        public void OnPointerEnter(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) RTTooltip.Show(Text, e.position, canvas);
        }
        public void OnPointerExit(PointerEventData e) => RTTooltip.Hide();
        void OnDisable() => RTTooltip.Hide();
    }

    internal static class RTTooltip
    {
        static GameObject      _go;
        static TextMeshProUGUI _tmp;

        internal static void Show(string text, Vector2 screenPos, Canvas canvas)
        {
            if (canvas == null) return;
            if (_go == null || _go.transform.parent != canvas.transform) Build(canvas);
            if (_go == null) return;
            _tmp.text = text;
            _go.SetActive(true);
            _go.transform.SetAsLastSibling();
            var canvasRT = canvas.GetComponent<RectTransform>();
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, cam, out local))
                _go.GetComponent<RectTransform>().anchoredPosition = local + new Vector2(12f, -12f);
        }

        internal static void Hide() { if (_go != null) _go.SetActive(false); }

        static void Build(Canvas canvas)
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = new GameObject("RTTooltip", typeof(RectTransform));
            _go.transform.SetParent(canvas.transform, false);
            _go.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = _go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            var bg = _go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.10f, 0.13f, 0.97f);
            bg.raycastTarget = false;
            var vlg = _go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(5, 5, 3, 3);
            vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            var csf = _go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            var textGO = new GameObject("T", typeof(RectTransform));
            textGO.transform.SetParent(_go.transform, false);
            _tmp = textGO.AddComponent<TextMeshProUGUI>();
            var font = ResourceTrackerInjector.FontAsset;
            if (font != null) _tmp.font = font;
            _tmp.fontSize = 8f;
            _tmp.color = new Color(0.85f, 0.85f, 0.85f);
            _tmp.enableWordWrapping = false;
            _tmp.raycastTarget = false;
            textGO.AddComponent<LayoutElement>().preferredWidth = 160f;
            _go.SetActive(false);
        }
    }
}
