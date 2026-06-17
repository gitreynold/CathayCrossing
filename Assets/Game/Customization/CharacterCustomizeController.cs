using System.Collections.Generic;
using CathayCrossing.Characters;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CathayCrossing.Customization
{
    /// <summary>
    /// Drives the LEGO-style customise scene. The right rail has one tab per
    /// <see cref="CharacterPartSlot"/> (Hair / Head / Body / Pants / Shoes);
    /// each tab shows the catalog's available options for that slot, one
    /// thumbnail-button per source character.
    ///
    /// State lives in <see cref="_selection"/> — a Slot → characterId map.
    /// Every change (tab switch isn't a change, only clicking a slot button)
    /// rebuilds the assembled preview via <see cref="CharacterAssembler"/>.
    ///
    /// Confirm writes each slot's pick into PlayerPrefs under keys like
    /// <c>Customize.Slot.Hair</c>; the office spawner reads the same keys
    /// to rebuild the exact same character in-world. We dropped the old
    /// single-variant <c>ActiveCharacterId</c> key — every slot now has
    /// its own state, so a single id can't capture the look any more.
    /// </summary>
    public class CharacterCustomizeController : MonoBehaviour
    {
        // Mirrored by OfficePlayerSpawner — keep the prefix in sync.
        public const string PlayerPrefsSlotPrefix = "Customize.Slot.";

        // Whole-body "hidden" character pick (e.g. the chairman). Empty string
        // means "no hidden character — use the assembled head/body look".
        // Mirrored by OfficePlayerSpawner.
        public const string PlayerPrefsHiddenKey = "Customize.HiddenCharacter";

        // Legacy single-variant key; we still clear it on Confirm so a
        // stale value can't shadow the new slot keys on next office boot.
        public const string LegacyActiveCharacterPrefsKey = "ActiveCharacterId";

        [System.Serializable]
        public struct CategoryTab
        {
            public CharacterPartSlot slot;
            public Button button;
        }

        [Header("Preview")]
        [Tooltip("Empty Transform that the assembled character is parented under.")]
        public Transform previewAnchor;

        [Header("Catalog")]
        [Tooltip("The five-slot part catalog. Loaded from Resources at " +
                 "runtime if left empty — the assembler also looks it up by " +
                 "name 'CharacterPartCatalog'.")]
        public CharacterPartCatalog catalog;

        [Header("Slot tabs (top of right rail)")]
        public CategoryTab[] categoryTabs;
        public Color accentColor = new Color(0.97f, 0.43f, 0.39f);

        [Header("Hidden whole-body characters tab")]
        [Tooltip("Extra tab (crown icon) that lists whole-body 'hidden' " +
                 "characters — definitions flagged hiddenWholeBody. Picking one " +
                 "replaces the assembled head/body look with that complete " +
                 "model. Leave null to disable the hidden tab.")]
        public Button hiddenTab;

        [Header("Variant grid (body of right rail)")]
        public Transform variantGridContainer;

        [Tooltip("Disabled button cloned per slot option. Should have a " +
                 "TMP_Text child named 'Label' and an optional 'Badge' child " +
                 "(TMP_Text) we reveal for combined body+pants entries.")]
        public Button variantButtonTemplate;

        [Header("Confirm")]
        public Button confirmButton;
        public string nextSceneName = "OfficeScene";

        [Header("Header label")]
        public TMP_Text selectedNameLabel;

        [Header("CJK font (optional)")]
        public TMP_FontAsset cjkFont;

        // Runtime state
        readonly Dictionary<CharacterPartSlot, string> _selection = new();
        readonly Dictionary<string, GameObject> _bodyByCharacterId = new();
        readonly List<Button> _slotOptionButtons = new();
        readonly Dictionary<string, CharacterDefinition> _definitionsById = new();
        readonly List<CharacterDefinition> _hiddenDefs = new();

        // Whole-body hidden character currently picked (id), or "" for none.
        // When non-empty it overrides the assembled head/body look.
        string _hiddenSelection = "";

        // LEGO mesh-swap restored 2026-05-26: Head + Body each have their
        // own list of variant options. Head is the default opened tab to
        // match the right-rail order in the mockup.
        CharacterPartSlot _activeTab = CharacterPartSlot.Head;
        // True while the hidden-characters tab is the open tab (the slot
        // _activeTab value is then ignored for option population).
        bool _activeTabIsHidden;
        GameObject _assembled;

        void Awake()
        {
            if (catalog == null) catalog = Resources.Load<CharacterPartCatalog>("CharacterPartCatalog");
            if (cjkFont != null) ApplyFontToAllText();
        }

        void Start()
        {
            if (previewAnchor == null || catalog == null || variantGridContainer == null || variantButtonTemplate == null)
            {
                Debug.LogError("[CharacterCustomizeController] Required fields not wired up.");
                return;
            }

            // 1. Index every CharacterDefinition under Resources/Characters/
            //    so we can later look up each character's FBX body by id.
            var defs = Resources.LoadAll<CharacterDefinition>("Characters");
            foreach (var def in defs)
            {
                if (def == null || string.IsNullOrEmpty(def.id) || def.body == null) continue;
                _definitionsById[def.id] = def;
                _bodyByCharacterId[def.id] = def.body;
                if (def.hiddenWholeBody) _hiddenDefs.Add(def);
            }
            // Stable order so the hidden options list doesn't shuffle per run.
            _hiddenDefs.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            // 2. Seed the selection from PlayerPrefs (or default to base).
            foreach (CharacterPartSlot slot in System.Enum.GetValues(typeof(CharacterPartSlot)))
            {
                string saved = PlayerPrefs.GetString(PlayerPrefsSlotPrefix + slot, catalog.baseCharacterId);
                _selection[slot] = ValidateSelection(saved, slot);
            }
            // Seed the hidden-character pick (validated against known hidden defs).
            string savedHidden = PlayerPrefs.GetString(PlayerPrefsHiddenKey, "");
            _hiddenSelection = IsKnownHidden(savedHidden) ? savedHidden : "";

            // 3. Wire static UI.
            WireCategoryTabs();
            WireHiddenTab();
            SetupTabIcons();
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveAllListeners();
                confirmButton.onClick.AddListener(Confirm);
            }

            // 4. Initial paint.
            SetActiveTab(_activeTab);
            RebuildAssembled();
        }

        // ─── Selection validation ───────────────────────────────────────

        string ValidateSelection(string candidate, CharacterPartSlot slot)
        {
            if (string.IsNullOrEmpty(candidate)) return catalog.baseCharacterId;
            if (catalog.Find(candidate, slot) != null) return candidate;
            return catalog.baseCharacterId;
        }

        // ─── Category tabs ──────────────────────────────────────────────

        void WireCategoryTabs()
        {
            if (categoryTabs == null) return;
            foreach (var tab in categoryTabs)
            {
                if (tab.button == null) continue;
                CharacterPartSlot captured = tab.slot;
                tab.button.onClick.RemoveAllListeners();
                tab.button.onClick.AddListener(() => SetActiveTab(captured));
            }
        }

        void WireHiddenTab()
        {
            if (hiddenTab == null) return;
            // No hidden characters available → hide the tab entirely.
            if (_hiddenDefs.Count == 0)
            {
                hiddenTab.gameObject.SetActive(false);
                return;
            }
            hiddenTab.gameObject.SetActive(true);
            hiddenTab.onClick.RemoveAllListeners();
            hiddenTab.onClick.AddListener(SetActiveHiddenTab);
        }

        bool IsKnownHidden(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var d in _hiddenDefs) if (d.id == id) return true;
            return false;
        }

        // Each tab is represented by a simple flat silhouette icon (a person
        // for Head, a t-shirt for Body) — the rotating 3D model previews are
        // used only inside the option boxes. The text "Label" child, if
        // present, is hidden; the icon goes into the "Icon" RawImage child.
        void SetupTabIcons()
        {
            if (categoryTabs == null) return;

            foreach (var tab in categoryTabs)
            {
                if (tab.button == null) continue;

                var label = tab.button.transform.Find("Label");
                if (label != null) label.gameObject.SetActive(false);

                var icon = tab.button.transform.Find("Icon")?.GetComponent<RawImage>();
                if (icon == null) continue;

                // Drop any leftover model-thumbnail renderer from earlier builds.
                var thumb = icon.GetComponent<PartThumbnailRenderer>();
                if (thumb != null) Destroy(thumb);

                icon.texture = TabIconFor(tab.slot);
                icon.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            }

            // Hidden-characters tab → crown icon.
            if (hiddenTab != null && hiddenTab.gameObject.activeSelf)
            {
                var label = hiddenTab.transform.Find("Label");
                if (label != null) label.gameObject.SetActive(false);
                var icon = hiddenTab.transform.Find("Icon")?.GetComponent<RawImage>();
                if (icon != null)
                {
                    var thumb = icon.GetComponent<PartThumbnailRenderer>();
                    if (thumb != null) Destroy(thumb);
                    icon.texture = TabIconFactory.Crown();
                    icon.color = new Color(0.95f, 0.95f, 0.95f, 1f);
                }
            }
        }

        static Texture2D TabIconFor(CharacterPartSlot slot)
        {
            switch (slot)
            {
                case CharacterPartSlot.Body: return TabIconFactory.Shirt();
                case CharacterPartSlot.Head: return TabIconFactory.Head();
                default: return TabIconFactory.Head();
            }
        }

        void SetActiveTab(CharacterPartSlot slot)
        {
            _activeTab = slot;
            _activeTabIsHidden = false;
            UpdateTabHighlights();
            RepopulateOptions(slot);
        }

        void SetActiveHiddenTab()
        {
            _activeTabIsHidden = true;
            UpdateTabHighlights();
            RepopulateHiddenOptions();
        }

        // Highlight whichever tab is open (slot tab or hidden tab) and dim the
        // rest. The icon RawImage is inset, so the button background reads as a
        // frame: accent when active, dark otherwise.
        void UpdateTabHighlights()
        {
            if (categoryTabs != null)
            {
                foreach (var tab in categoryTabs)
                {
                    if (tab.button == null) continue;
                    ApplyTabFrame(tab.button, !_activeTabIsHidden && tab.slot == _activeTab);
                }
            }
            if (hiddenTab != null && hiddenTab.gameObject.activeSelf)
                ApplyTabFrame(hiddenTab, _activeTabIsHidden);
        }

        void ApplyTabFrame(Button button, bool isActive)
        {
            var outline = button.GetComponent<Outline>();
            if (outline == null && isActive) outline = button.gameObject.AddComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = isActive;
                outline.effectColor = accentColor;
                outline.effectDistance = new Vector2(2f, 2f);
            }
            var img = button.GetComponent<Image>();
            if (img != null)
                img.color = isActive ? accentColor : new Color(0.18f, 0.18f, 0.22f, 1f);
        }

        // ─── Options grid ───────────────────────────────────────────────

        void RepopulateOptions(CharacterPartSlot slot)
        {
            // Clear previous buttons (skip the template itself). Deactivate
            // immediately so the old options leave the layout this frame even
            // though Destroy itself is deferred to end-of-frame.
            foreach (var btn in _slotOptionButtons)
            {
                if (btn == null) continue;
                btn.gameObject.SetActive(false);
                Destroy(btn.gameObject);
            }
            _slotOptionButtons.Clear();

            variantButtonTemplate.gameObject.SetActive(false);

            foreach (var entry in catalog.Options(slot))
            {
                var btn = Instantiate(variantButtonTemplate, variantGridContainer);
                btn.gameObject.SetActive(true);
                btn.name = "Option_" + slot + "_" + entry.sourceCharacterId;

                // Options are shown as a rotating 3D model of just this slot's
                // part(s), filling the box — no text. Hide the legacy label /
                // badge children and render the model into the "Icon" RawImage.
                var label = btn.transform.Find("Label");
                if (label != null) label.gameObject.SetActive(false);
                var badge = btn.transform.Find("Badge");
                if (badge != null) badge.gameObject.SetActive(false);

                var icon = btn.transform.Find("Icon")?.GetComponent<RawImage>();
                if (icon != null && _bodyByCharacterId.TryGetValue(entry.sourceCharacterId, out var body))
                {
                    var thumb = icon.GetComponent<PartThumbnailRenderer>();
                    if (thumb == null) thumb = icon.gameObject.AddComponent<PartThumbnailRenderer>();
                    thumb.Build(body, entry.partNames);
                }

                CharacterPartCatalog.Entry captured = entry;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Pick(captured));
                _slotOptionButtons.Add(btn);
            }

            RefreshOptionHighlights();
        }

        void RefreshOptionHighlights()
        {
            string currentForActiveSlot;
            if (!_selection.TryGetValue(_activeTab, out currentForActiveSlot)) currentForActiveSlot = catalog.baseCharacterId;
            foreach (var btn in _slotOptionButtons)
            {
                if (btn == null) continue;
                bool isActive = btn.name.EndsWith("_" + currentForActiveSlot);

                // The model thumbnail (RawImage) is inset, so the button
                // background shows as a frame: accent when picked, dark
                // otherwise.
                var img = btn.GetComponent<Image>();
                if (img != null)
                {
                    img.color = isActive ? accentColor : new Color(0.18f, 0.18f, 0.22f, 1f);
                }

                var outline = btn.GetComponent<Outline>();
                if (outline == null && isActive) outline = btn.gameObject.AddComponent<Outline>();
                if (outline != null)
                {
                    outline.enabled = isActive;
                    outline.effectColor = accentColor;
                    outline.effectDistance = new Vector2(2f, 2f);
                }
            }
        }

        // ─── Hidden whole-body options ──────────────────────────────────

        void RepopulateHiddenOptions()
        {
            foreach (var btn in _slotOptionButtons)
            {
                if (btn == null) continue;
                btn.gameObject.SetActive(false);
                Destroy(btn.gameObject);
            }
            _slotOptionButtons.Clear();

            variantButtonTemplate.gameObject.SetActive(false);

            foreach (var def in _hiddenDefs)
            {
                var btn = Instantiate(variantButtonTemplate, variantGridContainer);
                btn.gameObject.SetActive(true);
                btn.name = "Hidden_" + def.id;

                var label = btn.transform.Find("Label");
                if (label != null) label.gameObject.SetActive(false);
                var badge = btn.transform.Find("Badge");
                if (badge != null) badge.gameObject.SetActive(false);

                // Whole-body, T-pose, rotating thumbnail — null partNames keeps
                // the entire model (no head/body isolation).
                var icon = btn.transform.Find("Icon")?.GetComponent<RawImage>();
                if (icon != null && _bodyByCharacterId.TryGetValue(def.id, out var body))
                {
                    var thumb = icon.GetComponent<PartThumbnailRenderer>();
                    if (thumb == null) thumb = icon.gameObject.AddComponent<PartThumbnailRenderer>();
                    thumb.Build(body, null);
                }

                CharacterDefinition captured = def;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => PickHidden(captured));
                _slotOptionButtons.Add(btn);
            }

            RefreshHiddenHighlights();
        }

        void RefreshHiddenHighlights()
        {
            foreach (var btn in _slotOptionButtons)
            {
                if (btn == null) continue;
                bool isActive = btn.name == "Hidden_" + _hiddenSelection;
                var img = btn.GetComponent<Image>();
                if (img != null) img.color = isActive ? accentColor : new Color(0.18f, 0.18f, 0.22f, 1f);
                var outline = btn.GetComponent<Outline>();
                if (outline == null && isActive) outline = btn.gameObject.AddComponent<Outline>();
                if (outline != null)
                {
                    outline.enabled = isActive;
                    outline.effectColor = accentColor;
                    outline.effectDistance = new Vector2(2f, 2f);
                }
            }
        }

        void PickHidden(CharacterDefinition def)
        {
            _hiddenSelection = def.id;
            if (selectedNameLabel != null) selectedNameLabel.text = def.displayName;
            RefreshHiddenHighlights();
            RebuildAssembled();
        }

        // ─── Pick → rebuild ─────────────────────────────────────────────

        void Pick(CharacterPartCatalog.Entry entry)
        {
            _selection[entry.slot] = entry.sourceCharacterId;
            // Picking any head/body part returns to the assembled custom look,
            // clearing a previously chosen whole-body hidden character.
            _hiddenSelection = "";

            // The body slot is now a whole-body group (everything from the
            // neck down). No more cross-slot locking — each slot is
            // independent. We kept combinesBodyAndPants in the catalog
            // schema for compatibility with older serialised assets but
            // it's effectively unused in the two-slot world.

            if (selectedNameLabel != null)
            {
                selectedNameLabel.text = entry.displayName;
            }

            RefreshOptionHighlights();
            RebuildAssembled();
        }

        // ─── Assembly ───────────────────────────────────────────────────

        void RebuildAssembled()
        {
            if (_assembled != null)
            {
                if (Application.isPlaying) Destroy(_assembled);
                else DestroyImmediate(_assembled);
                _assembled = null;
            }

            // Hidden whole-body character: spawn the complete model (its own
            // rig) instead of the LEGO-assembled head/body. The shared
            // Humanoid AnimatorController retargets onto its avatar.
            if (!string.IsNullOrEmpty(_hiddenSelection)
                && _definitionsById.TryGetValue(_hiddenSelection, out var hiddenDef)
                && hiddenDef.body != null)
            {
                _assembled = Instantiate(hiddenDef.body, previewAnchor);
                _assembled.name = "Assembled_Hidden";
                _assembled.transform.localPosition = Vector3.zero;
                _assembled.transform.localRotation = Quaternion.identity;

                var hanim = _assembled.GetComponentInChildren<Animator>();
                if (hanim != null)
                {
                    if (hiddenDef.controller != null) hanim.runtimeAnimatorController = hiddenDef.controller;
                    hanim.applyRootMotion = false;
                }
                return;
            }

            _assembled = CharacterAssembler.Assemble(catalog, _selection, _bodyByCharacterId, previewAnchor);
            if (_assembled == null) return;

            // LEGO mesh-swap: the base rig (catalog.baseCharacterId,
            // typically Default3D) drives animation. Transplanted variant
            // SMRs follow the base bones — they don't have their own
            // Animator. So we attach the base character's controller.
            if (_definitionsById.TryGetValue(catalog.baseCharacterId, out var baseDef) && baseDef.controller != null)
            {
                var anim = _assembled.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    anim.runtimeAnimatorController = baseDef.controller;
                    anim.applyRootMotion = false;
                }
            }
        }

        // ─── CJK font ───────────────────────────────────────────────────

        void ApplyFontToAllText()
        {
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return;
            foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                t.font = cjkFont;
            }
        }

        // ─── Confirm ────────────────────────────────────────────────────

        void Confirm()
        {
            foreach (var kvp in _selection)
            {
                PlayerPrefs.SetString(PlayerPrefsSlotPrefix + kvp.Key, kvp.Value);
            }
            // Persist the whole-body hidden pick (empty string clears it so the
            // office falls back to the assembled head/body look).
            PlayerPrefs.SetString(PlayerPrefsHiddenKey, _hiddenSelection ?? "");
            // Drop the legacy single-variant key so OfficePlayerSpawner
            // doesn't accidentally pick it over the new slot prefs.
            PlayerPrefs.DeleteKey(LegacyActiveCharacterPrefsKey);
            PlayerPrefs.Save();
            Debug.Log("[CharacterCustomizeController] Confirmed " + DescribeSelection() + ". Loading '" + nextSceneName + "'.");
            SceneManager.LoadScene(nextSceneName);
        }

        string DescribeSelection()
        {
            var parts = new List<string>();
            foreach (var kvp in _selection) parts.Add(kvp.Key + "=" + kvp.Value);
            return "{" + string.Join(", ", parts) + "}";
        }
    }
}
