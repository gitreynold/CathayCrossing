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
    ///  1. Reimports <c>men_rigged.fbx</c> as Humanoid and bakes a fresh Avatar
    ///     out of its skeleton.
    ///  2. Reimports each Mixamo clip FBX (Without Skin) as Humanoid. Each clip
    ///     builds its own Avatar from its mixamorig:* bones — Unity retargets
    ///     through Mecanim's abstract humanoid bones at runtime.
    ///  3. Builds <c>PlayerAnimator.controller</c> with three states wired to
    ///     match the original Speed-float design plus a new Running state:
    ///        Idle ↔ Walking : gated by `Speed` (float) crossing the
    ///                         IdleSpeedThreshold (original behaviour).
    ///        Walking ↔ Running and Idle ↔ Running : gated by `IsRunning` (bool),
    ///                         set true while the player holds Shift while
    ///                         moving with WASD / arrow keys.
    ///
    ///     Walk and Run are two distinct states playing two distinct Mixamo
    ///     clips, so the visible difference is unambiguous — no speed-blend.
    ///
    /// Idempotent — running it again just overwrites the controller in place.
    /// </summary>
    public static class CharacterAnimatorSetup
    {
        const string CharacterFbx   = "Assets/Resources/Characters/men_rigged.fbx";
        const string AnimDir        = "Assets/Resources/Characters/Animations";
        const string IdleClipFile   = "Happy Idle.fbx";
        const string WalkClipFile   = "Walking.fbx";
        const string RunClipFile    = "Running.fbx";
        const string WaveClipFile   = "Waving.fbx";
        const string ControllerPath = "Assets/Resources/Characters/PlayerAnimator.controller";
        const string TexturesDir    = "Assets/Resources/Characters/Textures";
        const string MaterialsDir   = "Assets/Resources/Characters/Materials";

        const string SpeedParam     = "Speed";
        const string IsRunningParam = "IsRunning";
        const string WaveParam      = "Wave";

        // Same threshold as the original setup — controller velocity smoothing
        // lingers briefly after movement stops, so we drop a tick later.
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
            //    the material to URP/Lit so the chibi doesn't render flat grey.
            //    Idempotent — running again is a no-op once extracted.
            ExtractAndConvertCharacterMaterial(CharacterFbx);

            // 1) Character → Humanoid, build its OWN avatar.
            ConfigureCharacterAsHumanoid(CharacterFbx);

            // 2) Mixamo clips → Humanoid, each builds its OWN avatar from its
            //    "mixamorig:*" bone names. Runtime retargeting uses Mecanim's
            //    abstract humanoid bones, so Tencent ↔ Mixamo connect via the
            //    shared abstract rig.
            string idlePath = Path.Combine(AnimDir, IdleClipFile);
            string walkPath = Path.Combine(AnimDir, WalkClipFile);
            string runPath  = Path.Combine(AnimDir, RunClipFile);
            string wavePath = Path.Combine(AnimDir, WaveClipFile);

            ConfigureClipAsHumanoid(idlePath, isLoopable: true);
            ConfigureClipAsHumanoid(walkPath, isLoopable: true);
            ConfigureClipAsHumanoid(runPath,  isLoopable: true);
            // Wave is a one-shot greeting, not looping — Animator returns to
            // Idle automatically via exit-time.
            ConfigureClipAsHumanoid(wavePath, isLoopable: false);

            var idleClip = LoadFirstAnimationClip(idlePath);
            var walkClip = LoadFirstAnimationClip(walkPath);
            var runClip  = LoadFirstAnimationClip(runPath);
            var waveClip = LoadFirstAnimationClip(wavePath);

            if (idleClip == null || walkClip == null || runClip == null || waveClip == null)
            {
                Debug.LogError($"[CharacterAnimatorSetup] Couldn't find AnimationClip in one of: {idlePath}, {walkPath}, {runPath}, {wavePath}");
                return;
            }

            // 3) Build (or rebuild) the AnimatorController
            BuildController(idleClip, walkClip, runClip, waveClip);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CharacterAnimatorSetup] Done.\n" +
                      $"  Character: {CharacterFbx}\n" +
                      $"  Controller: {ControllerPath}\n" +
                      $"  Clips: Idle={idleClip.name}, Walk={walkClip.name}, Run={runClip.name}, Wave={waveClip.name}");
        }

        // ─── Texture / material extraction (URP) ───────────────────────────

        static void ExtractAndConvertCharacterMaterial(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            EnsureFolder(TexturesDir);
            EnsureFolder(MaterialsDir);

            // ExtractTextures returns false when there's nothing left to extract,
            // not an error. We don't gate on the bool.
            importer.ExtractTextures(TexturesDir);
            AssetDatabase.Refresh();

            // Pull every embedded material out into Materials/ as standalone .mat.
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

            // Tencent Hunyuan3D file pattern: `texture_pbr_<id>.png` (basecolor,
            // no suffix), `*_normal.png`, `*_metallic.png`, `*_roughness.png`.
            // Generic keyword search misses the basecolor (no descriptive suffix),
            // so try the named family first and fall back to keywords.
            Texture2D albedoTex, normalTex, metallicTex;
            FindTencentTextureSet(out albedoTex, out normalTex, out metallicTex);
            if (albedoTex == null)
                albedoTex = FindTextureByPattern("basecolor", "color", "diffuse", "albedo");
            if (normalTex == null)
                normalTex = FindTextureByPattern("normal");
            if (metallicTex == null)
                metallicTex = FindTextureByPattern("metallic");

            MarkAsNormalMap(normalTex);

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

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
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
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
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (importer.sourceAvatar != null) { importer.sourceAvatar = null; dirty = true; }

            var clipSettings = importer.defaultClipAnimations;
            if (clipSettings != null && clipSettings.Length > 0)
            {
                for (int i = 0; i < clipSettings.Length; i++)
                {
                    var c = clipSettings[i];
                    if (c.loopTime != isLoopable) { c.loopTime = isLoopable; dirty = true; }
                    if (isLoopable && !c.loopPose) { c.loopPose = true; dirty = true; }
                    // Lock root XZ + Y rotation so Mixamo's in-place clip doesn't drift.
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

        // State graph — four states, three parameters:
        //
        //                       Speed > 0.1
        //                       & !IsRunning
        //   ┌────────┐  ────────────────────────►  ┌──────────┐
        //   │  Idle  │                              │  Walking │
        //   │        │  ◄────────────────────────  │          │
        //   └────────┘   Speed < 0.1               └──────────┘
        //        │                                       │
        //        │  Speed > 0.1                          │  IsRunning
        //        │  & IsRunning                          │
        //        ▼                                       ▼
        //   ┌──────────┐  ◄──── !IsRunning & Speed>0.1 ──┘
        //   │  Running │
        //   └──────────┘  ────► Speed < 0.1 ────► Idle
        //
        //        Any State ────── Wave trigger ──────► ┌──────────┐
        //                                              │  Waving  │
        //                              exit @ 0.95 ◄── │  one-shot│
        //                                              └──────────┘
        //                                                    │
        //                                                    ▼
        //                                                  Idle
        //
        // Original behaviour preserved: Speed alone drives Idle ↔ Walking.
        // New behaviour: IsRunning bool (set by Shift while moving) escalates
        // Walking → Running, or skips straight from Idle → Running when the
        // player holds Shift before pressing WASD / arrow keys. Wave trigger
        // interrupts anything for a one-shot greeting. Movement is suppressed
        // by the runtime controller while the Waving state is current.
        static void BuildController(AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip, AnimationClip waveClip)
        {
            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(SpeedParam,     AnimatorControllerParameterType.Float);
            controller.AddParameter(IsRunningParam, AnimatorControllerParameterType.Bool);
            controller.AddParameter(WaveParam,      AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var idle = sm.AddState("Idle");
            idle.motion = idleClip;
            idle.writeDefaultValues = true;

            var walk = sm.AddState("Walking");
            walk.motion = walkClip;
            walk.writeDefaultValues = true;

            var run = sm.AddState("Running");
            run.motion = runClip;
            run.writeDefaultValues = true;

            var wave = sm.AddState("Waving");
            wave.motion = waveClip;
            wave.writeDefaultValues = true;

            sm.defaultState = idle;

            const float kBlend = 0.10f;

            // Idle → Walking (original Speed-float behaviour, but only when
            // Shift isn't held — otherwise we go straight to Running below).
            var idleToWalk = idle.AddTransition(walk);
            idleToWalk.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            idleToWalk.AddCondition(AnimatorConditionMode.IfNot,   0f,                 IsRunningParam);
            idleToWalk.hasExitTime = false;
            idleToWalk.duration    = kBlend;

            // Idle → Running (Shift held + WASD/arrows pressed from a standstill)
            var idleToRun = idle.AddTransition(run);
            idleToRun.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            idleToRun.AddCondition(AnimatorConditionMode.If,      0f,                 IsRunningParam);
            idleToRun.hasExitTime = false;
            idleToRun.duration    = kBlend;

            // Walking → Idle (original)
            var walkToIdle = walk.AddTransition(idle);
            walkToIdle.AddCondition(AnimatorConditionMode.Less, IdleSpeedThreshold, SpeedParam);
            walkToIdle.hasExitTime = false;
            walkToIdle.duration    = kBlend;

            // Walking → Running (press Shift while already walking)
            var walkToRun = walk.AddTransition(run);
            walkToRun.AddCondition(AnimatorConditionMode.If, 0f, IsRunningParam);
            walkToRun.hasExitTime = false;
            walkToRun.duration    = kBlend;

            // Running → Walking (release Shift, still moving)
            var runToWalk = run.AddTransition(walk);
            runToWalk.AddCondition(AnimatorConditionMode.IfNot,   0f,                 IsRunningParam);
            runToWalk.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            runToWalk.hasExitTime = false;
            runToWalk.duration    = kBlend;

            // Running → Idle (player stopped completely — drop straight to Idle
            // whether Shift is still held or not).
            var runToIdle = run.AddTransition(idle);
            runToIdle.AddCondition(AnimatorConditionMode.Less, IdleSpeedThreshold, SpeedParam);
            runToIdle.hasExitTime = false;
            runToIdle.duration    = kBlend;

            // Any State → Waving (H key sets the Wave trigger). canTransitionToSelf
            // is false so spamming H during a wave doesn't restart from frame 0.
            var anyToWave = sm.AddAnyStateTransition(wave);
            anyToWave.AddCondition(AnimatorConditionMode.If, 0f, WaveParam);
            anyToWave.duration            = kBlend;
            anyToWave.hasExitTime         = false;
            anyToWave.canTransitionToSelf = false;

            // Waving → Idle on completion. Animator resolves Walking/Running
            // next frame if the player is still moving (movement is locked
            // during the wave, but the player can press a direction the same
            // frame the wave ends).
            var waveToIdle = wave.AddTransition(idle);
            waveToIdle.hasExitTime = true;
            waveToIdle.exitTime    = 0.95f;
            waveToIdle.duration    = 0.20f;

            EditorUtility.SetDirty(controller);
        }
    }
}
