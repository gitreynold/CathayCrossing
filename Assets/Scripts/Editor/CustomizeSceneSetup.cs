using System.IO;
using CathayCrossing.Characters;
using CathayCrossing.Customization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CathayCrossing.HD2D.EditorTools
{
    /// <summary>
    /// One-stop setup for the character-customisation workflow:
    ///
    ///   Tools › CathayCrossing › Import Character Partials
    ///   Tools › CathayCrossing › Build Customize Scene
    ///   Tools › CathayCrossing › Setup Character Customization
    ///
    /// Import Partials copies the three source FBX files from
    /// <see cref="SourceDirAbsolute"/> into Resources/Characters/&lt;Name&gt;/,
    /// marks them as Humanoid, and creates a <see cref="CharacterDefinition"/>
    /// per variant so the runtime controller picks them up via
    /// Resources.LoadAll.
    ///
    /// Build Customize Scene generates Assets/Scenes/CustomizeScene.unity
    /// with the full layout: grid-floor preview, orbital camera, category
    /// tabs + variant thumbnail grid, pipeline status, confirm button. Every
    /// TMP label is bound to the CJK font asset (also created on demand
    /// from macOS Arial Unicode.ttf) so Chinese characters render properly
    /// instead of showing as empty boxes.
    /// </summary>
    public static class CustomizeSceneSetup
    {
        // ─── Paths ──────────────────────────────────────────────────────────

        const string SourceDirAbsolute = "/Users/twinb00598897/Desktop/patials_source";

        // macOS ships Arial Unicode at this path — it has full CJK coverage
        // and is the simplest font to bundle for a project that needs
        // Traditional Chinese without an internet download.
        const string SystemCjkFontPath = "/System/Library/Fonts/Supplemental/Arial Unicode.ttf";

        const string ProjectFontPath = "Assets/Fonts/ArialUnicode.ttf";
        const string CjkTmpAssetPath = "Assets/Fonts/CJKFont.asset";
        const string ScenePath       = "Assets/Scenes/CustomizeScene.unity";
        const string GridMatPath     = "Assets/Game/Customization/GridFloor.mat";
        const string GridShaderName  = "CathayCrossing/GridFloor";

        struct PartialEntry
        {
            public string SourceFileName;
            public string CharacterId;
            public string DisplayName;
            public string TargetFbxName;
            // Tint applied to the URP material when the FBX ships no embedded
            // textures (i.e. it's a true "partial" with mesh only). Without
            // this we'd leave the material's _BaseMap pointing at whatever
            // texture-extraction happened to assign by accident, which is
            // how Style3 ended up displaying Default's UVs as colour smears.
            public Color FallbackTint;
            // Spine euler offset applied at runtime to compensate for a
            // baked-in lean in the source FBX (Hunyuan3D doesn't lock a
            // bone-axis convention per generation, so the same Idle clip
            // tilts each variant differently). Zero = no correction.
            public Vector3 SpineCorrectionEuler;
        }

        static readonly PartialEntry[] Partials =
        {
            new PartialEntry { SourceFileName = "3d_men_default.fbx",        CharacterId = "Default3D",  DisplayName = "預設小人",  TargetFbxName = "3d_men_default",  FallbackTint = new Color(0.94f, 0.78f, 0.65f), SpineCorrectionEuler = Vector3.zero },
            new PartialEntry { SourceFileName = "3d_men_style3_partial.fbx", CharacterId = "Style3",     DisplayName = "藍襯衫造型", TargetFbxName = "3d_men_style3",   FallbackTint = new Color(0.30f, 0.45f, 0.75f), SpineCorrectionEuler = Vector3.zero },
            new PartialEntry { SourceFileName = "3d_men_jay_partial.fbx",    CharacterId = "JayPartial", DisplayName = "Jay 造型",   TargetFbxName = "3d_men_jay_partial", FallbackTint = new Color(0.86f, 0.50f, 0.42f), SpineCorrectionEuler = Vector3.zero },
        };

        // Right-rail category tabs. Single Chinese glyph keeps the bar
        // compact and matches the icon-like density of the reference mockup.
        // Hands removed per design — variant FBXs all share Default3D's
        // hand parts so there's nothing meaningful to swap there.
        static readonly (CharacterPartSlot slot, string label)[] CategoryTabs =
        {
            (CharacterPartSlot.Head,  "頭"),
            (CharacterPartSlot.Body,  "身"),
        };

        // ─── Part catalog (manual mapping baked from the FBX analysis) ──
        //
        // For each character × slot, the GameObject names of the mesh
        // parts that comprise the slot inside that character's FBX.
        // Numbers are unique to each generation — Hunyuan3D doesn't lock
        // a per-slot naming convention, so the only way to know which
        // part is which body region is to inspect bounding box centres
        // and dominant vertex groups (which is how this table was
        // produced).
        //
        // Style3 part_5 is special: a single mesh that spans both upper
        // body and pants. We list it under both Body and Pants with
        // combinesBodyAndPants=true; the controller force-locks the
        // other slot when the user picks Style3 in one of them.
        const string CatalogAssetPath = "Assets/Resources/CharacterPartCatalog.asset";

        struct PartMap
        {
            public CharacterPartSlot Slot;
            public string[] PartNames;
            public bool CombinesBodyAndPants;
        }

        static readonly System.Collections.Generic.Dictionary<string, PartMap[]> CharacterPartMaps = new()
        {
            // Head  = hair + every face sub-part (face main, ears, nose,
            //         eye details).
            // Body  = everything else — torso, arms, hands, hips, legs,
            //         feet, shoes. Sub-part numbers are unique per variant
            //         (Hunyuan3D doesn't lock a naming convention).
            ["Default3D"] = new[]
            {
                new PartMap { Slot = CharacterPartSlot.Head, PartNames = new[] { "part_0.001", "part_2.001", "part_4.001", "part_6.001", "part_8.001" } },
                new PartMap { Slot = CharacterPartSlot.Body, PartNames = new[] {
                    "part_1.001",  "part_3.001",  "part_5.001",  "part_7.001",
                    "part_9.001",  "part_10.001", "part_11.001", "part_12.001",
                } },
            },
            ["JayPartial"] = new[]
            {
                new PartMap { Slot = CharacterPartSlot.Head, PartNames = new[] { "part_0.001", "part_7.001", "part_8.001", "part_9.001", "part_10.001" } },
                new PartMap { Slot = CharacterPartSlot.Body, PartNames = new[] {
                    "part_1.001", "part_2.001", "part_3.001",  "part_4.001",
                    "part_5.001", "part_6.001", "part_11.001", "part_12.001",
                } },
            },
            ["Style3"] = new[]
            {
                new PartMap { Slot = CharacterPartSlot.Head, PartNames = new[] { "part_0.001", "part_1.001", "part_2.001", "part_7.001" } },
                new PartMap { Slot = CharacterPartSlot.Body, PartNames = new[] {
                    "part_3.001", "part_4.001", "part_5.001",
                    "part_6.001", "part_8.001", "part_9.001",
                } },
            },
        };

        // ─── Menu items ─────────────────────────────────────────────────────

        [MenuItem("Tools/CathayCrossing/Import Character Partials")]
        public static void ImportPartialsMenu()
        {
            int created = ImportPartials();
            EditorUtility.DisplayDialog("Import Character Partials",
                created > 0
                    ? $"Imported {created} partial(s) into Resources/Characters/."
                    : "No new partials imported (already present or source missing).",
                "OK");
        }

        [MenuItem("Tools/CathayCrossing/Build Customize Scene")]
        public static void BuildCustomizeSceneMenu()
        {
            EnsureCjkFontAsset();
            BuildCustomizeScene();
            EditorUtility.DisplayDialog("Build Customize Scene",
                $"CustomizeScene.unity rebuilt at {ScenePath}.\n\n" +
                "Add it to Build Settings if you want SceneManager.LoadScene " +
                "to find it at runtime.",
                "OK");
        }

        [MenuItem("Tools/CathayCrossing/Setup Character Customization")]
        public static void SetupAllMenu()
        {
            ImportPartials();
            EnsureCjkFontAsset();
            BuildCustomizeScene();
            EditorUtility.DisplayDialog("Setup Character Customization",
                "Imported partials, built CJK font asset, and rebuilt the customize scene.\n\n" +
                "Open Assets/Scenes/CustomizeScene.unity and press Play.",
                "OK");
        }

        // ─── Import partials ────────────────────────────────────────────────

        static int ImportPartials()
        {
            int created = 0;
            foreach (var entry in Partials)
            {
                string srcPath = Path.Combine(SourceDirAbsolute, entry.SourceFileName);
                if (!File.Exists(srcPath))
                {
                    Debug.LogWarning($"[CustomizeSceneSetup] Source missing: {srcPath}");
                    continue;
                }

                string charDir = $"Assets/Resources/Characters/{entry.CharacterId}";
                EnsureFolder(charDir);

                string dstFbx = $"{charDir}/{entry.TargetFbxName}.fbx";
                if (!File.Exists(dstFbx))
                {
                    File.Copy(srcPath, dstFbx, overwrite: false);
                    AssetDatabase.ImportAsset(dstFbx, ImportAssetOptions.ForceUpdate);
                }

                // Hand the new FBX to the existing per-character pipeline so
                // it gets the same treatment Default/Jay get: Humanoid avatar,
                // texture extraction, URP material rebind, and a per-variant
                // PlayerAnimator.controller built from the shared clip set.
                // Without this step the imported partials end up shading with
                // the FBX's embedded materials (the "smeared colour patches"
                // we saw in the customise preview).
                CharacterAnimatorSetup.SetupCharacterByName(entry.CharacterId, entry.TargetFbxName + ".fbx");

                // Some partial FBX files have no embedded textures — only mesh
                // + skeleton + an empty material reference. The shared
                // extraction pipeline ends up resolving those material's
                // texture slots to whatever GUIDs happen to collide (Style3
                // grabbed Default's textures by accident, producing the
                // "broken colour" look). Detect that case and clean up.
                SanitizeMaterialsIfNoOwnTextures(charDir, entry.FallbackTint);

                string defPath = $"{charDir}/{entry.CharacterId}.asset";
                var def = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(defPath);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<CharacterDefinition>();
                    AssetDatabase.CreateAsset(def, defPath);
                    created++;
                }
                def.id = entry.CharacterId;
                def.displayName = entry.DisplayName;
                def.body = AssetDatabase.LoadAssetAtPath<GameObject>(dstFbx);
                def.controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    $"{charDir}/PlayerAnimator.controller");
                def.rigSource = null;
                // Per-variant spine straightening — applied at runtime via
                // PostureCorrection in LateUpdate so animations still play.
                def.spineCorrectionEuler = entry.SpineCorrectionEuler;
                EditorUtility.SetDirty(def);
            }

            // Each variant keeps its own self-built Avatar with its own
            // bind-pose. Earlier we tried:
            //   1. CopyFromOther → broke animations (T-pose) because bone
            //      lengths differ across variants.
            //   2. Patching humanDescription.skeleton rotations to match
            //      Default3D → mesh skin weights are baked against the
            //      original bone rotations, so the mesh twisted severely.
            // The right pattern is mesh-swap: spawn Default3D's rig as the
            // base GameObject and rebind the variant's SkinnedMeshRenderer
            // onto its bone tree at runtime. That happens in the runtime
            // controller (CharacterCustomizeController.InstantiatePreview)
            // and the OfficePlayerSpawner, not in this importer pass.
            // Rebuild the LEGO part catalog so the runtime spawner +
            // customise controller can pick parts per slot.
            BuildPartCatalog();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return created;
        }

        // ─── LEGO part catalog ──────────────────────────────────────────────

        // Build / refresh the catalog ScriptableObject under Resources/ so
        // Resources.Load<CharacterPartCatalog>("CharacterPartCatalog") works
        // both in customise and in office scenes.
        static void BuildPartCatalog()
        {
            EnsureFolder("Assets/Resources");

            var catalog = AssetDatabase.LoadAssetAtPath<CharacterPartCatalog>(CatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CharacterPartCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            }
            catalog.baseCharacterId = "Default3D";

            // Friendly display names mirror what we set on each
            // CharacterDefinition. Reading them from the Partials table
            // keeps the customise UI in sync with the rest of the project.
            var displayByCharId = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var p in Partials) displayByCharId[p.CharacterId] = p.DisplayName;

            var entries = new System.Collections.Generic.List<CharacterPartCatalog.Entry>();
            foreach (var kvp in CharacterPartMaps)
            {
                string charId = kvp.Key;
                string display = displayByCharId.TryGetValue(charId, out var d) ? d : charId;
                foreach (var map in kvp.Value)
                {
                    entries.Add(new CharacterPartCatalog.Entry
                    {
                        sourceCharacterId = charId,
                        displayName = display,
                        slot = map.Slot,
                        partNames = map.PartNames,
                        combinesBodyAndPants = map.CombinesBodyAndPants,
                    });
                }
            }
            catalog.entries = entries.ToArray();

            EditorUtility.SetDirty(catalog);
            Debug.Log($"[CustomizeSceneSetup] CharacterPartCatalog rebuilt — {entries.Count} entries.");
        }

        // Master rig — every other variant uses this character's FBX as its
        // skeleton/Animator source via CharacterDefinition.rigSource. The
        // variant's own FBX still supplies the visible mesh + materials,
        // which the spawner hot-swaps onto the shared rig at runtime.
        const string MasterAvatarCharacterId = "Default3D";

        static string MasterBodyAssetPath()
        {
            foreach (var p in Partials)
            {
                if (p.CharacterId == MasterAvatarCharacterId)
                    return $"Assets/Resources/Characters/{MasterAvatarCharacterId}/{p.TargetFbxName}.fbx";
            }
            return null;
        }

        // ─── Material sanitiser ─────────────────────────────────────────────

        // For FBX files that ship without embedded texture data, the texture-
        // extraction pass leaves materials pointing at GUIDs that resolve to
        // other characters' textures (Style3 ended up sampling Default's
        // base map; the UVs don't line up so you get smeared colour). Clear
        // any texture reference that doesn't live inside this character's
        // own folder, and apply a clean solid tint so the variant reads as
        // a flat-shaded preview instead.
        static void SanitizeMaterialsIfNoOwnTextures(string charDir, Color fallbackTint)
        {
            string texturesDir = charDir + "/Textures";
            int ownTextureCount = 0;
            if (AssetDatabase.IsValidFolder(texturesDir))
            {
                ownTextureCount = AssetDatabase.FindAssets("t:Texture2D", new[] { texturesDir }).Length;
            }
            if (ownTextureCount > 0) return; // FBX shipped textures — leave the materials alone.

            string materialsDir = charDir + "/Materials";
            if (!AssetDatabase.IsValidFolder(materialsDir)) return;

            int cleaned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { materialsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                // _BaseMap and _BumpMap point at other characters' textures —
                // clear them so URP renders a clean tinted material.
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
                if (mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", null);
                    mat.DisableKeyword("_NORMALMAP");
                }
                if (mat.HasProperty("_MetallicGlossMap"))
                {
                    mat.SetTexture("_MetallicGlossMap", null);
                    mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", fallbackTint);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     fallbackTint);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);

                EditorUtility.SetDirty(mat);
                cleaned++;
            }

            if (cleaned > 0)
            {
                Debug.Log($"[CustomizeSceneSetup] Sanitised {cleaned} dangling material(s) in {materialsDir} (no embedded textures in source FBX) — fallback tint #{ColorUtility.ToHtmlStringRGB(fallbackTint)}.");
            }
        }

        // ─── Legacy cleanup ─────────────────────────────────────────────────

        [MenuItem("Tools/CathayCrossing/Remove Legacy Default+Jay Characters")]
        public static void RemoveLegacyCharactersMenu()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Remove legacy characters",
                "This deletes:\n" +
                "  Assets/Resources/Characters/Default\n" +
                "  Assets/Resources/Characters/Jay\n\n" +
                "Make sure the partial-based variants (Default3D / Style3 / " +
                "JayPartial) have been imported and set up first — they each " +
                "have their own PlayerAnimator.controller so they no longer " +
                "depend on Default's folder.\n\nProceed?",
                "Delete", "Cancel");
            if (!confirm) return;

            int removed = 0;
            foreach (var dir in new[] {
                "Assets/Resources/Characters/Default",
                "Assets/Resources/Characters/Jay",
            })
            {
                if (AssetDatabase.IsValidFolder(dir) && AssetDatabase.DeleteAsset(dir))
                {
                    removed++;
                    Debug.Log($"[CustomizeSceneSetup] Removed {dir}");
                }
            }
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Remove legacy characters",
                $"Removed {removed} legacy character folder(s).", "OK");
        }

        // ─── CJK font asset ─────────────────────────────────────────────────

        // Copies Arial Unicode from macOS into the project (one-time) and
        // builds a TMP_FontAsset in dynamic-atlas mode so any CJK glyph the
        // UI references gets baked on demand at runtime.
        //
        // Distributing macOS system fonts in a shipped build is a license
        // grey area; for an in-house dev tool this is the path of least
        // resistance. Swap in NotoSansTC or PingFang TC for distribution.
        static void EnsureCjkFontAsset()
        {
            EnsureFolder("Assets/Fonts");

            if (!File.Exists(ProjectFontPath))
            {
                if (!File.Exists(SystemCjkFontPath))
                {
                    Debug.LogWarning($"[CustomizeSceneSetup] System CJK font missing at {SystemCjkFontPath}. " +
                                     "Drop a CJK TTF/OTF at " + ProjectFontPath + " manually and re-run.");
                    return;
                }
                File.Copy(SystemCjkFontPath, ProjectFontPath, overwrite: false);
                AssetDatabase.ImportAsset(ProjectFontPath, ImportAssetOptions.ForceUpdate);
            }

            // Always rebuild — TMP_FontAsset.CreateFontAsset returns a fresh
            // instance whose atlas Texture2D is owned by the TMP_FontAsset
            // object. If we just CreateAsset(...) the outer ScriptableObject,
            // the texture isn't a sub-asset and Unity garbage-collects it on
            // the next domain reload ("Font Atlas Texture is missing"). We
            // delete any prior asset, recreate cleanly, then attach the
            // texture + material as sub-assets.
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkTmpAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(CjkTmpAssetPath);
            }

            var src = AssetDatabase.LoadAssetAtPath<Font>(ProjectFontPath);
            if (src == null)
            {
                Debug.LogError("[CustomizeSceneSetup] Failed to load source Font at " + ProjectFontPath);
                return;
            }

            // Dynamic atlas mode + multi-atlas support: TMP grows the atlas
            // texture as new glyphs are requested, so we don't have to
            // enumerate every CJK code point at build time.
            var fa = TMP_FontAsset.CreateFontAsset(
                font: src,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            AssetDatabase.CreateAsset(fa, CjkTmpAssetPath);

            // Attach atlas texture + material as sub-assets so they're
            // serialised with the font asset and survive domain reloads.
            if (fa.atlasTexture != null && !AssetDatabase.Contains(fa.atlasTexture))
            {
                fa.atlasTexture.name = "Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            }
            if (fa.atlasTextures != null)
            {
                foreach (var tex in fa.atlasTextures)
                {
                    if (tex != null && !AssetDatabase.Contains(tex))
                    {
                        AssetDatabase.AddObjectToAsset(tex, fa);
                    }
                }
            }
            if (fa.material != null && !AssetDatabase.Contains(fa.material))
            {
                fa.material.name = "Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }

            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(CjkTmpAssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[CustomizeSceneSetup] Created CJK TMP_FontAsset at " + CjkTmpAssetPath);
        }

        // ─── Grid floor material ────────────────────────────────────────────

        static Material EnsureGridFloorMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(GridMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find(GridShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[CustomizeSceneSetup] Shader '{GridShaderName}' missing — let Unity recompile first. " +
                                 "Falling back to URP/Unlit grey.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            var mat = new Material(shader);
            mat.name = "GridFloor";
            EnsureFolder("Assets/Game/Customization");
            AssetDatabase.CreateAsset(mat, GridMatPath);
            return mat;
        }

        // ─── Build the scene ────────────────────────────────────────────────

        static void BuildCustomizeScene()
        {
            EnsureFolder("Assets/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ─── Lighting ───────────────────────────────────────────────
            var sun = new GameObject("Directional Light");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.1f;
            sunLight.color = new Color(1f, 0.97f, 0.92f);
            sun.transform.rotation = Quaternion.Euler(55f, -25f, 0f);

            var fill = new GameObject("Fill Light");
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.45f;
            fillLight.color = new Color(0.72f, 0.82f, 1f);
            fill.transform.rotation = Quaternion.Euler(20f, 160f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.12f, 0.16f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.08f, 0.10f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);

            // ─── Grid floor ─────────────────────────────────────────────
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "GridFloor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.position = new Vector3(0f, 0f, 0f);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(20f, 20f, 1f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = EnsureGridFloorMaterial();

            // ─── Preview anchor ─────────────────────────────────────────
            var preview = new GameObject("PreviewAnchor");
            preview.transform.position = Vector3.zero;

            // ─── Orbital camera ─────────────────────────────────────────
            var camGo = new GameObject("PreviewCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f);
            cam.fieldOfView = 32f;
            var orbit = camGo.AddComponent<PreviewCameraOrbit>();
            orbit.target = preview.transform;
            orbit.yaw = 180f;
            orbit.pitch = 14f;
            // Pulled back + lower focus so the whole body (head → shoes)
            // fits in frame at the default distance.
            orbit.distance = 4.6f;
            orbit.targetOffset = new Vector3(0f, 0.85f, 0f);

            // ─── Canvas ─────────────────────────────────────────────────
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                // InputSystemUIInputModule lives in com.unity.inputsystem; if the
                // package is present we prefer it (the project is configured to
                // use the new Input System). Fall back to StandaloneInputModule
                // when not available so the scene still works.
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            }

            var cjkFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkTmpAssetPath);

            // Header
            var header = MakeUiText(canvas.transform, "Header", "人物客製化", 56, cjkFont);
            var headerRt = header.rectTransform;
            headerRt.anchorMin = new Vector2(0f, 1f); headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -36f);
            headerRt.sizeDelta = new Vector2(0f, 80f);
            header.alignment = TextAlignmentOptions.Center;

            var selectedLabel = MakeUiText(canvas.transform, "SelectedLabel", "—", 32, cjkFont);
            var selRt = selectedLabel.rectTransform;
            selRt.anchorMin = new Vector2(0f, 1f); selRt.anchorMax = new Vector2(1f, 1f);
            selRt.pivot = new Vector2(0.5f, 1f);
            selRt.anchoredPosition = new Vector2(0f, -110f);
            selRt.sizeDelta = new Vector2(0f, 40f);
            selectedLabel.alignment = TextAlignmentOptions.Center;
            selectedLabel.color = new Color(0.80f, 0.80f, 0.80f);

            // ─── Right rail ─────────────────────────────────────────────
            var rail = new GameObject("VariantRail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(canvas.transform, false);
            var railRt = (RectTransform)rail.transform;
            railRt.anchorMin = new Vector2(1f, 0f); railRt.anchorMax = new Vector2(1f, 1f);
            railRt.pivot = new Vector2(1f, 0.5f);
            railRt.sizeDelta = new Vector2(360f, -180f);
            railRt.anchoredPosition = new Vector2(0f, 0f);
            rail.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f, 0.88f);

            // Category tab strip across the top of the rail.
            var tabBar = new GameObject("TabBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tabBar.transform.SetParent(rail.transform, false);
            var tbRt = (RectTransform)tabBar.transform;
            tbRt.anchorMin = new Vector2(0f, 1f); tbRt.anchorMax = new Vector2(1f, 1f);
            tbRt.pivot = new Vector2(0.5f, 1f);
            tbRt.anchoredPosition = new Vector2(0f, -16f);
            tbRt.sizeDelta = new Vector2(-20f, 56f);
            var hlg = tabBar.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            var categoryTabs = new System.Collections.Generic.List<CharacterCustomizeController.CategoryTab>();
            foreach (var (slot, label) in CategoryTabs)
            {
                var tabBtn = MakeIconButton(tabBar.transform, "Tab_" + slot, label, cjkFont);
                categoryTabs.Add(new CharacterCustomizeController.CategoryTab
                {
                    slot = slot,
                    button = tabBtn,
                });
            }

            // Variant grid.
            var gridGo = new GameObject("VariantGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGo.transform.SetParent(rail.transform, false);
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.anchorMin = new Vector2(0f, 0f); gridRt.anchorMax = new Vector2(1f, 1f);
            gridRt.pivot = new Vector2(0.5f, 0.5f);
            gridRt.offsetMin = new Vector2(16f, 110f);
            gridRt.offsetMax = new Vector2(-16f, -80f);
            var glg = gridGo.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(150f, 150f);
            glg.spacing = new Vector2(12f, 12f);
            glg.padding = new RectOffset(0, 0, 0, 0);
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;

            var variantTemplate = MakeVariantButtonTemplate(rail.transform, cjkFont);

            // ─── Confirm button ─────────────────────────────────────────
            var confirmGo = new GameObject("ConfirmButton", typeof(RectTransform), typeof(Image), typeof(Button));
            confirmGo.transform.SetParent(canvas.transform, false);
            var cRt = (RectTransform)confirmGo.transform;
            cRt.anchorMin = new Vector2(1f, 0f); cRt.anchorMax = new Vector2(1f, 0f);
            cRt.pivot = new Vector2(1f, 0f);
            cRt.sizeDelta = new Vector2(320f, 80f);
            cRt.anchoredPosition = new Vector2(-40f, 40f);
            confirmGo.GetComponent<Image>().color = new Color(0.97f, 0.43f, 0.39f);
            var confirmText = MakeUiText(confirmGo.transform, "Label", "確認進入辦公室", 30, cjkFont);
            var ctRt = confirmText.rectTransform;
            ctRt.anchorMin = Vector2.zero; ctRt.anchorMax = Vector2.one;
            ctRt.offsetMin = Vector2.zero; ctRt.offsetMax = Vector2.zero;
            confirmText.alignment = TextAlignmentOptions.Center;
            confirmText.color = Color.white;
            var confirmBtn = confirmGo.GetComponent<Button>();

            // ─── Controller wiring ──────────────────────────────────────
            var controllerGo = new GameObject("CharacterCustomizeController");
            var ctrl = controllerGo.AddComponent<CharacterCustomizeController>();
            ctrl.previewAnchor = preview.transform;
            ctrl.catalog = AssetDatabase.LoadAssetAtPath<CharacterPartCatalog>(CatalogAssetPath);
            ctrl.variantGridContainer = gridGo.transform;
            ctrl.variantButtonTemplate = variantTemplate;
            ctrl.categoryTabs = categoryTabs.ToArray();
            ctrl.confirmButton = confirmBtn;
            ctrl.selectedNameLabel = selectedLabel;
            ctrl.nextSceneName = "OfficeScene";
            ctrl.cjkFont = cjkFont;

            // ─── Save ───────────────────────────────────────────────────
            bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!ok) { Debug.LogError($"[CustomizeSceneSetup] Failed to save {ScenePath}."); return; }
            Debug.Log($"[CustomizeSceneSetup] Customize scene saved at {ScenePath}.");
        }

        // ─── UI helpers ─────────────────────────────────────────────────────

        static Button MakeIconButton(Transform parent, string goName, string label, TMP_FontAsset font)
        {
            var go = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.22f, 1f);
            go.GetComponent<LayoutElement>().preferredWidth = 48f;
            var lbl = MakeUiText(go.transform, "Label", label, 24, font);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.color = new Color(0.95f, 0.95f, 0.95f);
            return go.GetComponent<Button>();
        }

        static Button MakeVariantButtonTemplate(Transform parent, TMP_FontAsset font)
        {
            var go = new GameObject("VariantButtonTemplate", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.22f, 1f);
            go.GetComponent<LayoutElement>().preferredHeight = 150f;
            go.GetComponent<LayoutElement>().preferredWidth = 150f;

            var label = MakeUiText(go.transform, "Label", "Variant", 22, font);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 8f);
            lrt.sizeDelta = new Vector2(-8f, 32f);
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f);

            go.SetActive(false);
            return go.GetComponent<Button>();
        }

        static void MakeStageRow(Transform parent, string rowName, string labelText, TMP_FontAsset font)
        {
            var row = new GameObject(rowName, typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 36f;

            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(row.transform, false);
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.anchorMin = new Vector2(0f, 0.5f); dotRt.anchorMax = new Vector2(0f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.sizeDelta = new Vector2(16f, 16f);
            dotRt.anchoredPosition = new Vector2(14f, 0f);
            dotGo.GetComponent<Image>().color = new Color(0.55f, 0.55f, 0.55f, 0.4f);

            var lbl = MakeUiText(row.transform, "Label", labelText, 22, font);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(40f, 0f); lrt.offsetMax = new Vector2(-8f, 0f);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.color = new Color(0.86f, 0.86f, 0.86f);
        }

        static TextMeshProUGUI MakeUiText(Transform parent, string goName, string text, float size, TMP_FontAsset font)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.enableAutoSizing = false;
            tmp.color = Color.white;
            if (font != null) tmp.font = font;
            return tmp;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
