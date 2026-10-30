# WebPushStandalone — IIS-publishable .NET Framework 4.7 demo

This Visual Studio 2022 ASP.NET Web API 2 solution implements the prior demo as a self-contained project: subscriber page, admin broadcast, Service Worker, file-backed subscriptions and persisted VAPID keys. It is a **single-instance development demo**, not production security architecture.

## Changes from previous solutions

- Web Application MSBuild targets path initialized for Visual Studio 2022; publish targets no longer silently skipped (requires ASP.NET/web development workload).
- WebPush `1.0.12`, Newtonsoft.Json `13.0.3` (redirect to assembly 13.0.0.0), Portable.BouncyCastle `1.8.1.3`, matching selected legacy .NET Framework dependencies. Clear `bin`/`obj` and restore packages when switching versions.
- Edge WNS subscription endpoints, including `wns2-*.notify.windows.com`, allowed alongside FCM, Firefox and Apple. Endpoint hostname validation is a demo baseline, **not** complete SSRF protection.
- Relative frontend API and worker URLs support both site root and `/WebPublish/` virtual application path. The service worker scope is the current application path.
- `GET api/pushdemo/ping` checks routing without touching the VAPID key file; `GET api/pushdemo/config` loads/generates VAPID keys.
- Details/errors are local-only by default, and debug compilation is disabled.

## Build

1. Install Visual Studio 2022: **ASP.NET and web development** workload and .NET Framework 4.7 targeting pack.
2. Open `WebPushStandalone.sln`, restore NuGet packages, **Clean** then **Rebuild** in Release mode.
3. Verify output includes `WebPushStandalone.dll`, `System.Web.Http.dll`, `System.Web.Http.WebHost.dll`, `WebPush.dll`, `Newtonsoft.Json.dll`, `BouncyCastle.Crypto.dll`. Do not use stale DLLs.
4. In `Web.config`, change `PushDemo.AdminKey` from `CHANGE_ME` to a strong test secret and set `PushDemo.VapidSubject` to a real `mailto:` contact.
5. Publish > Folder. Use an empty staging folder on first deployment. Avoid overwriting the existing `App_Data/vapid-keys.json` or subscription file after initial setup.

## IIS deployment for TarekSites

- Site: `TarekSites`; application alias: `/WebPublish`; physical path: `C:\inetpub\TarekSites\WebPublish`; application pool: `TarekSites`.
- Application pool .NET CLR **v4.0**, Integrated pipeline; enable ASP.NET 4.x and IIS `ExtensionlessUrlHandler-Integrated-4.0`.
- Deploy the *published output*, not the solution/source folder, into the physical directory.
- Ensure `Global.asax`, `Web.config`, `bin/`, `index.html`, `admin.html`, `sw.js`, and `App_Data/` exist.
- Grant Modify **only** on `App_Data` to `IIS AppPool\TarekSites` (or custom actual pool identity):

```bat
icacls "C:\inetpub\TarekSites\WebPublish\App_Data" /grant "IIS AppPool\TarekSites:(OI)(CI)M"
```

- Protect `App_Data` from web access (ASP.NET normally does this), directory browsing and other accounts; it contains VAPID private keys and subscription endpoints.
- Configure trusted HTTPS binding on the **parent** TarekSites site. Certificate SAN must match hostname `tareksites`; trust certificate on each client. Web Push cannot be subscribed on ordinary `http://tareksites` (localhost is exempt).
- Browse `https://tareksites/WebPublish/api/pushdemo/ping` first. Expected `{ "ok": true, ... }`.
- Next browse `https://tareksites/WebPublish/api/pushdemo/config`. Expected `{ "publicKey": "..." }`.
- Then browse `https://tareksites/WebPublish/index.html`, enable notifications, and send from `https://tareksites/WebPublish/admin.html`.

## Diagnosing HTTP 404 and HTTP 500

- `ping` 404: check published `bin/WebPushStandalone.dll`, IIS substatus, Web API routing, `Global.asax`, ASP.NET features and extensionless handler; `ping` avoids key generation.
- `ping` 200 but `config` 500: check `App_Data` Modify permission, corrupt or missing VAPID key file, and BouncyCastle assembly loading. Use Windows Event Viewer or local-only detailed errors.
- `config` 200 but subscribe fails: inspect subscription endpoint. Edge uses `*.notify.windows.com`, Chrome generally uses FCM.
- Browser push shows Accepted but no notification: enable notifications for Chrome/Edge in **Windows Settings > Notifications**, inspect worker under DevTools Application > Service Workers and Push Messaging.
- If client sees HTTP 404 on root resource, verify deployment under `/WebPublish/`: frontend paths use relative `api/pushdemo/...`, and `register('sw.js')` is also relative.
- For debugging detailed errors on a private machine, temporarily change `customErrors` to `Off`; revert after. Never display server exceptions publicly.
- Detailed IIS substatus: inspect IIS logs under `C:\inetpub\logs\LogFiles` and record `sc-status`, `sc-substatus`, `sc-win32-status`.

## Behavioral limitations

- Each browser/device must visit the page and grant notifications. It is not enough to merely open the URL.
- Browser windows can be closed; background delivery depends on browser/OS policy. Device power-off means delivery cannot happen immediately.
- iOS/iPadOS Web Push requires installed Home Screen web app and notification permission. Public mobile tests need reachable trusted HTTPS, and internal-only URLs may require VPN.
- Test on one IIS server/process only. File-based storage does not support high concurrency/multiple nodes.
- The demo admin key is not production authentication. Do **not** expose `admin.html` and the broadcast endpoint to untrusted users or the public internet.
- Accepted counts only acknowledge the push service, not device display.
- The chosen older WebPush and BouncyCastle versions require a security review before production.

## URLs

| Local IIS Express | Published IIS |
|---|---|
| `http://localhost:57814/api/pushdemo/ping` | `https://tareksites/WebPublish/api/pushdemo/ping` |
| `http://localhost:57814/api/pushdemo/config` | `https://tareksites/WebPublish/api/pushdemo/config` |
| `http://localhost:57814/index.html` | `https://tareksites/WebPublish/index.html` |
| `http://localhost:57814/admin.html` | `https://tareksites/WebPublish/admin.html` |

## Verification status

Package XML, JavaScript parser checks, project file references and ZIP contents are checked in the authoring environment. **Windows MSBuild, NuGet restore, IIS publishing and live push delivery are not runnable/verified in this environment.**
