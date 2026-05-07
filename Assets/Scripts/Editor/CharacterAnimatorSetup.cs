using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CathayCrossing.HD2D.EditorTools
{
    /// <summary>
    /// One-click wiring for the player character + Mixamo animations.
    ///
    /// Run via: <c>Tools › CathayCrossing › Setup Character Animator</c>.
    ///
    /// Performs three jobs in order so the asset graph stays consistent:
    ///  1. Reimports <c>men_3D_color.fbx</c> as Humanoid and bakes a fresh Avatar
    ///     out of its skeleton (Tencent's auto-rig output, 28 bones).
    ///  2. Reimports each Mixamo clip FBX (Without Skin) as Humanoid and points
    ///     its Avatar Definition at the character's Avatar — this is what lets
    ///     Mixamo's mixamorig:* bones drive Tencent's differently-named rig.
    ///  3. Builds <c>PlayerAnimator.controller</c> with three states (Idle /
    ///     Walking / Waving), wired by a Speed float (Idle ↔ Walking) and a
    ///     Wave trigger (Any → Waving → exit).
    ///
    /// Idempotent — running it again just overwrites the controller in place.
    /// </summary>
    public static class CharacterAnimatorSetup
    {
        const string CharacterFbx   = "Assets/Resources/Characters/men_3D_color.fbx";
        const string AnimDir        = "Assets/Resources/Characters/Animations";
        const string IdleClipFile   = "Happy Idle.fbx";
        const string WalkClipFile   = "Walking.fbx";
        const string WaveClipFile   = "Waving.fbx";
        const string ControllerPath = "Assets/Resources/Characters/PlayerAnimator.controller";
        const string TexturesDir    = "Assets/Resources/Characters/Textures";
        const string MaterialsDir   = "Assets/Resources/Characters/Materials";

        const string SpeedParam = "Speed";
        const string WaveParam  = "Wave";

        // Speed threshold below which we consider the player idle. The
        // controller's velocity smoothing means the value lingers a tick
        // after movement stops; 0.1 m/s ignores that without dropping early.
        const float IdleSpeedThreshold = 0.1f;

        [MenuItem("Tools/CathayCrossing/Setup Character Animator")]
        public static void Setup()
        {
            if (!File.Exists(CharacterFbx))
            {
                Debug.LogError($"[CharacterAnimatorSetup] Character FBX missing at {CharacterFbx}.\n" +
                               "Copy the rigged FBX into Assets/Resources/Characters/ first.");
                return;
            }

            // 0) Pull the embedded textures + material out of the FBX and rewrite
            //    the material to URP/Lit. Without this the chibi renders flat
            //    grey because Unity's auto-imported material on URP loses the
            //    textures. Idempotent — running again is a no-op once extracted.
            ExtractAndConvertCharacterMaterial(CharacterFbx);

            // 1) Character → Humanoid, build its OWN avatar (Tencent bone names
            //    "Hips/Spine/..." are Unity-recognized standard humanoid names).
            ConfigureCharacterAsHumanoid(CharacterFbx);

            // 2) Mixamo clips → Humanoid, each builds its OWN avatar from its
            //    "mixamorig:*" bone names (also Unity-recognized). Don't Copy
            //    From Other — Tencent's avatar uses bone names "Hips" while
            //    Mixamo's bones are "mixamorig:Hips"; copying would look for
            //    "Hips" in the Mixamo skeleton and fail. Unity Humanoid handles
            //    the retarget at runtime via its abstract bone names.
            string idlePath = Path.Combine(AnimDir, IdleClipFile);
            string walkPath = Path.Combine(AnimDir, WalkClipFile);
            string wavePath = Path.Combine(AnimDir, WaveClipFile);

            ConfigureClipAsHumanoid(idlePath, isLoopable: true);
            ConfigureClipAsHumanoid(walkPath, isLoopable: true);
            ConfigureClipAsHumanoid(wavePath, isLoopable: false);

            var idleClip = LoadFirstAnimationClip(idlePath);
            var walkClip = LoadFirstAnimationClip(walkPath);
            var waveClip = LoadFirstAnimationClip(wavePath);

            if (idleClip == null || walkClip == null || waveClip == null)
            {
                Debug.LogError($"[CharacterAnimatorSetup] Couldn't find AnimationClip in one of: {idlePath}, {walkPath}, {wavePath}");
                return;
            }

            // 3) Build (or rebuild) the AnimatorController
            BuildController(idleClip, walkClip, waveClip);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CharacterAnimatorSetup] Done.\n" +
                      $"  Character: {CharacterFbx}\n" +
                      $"  Controller: {ControllerPath}\n" +
                      $"  Clips: Idle={idleClip.name}, Walk={walkClip.name}, Wave={waveClip.name}");
        }

        // ─── Texture / material extraction (URP) ───────────────────────────

        // Tencent Hunyuan3D ships its FBX with embedded textures referenced by
        // an internal material that Unity defaults to a Standard/Autodesk
        // shader — both render flat under URP. We:
        //   1. Pull the texture PNGs out into a sibling Textures/ folder.
        //   2. Pull the material out into Materials/.
        //   3. Convert the extracted material to URP/Lit and re-bind the maps
        //      to URP's slot names (_BaseMap / _BumpMap / _MetallicGlossMap).
        static void ExtractAndConvertCharacterMaterial(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            EnsureFolder(TexturesDir);
            EnsureFolder(MaterialsDir);

            // ExtractTextures returns false if there's nothing left to extract,
            // not an error. We don't gate on the bool.
            importer.ExtractTextures(TexturesDir);
            AssetDatabase.Refresh();

            // Pull every embedded material out into Materials/ as standalone .mat.
            // ExtractAsset breaks the FBX↔material link so Unity stops re-creating
            // them on every reimport.
            int extracted = 0;
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (sub is Material mat && AssetDatabase.IsSubAsset(mat))
                {
                    string targetPath = AssetDatabase.GenerateUniqueAssetPath($"{MaterialsDir}/{mat.name}.mat");
                    string err = AssetDatabase.ExtractAsset(mat, targetPath);
                    if (string.IsNullOrEmpty(err)) extracted++;
                }
            }

            if (extracted > 0)
            {
                AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            }

            ConvertExtractedMaterialsToUrp();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void ConvertExtractedMaterialsToUrp()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogWarning("[CharacterAnimatorSetup] URP/Lit shader missing — leaving materials on their original shader.");
                return;
            }

            // Tencent Hunyuan3D: filename pattern `texture_pbr_<id>.png` for
            // basecolor, `texture_pbr_<id>_normal.png`, `*_metallic.png`,
            // `*_roughness.png`. Notable quirk: basecolor has NO suffix —
            // generic "basecolor" keyword search would miss it (and would
            // accidentally pick up Tripo's `cartooncharacter3dmodel_basecolor.PNG`
            // left over in the same folder, mixing UVs across two models).
            //
            // Strategy: prefer the modern `texture_pbr_*` family; only fall
            // back to keyword search if Tencent-style files aren't present.
            Texture2D albedoTex, normalTex, metallicTex;
            FindTencentTextureSet(out albedoTex, out normalTex, out metallicTex);
            if (albedoTex == null)
                albedoTex = FindTextureByPattern("basecolor", "color", "diffuse", "albedo");
            if (normalTex == null)
                normalTex = FindTextureByPattern("normal");
            if (metallicTex == null)
                metallicTex = FindTextureByPattern("metallic");
            // URP/Lit packs roughness into the A channel of _MetallicGlossMap;
            // a separate roughness PNG can't be wired in directly here.

            // Normal maps need the importer flag flipped so URP samples them
            // through the right channel decoder.
            MarkAsNormalMap(normalTex);

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                // Capture color tint before swapping shader so we keep authored
                // multiplier (often pure white but defensive).
                Color tint = TryGetColor(mat, "_Color", "_BaseColor", Color.white);

                if (mat.shader != urpLit) mat.shader = urpLit;

                if (albedoTex != null) mat.SetTexture("_BaseMap", albedoTex);
                mat.SetColor("_BaseColor", tint);
                if (normalTex != null)
                {
                    mat.SetTexture("_BumpMap", normalTex);
                    mat.EnableKeyword("_NORMALMAP");
                }
                if (metallicTex != null)
                {
                    mat.SetTexture("_MetallicGlossMap", metallicTex);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                // Tencent's chibi reads matte-soft; bias smoothness low and
                // metallic to zero (overrides any leftover Standard-shader values).
                if (mat.HasProperty("_Smoothness"))  mat.SetFloat("_Smoothness",  0.30f);
                if (mat.HasProperty("_Metallic"))    mat.SetFloat("_Metallic",    0f);

                EditorUtility.SetDirty(mat);
                converted++;
            }
            string boundList = $"albedo={(albedoTex?.name ?? "(none)")}, normal={(normalTex?.name ?? "(none)")}, metallic={(metallicTex?.name ?? "(none)")}";
            Debug.Log($"[CharacterAnimatorSetup] Converted {converted} materials to URP/Lit. Bound: {boundList}");
        }

        static Texture2D FindTextureByPattern(params string[] keywords)
        {
            // First pass: name contains any keyword (case-insensitive).
            // Skips Tripo's leftover `cartooncharacter3dmodel_*` textures so a
            // different model's UVs don't accidentally win the match.
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (lower.StartsWith("cartooncharacter3dmodel")) continue;
                foreach (var kw in keywords)
                {
                    if (lower.Contains(kw))
                    {
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    }
                }
            }
            return null;
        }

        // Tencent Hunyuan3D names its 4-PNG set as
        //     texture_pbr_<id>.png            (basecolor — no suffix!)
        //     texture_pbr_<id>_normal.png
        //     texture_pbr_<id>_metallic.png
        //     texture_pbr_<id>_roughness.png
        // The basecolor having no descriptive suffix means a generic keyword
        // search misses it. This method finds the family explicitly.
        static void FindTencentTextureSet(out Texture2D albedo, out Texture2D normal, out Texture2D metallic)
        {
            albedo = null; normal = null; metallic = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(p);
                string lower = name.ToLowerInvariant();
                if (!lower.StartsWith("texture_pbr")) continue;

                if (lower.EndsWith("_normal"))        normal   = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                else if (lower.EndsWith("_metallic")) metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                else if (lower.EndsWith("_roughness")) { /* URP packs into MetallicGloss A */ }
                else                                   albedo   = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
        }

        static void MarkAsNormalMap(Texture2D tex)
        {
            if (tex == null) return;
            string p = AssetDatabase.GetAssetPath(tex);
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) return;
            if (ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }

        static Texture TryGetTexture(Material mat, params string[] propertyNames)
        {
            foreach (var p in propertyNames)
            {
                if (mat.HasProperty(p))
                {
                    var t = mat.GetTexture(p);
                    if (t != null) return t;
                }
            }
            return null;
        }

        static Color TryGetColor(Material mat, string p1, string p2, Color fallback)
        {
            if (mat.HasProperty(p1)) return mat.GetColor(p1);
            if (mat.HasProperty(p2)) return mat.GetColor(p2);
            return fallback;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name   = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        // ─── Importer config ───────────────────────────────────────────────

        static void ConfigureCharacterAsHumanoid(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; dirty = true; }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (!importer.optimizeGameObjects) { importer.optimizeGameObjects = false; dirty = true; }
            // Tencent's chibi is symmetric — let Unity infer T-pose mapping rather than enforcing
            // any particular pose. importBlendShapes left at default (off for our chibi).
            if (dirty) importer.SaveAndReimport();
        }

        static void ConfigureClipAsHumanoid(string fbxPath, bool isLoopable)
        {
            if (!File.Exists(fbxPath))
            {
                Debug.LogWarning($"[CharacterAnimatorSetup] Clip FBX missing: {fbxPath}");
                return;
            }
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; dirty = true; }
            // CreateFromThisModel: each Mixamo clip builds its own avatar from
            // its mixamorig:* bones. Runtime retarget uses Mecanim's abstract
            // humanoid bones — Tencent's character avatar listens on the same
            // abstract bones, so the two ends connect automatically.
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (importer.sourceAvatar != null) { importer.sourceAvatar = null; dirty = true; }

            // Tweak the clip's loop / lock flags so Idle and Walking blend smoothly.
            var clipSettings = importer.defaultClipAnimations;
            if (clipSettings != null && clipSettings.Length > 0)
            {
                for (int i = 0; i < clipSettings.Length; i++)
                {
                    var c = clipSettings[i];
                    if (c.loopTime != isLoopable) { c.loopTime = isLoopable; dirty = true; }
                    // loopPose = "Loop Pose" inspector checkbox: matches first/last
                    // frame so the loop point doesn't snap. Modern Unity name —
                    // older `loopBlend` was removed.
                    if (isLoopable && !c.loopPose) { c.loopPose = true; dirty = true; }
                    // Locking root XZ + Y rotation prevents Mixamo's "in-place" walk from drifting.
                    if (!c.lockRootPositionXZ) { c.lockRootPositionXZ = true; dirty = true; }
                    if (!c.lockRootRotation)   { c.lockRootRotation   = true; dirty = true; }
                    clipSettings[i] = c;
                }
                importer.clipAnimations = clipSettings;
            }

            if (dirty) importer.SaveAndReimport();
        }

        static AnimationClip LoadFirstAnimationClip(string fbxPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        // ─── Controller build ──────────────────────────────────────────────

        static void BuildController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip waveClip)
        {
            // Wipe any previous version so re-running from menu doesn't accumulate
            // state machines or stale parameters.
            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(SpeedParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(WaveParam,  AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var idle = sm.AddState("Idle");
            idle.motion = idleClip;
            idle.writeDefaultValues = true;

            var walk = sm.AddState("Walking");
            walk.motion = walkClip;
            walk.writeDefaultValues = true;

            var wave = sm.AddState("Waving");
            wave.motion = waveClip;
            wave.writeDefaultValues = true;

            sm.defaultState = idle;

            // Idle → Walking
            var i2w = idle.AddTransition(walk);
            i2w.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            i2w.hasExitTime = false;
            i2w.duration    = 0.10f;

            // Walking → Idle
            var w2i = walk.AddTransition(idle);
            w2i.AddCondition(AnimatorConditionMode.Less, IdleSpeedThreshold, SpeedParam);
            w2i.hasExitTime = false;
            w2i.duration    = 0.10f;

            // Any State → Waving (immediate, ignores Walking/Idle)
            var anyToWave = sm.AddAnyStateTransition(wave);
            anyToWave.AddCondition(AnimatorConditionMode.If, 0f, WaveParam);
            anyToWave.duration = 0.10f;
            anyToWave.canTransitionToSelf = false;

            // Waving → Idle (auto on completion; controller resolves Walking next
            // frame if Speed is still high).
            var waveToIdle = wave.AddTransition(idle);
            waveToIdle.hasExitTime = true;
            waveToIdle.exitTime    = 0.95f;
            waveToIdle.duration    = 0.20f;

            EditorUtility.SetDirty(controller);
        }
    }
}
