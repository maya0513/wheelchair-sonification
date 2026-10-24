using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
namespace WheelchairSonification.Runtime
{
    public sealed class DemoController : MonoBehaviour
    {
        [SerializeField] bool externalPresentation;
        public UnityFeedbackOutput Output { get; private set; }
        void Awake()
        {
            Output = gameObject.AddComponent<UnityFeedbackOutput>();
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Input events", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
        void Start()
        {
            if (externalPresentation) return;
            Application.runInBackground = true; Application.targetFrameRate = 90;
            if (Camera.main == null) new GameObject("Desktop camera", typeof(Camera), typeof(AudioListener)).tag = "MainCamera";
            var research = gameObject.AddComponent<ResearchController>(); research.Output = Output; research.PresentationReady = true;
            var host = gameObject.AddComponent<ResearchVisitHost>(); host.Initialize(research); host.SetInput("synthetic");
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--research-fixture-root");
            string root = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            if (root != null) { research.RootOverride = Path.Combine(root, "ResearchSessions"); host.RootOverride = Path.Combine(root, "ResearchVisits"); Directory.CreateDirectory(root); }
            var link = gameObject.AddComponent<OperatorLink>(); link.Host = host; link.EndpointOverride = "http://127.0.0.1:8765"; link.Begin();
            if (root != null) File.WriteAllText(Path.Combine(root, "connection.json"), "{\"pairCode\":\"" + link.PairingCode + "\",\"synthetic\":true}");
            Debug.Log("Desktop synthetic fixture: pair code " + link.PairingCode);
        }
    }
}
