# WMS Pickers - Android app (TWA)

This turns the already-live PWA at `https://gcpl.logivue.in/` into a real, sideloadable `.apk` -
a Trusted Web Activity (TWA): a thin Android wrapper that launches the site full-screen with its
own launcher icon, no browser address bar. It is **not** a native rewrite; it's the same web app,
packaged.

This folder is intentionally just documentation + a reference config - the actual Android/Gradle
project isn't generated here because this environment has Node.js but no JDK, and Bubblewrap's
`build` step needs a JDK + Android SDK to run Gradle. `android-twa/` is not referenced by
`GodrejWMS.slnx` and has no effect on the .NET build.

## Prerequisites before you start

The PWA layer this depends on is already in the repo (`src/GodrejWMS.Web/wwwroot/manifest.webmanifest`,
`service-worker.js`, `icons/`) and must be **deployed to production** (`https://gcpl.logivue.in/`)
before any of the steps below will produce a working app - Bubblewrap reads the live manifest, and
Android's Digital Asset Links verification (the thing that removes the address bar) checks the live
`https://gcpl.logivue.in/.well-known/assetlinks.json`.

## Option A - build it yourself (full control over the signing key)

Needs: Node.js (already required for Bubblewrap itself), and a JDK (17 is a safe choice) + Android
SDK - Bubblewrap can download the Android SDK for you on first run if you don't have Android
Studio installed.

```bash
# 1. Scaffold the project from the live PWA manifest - this generates the real twa-manifest.json
#    (see twa-manifest.reference.json in this folder for the answers to give at each prompt) and,
#    unless you already have one, a new upload keystore (android.keystore) used to sign the APK.
cd android-twa
npx @bubblewrap/cli init --manifest=https://gcpl.logivue.in/manifest.webmanifest

# 2. Build the signed APK.
npx @bubblewrap/cli build
```

That produces `app-release-signed.apk` in this folder - that's the file to send to Picker devices.
They'll need "Install unknown apps" allowed for whatever app they install it through (Files,
email client, etc.) - normal for any sideloaded Android app, one-time per device.

### Finish verifying the app (removes the browser address bar)

1. Get the signing certificate's SHA-256 fingerprint:
   ```bash
   keytool -list -v -keystore android-twa/android.keystore -alias android
   ```
   Copy the line starting `SHA256:` (format: `14:6D:E9:83:...`, colon-separated hex).
2. Put that value into `src/GodrejWMS.Web/wwwroot/.well-known/assetlinks.json`, replacing the
   placeholder `sha256_cert_fingerprints` entry (currently all-zeros).
3. Deploy that one-line change to production. Once `https://gcpl.logivue.in/.well-known/assetlinks.json`
   serves the real fingerprint, re-launching the installed app shows it full-screen, no address bar.
   (Until then, the APK still works - it just looks like a plain browser tab instead of an app.)

**Keep `android.keystore` somewhere safe** (not committed to git - add it to `.gitignore` if you
keep it in this folder). Any future update to the app must be signed with the same key, or Android
will refuse to install it over the existing one.

## Option B - no local install needed (pwabuilder.com)

If installing a JDK/Android SDK isn't worth it for this:

1. Go to https://www.pwabuilder.com, paste `https://gcpl.logivue.in/`, let it analyze the manifest.
2. Choose the Android package option → it builds a signed `.apk` (with its own generated keystore,
   downloadable from the same flow) entirely in-browser.
3. Follow the same "finish verifying" steps above using the fingerprint PWABuilder gives you for
   its keystore.

This is faster to get a first APK in hand, at the cost of PWABuilder (not you) holding the signing
key by default - fine for an internal tool, worth knowing if you ever need to re-sign an update.

## Updating the app later

Any change to the PWA (new icon, renamed app, etc.) just needs `wwwroot/manifest.webmanifest`
(and/or the icon files) updated and redeployed - the installed APK reads the live manifest/service
worker over the network, so most changes don't need a new APK build at all. A new APK build is only
needed for things Android itself cares about at install time: the app's name/icon as Android shows
them in the launcher/app list, or the `packageId`/signing key (which should never change for an
existing install).
