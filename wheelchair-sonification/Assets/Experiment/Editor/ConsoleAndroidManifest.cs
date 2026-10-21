using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;

namespace WheelchairSonification.Editor
{
    public sealed class ConsoleAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var doc = XDocument.Load(file);
            XNamespace android = "http://schemas.android.com/apk/res/android";
            foreach (string permission in new[] { "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE" })
                if (!doc.Root.Elements("uses-permission").Any(e => (string)e.Attribute(android + "name") == permission))
                    doc.Root.Add(new XElement("uses-permission", new XAttribute(android + "name", permission)));
            doc.Root.Element("application").SetAttributeValue(android + "usesCleartextTraffic", "true");
            doc.Save(file);
        }
    }
}
