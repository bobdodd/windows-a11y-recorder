# Android APK evidence fixtures

There are two fixture types: a small installable **APK Evidence Fixtures** app and test-only package fragments. They contain synthetic data only. Neither changes the existing Android check fixture or its recorded tour.

## Installable Views and WebView fixture

From a Windows checkout, build it with the existing pinned Android toolchain:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\scripts\Build-CompanionApp.ps1 -App apk-fixtures
```

The output is `artifacts\apk-fixture-app\output\a11yrecorder-apk-fixtures.apk`. The app requests no permissions. It does not run the timer until **Start fixture timer** is tapped. The separate refresh example is bundled for inspection but not opened by the app. The WebView displays a local form, blocks network loads and leaves JavaScript disabled.

| Case | Package expectation | Runtime case for a later recording |
| --- | --- | --- |
| Font family and size | Both original fonts are bundled. The default and landscape heading styles declare `18sp` and `26sp`, and the layout label declares `14dp`. Only the block font is referenced. | Compare the active heading and label with the supported size extra. Do not infer the effective unit or typeface from the APK. |
| Native forms | A compiled label has a `labelFor` link; another field only has a hint. The code-created label and `21sp` value leave API symbols, not XML declarations. | Inspect the explicit XML and code-created label links separately from hints. Treat the hint-only field as a negative control. |
| Native timing | DEX contains timer and recommended-timeout symbols, not call durations or proof that the timer ran. | The button starts the timer and the status becomes visible. The other button cancels it. Elapsed time, settings and announcements require recorded evidence. |
| Packaged web and SVG | HTML declares a heading, label association, required and hint-only inputs, and a status region. SVG declares a role and accessible label. The unopened page declares a 12-second refresh. | Observe what the local WebView publishes; it is not a live DOM capture. The refresh page is not part of this screen. |

The fixture also contains negative controls: an unreferenced resource string, an HTML input value, a script-generated label and a refresh destination. The inventory must not retain them as accessibility declarations.

## TalkBack navigation fixture

The same APK has a separate launcher entry, **TalkBack Navigation
Fixture**. Its fixed native viewport contains two labelled buttons, a
summary group and a canvas-drawn tap target whose label and action are
not exposed as accessibility semantics. An on-screen status shows
whether a button was activated. This entry does not change the original
APK evidence screen or its tour.

## Automated split matrix

`AndroidApkGeneratedFixtureTests` exercises the production selected-app preflight with fake ADB, then checks the persisted evidence and playback groups. The checked-in generated inputs are small, unsigned, and used solely as test data. The test assembles additional archives in temporary storage and asserts that every pulled copy is removed.

- **Native baseline:** Actual AAPT2-compiled XML/resources and D8-compiled Java go into `native-web.zip`. The archive mirrors the fixture app, before APK alignment and signing.
- **Flutter:** A synthetic font manifest declares one bundled font and one declaration-only font. Empty library marker entries test identification; there is no Flutter executable in the test.
- **Compose:** A small DEX compiled from a synthetic Java caller of real AndroidX semantics APIs is packaged with a Compose marker. The library itself is not committed. API symbols do not declare the values sent through them.
- **React Native:** A synthetic JavaScript specimen is compiled by the matching Hermes compiler and packaged with a React Native marker. A different split uses a `.hbc` filename without the signature. The test needs no React Native native binaries or runtime.
- **Missing split:** A partial pull leaves a disposable file and reports incomplete coverage without dropping valid evidence from the other splits.

The matrix does not install these combined splits, run any framework, or test how TalkBack presents a screen. The existing recorded tour remains a separate native Views and WebView replay baseline. No production rule or model is added.

## Refresh generated inputs

Linux regeneration uses the JDK, Android Build-Tools and platform pinned in `scripts/Build-CompanionApp.ps1`. The optional font regeneration requires fontTools. The original font sources cover only the letters in `APK`.

```bash
python3 android/apk-fixtures/make_fonts.py
JAVA_HOME=/path/to/jdk \
ANDROID_BUILD_TOOLS=/path/to/build-tools \
ANDROID_PLATFORM_JAR=/path/to/android.jar \
COMPOSE_UI_AAR=/path/to/ui-android-1.7.8.aar \
HERMESC=/path/to/hermesc \
bash scripts/Build-ApkEvidenceTestInputs.sh
dotnet test tests/Recorder.Tests/Recorder.Tests.csproj --filter FullyQualifiedName~AndroidApkGeneratedFixtureTests
```

Use the [official AndroidX Compose UI Android 1.7.8 AAR](https://dl.google.com/dl/android/maven2/androidx/compose/ui/ui-android/1.7.8/ui-android-1.7.8.aar) and `package/sdks/hermesc/linux64-bin/hermesc` from the [React Native 0.76.9 package](https://registry.npmjs.org/react-native/-/react-native-0.76.9.tgz). Normal .NET tests consume the committed inputs without downloading these tools.
