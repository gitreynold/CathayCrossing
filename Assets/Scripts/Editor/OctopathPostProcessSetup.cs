using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CathayCrossing.HD2D.EditorTools
{
    public static class OctopathPostProcessSetup
    {
        public const string ProfilePath = "Assets/Settings/OctopathHD2D_Profile.asset";

        public static VolumeProfile CreateOrUpdateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                for (int i = profile.components.Count - 1; i >= 0; i--)
                {
                    var c = profile.components[i];
                    if (c != null) Object.DestroyImmediate(c, true);
                }
                profile.components.Clear();
            }

            // Bloom — soft glow on highlights, signature HD-2D look
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.4f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.95f, 0.85f, 1f));

            // Depth of Field — Bokeh, focuses mid plane, blurs near & far for diorama feel
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(14f);
            dof.focalLength.Override(85f);
            dof.aperture.Override(4.5f);
            dof.bladeCount.Override(6);

            // Vignette — gentle darkening at edges
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.05f, 0.03f, 0.08f, 1f));

            // Color Adjustments — slight warm tone, contrast bump
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
            color.colorFilter.Override(new Color(1.0f, 0.97f, 0.92f, 1f));

            // White balance — warmer overall
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(8f);
            wb.tint.Override(-3f);

            // Tonemapping — ACES for cinematic feel
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            // Film grain — subtle
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.18f);
            grain.response.Override(0.8f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
