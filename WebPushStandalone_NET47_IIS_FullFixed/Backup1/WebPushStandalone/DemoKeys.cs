using System;
using System.IO;
using System.Web.Hosting;
using Newtonsoft.Json;
using WebPush;

namespace WebPushStandalone
{
    // Demo only: single IIS process, local filesystem. Keys persist across app restarts.
    public static class DemoKeys
    {
        private static readonly object Gate = new object();
        private static KeyPair _cached;
        public sealed class KeyPair
        {
            public string PublicKey { get; set; }
            public string PrivateKey { get; set; }
        }
        public static KeyPair Get()
        {
            lock (Gate)
            {
                if (_cached != null) return _cached;
                var path = HostingEnvironment.MapPath("~/App_Data/vapid-keys.json");
                if (path == null) throw new InvalidOperationException("Web application path unavailable.");
                if (File.Exists(path))
                    _cached = JsonConvert.DeserializeObject<KeyPair>(File.ReadAllText(path));
                else
                {
                    var keys = VapidHelper.GenerateVapidKeys();
                    _cached = new KeyPair { PublicKey = keys.PublicKey, PrivateKey = keys.PrivateKey };
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, JsonConvert.SerializeObject(_cached));
                }
                if (_cached == null || String.IsNullOrWhiteSpace(_cached.PublicKey) ||
                    String.IsNullOrWhiteSpace(_cached.PrivateKey))
                    throw new InvalidOperationException("Invalid persisted VAPID key file.");
                return _cached;
            }
        }
    }
}
