#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;
namespace WheelchairSonification.Editor
{
    public sealed class XrPreviewRunner : MonoBehaviour
    {
        public string OutputDirectory; public bool Interactive;
        FocusVisionDemo xr; RenderTexture target;
        readonly List<Shot> shots=new List<Shot>(); readonly List<string> errors=new List<string>();
        [Serializable] sealed class Shot { public string name,file,title,crop_title,crop_gauge,crop_button; public float assumed_vertical_fov_degrees; public bool light_background,intentional_world_panel_offscreen; }
        [Serializable] sealed class Manifest { public bool passed,physical_device_measured=false; public string unity_version,source_commit; public Shot[] shots; public string[] errors; }
        [Serializable] sealed class PreparationFixtures { public string provenance="Generated from PreparationWorkflow.Preview; synthetic UI states, not device measurements";public PreparationSnapshot[] stages; }
        IEnumerator Start()
        {
            yield return null; yield return null;
            xr=FindAnyObjectByType<FocusVisionDemo>();if(xr==null){EditorApplication.Exit(1);yield break;}xr.EnableEditorPreview();
            if(Interactive)
            {
                gameObject.AddComponent<EditorXrSimulator>().Initialize(xr.Rig.Camera.transform);
                xr.Link.EndpointOverride="http://127.0.0.1:8765";xr.Link.Begin();
                if(Environment.GetEnvironmentVariable("WHEELCHAIR_INTERACTIVE_SMOKE")=="1"){for(int i=0;i<12;i++)yield return null;EditorApplication.Exit(xr.Ready?0:1);}yield break;
            }
            Directory.CreateDirectory(OutputDirectory);AudioListener.volume=0;
            xr.enabled=false; // Fixed visual fixtures use the same instantiated prefab without runtime state overwriting each frame.
            QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            xr.Rig.Camera.aspect=1;target=new RenderTexture(1440,1440,24,RenderTextureFormat.ARGB32){antiAliasing=4};target.Create();
            yield return null;
            try
            {
                int number=0;
                foreach(float fov in new[]{70f,90f})foreach(bool light in new[]{true,false})
                {
                    xr.Rig.Camera.fieldOfView=fov;xr.Rig.Camera.clearFlags=CameraClearFlags.SolidColor;xr.Rig.Camera.backgroundColor=light?new Color(.75f,.76f,.78f):new Color(.12f,.13f,.15f);
                    xr.Gauge.SetVisible(false);xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Home"));ShotImage($"{++number:00}-pairing", "起動・単体チェックと接続番号");
                    xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Waiting"));ShotImage($"{++number:00}-waiting", "停止中・短文だけ");
                    xr.ShowParticipantPreview("",false);xr.Gauge.SetValues(.33,.67);xr.Gauge.SetVisible(true);ShotImage($"{++number:00}-bilateral", "左右の速度差・視覚訓練");
                    xr.Gauge.SetVisible(false);ShotImage($"{++number:00}-no-feedback", "聴覚・追加FBなし・評価中の無表示");
                }
                xr.Rig.Camera.fieldOfView=70;xr.Gauge.SetVisible(true);xr.Gauge.SetValues(1,0);ShotImage("17-left-only","左だけ・表示上限");
                xr.Gauge.SetValues(0,1);ShotImage("18-right-only","右だけ・表示上限");
                xr.Gauge.SetValues(.6,.4);xr.Rig.Camera.transform.rotation=Quaternion.Euler(0,60,0);ShotImage("19-head-turned","頭部回転後・下部の1枚だけ");
                xr.Recenter();ShotImage("20-recentered","再配置後");
                xr.Gauge.SetValues(0,0);ShotImage("21-stopped","停止付近・両方ゼロ");
                xr.Gauge.SetVisible(false);ShotImage("22-missing","車輪・車体の欠測時・提示なし");
                xr.Rig.Camera.transform.rotation=Quaternion.identity;xr.PreparationUi.Recenter();
                var stages=new[]{"Home","Still","ForwardReady","Forward","BackwardReady","Candidate","Manual","WristsChoice","VerifyReady","VerifyLeft","VisualAnswer","AudioAnswer","Paused","Result","Waiting"};
                var fixtures=stages.Select(PreparationWorkflow.Preview).ToArray();
                File.WriteAllText(Path.Combine(OutputDirectory,"preparation-fixtures.json"),JsonUtility.ToJson(new PreparationFixtures{stages=fixtures},true));
                int index=23;
                foreach(var stage in fixtures){xr.PreparationUi.ShowPreview(stage);ShotImage($"{index++:00}-prep-{stage.stage.ToLowerInvariant()}","準備："+stage.title);}
                foreach(float fov in new[]{70f,90f})foreach(bool light in new[]{true,false})
                {xr.Rig.Camera.fieldOfView=fov;xr.Rig.Camera.backgroundColor=light?new Color(.75f,.76f,.78f):new Color(.12f,.13f,.15f);xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Home"));ShotImage($"{index++:00}-prep-field","準備画面・視野と背景");}
                xr.Rig.Camera.fieldOfView=70;xr.Rig.Camera.transform.rotation=Quaternion.Euler(0,60,0);ShotImage("42-prep-head-turned","準備パネルは空間に固定・頭部回転後",true);
                xr.PreparationUi.Recenter();ShotImage("43-prep-recentered","準備パネルを正面へ再配置");
                xr.Rig.Camera.transform.rotation=Quaternion.Euler(35,60,0);xr.PreparationUi.Recenter();ShotImage("44-prep-looking-down","下を向いた状態でも視線の正面へ再配置");
                MakeContactSheet();AudioSampleExporter.Export(OutputDirectory);
            }
            catch(Exception e){errors.Add(e.ToString());}
            var m=new Manifest{passed=errors.Count==0,unity_version=Application.unityVersion,source_commit=Environment.GetEnvironmentVariable("WHEELCHAIR_SOURCE_COMMIT")??"unknown",shots=shots.ToArray(),errors=errors.ToArray()};
            File.WriteAllText(Path.Combine(OutputDirectory,"manifest.json"),JsonUtility.ToJson(m,true));RenderTexture.active=null;target.Release();Destroy(target);EditorApplication.Exit(m.passed?0:1);
        }
        void ShotImage(string name,string title,bool intentionalOffscreen=false)
        {
            Canvas.ForceUpdateCanvases();
            foreach(var label in xr.GetComponentsInChildren<TMP_Text>()){label.ForceMeshUpdate();if(label.isTextOverflowing)errors.Add(name+": text overflow "+label.text);}
            var camera=xr.Rig.Camera;
            if(xr.PreparationUi.PanelVisible&&!intentionalOffscreen)
            {
                var corners=new Vector3[4];xr.PreparationUi.Panel.GetWorldCorners(corners);
                foreach(var c in corners){var p=camera.WorldToViewportPoint(c);if(p.z<=0||p.x<.02||p.x>.98||p.y<.02||p.y>.98)errors.Add(name+": preparation panel offscreen");}
            }
            if(xr.Gauge.Visible)
            {
                var corners=new Vector3[4];((RectTransform)xr.Gauge.transform).GetWorldCorners(corners);
                foreach(var c in corners){var p=camera.WorldToViewportPoint(c);if(p.z<=0||p.x<.02||p.x>.98||p.y<.02||p.y>.98)errors.Add(name+": gauge offscreen");}
            }
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};RenderPipeline.SubmitRenderRequest(camera,request);
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1440,1440,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,1440),0,0);image.Apply();RenderTexture.active=previous;
            var shot=new Shot{name=name,file=name+".png",title=title,assumed_vertical_fov_degrees=camera.fieldOfView,light_background=camera.backgroundColor.r>.5f,intentional_world_panel_offscreen=intentionalOffscreen};
            File.WriteAllBytes(Path.Combine(OutputDirectory,shot.file),image.EncodeToPNG());
            if(xr.Gauge.Visible)shot.crop_gauge=Crop(image,(RectTransform)xr.Gauge.transform,name+"-gauge.png");
            if(xr.WaitingLabel.gameObject.activeInHierarchy&&!intentionalOffscreen)shot.crop_title=Crop(image,xr.WaitingLabel.rectTransform,name+"-title.png");
            var button=xr.PreparationUi.Panel.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(b=>b.gameObject.activeInHierarchy);
            if(button!=null&&!intentionalOffscreen)shot.crop_button=Crop(image,(RectTransform)button.transform,name+"-button.png");
            Destroy(image);shots.Add(shot);
        }
        private string Crop(Texture2D source, RectTransform rect, string file)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(c => xr.Rig.Camera.WorldToViewportPoint(c)).ToArray();
            int x = Mathf.Clamp(Mathf.FloorToInt(points.Min(p => p.x) * source.width) - 6, 0, source.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(points.Min(p => p.y) * source.height) - 6, 0, source.height - 1);
            int w = Mathf.Min(source.width - x, Mathf.CeilToInt(points.Max(p => p.x) * source.width) + 6 - x);
            int h = Mathf.Min(source.height - y, Mathf.CeilToInt(points.Max(p => p.y) * source.height) + 6 - y);
            if (w <= 0 || h <= 0) throw new InvalidOperationException("切り出し領域が不正です。");
            var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
            crop.SetPixels(source.GetPixels(x, y, w, h)); crop.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, file), crop.EncodeToPNG()); Destroy(crop); return file;
        }

        private void MakeContactSheet()
        {
            const int size = 360, columns = 4;
            int rows = (shots.Count + columns - 1) / columns;
            var sheet = new Texture2D(columns * size, rows * size, TextureFormat.RGB24, false);
            var thumbnailTarget = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            for (int i = 0; i < shots.Count; i++)
            {
                var image = new Texture2D(2, 2); image.LoadImage(File.ReadAllBytes(Path.Combine(OutputDirectory, shots[i].file)));
                Graphics.Blit(image, thumbnailTarget);
                var previous = RenderTexture.active; RenderTexture.active = thumbnailTarget;
                var thumb = new Texture2D(size, size, TextureFormat.RGB24, false); thumb.ReadPixels(new Rect(0, 0, size, size), 0, 0); thumb.Apply();
                RenderTexture.active = previous;
                sheet.SetPixels(i % columns * size, (rows - 1 - i / columns) * size, size, size, thumb.GetPixels());
                Destroy(thumb); Destroy(image);
            }
            sheet.Apply(); File.WriteAllBytes(Path.Combine(OutputDirectory, "contact-sheet.png"), sheet.EncodeToPNG());
            RenderTexture.ReleaseTemporary(thumbnailTarget); Destroy(sheet);
        }
    }
}
#endif
