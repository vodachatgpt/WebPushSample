using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Newtonsoft.Json;
using WebPush;
using WebPushStandalone;

namespace WebPushStandalone.Controllers
{
    // Web API 2 attribute routing: config.MapHttpAttributeRoutes();
    [RoutePrefix("api/pushdemo")]
    public class PushDemoController : ApiController
    {
        private static readonly object FileLock = new object();
        private static string StoragePath
        {
            get { return HttpContext.Current.Server.MapPath("~/App_Data/pushdemo-subscriptions.json"); }
        }

        public class BrowserKeys { public string p256dh { get; set; } public string auth { get; set; } }
        public class BrowserSubscription
        {
            public string endpoint { get; set; }
            public BrowserKeys keys { get; set; }
        }
        public class SendRequest
        {
            public string Title { get; set; }
            public string Message { get; set; }
        }
        private static List<BrowserSubscription> Load(string path)
        {
            if (!File.Exists(path)) return new List<BrowserSubscription>();
            return JsonConvert.DeserializeObject<List<BrowserSubscription>>(File.ReadAllText(path))
                   ?? new List<BrowserSubscription>();
        }
        private static void Save(string path, List<BrowserSubscription> list)
        {
            var directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            // Demo only: single IIS process/node. Do not use this on multiple servers.
            File.WriteAllText(path, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
        private static bool IsTrustedEndpoint(string endpoint)
        {
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length > 0) return false;
            // Demo allowlist: extend only after verifying the actual browser's subscription endpoint.
            var host = uri.DnsSafeHost.ToLowerInvariant();
            return (host == "fcm.googleapis.com" || (host.StartsWith("fcm", StringComparison.Ordinal) && host.EndsWith(".googleapis.com", StringComparison.Ordinal))) || host == "updates.push.services.mozilla.com" ||
                   host == "web.push.apple.com" || host.EndsWith(".push.apple.com", StringComparison.Ordinal) ||
                   host == "fcm-xmpp.googleapis.com" ||
                   host == "notify.windows.com" || host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
        }
        private static bool IsValid(BrowserSubscription s)
        {
            return s != null && !String.IsNullOrWhiteSpace(s.endpoint) && s.endpoint.Length <= 2048 &&
                   s.keys != null && !String.IsNullOrWhiteSpace(s.keys.p256dh) &&
                   !String.IsNullOrWhiteSpace(s.keys.auth) && s.keys.p256dh.Length <= 512 &&
                   s.keys.auth.Length <= 512 && IsTrustedEndpoint(s.endpoint);
        }
        private static bool IsAdmin(System.Net.Http.HttpRequestMessage request)
        {
            IEnumerable<string> supplied;
            if (!request.Headers.TryGetValues("X-Demo-Admin-Key", out supplied)) return false;
            string expected = ConfigurationManager.AppSettings["PushDemo.AdminKey"];
            if (String.IsNullOrWhiteSpace(expected) || expected == "CHANGE_ME") return false;
            string actual = supplied.FirstOrDefault() ?? "";
            // A demo-only shared secret, never embed it in frontend source.
            if (actual.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < actual.Length; i++) diff |= actual[i] ^ expected[i];
            return diff == 0;
        }

        // Does not touch the VAPID file; separates API routing failures from key/storage failures.
        [HttpGet, Route("ping")]
        public IHttpActionResult Ping() { return Ok(new { ok = true, application = "WebPushStandalone", utc = DateTime.UtcNow }); }

        [HttpGet, Route("config")]
        public IHttpActionResult Config()
        {
            string key = DemoKeys.Get().PublicKey;
            if (String.IsNullOrWhiteSpace(key)) return InternalServerError(new InvalidOperationException("Set VAPID keys in Web.config."));
            return Ok(new { publicKey = key });
        }

        [HttpPost, Route("subscribe")]
        public IHttpActionResult Subscribe([FromBody] BrowserSubscription subscription)
        {
            if (!IsValid(subscription)) return BadRequest("Invalid or unsupported browser push subscription endpoint.");
            lock (FileLock)
            {
                string path = StoragePath;
                var list = Load(path);
                var previous = list.FindIndex(x => x.endpoint == subscription.endpoint);
                if (previous >= 0) list[previous] = subscription;
                else list.Add(subscription);
                Save(path, list);
                return Ok(new { subscribed = true, deviceCount = list.Count });
            }
        }

        [HttpGet, Route("count")]
        public IHttpActionResult Count()
        {
            if (!IsAdmin(Request)) return Unauthorized();
            lock (FileLock) return Ok(new { deviceCount = Load(StoragePath).Count });
        }

        [HttpPost, Route("send")]
        public async Task<IHttpActionResult> Send([FromBody] SendRequest data)
        {
            if (!IsAdmin(Request)) return Unauthorized();
            if (data == null || String.IsNullOrWhiteSpace(data.Title) || String.IsNullOrWhiteSpace(data.Message))
                return BadRequest("Title and message required.");
            if (data.Title.Length > 120 || data.Message.Length > 500) return BadRequest("Title max 120, message max 500 characters.");

            var keys = DemoKeys.Get();
            string publicKey = keys.PublicKey;
            string privateKey = keys.PrivateKey;
            string subject = ConfigurationManager.AppSettings["PushDemo.VapidSubject"];
            if (String.IsNullOrWhiteSpace(publicKey) || String.IsNullOrWhiteSpace(privateKey) || String.IsNullOrWhiteSpace(subject))
                return InternalServerError(new InvalidOperationException("VAPID configuration missing."));

            List<BrowserSubscription> subscribers;
            lock (FileLock) subscribers = Load(StoragePath).ToList();
            int accepted = 0, failed = 0;
            var stale = new HashSet<string>(StringComparer.Ordinal);
            var errors = new List<string>();
            var vapid = new VapidDetails(subject, publicKey, privateKey);
            string payload = JsonConvert.SerializeObject(new { title = data.Title, body = data.Message, url = "index.html", id = Guid.NewGuid().ToString("N") });
            using (var client = new WebPushClient())
            {
                foreach (var s in subscribers)
                {
                    // Important: revalidate endpoint before any server-side HTTP request.
                    if (!IsValid(s)) { failed++; continue; }
                    try
                    {
                        await client.SendNotificationAsync(new PushSubscription(s.endpoint, s.keys.p256dh, s.keys.auth), payload, vapid);
                        accepted++;
                    }
                    catch (WebPushException ex)
                    {
                        failed++;
                        if (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
                            stale.Add(s.endpoint);
                        if (errors.Count < 10) errors.Add("Push provider HTTP " + (int)ex.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (errors.Count < 10) errors.Add(ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            if (stale.Count > 0)
            {
                lock (FileLock)
                {
                    var list = Load(StoragePath);
                    list.RemoveAll(x => stale.Contains(x.endpoint));
                    Save(StoragePath, list);
                }
            }
            return Ok(new { subscriptions = subscribers.Count, accepted, failed, removedExpired = stale.Count, errors, note = "Accepted means push-service acceptance, not proof of device display." });
        }
    }
}
