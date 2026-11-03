using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.XR;

namespace WheelchairSonification.Editor
{
    public static class ParticipantUiAssets
    {
        public const string Path = "Assets/Experiment/Resources/BilateralFeedback.prefab";

        [MenuItem("Experiment/Create participant UI prefab")]
        public static void Prepare()
        {
            if (!File.Exists(Path))
                Rebuild();
        }

        public static void ConfigureVerticalFeedback()
        {
            Rebuild();
            FocusVisionProject.Configure();
        }

        [MenuItem("Experiment/Rebuild vertical participant UI prefab")]
        public static void Rebuild()
        {
            // Explicit authoring command; builds preserve Inspector edits to the serialized asset.
            var root = XrUi.Canvas(
                "Bilateral vertical speed feedback",
                null,
                new Vector2(480, 410)
            );
            var gauge = root.gameObject.AddComponent<XrGauge>();
            gauge.fullHeight = 320;
            var settings = gauge.Anchor.Placement;
            float units =
                gauge.fullHeight
                / (
                    2
                    * settings.distanceMeters
                    * Mathf.Tan(settings.barHeightDegrees / 2 * Mathf.Deg2Rad)
                );
            float offset =
                settings.distanceMeters
                * Mathf.Tan(settings.horizontalDegrees * Mathf.Deg2Rad)
                * units;
            float width =
                2
                * settings.distanceMeters
                * Mathf.Tan(settings.barWidthDegrees / 2 * Mathf.Deg2Rad)
                * units;
            for (int i = 0; i < 2; i++)
            {
                float x = 240 + (i == 0 ? -offset : offset) - width / 2;
                var track = XrUi.Rect(
                    i == 0 ? "Left track" : "Right track",
                    root,
                    x,
                    0,
                    width,
                    320
                );
                XrUi.Fill(track, new Color(.15f, .18f, .22f, settings.trackOpacity));
                var fill = XrUi.Rect(
                    i == 0 ? "Left speed" : "Right speed",
                    track,
                    0,
                    320,
                    width,
                    0
                );
                fill.anchorMin = fill.anchorMax = Vector2.zero;
                fill.pivot = Vector2.zero;
                fill.anchoredPosition = Vector2.zero;
                var blue = XrUi.Blue;
                blue.a = settings.fillOpacity;
                XrUi.Fill(fill, blue);
                if (i == 0)
                    gauge.leftFill = fill;
                else
                    gauge.rightFill = fill;
                // A uGUI Outline duplicates the entire translucent quad and makes its interior opaque.
                // Draw actual narrow edges without increasing the configured track opacity.
                foreach (
                    var edge in new[]
                    {
                        new Vector4(-2, -2, width + 4, 2),
                        new Vector4(-2, 320, width + 4, 2),
                        new Vector4(-2, 0, 2, 320),
                        new Vector4(width, 0, 2, 320),
                    }
                )
                    XrUi.Fill(
                        XrUi.Rect("Dark edge", track, edge.x, edge.y, edge.z, edge.w),
                        new Color(0, 0, 0, .9f)
                    );
                foreach (
                    var edge in new[]
                    {
                        new Vector4(0, 0, width, 1),
                        new Vector4(0, 319, width, 1),
                        new Vector4(0, 0, 1, 320),
                        new Vector4(width - 1, 0, 1, 320),
                    }
                )
                    XrUi.Fill(
                        XrUi.Rect("Light edge", track, edge.x, edge.y, edge.z, edge.w),
                        new Color(1, 1, 1, settings.labelOpacity)
                    );
                var label = XrUi.Text(
                    root,
                    i == 0 ? "Left label" : "Right label",
                    i == 0 ? "左" : "右",
                    x - 26,
                    324,
                    width + 52,
                    72,
                    48
                );
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(1, 1, 1, settings.labelOpacity);
            }
            // Origin is the centre of the bar pair, rather than the centre of the labels below it.
            root.pivot = new Vector2(.5f, 1 - 160f / 410);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Path);
            Object.DestroyImmediate(root.gameObject);
            AssetDatabase.SaveAssets();
        }
    }
}
