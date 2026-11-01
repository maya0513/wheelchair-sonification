using System;
using System.Net;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public static class AndroidLanGateway
    {
        public static bool PrivateV4(string value)
        {
            if (
                !IPAddress.TryParse(value, out var address)
                || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            )
                return false;
            var a = address.GetAddressBytes();
            return a[0] == 10
                || a[0] == 192 && a[1] == 168
                || a[0] == 172 && a[1] >= 16 && a[1] <= 31;
        }

        public static string Endpoint()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var manager = activity.Call<AndroidJavaObject>(
                    "getSystemService",
                    "connectivity"
                );
                foreach (var network in manager.Call<AndroidJavaObject[]>("getAllNetworks"))
                {
                    using (network)
                    using (
                        var capabilities = manager.Call<AndroidJavaObject>(
                            "getNetworkCapabilities",
                            network
                        )
                    )
                    {
                        if (capabilities == null || !capabilities.Call<bool>("hasTransport", 1))
                            continue; // Wi-Fi, even without Internet validation.
                        using var properties = manager.Call<AndroidJavaObject>(
                            "getLinkProperties",
                            network
                        );
                        using var routes = properties.Call<AndroidJavaObject>("getRoutes");
                        for (int i = 0; i < routes.Call<int>("size"); i++)
                        {
                            using var route = routes.Call<AndroidJavaObject>("get", i);
                            if (!route.Call<bool>("hasGateway"))
                                continue;
                            using var gateway = route.Call<AndroidJavaObject>("getGateway");
                            string address = gateway.Call<string>("getHostAddress");
                            if (PrivateV4(address))
                                return "http://" + address + ":8765";
                        }
                    }
                }
            }
            catch (Exception)
            { /* State is reported as unavailable; no Internet or simulated fallback. */
            }
#endif
            return null;
        }
    }
}
