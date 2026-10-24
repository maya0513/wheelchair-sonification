using System.IO;
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
            if (File.Exists(Path)) return; // Preserve intentional Inspector edits.
            var root = XrUi.Canvas("Bilateral speed feedback", null, new Vector2(720,240));
            XrUi.Fill(root,XrUi.Paper);
            var shadow = root.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0,0,0,.2f); shadow.effectDistance = new Vector2(0,-5);
            var border = root.gameObject.AddComponent<Outline>(); border.effectColor = new Color32(160,171,187,255); border.effectDistance = new Vector2(1,-1);
            var g = root.gameObject.AddComponent<XrGauge>();
            for (int i=0; i<2; i++)
            {
                float x = 24 + i * 360;
                XrUi.Text(root,i==0?"Left label":"Right label",i==0?"左":"右",x,4,304,100,44);
                var track = XrUi.Rect(i==0?"Left track":"Right track",root,x,112,304,92); XrUi.Fill(track,new Color32(220,225,233,255));
                var bar = XrUi.Rect(i==0?"Left speed":"Right speed",track,0,0,0,92); XrUi.Fill(bar,XrUi.Blue);
                if (i==0) g.leftFill=bar; else g.rightFill=bar;
            }
            PrefabUtility.SaveAsPrefabAsset(root.gameObject,Path); Object.DestroyImmediate(root.gameObject); AssetDatabase.SaveAssets();
        }
    }
}
