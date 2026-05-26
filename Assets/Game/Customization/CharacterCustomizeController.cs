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

        // LEGO mesh-swap restored 2026-05-26: Head + Body each have their
        // own list of variant options. Head is the default opened tab to
        // match the right-rail order in the mockup.
        CharacterPartSlot _activeTab = CharacterPartSlot.Head;
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
            }

            // 2. Seed the selection from PlayerPrefs (or default to base).
            foreach (CharacterPartSlot slot in System.Enum.GetValues(typeof(CharacterPartSlot)))
            {
                string saved = PlayerPrefs.GetString(PlayerPrefsSlotPrefix + slot, catalog.baseCharacterId);
                _selection[slot] = ValidateSelection(saved, slot);
            }

            // 3. Wire static UI.
            WireCategoryTabs();
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

        void SetActiveTab(CharacterPartSlot slot)
        {
            _activeTab = slot;

            // Highlight the active tab button + dim the rest.
            if (categoryTabs != null)
            {
                foreach (var tab in categoryTabs)
                {
                    if (tab.button == null) continue;
                    bool isActive = tab.slot == slot;
                    var outline = tab.button.GetComponent<Outline>();
                    if (outline == null && isActive) outline = tab.button.gameObject.AddComponent<Outline>();
                    if (outline != null)
                    {
                        outline.enabled = isActive;
                        outline.effectColor = accentColor;
                        outline.effectDistance = new Vector2(2f, 2f);
                    }
                    var img = tab.button.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = isActive
                            ? new Color(accentColor.r * 0.4f, accentColor.g * 0.4f, accentColor.b * 0.4f, 1f)
                            : new Color(0.18f, 0.18f, 0.22f, 1f);
                    }
                }
            }

            RepopulateOptions(slot);
        }

        // ─── Options grid ───────────────────────────────────────────────

        void RepopulateOptions(CharacterPartSlot slot)
        {
            // Clear previous buttons (skip the template itself).
            foreach (var btn in _slotOptionButtons)
            {
                if (btn != null) Destroy(btn.gameObject);
            }
            _slotOptionButtons.Clear();

            variantButtonTemplate.gameObject.SetActive(false);

            foreach (var entry in catalog.Options(slot))
            {
                var btn = Instantiate(variantButtonTemplate, variantGridContainer);
                btn.gameObject.SetActive(true);
                btn.name = "Option_" + slot + "_" + entry.sourceCharacterId;

                // Label
                var label = btn.transform.Find("Label")?.GetComponent<TMP_Text>();
                if (label == null) label = btn.GetComponentInChildren<TMP_Text>(includeInactive: true);
                if (label != null)
                {
                    label.text = entry.displayName;
                    if (cjkFont != null) label.font = cjkFont;
                }

                // Combined badge — visible only on entries whose body+pants
                // are baked into one mesh, so the user knows picking it
                // locks the other slot too.
                var badge = btn.transform.Find("Badge")?.GetComponent<TMP_Text>();
                if (badge != null)
                {
                    badge.gameObject.SetActive(entry.combinesBodyAndPants);
                    if (entry.combinesBodyAndPants)
                    {
                        badge.text = "整套";
                        if (cjkFont != null) badge.font = cjkFont;
                    }
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

        // ─── Pick → rebuild ─────────────────────────────────────────────

        void Pick(CharacterPartCatalog.Entry entry)
        {
            _selection[entry.slot] = entry.sourceCharacterId;

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
