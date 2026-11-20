package io.github.bobdodd.a11yrecorder.fixtures;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

/**
 * Charts for the check of a chart that relies on colour (WCAG 1.4.1),
 * drawn as graphics only. Every word a chart shows, its legend, labels and
 * values, is a TextView beside or over it, so each line OCR reads is in the
 * tree and the check of text not in the tree has nothing to find. Sizes are
 * fixed in dp, so the screens can put the words where the graphics are.
 */
final class Charts {
    static final int NONE = 0;
    static final int CIRCLE = 1;
    static final int SQUARE = 2;
    static final int TRIANGLE = 3;

    /** The axes' and outlines' colour, Ui.TEXT, about 16:1 on white. */
    static final int INK = Ui.TEXT;

    private Charts() {
    }

    /** Draws a chart or a swatch on a canvas of the given size, in dp. */
    interface Painter {
        void paint(Canvas canvas, float dp);
    }

    /** How one series, slice, segment or level is drawn. */
    static final class Look {
        final int colour;
        boolean hatched;
        boolean outlined;
        boolean dashedOutline;
        int marker = NONE;
        boolean dashedLine;

        Look(int colour) {
            this.colour = colour;
        }

        /** White diagonal stripes over the fill, a pattern as G111 has. */
        Look hatched() {
            hatched = true;
            return this;
        }

        /** A 2 dp outline in INK. */
        Look outlined() {
            outlined = true;
            return this;
        }

        /** A 2 dp outline in INK, dashed. */
        Look dashedOutline() {
            outlined = true;
            dashedOutline = true;
            return this;
        }

        Look marker(int marker) {
            this.marker = marker;
            return this;
        }

        Look dashedLine() {
            dashedLine = true;
            return this;
        }
    }

    /** A view of the given size in dp that the painter draws. */
    static final class ChartView extends View {
        private final Painter painter;
        private final int widthPx;
        private final int heightPx;

        ChartView(Context context, int widthDp, int heightDp, Painter painter) {
            super(context);
            this.painter = painter;
            this.widthPx = Ui.dp(context, widthDp);
            this.heightPx = Ui.dp(context, heightDp);
        }

        @Override
        protected void onMeasure(int widthMeasureSpec, int heightMeasureSpec) {
            setMeasuredDimension(widthPx, heightPx);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            painter.paint(canvas, getResources().getDisplayMetrics().density);
        }
    }

    /** The chart as a bitmap, for a chart shown in an ImageView. */
    static Bitmap bitmap(Context context, int widthDp, int heightDp, Painter painter) {
        Bitmap bitmap = Bitmap.createBitmap(Ui.dp(context, widthDp), Ui.dp(context, heightDp), Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        canvas.drawColor(Ui.BACKGROUND);
        painter.paint(canvas, context.getResources().getDisplayMetrics().density);
        return bitmap;
    }

    // ---- Charts ------------------------------------------------------------

    /**
     * Vertical bars in groups, one group a category, one bar a series,
     * over a baseline. looks[series][category] says how each bar is drawn.
     */
    static Painter bars(int widthDp, int heightDp, float[][] values, float max, Look[][] looks) {
        return (canvas, dp) -> {
            int categories = values[0].length;
            int series = values.length;
            float group = widthDp / (float) categories;
            float bar = group * 0.6f / series;
            float bottom = heightDp - 1;
            for (int c = 0; c < categories; c++) {
                float left = c * group + group * 0.2f;
                for (int s = 0; s < series; s++) {
                    float top = bottom - (heightDp - 8) * values[s][c] / max;
                    rect(canvas, dp, new RectF(left + s * bar, top, left + (s + 1) * bar - 2, bottom), looks[s][c]);
                }
            }
            baseline(canvas, dp, widthDp, bottom);
        };
    }

    /** The same look for every bar of a series. */
    static Look[][] bySeries(int categories, Look... looks) {
        Look[][] all = new Look[looks.length][categories];
        for (int s = 0; s < looks.length; s++) {
            for (int c = 0; c < categories; c++) {
                all[s][c] = looks[s];
            }
        }
        return all;
    }

    /**
     * Horizontal bars, each split into segments of the given percentages,
     * 2 dp of white between segments, starting at startDp.
     */
    static Painter stacked(int widthDp, float startDp, float barDp, float gapDp, float[][] percentages, Look[] looks) {
        return (canvas, dp) -> {
            float span = widthDp - startDp;
            for (int b = 0; b < percentages.length; b++) {
                float top = b * (barDp + gapDp);
                float left = startDp;
                for (int s = 0; s < percentages[b].length; s++) {
                    float width = span * percentages[b][s] / 100f;
                    rect(canvas, dp, new RectF(left, top, left + width - 2, top + barDp), looks[s]);
                    left += width;
                }
            }
        };
    }

    /** Where segment s of bar b starts, in dp, for its label. */
    static float segmentStart(int widthDp, float startDp, float[] percentages, int s) {
        float left = startDp;
        for (int i = 0; i < s; i++) {
            left += (widthDp - startDp) * percentages[i] / 100f;
        }
        return left;
    }

    /**
     * Lines of points spread evenly across widthDp less reserveDp, which is
     * left on the right for labels, over a baseline.
     */
    static Painter lines(int widthDp, int heightDp, float reserveDp, float[][] values, float max, Look[] looks) {
        return (canvas, dp) -> {
            float bottom = heightDp - 1;
            for (int s = 0; s < values.length; s++) {
                Paint paint = stroke(looks[s].colour, 3, dp);
                if (looks[s].dashedLine) {
                    paint.setPathEffect(new DashPathEffect(new float[] {8 * dp, 5 * dp}, 0));
                }
                Path path = new Path();
                for (int p = 0; p < values[s].length; p++) {
                    float x = lineX(widthDp, reserveDp, values[s].length, p);
                    float y = lineY(heightDp, values[s][p], max);
                    if (p == 0) {
                        path.moveTo(x * dp, y * dp);
                    } else {
                        path.lineTo(x * dp, y * dp);
                    }
                }
                canvas.drawPath(path, paint);
                for (int p = 0; p < values[s].length; p++) {
                    marker(canvas, dp, lineX(widthDp, reserveDp, values[s].length, p), lineY(heightDp, values[s][p], max), 8, looks[s]);
                }
            }
            baseline(canvas, dp, widthDp - reserveDp, bottom);
        };
    }

    static float lineX(int widthDp, float reserveDp, int points, int p) {
        return 8 + (widthDp - reserveDp - 16) * p / (float) (points - 1);
    }

    static float lineY(int heightDp, float value, float max) {
        return heightDp - 6 - (heightDp - 14) * value / max;
    }

    /**
     * A pie of the given percentages, diameter sizeDp, its left edge at
     * leftDp, 2 dp of white between slices, as G209 has. With places (see
     * pieLabelPlaces), a line from each slice to its label's place.
     */
    static Painter pie(float leftDp, float sizeDp, float[] percentages, Look[] looks, float[][] places) {
        return (canvas, dp) -> {
            RectF box = new RectF((leftDp + 1) * dp, 1 * dp, (leftDp + sizeDp - 1) * dp, (sizeDp - 1) * dp);
            Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
            float start = -90;
            for (int s = 0; s < percentages.length; s++) {
                float sweep = 360 * percentages[s] / 100f;
                fill.setColor(looks[s].colour);
                canvas.drawArc(box, start, sweep, true, fill);
                start += sweep;
            }
            Paint gap = stroke(Ui.BACKGROUND, 2, dp);
            float radius = sizeDp / 2;
            float cx = leftDp + radius;
            start = -90;
            for (float percentage : percentages) {
                double angle = Math.toRadians(start);
                canvas.drawLine(cx * dp, radius * dp,
                        (float) (cx + radius * Math.cos(angle)) * dp, (float) (radius + radius * Math.sin(angle)) * dp, gap);
                start += 360 * percentage / 100f;
            }
            if (places == null) {
                return;
            }
            Paint leader = stroke(INK, 1, dp);
            start = -90;
            for (int s = 0; s < percentages.length; s++) {
                float sweep = 360 * percentages[s] / 100f;
                double middle = Math.toRadians(start + sweep / 2);
                float x = (float) (cx + radius * 0.7 * Math.cos(middle));
                float y = (float) (radius + radius * 0.7 * Math.sin(middle));
                canvas.drawCircle(x * dp, y * dp, 2.5f * dp, filled(INK));
                canvas.drawLine(x * dp, y * dp, places[s][0] * dp, places[s][1] * dp, leader);
                start += sweep;
            }
        };
    }

    /**
     * Where each slice's label goes: { the end of its leader's x, its y,
     * 1 for a label on the right or -1 on the left }, in dp, the labels on
     * each side at least 24 dp apart and within the pie's height.
     */
    static float[][] pieLabelPlaces(float leftDp, float sizeDp, float[] percentages) {
        float radius = sizeDp / 2;
        float[][] places = new float[percentages.length][3];
        float start = -90;
        for (int s = 0; s < percentages.length; s++) {
            float sweep = 360 * percentages[s] / 100f;
            double middle = Math.toRadians(start + sweep / 2);
            boolean right = Math.cos(middle) >= 0;
            places[s][0] = right ? leftDp + sizeDp + 8 : leftDp - 8;
            places[s][1] = (float) Math.max(12, Math.min(sizeDp - 12, radius + radius * 1.05 * Math.sin(middle)));
            places[s][2] = right ? 1 : -1;
            start += sweep;
        }
        for (int side = -1; side <= 1; side += 2) {
            float last = -1000;
            for (int s = 0; s < percentages.length; s++) {
                int index = side == 1 ? s : percentages.length - 1 - s;
                if (places[index][2] != side) {
                    continue;
                }
                places[index][1] = Math.max(places[index][1], last + 24);
                last = places[index][1];
            }
        }
        return places;
    }

    /** Points, x and y each 0 to 1, in each series' marker, over a baseline. */
    static Painter scatter(int widthDp, int heightDp, float[][][] points, Look[] looks) {
        return (canvas, dp) -> {
            for (int s = 0; s < points.length; s++) {
                for (float[] point : points[s]) {
                    float x = 8 + (widthDp - 16) * point[0];
                    float y = heightDp - 8 - (heightDp - 16) * point[1];
                    marker(canvas, dp, x, y, 10, looks[s]);
                }
            }
            baseline(canvas, dp, widthDp, heightDp - 1);
        };
    }

    /** A grid of cells, each in its level's look, startDp from the left, 2 dp of white between. */
    static Painter heat(float startDp, float cellWidthDp, float cellHeightDp, int[][] levels, Look[] looks) {
        return (canvas, dp) -> {
            for (int r = 0; r < levels.length; r++) {
                for (int c = 0; c < levels[r].length; c++) {
                    float left = startDp + c * cellWidthDp;
                    float top = r * cellHeightDp;
                    rect(canvas, dp, new RectF(left, top, left + cellWidthDp - 2, top + cellHeightDp - 2), looks[levels[r][c]]);
                }
            }
        };
    }

    // ---- Swatches, for legends ----------------------------------------------

    /** A filled box, as the bars are drawn. */
    static Painter boxSwatch(Look look) {
        return (canvas, dp) -> rect(canvas, dp, new RectF(2, 2, 22, 18), look);
    }

    /** A short line with its marker, as the lines are drawn. */
    static Painter lineSwatch(Look look) {
        return (canvas, dp) -> {
            Paint paint = stroke(look.colour, 3, dp);
            if (look.dashedLine) {
                paint.setPathEffect(new DashPathEffect(new float[] {6 * dp, 3 * dp}, 0));
            }
            canvas.drawLine(1 * dp, 10 * dp, 27 * dp, 10 * dp, paint);
            marker(canvas, dp, 14, 10, 8, look);
        };
    }

    /** A marker alone, as the points are drawn. */
    static Painter markerSwatch(Look look) {
        return (canvas, dp) -> marker(canvas, dp, 12, 10, 10, look);
    }

    // ---- Drawing -------------------------------------------------------------

    private static void rect(Canvas canvas, float dp, RectF box, Look look) {
        RectF px = new RectF(box.left * dp, box.top * dp, box.right * dp, box.bottom * dp);
        canvas.drawRect(px, filled(look.colour));
        if (look.hatched) {
            canvas.save();
            canvas.clipRect(px);
            Paint stripe = stroke(Ui.BACKGROUND, 2, dp);
            float step = 7 * dp;
            for (float x = px.left - px.height(); x < px.right; x += step) {
                canvas.drawLine(x, px.bottom, x + px.height(), px.top, stripe);
            }
            canvas.restore();
        }
        if (look.outlined) {
            Paint outline = stroke(INK, 2, dp);
            if (look.dashedOutline) {
                outline.setPathEffect(new DashPathEffect(new float[] {5 * dp, 4 * dp}, 0));
            }
            canvas.drawRect(px.left + dp, px.top + dp, px.right - dp, px.bottom - dp, outline);
        }
    }

    private static void marker(Canvas canvas, float dp, float x, float y, float size, Look look) {
        float half = size / 2 * dp;
        float cx = x * dp;
        float cy = y * dp;
        Paint paint = filled(look.colour);
        switch (look.marker) {
            case CIRCLE:
                canvas.drawCircle(cx, cy, half, paint);
                break;
            case SQUARE:
                canvas.drawRect(cx - half, cy - half, cx + half, cy + half, paint);
                break;
            case TRIANGLE:
                Path path = new Path();
                path.moveTo(cx, cy - half);
                path.lineTo(cx + half, cy + half);
                path.lineTo(cx - half, cy + half);
                path.close();
                canvas.drawPath(path, paint);
                break;
            default:
                break;
        }
    }

    private static void baseline(Canvas canvas, float dp, float widthDp, float yDp) {
        canvas.drawLine(0, yDp * dp, widthDp * dp, yDp * dp, stroke(INK, 1, dp));
    }

    private static Paint filled(int colour) {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setColor(colour);
        paint.setStyle(Paint.Style.FILL);
        return paint;
    }

    private static Paint stroke(int colour, float widthDp, float dp) {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setColor(colour);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(widthDp * dp);
        return paint;
    }
}
