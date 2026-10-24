using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace WheelchairSonification.Editor
{
    public static class XrResources
    {
        [MenuItem("Experiment/Prepare XR resources")]
        public static void Prepare()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null)
            {
                throw new InvalidOperationException("Git管理されたTMP Essential Resourcesがありません。Assets/TextMesh Proを復元してください。");
            }
            const string path = "Assets/Experiment/Resources/XrJapanese.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null)
            {
                var source = Resources.Load<Font>("DemoJapanese");
                var font = TMP_FontAsset.CreateFontAsset(source, 80, 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (font == null) throw new InvalidOperationException("日本語SDFフォントを生成できません。");
                font.name = "XrJapanese";
                AssetDatabase.CreateAsset(font, path);
                // Prewarm actual interface strings; keep dynamic multi-atlas fallback for runtime messages.
                string text = string.Join("", Directory.GetFiles("Assets/Experiment", "*.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText));
                string characters = new string(text.Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).Distinct().ToArray());
                font.TryAddCharacters(characters, out string missing);
                foreach (var texture in font.atlasTextures) if (texture != null && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
                if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
                EditorUtility.SetDirty(font);
            }
            AssetDatabase.SaveAssets();
            ParticipantUiAssets.Prepare();
            FocusVisionProject.Configure();
            Debug.Log("XR resources prepared.");
        }
    }
}
