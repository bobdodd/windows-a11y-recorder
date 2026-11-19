package io.github.bobdodd.a11yrecorder.fixtures;

import android.app.Activity;
import android.view.View;

/** One fixture screen: its name for the tour, its title, and how it is made. */
final class Screen {
    interface Builder {
        View build(Activity activity);
    }

    /** The name passed with am start --es screen, as in expected.json. */
    final String id;

    /** The activity's title, which the recorder uses to tell screens apart. */
    final String title;

    final Builder builder;

    Screen(String id, String title, Builder builder) {
        this.id = id;
        this.title = title;
        this.builder = builder;
    }
}
