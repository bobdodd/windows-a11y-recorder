# Fixture app

An Android app whose screens hold known faults, and near misses that
are correct, for testing the recorder's Android checks. It is for
testing only and never published. The plan, and what is still to be
built, are in `docs/architecture/android-fixtures.md`.

- `AndroidManifest.xml`: the app and its one activity.
- `expected.json`: each screen, how it is opened, and what each check
  should find on each element. `tests/Recorder.Tests/AndroidFixtureAppTests.cs`
  keeps it in step with the source and the recorder's check names.
- `res/values/ids.xml`: the stable IDs of the elements the expected
  answers name.
- `res/drawable/`: the icons, drawn for the app.
- `assets/web/`: the WebView screen's pages.
- `src/`: the Java source. `Screens.java` holds the screens.

Build it with `scripts/Build-FixtureApp.ps1`, which runs the companion
app's pinned build with `-App fixtures`. `scripts/Run-FixtureTour.ps1`
installs it and opens each tour screen while the recorder records.

Every word, picture and colour in the app is made up for it.
