using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CathayCrossing.HD2D.EditorTools
{
    /// <summary>
    /// Per-character animator wiring. Invoked from
    /// <see cref="CustomizeSceneSetup.ImportPartials"/> via the public
    /// <see cref="SetupCharacterByName"/> entry point — one call per
    /// imported partial FBX (Default3D / JayPartial / Style3).
    ///
    /// For each character it:
    ///  1. Reimports its rigged FBX as Humanoid (its own Avatar).
    ///  2. Reimports each Mixamo clip FBX as Humanoid (each builds its own
    ///     avatar from "mixamorig:*" bones; Mecanim retargets via the abstract
    ///     humanoid rig at runtime).
    ///  3. Pulls embedded textures/materials out into per-character
    ///     Textures/ and Materials/ folders and rebinds them to URP/Lit.
    ///  4. Builds a per-character <c>PlayerAnimator.controller</c> with five
    ///     states (Idle / Walking / Running / Waving / Dance) and four
    ///     parameters (Speed / IsRunning / Wave / Dance).
    ///
    /// Idempotent — running again overwrites the controller in place.
    ///
    /// Folder convention every character must follow:
    ///   Assets/Resources/Characters/&lt;Name&gt;/
    ///     &lt;MeshFile&gt;.fbx
    ///     PlayerAnimator.controller   (generated)
    ///     Materials/    (extracted)
    ///     Textures/     (extracted)
    ///
    /// Animation clips are SHARED across characters (Mecanim Humanoid retargets
    /// any clip onto any humanoid avatar) and live at:
    ///   Assets/Game/Characters/Animations/
    ///     Happy Idle.fbx
    ///     Walking.fbx
    ///     Running.fbx
    ///     Waving.fbx
    ///     Dance.fbx
    /// </summary>
    public static class CharacterAnimatorSetup
    {
        // Per-character config so adding a third one is just one new entry.
        struct CharacterConfig
        {
            public string Name;       // folder name under Resources/Characters/
            public string MeshFile;   // FBX filename inside that folder
        }

        const string IdleClipFile   = "Happy Idle.fbx";
        const string WalkClipFile   = "Walking.fbx";
        const string RunClipFile    = "Running.fbx";
        const string WaveClipFile   = "Waving.fbx";
        const string DanceClipFile  = "Dance.fbx";

        // Mecanim Humanoid retargets a single clip onto any humanoid avatar, so
        // every character shares one animation set. Curated picks (per request):
        //   Dance / Walking / Running / Waving → from Default's Mixamo export
        //   Happy Idle                          → from Jay's export
        // Kept outside Resources/ so the FBX files don't auto-bundle — clips are
        // pulled into builds via the PlayerAnimator.controller's GUID references.
        const string SharedAnimDir = "Assets/Game/Characters/Animations";

        const string SpeedParam     = "Speed";
        const string IsRunningParam = "IsRunning";
        const string WaveParam      = "Wave";
        const string DanceParam     = "Dance";

        const float IdleSpeedThreshold = 0.1f;

        // ─── Public entry point ─────────────────────────────────────────────

        // Called by CustomizeSceneSetup.ImportPartials for each FBX it
        // copies into Resources/Characters/<folderName>/<meshFileName>.
        // The folder/file naming convention is the same one SetupCharacter
        // expects.
        public static void SetupCharacterByName(string folderName, string meshFileName)
        {
            SetupCharacter(new CharacterConfig { Name = folderName, MeshFile = meshFileName });
        }

        // ─── Per-character pipeline ────────────────────────────────────────

        static void SetupCharacter(CharacterConfig cfg)
        {
            string charDir       = $"Assets/Resources/Characters/{cfg.Name}";
            string characterFbx  = $"{charDir}/{cfg.MeshFile}";
            string animDir       = SharedAnimDir;
            string controllerPath= $"{charDir}/PlayerAnimator.controller";
            string texturesDir   = $"{charDir}/Textures";
            string materialsDir  = $"{charDir}/Materials";

            if (!File.Exists(characterFbx))
            {
                Debug.LogError($"[CharacterAnimatorSetup] '{cfg.Name}' FBX missing at {characterFbx}");
                return;
            }

            ExtractAndConvertCharacterMaterial(characterFbx, texturesDir, materialsDir);
            ConfigureCharacterAsHumanoid(characterFbx);

            string idlePath  = Path.Combine(animDir, IdleClipFile);
            string walkPath  = Path.Combine(animDir, WalkClipFile);
            string runPath   = Path.Combine(animDir, RunClipFile);
            string wavePath  = Path.Combine(animDir, WaveClipFile);
            string dancePath = Path.Combine(animDir, DanceClipFile);

            ConfigureClipAsHumanoid(idlePath,  isLoopable: true);
            ConfigureClipAsHumanoid(walkPath,  isLoopable: true);
            ConfigureClipAsHumanoid(runPath,   isLoopable: true);
            // Wave and Dance are one-shot — Animator exits to Idle via exit-time.
            ConfigureClipAsHumanoid(wavePath,  isLoopable: false);
            ConfigureClipAsHumanoid(dancePath, isLoopable: false);

            var idleClip  = LoadFirstAnimationClip(idlePath);
            var walkClip  = LoadFirstAnimationClip(walkPath);
            var runClip   = LoadFirstAnimationClip(runPath);
            var waveClip  = LoadFirstAnimationClip(wavePath);
            var danceClip = LoadFirstAnimationClip(dancePath);

            if (idleClip == null || walkClip == null || runClip == null || waveClip == null || danceClip == null)
            {
                Debug.LogError($"[CharacterAnimatorSetup] '{cfg.Name}' missing a clip in {animDir}.");
                return;
            }

            BuildController(controllerPath, idleClip, walkClip, runClip, waveClip, danceClip);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CharacterAnimatorSetup] '{cfg.Name}' done.\n" +
                      $"  Character: {characterFbx}\n" +
                      $"  Controller: {controllerPath}\n" +
                      $"  Clips: Idle={idleClip.name}, Walk={walkClip.name}, Run={runClip.name}, Wave={waveClip.name}, Dance={danceClip.name}");
        }

        // ─── Texture / material extraction (URP) ───────────────────────────

        static void ExtractAndConvertCharacterMaterial(string fbxPath, string texturesDir, string materialsDir)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            EnsureFolder(texturesDir);
            EnsureFolder(materialsDir);

            // ExtractTextures returns false when there's nothing left to extract,
            // not an error. We don't gate on the bool.
            importer.ExtractTextures(texturesDir);
            AssetDatabase.Refresh();

            int extracted = 0;
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (sub is Material mat && AssetDatabase.IsSubAsset(mat))
                {
                    string targetPath = AssetDatabase.GenerateUniqueAssetPath($"{materialsDir}/{mat.name}.mat");
                    string err = AssetDatabase.ExtractAsset(mat, targetPath);
                    if (string.IsNullOrEmpty(err)) extracted++;
                }
            }

            if (extracted > 0)
            {
                AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            }

            ConvertExtractedMaterialsToUrp(texturesDir, materialsDir);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void ConvertExtractedMaterialsToUrp(string texturesDir, string materialsDir)
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogWarning("[CharacterAnimatorSetup] URP/Lit shader missing — leaving materials on their original shader.");
                return;
            }

            // Tencent Hunyuan3D file pattern: `texture_pbr_<id>.png` (basecolor,
            // no suffix), `*_normal.png`, `*_metallic.png`, `*_roughness.png`.
            Texture2D albedoTex, normalTex, metallicTex;
            FindTencentTextureSet(texturesDir, out albedoTex, out normalTex, out metallicTex);
            if (albedoTex == null)   albedoTex   = FindTextureByPattern(texturesDir, "basecolor", "color", "diffuse", "albedo");
            if (normalTex == null)   normalTex   = FindTextureByPattern(texturesDir, "normal");
            if (metallicTex == null) metallicTex = FindTextureByPattern(texturesDir, "metallic");

            MarkAsNormalMap(normalTex);
            // Metallic / roughness are data, not color — sampling them as sRGB
            // bends the values through gamma and produces splotchy speculars
            // (the "scratch marks" all over the chibi body during runtime).
            MarkAsLinearData(metallicTex);
            // Roughness sits in a separate file Tencent ships; nothing in
            // URP/Lit reads it directly but we still want it linear in case
            // someone wires it up later.
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texturesDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p).ToLowerInvariant().EndsWith("_roughness"))
                    MarkAsLinearData(AssetDatabase.LoadAssetAtPath<Texture2D>(p));
            }

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { materialsDir }))
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
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.30f);
                if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);

                EditorUtility.SetDirty(mat);
                converted++;
            }
            string boundList = $"albedo={(albedoTex?.name ?? "(none)")}, normal={(normalTex?.name ?? "(none)")}, metallic={(metallicTex?.name ?? "(none)")}";
            Debug.Log($"[CharacterAnimatorSetup] Converted {converted} material(s) in {materialsDir} to URP/Lit. Bound: {boundList}");
        }

        static Texture2D FindTextureByPattern(string texturesDir, params string[] keywords)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texturesDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                foreach (var kw in keywords)
                {
                    if (lower.Contains(kw))
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
            }
            return null;
        }

        static void FindTencentTextureSet(string texturesDir, out Texture2D albedo, out Texture2D normal, out Texture2D metallic)
        {
            albedo = null; normal = null; metallic = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texturesDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(p);
                string lower = name.ToLowerInvariant();
                if (!lower.StartsWith("texture_pbr")) continue;

                if (lower.EndsWith("_normal"))        normal   = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                else if (lower.EndsWith("_metallic")) metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                else if (lower.EndsWith("_roughness")) { /* packed into MetallicGloss A */ }
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

        static void MarkAsLinearData(Texture2D tex)
        {
            if (tex == null) return;
            string p = AssetDatabase.GetAssetPath(tex);
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) return;
            if (ti.sRGBTexture)
            {
                ti.sRGBTexture = false;
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

        // 5 states, 4 parameters — identical graph shape for every character.
        // See README block at the top of this file for the transition map.
        static void BuildController(string controllerPath, AnimationClip idleClip, AnimationClip walkClip, AnimationClip runClip, AnimationClip waveClip, AnimationClip danceClip)
        {
            if (File.Exists(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter(SpeedParam,     AnimatorControllerParameterType.Float);
            controller.AddParameter(IsRunningParam, AnimatorControllerParameterType.Bool);
            controller.AddParameter(WaveParam,      AnimatorControllerParameterType.Trigger);
            controller.AddParameter(DanceParam,     AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var idle  = sm.AddState("Idle");    idle.motion  = idleClip;  idle.writeDefaultValues  = true;
            var walk  = sm.AddState("Walking"); walk.motion  = walkClip;  walk.writeDefaultValues  = true;
            var run   = sm.AddState("Running"); run.motion   = runClip;   run.writeDefaultValues   = true;
            var wave  = sm.AddState("Waving");  wave.motion  = waveClip;  wave.writeDefaultValues  = true;
            var dance = sm.AddState("Dance");   dance.motion = danceClip; dance.writeDefaultValues = true;

            sm.defaultState = idle;

            const float kBlend = 0.10f;

            // Idle → Walking (Speed up, Shift not held)
            var idleToWalk = idle.AddTransition(walk);
            idleToWalk.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            idleToWalk.AddCondition(AnimatorConditionMode.IfNot,   0f,                 IsRunningParam);
            idleToWalk.hasExitTime = false;
            idleToWalk.duration    = kBlend;

            // Idle → Running (Shift + WASD from a standstill)
            var idleToRun = idle.AddTransition(run);
            idleToRun.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            idleToRun.AddCondition(AnimatorConditionMode.If,      0f,                 IsRunningParam);
            idleToRun.hasExitTime = false;
            idleToRun.duration    = kBlend;

            // Walking → Idle
            var walkToIdle = walk.AddTransition(idle);
            walkToIdle.AddCondition(AnimatorConditionMode.Less, IdleSpeedThreshold, SpeedParam);
            walkToIdle.hasExitTime = false;
            walkToIdle.duration    = kBlend;

            // Walking → Running
            var walkToRun = walk.AddTransition(run);
            walkToRun.AddCondition(AnimatorConditionMode.If, 0f, IsRunningParam);
            walkToRun.hasExitTime = false;
            walkToRun.duration    = kBlend;

            // Running → Walking
            var runToWalk = run.AddTransition(walk);
            runToWalk.AddCondition(AnimatorConditionMode.IfNot,   0f,                 IsRunningParam);
            runToWalk.AddCondition(AnimatorConditionMode.Greater, IdleSpeedThreshold, SpeedParam);
            runToWalk.hasExitTime = false;
            runToWalk.duration    = kBlend;

            // Running → Idle
            var runToIdle = run.AddTransition(idle);
            runToIdle.AddCondition(AnimatorConditionMode.Less, IdleSpeedThreshold, SpeedParam);
            runToIdle.hasExitTime = false;
            runToIdle.duration    = kBlend;

            // Any State → Waving (H trigger)
            var anyToWave = sm.AddAnyStateTransition(wave);
            anyToWave.AddCondition(AnimatorConditionMode.If, 0f, WaveParam);
            anyToWave.duration            = kBlend;
            anyToWave.hasExitTime         = false;
            anyToWave.canTransitionToSelf = false;

            // Waving → Idle on completion
            var waveToIdle = wave.AddTransition(idle);
            waveToIdle.hasExitTime = true;
            waveToIdle.exitTime    = 0.95f;
            waveToIdle.duration    = 0.20f;

            // Any State → Dance (F trigger)
            var anyToDance = sm.AddAnyStateTransition(dance);
            anyToDance.AddCondition(AnimatorConditionMode.If, 0f, DanceParam);
            anyToDance.duration            = kBlend;
            anyToDance.hasExitTime         = false;
            anyToDance.canTransitionToSelf = false;

            // Dance → Idle on completion
            var danceToIdle = dance.AddTransition(idle);
            danceToIdle.hasExitTime = true;
            danceToIdle.exitTime    = 0.95f;
            danceToIdle.duration    = 0.25f;

            EditorUtility.SetDirty(controller);
        }
    }
}
