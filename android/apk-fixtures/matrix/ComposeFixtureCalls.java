package org.example.synthetic;

import androidx.compose.ui.semantics.SemanticsPropertiesKt;
import androidx.compose.ui.semantics.SemanticsPropertyReceiver;

/** Compile against the official Compose UI library; this specimen is not launched. */
public final class ComposeFixtureCalls {
    public static void describe(SemanticsPropertyReceiver receiver) {
        SemanticsPropertiesKt.heading(receiver);
        SemanticsPropertiesKt.error(receiver, "compose-not-an-observation-sentinel");
    }
}
