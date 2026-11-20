package io.github.bobdodd.a11yrecorder.fixtures;

import android.app.Activity;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.Locale;

/**
 * The screens for the check not yet built of a chart that relies on colour
 * (WCAG 1.4.1), each a chart that fails and one that passes, of one kind.
 * A chart fails when its series, slices, segments or levels are told apart
 * only by colour: by hue alone, by a lightness difference under 3:1, or by
 * a particular colour having a meaning, as red for over budget, whatever
 * the contrast. It passes with a pattern (G111), markers or a dashed line,
 * words on the chart (G14), or colours that also differ by 3:1 or more in
 * lightness (Understanding 1.4.1). Every chart has a content description
 * giving its data and not its colours, so the text alternative is not
 * what is tested (F13's 1.1.1 part). Each chart's colours are at least
 * 3:1 on white, but the outlined bars', which have a 2 dp outline in
 * Charts.INK instead, and the words are TextViews in Ui.TEXT, or white on
 * fills at least 4.5:1, so the contrast checks have nothing to find. See
 * docs/architecture/android-fixtures.md, "The chart screens".
 */
final class ChartScreens {
    /** Every chart's width, in dp; the screens are at least 411 dp wide inside their padding on the owner's phone. */
    private static final int WIDTH = 360;

    // Hue only: each pair under 1.3:1 against each other (measured with
    // WCAG's relative luminance, 2026-11-20).
    private static final int RED = 0xFFC62828;      // 5.6:1 on white
    private static final int GREEN = 0xFF2E7D32;    // 5.1:1; 1.10:1 against RED
    private static final int MAGENTA = 0xFFB0306A;  // 6.0:1
    private static final int TEAL = 0xFF00796B;     // 5.3:1; 1.14:1 against MAGENTA
    private static final int ORANGE = 0xFFA85400;   // 5.3:1; 1.05:1 against RED
    private static final int PURPLE = 0xFF7B3FA0;   // 6.9:1
    private static final int BROWN = 0xFF8D5A2B;    // 5.8:1; 1.09:1 against TEAL

    // Lightness: 1.61:1 against each other (fails), and 3.61:1 (passes).
    private static final int BLUE_MID = 0xFF3F6FD8;  // 4.7:1
    private static final int BLUE_DEEP = 0xFF2A4FA8; // 7.6:1
    private static final int NAVY = 0xFF1A237E;      // 13.2:1
    private static final int BLUE_LIGHT = 0xFF5C7CFA; // 3.7:1; 3.61:1 against NAVY

    // A meaning in a particular colour: red for over budget, 3.13:1 against NAVY.
    private static final int RED_BRIGHT = 0xFFE53935; // 4.2:1

    // Pastels for the outlined bars, under 3:1 on white, 1.08:1 against each other.
    private static final int PINK = 0xFFF4B6C2;     // 1.7:1
    private static final int MINT = 0xFFA8DAB5;     // 1.6:1

    private ChartScreens() {
    }

    // ---- Bars: hue alone, and a pattern ---------------------------------------

    private static final String[] SEASONS = {"Spring", "Summer", "Autumn", "Winter"};
    private static final String[] PLANTS = {"Roses", "Tulips"};

    static View bars(Activity activity) {
        LinearLayout column = start(activity, "Plant sales", "Plants sold at the market each season.");
        float[][] last = {{12, 18, 9, 4}, {16, 7, 5, 2}};
        float[][] now = {{10, 15, 11, 3}, {14, 9, 6, 3}};
        Charts.Look roses = new Charts.Look(RED);
        Charts.Look tulips = new Charts.Look(GREEN);
        Charts.Look tulipsHatched = new Charts.Look(GREEN).hatched();
        barBlock(activity, column, R.id.chart_bars_hue, "Last year", "plants sold last year", SEASONS, PLANTS, last, 20,
                Charts.bySeries(4, roses, tulips), boxes(roses, tulips));
        barBlock(activity, column, R.id.chart_bars_pattern, "This year", "plants sold this year", SEASONS, PLANTS, now, 20,
                Charts.bySeries(4, roses, tulipsHatched), boxes(roses, tulipsHatched));
        return column;
    }

    // ---- Bars: lightness under 3:1, and over ----------------------------------

    private static final String[] QUARTERS = {"Jan", "Apr", "Jul", "Oct"};
    private static final String[] SEEDS = {"Beans", "Peas"};

    static View lightness(Activity activity) {
        LinearLayout column = start(activity, "Seed orders", "Seed packets ordered each quarter.");
        float[][] online = {{30, 45, 20, 10}, {25, 35, 30, 15}};
        float[][] shop = {{20, 40, 25, 15}, {35, 30, 20, 10}};
        Charts.Look beansMid = new Charts.Look(BLUE_MID);
        Charts.Look peasDeep = new Charts.Look(BLUE_DEEP);
        Charts.Look beansNavy = new Charts.Look(NAVY);
        Charts.Look peasLight = new Charts.Look(BLUE_LIGHT);
        barBlock(activity, column, R.id.chart_bars_close, "Online", "seed packets ordered online", QUARTERS, SEEDS, online, 50,
                Charts.bySeries(4, beansMid, peasDeep), boxes(beansMid, peasDeep));
        barBlock(activity, column, R.id.chart_bars_lightness, "In the shop", "seed packets ordered in the shop", QUARTERS, SEEDS, shop, 50,
                Charts.bySeries(4, beansNavy, peasLight), boxes(beansNavy, peasLight));
        return column;
    }

    // ---- Bars with outlines: the same outline, and a dashed one ----------------

    private static final String[] DAYS = {"Mon", "Tue", "Wed", "Thu", "Fri"};
    private static final String[] SHOPS = {"North shop", "South shop"};

    static View outlines(Activity activity) {
        LinearLayout column = start(activity, "Shop visits", "People who came into each shop, by day.");
        float[][] last = {{40, 55, 35, 60, 80}, {50, 45, 30, 70, 65}};
        float[][] now = {{45, 50, 40, 65, 75}, {55, 40, 35, 60, 70}};
        Charts.Look north = new Charts.Look(PINK).outlined();
        Charts.Look south = new Charts.Look(MINT).outlined();
        Charts.Look southDashed = new Charts.Look(MINT).dashedOutline();
        barBlock(activity, column, R.id.chart_outlined_hue, "Last week", "visits last week", DAYS, SHOPS, last, 80,
                Charts.bySeries(5, north, south), boxes(north, south));
        barBlock(activity, column, R.id.chart_outlined_dashed, "This week", "visits this week", DAYS, SHOPS, now, 80,
                Charts.bySeries(5, north, southDashed), boxes(north, southDashed));
        return column;
    }

    // ---- Lines: colour alone, and markers with a dashed line --------------------

    private static final String[] MONTHS = {"Jan", "Feb", "Mar", "Apr", "May", "Jun"};
    private static final String[] PLACES = {"Hilltop", "Valley"};

    static View lines(Activity activity) {
        LinearLayout column = start(activity, "Rainfall", "Rain in millimetres, by month.");
        float[][] last = {{80, 65, 70, 50, 40, 30}, {60, 70, 45, 55, 35, 45}};
        float[][] now = {{75, 70, 60, 55, 35, 25}, {55, 60, 50, 40, 45, 35}};
        Charts.Look hilltop = new Charts.Look(RED);
        Charts.Look valley = new Charts.Look(GREEN);
        Charts.Look hilltopCircles = new Charts.Look(RED).marker(Charts.CIRCLE);
        Charts.Look valleySquares = new Charts.Look(GREEN).marker(Charts.SQUARE).dashedLine();
        lineBlock(activity, column, R.id.chart_lines_hue, "Last year", "rain last year", PLACES, last, 0,
                new Charts.Look[] {hilltop, valley}, legend(activity, lineSwatches(hilltop, valley), PLACES));
        lineBlock(activity, column, R.id.chart_lines_markers, "This year", "rain this year", PLACES, now, 0,
                new Charts.Look[] {hilltopCircles, valleySquares}, legend(activity, lineSwatches(hilltopCircles, valleySquares), PLACES));
        return column;
    }

    // ---- Lines: a legend in the lines' colours, and names on the lines ----------

    private static final String[] RIVERS = {"North river", "South river"};

    static View lineLabels(Activity activity) {
        LinearLayout column = start(activity, "River levels", "Water depth in metres, by month.");
        float[][] last = {{3, 4, 3.5f, 2.5f, 2, 1.5f}, {2, 2.5f, 3, 3.5f, 3, 2.5f}};
        float[][] now = {{3.5f, 3, 2.5f, 2, 2.5f, 2}, {1.5f, 2, 2.5f, 3, 3.5f, 4}};
        Charts.Look north = new Charts.Look(RED);
        Charts.Look south = new Charts.Look(GREEN);

        // The legend is the rivers' names in the lines' colours, with nothing else to tell them apart.
        LinearLayout names = Ui.row(activity);
        names.addView(small(activity, RIVERS[0], RED, 16));
        names.addView(small(activity, RIVERS[1], GREEN, 16));
        lineBlock(activity, column, R.id.chart_lines_coloured_legend, "Last year", "water depth last year", RIVERS, last, 0,
                new Charts.Look[] {north, south}, names);

        // Each line's name at its right-hand end, in Ui.TEXT, and no legend.
        float reserve = 96;
        FrameLayout labelled = lineBlock(activity, column, R.id.chart_lines_labelled, "This year", "water depth this year", RIVERS, now, reserve,
                new Charts.Look[] {north, south}, null);
        for (int s = 0; s < now.length; s++) {
            float y = Charts.lineY(LINE_HEIGHT, now[s][now[s].length - 1], 5);
            place(activity, labelled, small(activity, RIVERS[s], Ui.TEXT, 0), WIDTH - reserve + 4, y - 10, 92, Gravity.START);
        }
        return column;
    }

    // ---- Pie: a legend, and labels with leaders --------------------------------

    private static final String[] SPENDING = {"Rent", "Food", "Travel", "Other"};
    private static final int PIE = 130;
    private static final float PIE_LEFT = (WIDTH - PIE) / 2f;

    static View pie(Activity activity) {
        LinearLayout column = start(activity, "Household budget", "How the month's money was spent.");
        Charts.Look[] looks = {new Charts.Look(MAGENTA), new Charts.Look(TEAL), new Charts.Look(ORANGE), new Charts.Look(PURPLE)};

        float[] last = {40, 30, 20, 10};
        column.addView(Ui.body(activity, "Last month"));
        column.addView(chart(activity, R.id.chart_pie_hue, WIDTH, PIE, Charts.pie(PIE_LEFT, PIE, last, looks, null),
                "Pie chart of the money spent last month: " + shares(SPENDING, last)));
        column.addView(legend(activity, boxes(looks), SPENDING));

        float[] now = {45, 25, 20, 10};
        column.addView(Ui.space(activity, 1, 8));
        column.addView(Ui.body(activity, "This month"));
        float[][] places = Charts.pieLabelPlaces(PIE_LEFT, PIE, now);
        FrameLayout labelled = new FrameLayout(activity);
        labelled.addView(chart(activity, R.id.chart_pie_labelled, WIDTH, PIE, Charts.pie(PIE_LEFT, PIE, now, looks, places),
                "Pie chart of the money spent this month: " + shares(SPENDING, now)));
        for (int s = 0; s < now.length; s++) {
            String text = SPENDING[s] + " " + Math.round(now[s]) + "%";
            boolean right = places[s][2] > 0;
            float width = 96;
            place(activity, labelled, small(activity, text, Ui.TEXT, 0),
                    right ? places[s][0] + 4 : places[s][0] - 4 - width, places[s][1] - 10, width, right ? Gravity.START : Gravity.END);
        }
        column.addView(labelled);
        return column;
    }

    // ---- Stacked bars: a legend, and words in the segments -----------------------

    private static final String[] YEARS = {"2024", "2025", "2026"};
    private static final String[] SOURCES = {"Sun", "Wind", "Gas"};
    private static final float STACK_START = 56;
    private static final float STACK_BAR = 28;
    private static final float STACK_GAP = 14;

    static View stacked(Activity activity) {
        LinearLayout column = start(activity, "Energy mix", "Where the village's power came from.");
        Charts.Look[] looks = {new Charts.Look(BROWN), new Charts.Look(TEAL), new Charts.Look(PURPLE)};

        float[][] spring = {{40, 35, 25}, {45, 30, 25}, {50, 25, 25}};
        column.addView(Ui.body(activity, "Spring"));
        column.addView(stackedChart(activity, R.id.chart_stacked_hue, "power in spring", spring, looks, false));
        column.addView(legend(activity, boxes(looks), SOURCES));

        float[][] autumn = {{35, 40, 25}, {40, 35, 25}, {45, 30, 25}};
        column.addView(Ui.space(activity, 1, 8));
        column.addView(Ui.body(activity, "Autumn"));
        column.addView(stackedChart(activity, R.id.chart_stacked_labelled, "power in autumn", autumn, looks, true));
        return column;
    }

    private static FrameLayout stackedChart(Activity activity, int id, String what, float[][] percentages, Charts.Look[] looks, boolean words) {
        int height = Math.round(percentages.length * STACK_BAR + (percentages.length - 1) * STACK_GAP);
        StringBuilder description = new StringBuilder("Stacked bar chart of the ").append(what).append(", by year. ");
        for (int b = 0; b < percentages.length; b++) {
            description.append(YEARS[b]).append(": ").append(shares(SOURCES, percentages[b])).append(' ');
        }
        FrameLayout frame = new FrameLayout(activity);
        frame.addView(chart(activity, id, WIDTH, height,
                Charts.stacked(WIDTH, STACK_START, STACK_BAR, STACK_GAP, percentages, looks), description.toString().trim()));
        for (int b = 0; b < percentages.length; b++) {
            float top = b * (STACK_BAR + STACK_GAP);
            place(activity, frame, small(activity, YEARS[b], Ui.TEXT, 0), 0, top + STACK_BAR / 2 - 10, STACK_START - 8, Gravity.START);
            if (!words) {
                continue;
            }
            for (int s = 0; s < percentages[b].length; s++) {
                float left = Charts.segmentStart(WIDTH, STACK_START, percentages[b], s);
                String text = SOURCES[s] + " " + Math.round(percentages[b][s]) + "%";
                place(activity, frame, small(activity, text, Ui.BACKGROUND, 0), left + 4, top + STACK_BAR / 2 - 10,
                        (WIDTH - STACK_START) * percentages[b][s] / 100f - 8, Gravity.START);
            }
        }
        return frame;
    }

    // ---- Highlight: red for over budget, and red with a pattern and a word ------

    private static final String[] HALF_YEAR = {"Jan", "Feb", "Mar", "Apr", "May", "Jun"};
    private static final float BUDGET = 100;

    static View highlight(Activity activity) {
        LinearLayout column = start(activity, "Monthly spending", "Spending each month against the budget of 100 dollars.");
        Charts.Look within = new Charts.Look(NAVY);
        Charts.Look over = new Charts.Look(RED_BRIGHT);
        Charts.Look overHatched = new Charts.Look(RED_BRIGHT).hatched();

        float[] last = {80, 105, 90, 112, 95, 120};
        highlightBlock(activity, column, R.id.chart_highlight_colour, "Last half year", "spending in the last half year",
                last, within, over, false);
        float[] now = {85, 110, 92, 98, 115, 90};
        highlightBlock(activity, column, R.id.chart_highlight_marked, "This half year", "spending in this half year",
                now, within, overHatched, true);
        return column;
    }

    private static void highlightBlock(Activity activity, LinearLayout column, int id, String caption, String what,
            float[] values, Charts.Look within, Charts.Look over, boolean words) {
        column.addView(Ui.body(activity, caption));
        Charts.Look[][] looks = new Charts.Look[1][values.length];
        StringBuilder description = new StringBuilder("Bar chart of the ").append(what).append(", in dollars, by month. ");
        for (int m = 0; m < values.length; m++) {
            boolean isOver = values[m] > BUDGET;
            looks[0][m] = isOver ? over : within;
            description.append(HALF_YEAR[m]).append(' ').append(Math.round(values[m])).append(isOver ? ", over budget. " : ". ");
        }
        float group = WIDTH / (float) values.length;
        if (words) {
            FrameLayout above = new FrameLayout(activity);
            above.setMinimumHeight(Ui.dp(activity, 22));
            for (int m = 0; m < values.length; m++) {
                if (values[m] > BUDGET) {
                    place(activity, above, small(activity, "Over", Ui.TEXT, 0), m * group, 0, group, Gravity.CENTER_HORIZONTAL);
                }
            }
            column.addView(above);
        }
        column.addView(chart(activity, id, WIDTH, BAR_HEIGHT, Charts.bars(WIDTH, BAR_HEIGHT, new float[][] {values}, 130, looks),
                description.toString().trim()));
        column.addView(axisLabels(activity, HALF_YEAR, values.length));
        column.addView(legend(activity, boxes(within, over), new String[] {"Within budget", "Over budget"}));
    }

    // ---- Scatter: dots in two colours, and two shapes ---------------------------

    private static final String[] ORCHARDS = {"Top orchard", "Low orchard"};

    static View scatter(Activity activity) {
        LinearLayout column = start(activity, "Orchard survey", "Height and age of the trees, by orchard.");
        float[][][] last = {
            {{0.05f, 0.2f}, {0.15f, 0.35f}, {0.3f, 0.4f}, {0.45f, 0.6f}, {0.6f, 0.55f}, {0.75f, 0.8f}, {0.9f, 0.85f}},
            {{0.1f, 0.1f}, {0.25f, 0.15f}, {0.4f, 0.3f}, {0.55f, 0.35f}, {0.7f, 0.45f}, {0.85f, 0.5f}, {0.95f, 0.6f}},
        };
        float[][][] now = {
            {{0.08f, 0.25f}, {0.2f, 0.3f}, {0.35f, 0.5f}, {0.5f, 0.65f}, {0.65f, 0.6f}, {0.8f, 0.85f}, {0.92f, 0.9f}},
            {{0.12f, 0.05f}, {0.28f, 0.2f}, {0.42f, 0.25f}, {0.58f, 0.4f}, {0.72f, 0.4f}, {0.88f, 0.55f}, {0.97f, 0.65f}},
        };
        Charts.Look top = new Charts.Look(MAGENTA).marker(Charts.CIRCLE);
        Charts.Look low = new Charts.Look(TEAL).marker(Charts.CIRCLE);
        Charts.Look lowTriangles = new Charts.Look(TEAL).marker(Charts.TRIANGLE);
        scatterBlock(activity, column, R.id.chart_scatter_hue, "Last survey", "the trees in the last survey", last,
                new Charts.Look[] {top, low});
        scatterBlock(activity, column, R.id.chart_scatter_shapes, "This survey", "the trees in this survey", now,
                new Charts.Look[] {top, lowTriangles});
        return column;
    }

    private static void scatterBlock(Activity activity, LinearLayout column, int id, String caption, String what,
            float[][][] points, Charts.Look[] looks) {
        column.addView(Ui.body(activity, caption));
        StringBuilder description = new StringBuilder("Scatter chart of ").append(what)
                .append(", age in years across and height in metres up. ");
        for (int s = 0; s < points.length; s++) {
            description.append(ORCHARDS[s]).append(':');
            for (float[] point : points[s]) {
                description.append(String.format(Locale.ROOT, " %d years %d metres,", Math.round(point[0] * 40), Math.round(point[1] * 10)));
            }
            description.setLength(description.length() - 1);
            description.append(". ");
        }
        column.addView(chart(activity, id, WIDTH, LINE_HEIGHT, Charts.scatter(WIDTH, LINE_HEIGHT, points, looks), description.toString().trim()));
        Charts.Painter[] swatches = new Charts.Painter[looks.length];
        for (int s = 0; s < looks.length; s++) {
            swatches[s] = Charts.markerSwatch(looks[s]);
        }
        column.addView(legend(activity, swatches, ORCHARDS));
    }

    // ---- Heat map: three hues, and the numbers in the cells ----------------------

    private static final String[] CLASSES = {"Yoga", "Pilates", "Spin"};
    private static final String[] LEVELS = {"Under 60%", "60 to 80%", "Over 80%"};
    private static final float HEAT_START = 72;
    private static final float CELL_WIDTH = 56;
    private static final float CELL_HEIGHT = 30;

    static View heat(Activity activity) {
        LinearLayout column = start(activity, "Class attendance", "How full each class was, by day.");
        Charts.Look[] looks = {new Charts.Look(RED), new Charts.Look(ORANGE), new Charts.Look(GREEN)};
        int[][] last = {{50, 72, 85, 64, 90}, {82, 55, 70, 88, 58}, {66, 91, 45, 75, 84}};
        int[][] now = {{62, 85, 58, 92, 74}, {48, 77, 89, 66, 83}, {90, 52, 79, 61, 86}};
        heatBlock(activity, column, R.id.chart_heat_hue, "Last week", "last week", last, looks, false);
        heatBlock(activity, column, R.id.chart_heat_numbers, "This week", "this week", now, looks, true);
        return column;
    }

    private static void heatBlock(Activity activity, LinearLayout column, int id, String caption, String when,
            int[][] percentages, Charts.Look[] looks, boolean numbers) {
        column.addView(Ui.body(activity, caption));
        FrameLayout days = new FrameLayout(activity);
        for (int c = 0; c < DAYS.length; c++) {
            place(activity, days, small(activity, DAYS[c], Ui.TEXT, 0), HEAT_START + c * CELL_WIDTH, 0, CELL_WIDTH - 2, Gravity.CENTER_HORIZONTAL);
        }
        days.setMinimumHeight(Ui.dp(activity, 22));
        column.addView(days);

        int[][] levels = new int[percentages.length][DAYS.length];
        StringBuilder description = new StringBuilder("Heat map of how full each class was ").append(when).append(", by day. ");
        for (int r = 0; r < percentages.length; r++) {
            description.append(CLASSES[r]).append(':');
            for (int c = 0; c < DAYS.length; c++) {
                int value = percentages[r][c];
                levels[r][c] = value < 60 ? 0 : value <= 80 ? 1 : 2;
                description.append(' ').append(DAYS[c]).append(' ').append(value).append("%,");
            }
            description.setLength(description.length() - 1);
            description.append(". ");
        }
        int height = Math.round(percentages.length * CELL_HEIGHT);
        FrameLayout frame = new FrameLayout(activity);
        frame.addView(chart(activity, id, WIDTH, height, Charts.heat(HEAT_START, CELL_WIDTH, CELL_HEIGHT, levels, looks),
                description.toString().trim()));
        for (int r = 0; r < percentages.length; r++) {
            float top = r * CELL_HEIGHT + (CELL_HEIGHT - 2) / 2 - 10;
            place(activity, frame, small(activity, CLASSES[r], Ui.TEXT, 0), 0, top, HEAT_START - 8, Gravity.START);
            if (!numbers) {
                continue;
            }
            for (int c = 0; c < DAYS.length; c++) {
                place(activity, frame, small(activity, percentages[r][c] + "%", Ui.BACKGROUND, 0),
                        HEAT_START + c * CELL_WIDTH, top, CELL_WIDTH - 2, Gravity.CENTER_HORIZONTAL);
            }
        }
        column.addView(frame);
        column.addView(legend(activity, boxes(looks), LEVELS));
    }

    // ---- A chart in an ImageView: hue alone, and a pattern -----------------------

    private static final String[] SHOW_DAYS = {"Fri", "Sat", "Sun"};
    private static final String[] SHOWINGS = {"Matinee", "Evening"};

    static View image(Activity activity) {
        LinearLayout column = start(activity, "Ticket sales", "Tickets sold for each show.");
        float[][] last = {{120, 180, 150}, {200, 240, 90}};
        float[][] now = {{140, 160, 170}, {210, 220, 110}};
        Charts.Look matinee = new Charts.Look(RED);
        Charts.Look evening = new Charts.Look(GREEN);
        Charts.Look eveningHatched = new Charts.Look(GREEN).hatched();
        imageBlock(activity, column, R.id.chart_image_hue, "Last weekend", "tickets sold last weekend", last,
                Charts.bySeries(3, matinee, evening), boxes(matinee, evening));
        imageBlock(activity, column, R.id.chart_image_pattern, "This weekend", "tickets sold this weekend", now,
                Charts.bySeries(3, matinee, eveningHatched), boxes(matinee, eveningHatched));
        return column;
    }

    private static void imageBlock(Activity activity, LinearLayout column, int id, String caption, String what,
            float[][] values, Charts.Look[][] looks, Charts.Painter[] swatches) {
        column.addView(Ui.body(activity, caption));
        ImageView picture = new ImageView(activity);
        picture.setId(id);
        picture.setImageBitmap(Charts.bitmap(activity, WIDTH, BAR_HEIGHT, Charts.bars(WIDTH, BAR_HEIGHT, values, 250, looks)));
        picture.setContentDescription(barDescription("Bar chart of the " + what, SHOW_DAYS, SHOWINGS, values));
        picture.setLayoutParams(new LinearLayout.LayoutParams(Ui.dp(activity, WIDTH), Ui.dp(activity, BAR_HEIGHT)));
        column.addView(picture);
        column.addView(axisLabels(activity, SHOW_DAYS, SHOW_DAYS.length));
        column.addView(legend(activity, swatches, SHOWINGS));
    }

    // ---- Parts -------------------------------------------------------------------

    private static final int BAR_HEIGHT = 120;
    private static final int LINE_HEIGHT = 110;

    // The screen's heading and first line, then a gap clear of what
    // TalkBack draws (see Screens.clearOfTalkBack), as the checks read the
    // charts' colours.
    private static LinearLayout start(Activity activity, String title, String line) {
        LinearLayout column = Ui.column(activity);
        column.addView(Ui.heading(activity, title));
        column.addView(Ui.body(activity, line));
        column.addView(Ui.space(activity, 1, Screens.TALKBACK_GAP_DP));
        return column;
    }

    private static void barBlock(Activity activity, LinearLayout column, int id, String caption, String what,
            String[] categories, String[] series, float[][] values, float max, Charts.Look[][] looks, Charts.Painter[] swatches) {
        column.addView(Ui.body(activity, caption));
        column.addView(chart(activity, id, WIDTH, BAR_HEIGHT, Charts.bars(WIDTH, BAR_HEIGHT, values, max, looks),
                barDescription("Bar chart of " + what, categories, series, values)));
        column.addView(axisLabels(activity, categories, categories.length));
        column.addView(legend(activity, swatches, series));
    }

    private static FrameLayout lineBlock(Activity activity, LinearLayout column, int id, String caption, String what,
            String[] series, float[][] values, float reserve, Charts.Look[] looks, View legend) {
        column.addView(Ui.body(activity, caption));
        float max = 0;
        for (float[] line : values) {
            for (float value : line) {
                max = Math.max(max, value);
            }
        }
        max = (float) Math.ceil(max * 1.1f);
        String[] months = MONTHS;
        StringBuilder description = new StringBuilder("Line chart of ").append(what).append(", by month. ");
        for (int s = 0; s < values.length; s++) {
            description.append(series[s]).append(':');
            for (int p = 0; p < values[s].length; p++) {
                description.append(' ').append(months[p]).append(' ').append(number(values[s][p])).append(',');
            }
            description.setLength(description.length() - 1);
            description.append(". ");
        }
        FrameLayout frame = new FrameLayout(activity);
        frame.addView(chart(activity, id, WIDTH, LINE_HEIGHT, Charts.lines(WIDTH, LINE_HEIGHT, reserve, values, max, looks),
                description.toString().trim()));
        column.addView(frame);
        FrameLayout labels = new FrameLayout(activity);
        labels.setMinimumHeight(Ui.dp(activity, 22));
        for (int p = 0; p < months.length; p++) {
            float x = Charts.lineX(WIDTH, reserve, months.length, p);
            place(activity, labels, small(activity, months[p], Ui.TEXT, 0), x - 24, 0, 48, Gravity.CENTER_HORIZONTAL);
        }
        column.addView(labels);
        if (legend != null) {
            column.addView(legend);
        }
        return frame;
    }

    private static Charts.ChartView chart(Activity activity, int id, int widthDp, int heightDp, Charts.Painter painter, String description) {
        Charts.ChartView chart = new Charts.ChartView(activity, widthDp, heightDp, painter);
        chart.setId(id);
        chart.setContentDescription(description);
        chart.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_YES);
        return chart;
    }

    // The categories' names under a bar chart's groups.
    private static FrameLayout axisLabels(Activity activity, String[] categories, int count) {
        FrameLayout labels = new FrameLayout(activity);
        labels.setMinimumHeight(Ui.dp(activity, 22));
        float group = WIDTH / (float) count;
        for (int c = 0; c < count; c++) {
            place(activity, labels, small(activity, categories[c], Ui.TEXT, 0), c * group, 0, group, Gravity.CENTER_HORIZONTAL);
        }
        return labels;
    }

    // A legend: each swatch, not important for accessibility, and its name.
    private static LinearLayout legend(Activity activity, Charts.Painter[] swatches, String[] names) {
        LinearLayout row = Ui.row(activity);
        row.setGravity(Gravity.CENTER_VERTICAL);
        for (int i = 0; i < names.length; i++) {
            Charts.ChartView swatch = new Charts.ChartView(activity, 28, 20, swatches[i]);
            swatch.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
            row.addView(swatch);
            row.addView(small(activity, names[i], Ui.TEXT, 16));
        }
        return row;
    }

    private static Charts.Painter[] boxes(Charts.Look... looks) {
        Charts.Painter[] swatches = new Charts.Painter[looks.length];
        for (int i = 0; i < looks.length; i++) {
            swatches[i] = Charts.boxSwatch(looks[i]);
        }
        return swatches;
    }

    private static Charts.Painter[] lineSwatches(Charts.Look... looks) {
        Charts.Painter[] swatches = new Charts.Painter[looks.length];
        for (int i = 0; i < looks.length; i++) {
            swatches[i] = Charts.lineSwatch(looks[i]);
        }
        return swatches;
    }

    // Words at 14 sp, with no padding but endDp on the right.
    private static TextView small(Activity activity, String text, int colour, int endDp) {
        TextView view = Ui.text(activity, text, 14, colour);
        view.setPadding(Ui.dp(activity, 4), 0, Ui.dp(activity, endDp), 0);
        return view;
    }

    // Puts a view in a frame at x and y, in dp, widthDp wide.
    private static void place(Activity activity, FrameLayout frame, TextView view, float xDp, float yDp, float widthDp, int gravity) {
        view.setGravity(gravity);
        view.setPadding(0, 0, 0, 0);
        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(Ui.dp(activity, widthDp), FrameLayout.LayoutParams.WRAP_CONTENT);
        params.leftMargin = Ui.dp(activity, Math.max(0, xDp));
        params.topMargin = Ui.dp(activity, Math.max(0, yDp));
        frame.addView(view, params);
    }

    private static String barDescription(String start, String[] categories, String[] series, float[][] values) {
        StringBuilder description = new StringBuilder(start).append(". ");
        for (int c = 0; c < categories.length; c++) {
            description.append(categories[c]).append(':');
            for (int s = 0; s < series.length; s++) {
                description.append(' ').append(series[s].toLowerCase(Locale.ROOT)).append(' ').append(number(values[s][c])).append(',');
            }
            description.setLength(description.length() - 1);
            description.append(". ");
        }
        return description.toString().trim();
    }

    private static String shares(String[] names, float[] percentages) {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < names.length; i++) {
            text.append(i == 0 ? "" : ", ").append(names[i]).append(' ').append(Math.round(percentages[i])).append('%');
        }
        return text.append('.').toString();
    }

    private static String number(float value) {
        return value == Math.round(value) ? Integer.toString(Math.round(value)) : String.format(Locale.ROOT, "%.1f", value);
    }
}
