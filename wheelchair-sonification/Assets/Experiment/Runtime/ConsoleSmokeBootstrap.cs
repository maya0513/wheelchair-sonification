#if !UNITY_ANDROID
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    // Development Linux player only. Android APKs do not contain this entry point.
    public sealed class ConsoleSmokeBootstrap : MonoBehaviour
    {
        private ResearchVisitHost host;
        private OperatorLink link;
        private DemoController app;
        private double cueEnd;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || !Debug.isDebugBuild || Array.IndexOf(Environment.GetCommandLineArgs(), "--research-console-smoke") < 0) return;
            new GameObject("Explicit console integration fixture").AddComponent<ConsoleSmokeBootstrap>();
        }
        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--research-fixture-root");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("実験データと分離したテスト出力先が必要です。");
            string root = args[index + 1]; Directory.CreateDirectory(root);
            Application.runInBackground = true; Application.targetFrameRate = 90;
            app = FindAnyObjectByType<DemoController>();
            app.SaveRootOverride = Path.Combine(root, "DemoSessions"); app.SetPresentationReady(true, "explicit Linux synthetic integration");
            app.View.OperatorPanel.SetActive(false);
            var research = gameObject.AddComponent<ResearchController>(); research.Output = app.Output;
            research.RootOverride = Path.Combine(root, "ResearchSessions"); research.PresentationReady = true;
            host = gameObject.AddComponent<ResearchVisitHost>(); host.RootOverride = Path.Combine(root, "ResearchVisits"); host.Initialize(research);
            host.Present = (group, error) => { app.QuickError(group, .01f, error); cueEnd = Time.realtimeSinceStartupAsDouble + 1; };
            host.StopPresentation = () => { if (app.Running) app.EndSession(); };
            host.SetInput("synthetic");
            link = gameObject.AddComponent<OperatorLink>(); link.Host = host; link.EndpointOverride = "http://127.0.0.1:8765"; link.Begin();
            File.WriteAllText(Path.Combine(root, "connection.json"), "{\"pairCode\":\"" + link.PairingCode + "\",\"synthetic\":true}");
        }
        private void Update()
        {
            if (app != null && app.Running && Time.realtimeSinceStartupAsDouble >= cueEnd) app.EndSession();
        }
    }
}
#endif
