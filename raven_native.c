/*
 * raven_native.c — Native C imaging functions with x87 extended-precision math.
 *
 * Compiled with -mfpmath=387 to use x87 FPU for all floating-point operations,
 * matching Delphi's extended precision (80-bit) behavior exactly.
 *
 * Build (64-bit Windows DLL, from WSL):
 *   x86_64-w64-mingw32-gcc -mfpmath=387 -O2 -shared -DWIN32 \
 *     -o raven_native.dll raven_native.c
 *
 * Build (32-bit for testing against original DLL):
 *   i686-w64-mingw32-gcc -mfpmath=387 -O2 -shared -DWIN32 \
 *     -o raven_native_32.dll raven_native.c
 *
 * Functions exported:
 *   - AdaptiveThresholdAverage  (replaces ImgAdaptiveThresholdAverage pipeline)
 *   - DynamicThresholdAverage   (replaces ImgDynamicThresholdAverage pipeline)
 *   - RemoveBleedThrough        (replaces ImgRemoveBleedThrough pixel loop)
 *   - RefineThreshold           (replaces ImgRefineThreshold CC analysis)
 *   - BoxAverage_x87            (shared helper, also usable standalone)
 *   - RgbToGray                 (RGB to grayscale conversion)
 *   - Despeckle                 (remove small CCs from 1bpp)
 *   - RemoveBlackWires          (remove isolated noise pixels from 1bpp)
 *   - RemoveVerticalLines       (remove vertical lines from 1bpp)
 *   - AutoThreshold             (Otsu/Kittler/Ridler-Calvard threshold)
 *   - FindBlackBorder           (detect black border in 1/8/24bpp)
 */

#include <stdlib.h>
#include <string.h>
#include <math.h>
#ifdef _OPENMP
#include <omp.h>
#endif

#ifdef _WIN32
#define EXPORT __declspec(dllexport) __stdcall
#else
#define EXPORT
#endif

/* ══════════════════════════════════════════════════════════════════════
 * x87 FPU helpers
 * ══════════════════════════════════════════════════════════════════════ */

static void set_fpu_extended(void) {
    unsigned short cw = 0x037F;  /* extended prec, round-to-nearest, all masked */
    __asm__ volatile ("fldcw %0" : : "m"(cw));
}

static int bankers_round(long double val) {
    int result;
    __asm__ volatile (
        "fistpl %0"
        : "=m"(result)
        : "t"(val)
        : "st"
    );
    return result;
}

/* ══════════════════════════════════════════════════════════════════════
 * Grayscale LUT (NTSC weights: R=0.30, G=0.59, B=0.11)
 * ══════════════════════════════════════════════════════════════════════ */

static unsigned char g_lutR[256], g_lutG[256], g_lutB[256];
static int g_luts_init = 0;

static void init_luts(void) {
    if (g_luts_init) return;
    set_fpu_extended();
    for (int i = 0; i < 256; i++) {
        g_lutR[i] = (unsigned char)bankers_round((long double)i * 0.30L);
        g_lutG[i] = (unsigned char)bankers_round((long double)i * 0.59L);
        g_lutB[i] = (unsigned char)bankers_round((long double)i * 0.11L);
    }
    g_luts_init = 1;
}

/* Convert 24bpp BGR pixel data to grayscale using x87 extended precision LUT */
void EXPORT RgbToGray(const unsigned char *bgr, unsigned char *gray,
                      int w, int h, int stride) {
    init_luts();
    for (int y = 0; y < h; y++) {
        const unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            gray[y * w + x] = g_lutR[rv] + g_lutG[gv] + g_lutB[bv];
        }
    }
}

/* ══════════════════════════════════════════════════════════════════════
 * Integral image
 * ══════════════════════════════════════════════════════════════════════ */

static long long *compute_integral(const unsigned char *data, int w, int h) {
    int iw = w + 1;
    long long *integral = (long long *)calloc((size_t)iw * (h + 1), sizeof(long long));
    if (!integral) return NULL;
    for (int y = 0; y < h; y++) {
        long long rowSum = 0;
        for (int x = 0; x < w; x++) {
            rowSum += data[y * w + x];
            integral[(y + 1) * iw + (x + 1)] = rowSum + integral[y * iw + (x + 1)];
        }
    }
    return integral;
}

/* Forward declarations for functions defined later */
static void box_average_fast(const unsigned char *input, unsigned char *output,
                              int w, int h, int blockW, int blockH);
static void box_average_fast_par(const unsigned char *input, unsigned char *output,
                                  int w, int h, int blockW, int blockH);
static void sobel_magnitude_l1(const unsigned char *gray, unsigned char *sobel, int w, int h);
#ifdef _OPENMP
static void sobel_magnitude_l1_par(const unsigned char *gray, unsigned char *sobel, int w, int h);
#define SOBEL_IMPL sobel_magnitude_l1_par
#else
#define SOBEL_IMPL sobel_magnitude_l1
#endif

/* ══════════════════════════════════════════════════════════════════════
 * Box average with x87 extended precision + banker's rounding
 * Matches DLL's 0xa41c08 (FPU CW 0x1332, FISTP for rounding)
 * ══════════════════════════════════════════════════════════════════════ */

static void box_average_x87(const unsigned char *input, unsigned char *output,
                             int w, int h, int blockW, int blockH) {
    set_fpu_extended();
    int halfW = (blockW + 1) >> 1;
    if (halfW < 1) halfW = 1;
    int halfH = (blockH + 1) >> 1;
    if (halfH < 1) halfH = 1;

    long long *integral = compute_integral(input, w, h);
    if (!integral) return;
    int iw = w + 1;

    for (int y = 0; y < h; y++) {
        int y1 = y - halfH; if (y1 < 0) y1 = 0;
        int y2 = y + halfH; if (y2 > h - 1) y2 = h - 1;
        for (int x = 0; x < w; x++) {
            int x1 = x - halfW; if (x1 < 0) x1 = 0;
            int x2 = x + halfW; if (x2 > w - 1) x2 = w - 1;
            long long sum = integral[(y2 + 1) * iw + (x2 + 1)]
                          - integral[y1 * iw + (x2 + 1)]
                          - integral[(y2 + 1) * iw + x1]
                          + integral[y1 * iw + x1];
            int count = (y2 - y1 + 1) * (x2 - x1 + 1);
            int val = bankers_round((long double)sum / (long double)count);
            if (val < 0) val = 0;
            if (val > 255) val = 255;
            output[y * w + x] = (unsigned char)val;
        }
    }
    free(integral);
}

void EXPORT BoxAverage_x87(const unsigned char *input, unsigned char *output,
                           int w, int h, int blockW, int blockH) {
    box_average_x87(input, output, w, h, blockW, blockH);
}

/* ══════════════════════════════════════════════════════════════════════
 * Sobel L1 magnitude: min(|Gx|+|Gy|, 255)
 * Matches DLL 0xa7f764
 * ══════════════════════════════════════════════════════════════════════ */

static void sobel_magnitude_l1(const unsigned char *gray, unsigned char *sobel, int w, int h) {
    if (h == 0 || w == 0) return;

    /* Row 0: boundary row with clamped yp */
    {
        const unsigned char *rp = gray;           /* yp = 0 (clamped) */
        const unsigned char *rc = gray;           /* y = 0 */
        const unsigned char *rn = h > 1 ? gray + w : gray; /* yn */
        /* x = 0 (boundary) */
        {
            int tl = rp[0], tc = rp[0], tr = rp[w > 1 ? 1 : 0];
            int cl = rc[0],              cr = rc[w > 1 ? 1 : 0];
            int bl = rn[0], bc = rn[0], br = rn[w > 1 ? 1 : 0];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            sobel[0] = (unsigned char)(mag < 255 ? mag : 255);
        }
        /* x = 1..w-2 (interior columns) */
        for (int x = 1; x < w - 1; x++) {
            int tl = rp[x-1], tc = rp[x], tr = rp[x+1];
            int cl = rc[x-1],              cr = rc[x+1];
            int bl = rn[x-1], bc = rn[x], br = rn[x+1];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            sobel[x] = (unsigned char)(mag < 255 ? mag : 255);
        }
        /* x = w-1 (boundary), only if w > 1 */
        if (w > 1) {
            int x = w - 1;
            int tl = rp[x-1], tc = rp[x], tr = rp[x];
            int cl = rc[x-1],              cr = rc[x];
            int bl = rn[x-1], bc = rn[x], br = rn[x];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            sobel[x] = (unsigned char)(mag < 255 ? mag : 255);
        }
    }

    /* Rows 1..h-2: bulk interior with row-pointer pre-computation */
    for (int y = 1; y < h - 1; y++) {
        const unsigned char *rp = gray + (y - 1) * w;
        const unsigned char *rc = gray + y * w;
        const unsigned char *rn = gray + (y + 1) * w;
        unsigned char *out = sobel + y * w;

        /* x = 0 (left boundary) */
        {
            int tl = rp[0], tc = rp[0], tr = rp[1];
            int cl = rc[0],              cr = rc[1];
            int bl = rn[0], bc = rn[0], br = rn[1];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            out[0] = (unsigned char)(mag < 255 ? mag : 255);
        }

        /* x = 1..w-2 (interior: no boundary checks) */
        for (int x = 1; x < w - 1; x++) {
            int tl = rp[x-1], tc = rp[x], tr = rp[x+1];
            int cl = rc[x-1],              cr = rc[x+1];
            int bl = rn[x-1], bc = rn[x], br = rn[x+1];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            out[x] = (unsigned char)(mag < 255 ? mag : 255);
        }

        /* x = w-1 (right boundary) */
        {
            int x = w - 1;
            int tl = rp[x-1], tc = rp[x], tr = rp[x];
            int cl = rc[x-1],              cr = rc[x];
            int bl = rn[x-1], bc = rn[x], br = rn[x];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            out[x] = (unsigned char)(mag < 255 ? mag : 255);
        }
    }

    /* Row h-1: boundary row with clamped yn */
    if (h > 1) {
        int y = h - 1;
        const unsigned char *rp = gray + (y - 1) * w;
        const unsigned char *rc = gray + y * w;
        const unsigned char *rn = rc;  /* yn clamped to h-1 */
        unsigned char *out = sobel + y * w;
        /* x = 0 */
        {
            int tl = rp[0], tc = rp[0], tr = rp[w > 1 ? 1 : 0];
            int cl = rc[0],              cr = rc[w > 1 ? 1 : 0];
            int bl = rn[0], bc = rn[0], br = rn[w > 1 ? 1 : 0];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            out[0] = (unsigned char)(mag < 255 ? mag : 255);
        }
        for (int x = 1; x < w - 1; x++) {
            int tl = rp[x-1], tc = rp[x], tr = rp[x+1];
            int cl = rc[x-1],              cr = rc[x+1];
            int bl = rn[x-1], bc = rn[x], br = rn[x+1];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            out[x] = (unsigned char)(mag < 255 ? mag : 255);
        }
        if (w > 1) {
            int x = w - 1;
            int tl = rp[x-1], tc = rp[x], tr = rp[x];
            int cl = rc[x-1],              cr = rc[x];
            int bl = rn[x-1], bc = rn[x], br = rn[x];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            out[x] = (unsigned char)(mag < 255 ? mag : 255);
        }
    }
}

/* ══════════════════════════════════════════════════════════════════════
 * Otsu threshold
 * ══════════════════════════════════════════════════════════════════════ */

static int otsu_threshold(const unsigned char *data, int total) {
    long long hist[256];
    memset(hist, 0, sizeof(hist));
    for (int i = 0; i < total; i++) hist[data[i]]++;

    long long sumAll = 0;
    for (int i = 0; i < 256; i++) sumAll += (long long)i * hist[i];

    long long sumB = 0, wB = 0;
    double maxVar = 0;
    int bestT = 0;
    for (int t = 0; t < 256; t++) {
        wB += hist[t];
        if (wB == 0) continue;
        long long wF = total - wB;
        if (wF == 0) break;
        sumB += (long long)t * hist[t];
        double mB = (double)sumB / (double)wB;
        double mF = (double)(sumAll - sumB) / (double)wF;
        double diff = mB - mF;
        double var = (double)wB * (double)wF * diff * diff;
        if (var > maxVar) { maxVar = var; bestT = t; }
    }
    return bestT;
}

/* ══════════════════════════════════════════════════════════════════════
 * DifferenceHistogram — 4-neighbor diffs, rows 1..h-2, cols 1..w-2
 * Matches DLL 0xa5d6c8 (8bpp path)
 * ══════════════════════════════════════════════════════════════════════ */

static void difference_histogram(const unsigned char *gray, int w, int h, long long *hist) {
    memset(hist, 0, 256 * sizeof(long long));
    for (int row = 1; row <= h - 2; row++) {
        for (int col = 1; col <= w - 2; col++) {
            int val = gray[row * w + col];
            hist[abs(val - gray[(row+1)*w + col])]++;
            hist[abs(val - gray[(row-1)*w + col])]++;
            hist[abs(val - gray[row*w + col+1])]++;
            hist[abs(val - gray[row*w + col-1])]++;
        }
    }
}

/* ══════════════════════════════════════════════════════════════════════
 * AutoThreshold — i²+scaled² minimization with 1/3 mode check
 * Matches DLL 0xa60134
 * ══════════════════════════════════════════════════════════════════════ */

static int auto_threshold(const long long *hist) {
    set_fpu_extended();
    long long maxCount = 0;
    for (int i = 0; i < 256; i++)
        if (hist[i] > maxCount) maxCount = hist[i];
    if (maxCount == 0) return 0;

    long double scale = 256.0L / (long double)maxCount;
    int result = 0;
    long long prevAccum = 0;
    int prevAccumSet = 0;

    for (int i = 1; i < 256; i++) {
        long long scaled = (long long)bankers_round((long double)hist[i] * scale);
        long long accum = (long long)i * i + scaled * scaled;
        if (accum < prevAccum || !prevAccumSet) {
            int hasLarger = 0;
            long long th3 = hist[i] * 3;
            for (int j = i + 1; j < 256; j++)
                if (hist[j] > th3) { hasLarger = 1; break; }
            if (!hasLarger) {
                prevAccum = accum;
                prevAccumSet = 1;
                result = i;
            }
        }
    }
    return result;
}

/* ══════════════════════════════════════════════════════════════════════
 * DynamicThreshold on gate image
 * Applies: Otsu offset → DiffHist → AutoThreshold → BoxAvg compare → post-binarize
 * Matches DLL 0xa7712c
 * ══════════════════════════════════════════════════════════════════════ */

static void dynamic_threshold_on_gate(unsigned char *gate, int w, int h,
                                       int blockW, int blockH) {
    int total = w * h;
    int offset = otsu_threshold(gate, total);

    long long diffHist[256];
    difference_histogram(gate, w, h, diffHist);
    int threshold = auto_threshold(diffHist);
    if (threshold > 255) threshold = 255;

    unsigned char *filterGate = (unsigned char *)malloc(total);
    box_average_fast(gate, filterGate, w, h, blockW, blockH);

    for (int i = 0; i < total; i++) {
        unsigned char g = gate[i];
        int diff = (int)filterGate[i] - (int)g;
        if (abs(diff) > threshold)
            g = (unsigned char)(diff > 0 ? 0 : 255);
        gate[i] = (unsigned char)(g > offset ? 255 : 0);
    }
    free(filterGate);
}

/* Test version: DynamicThreshold with overridable threshold */
static void dynamic_threshold_on_gate_ex(unsigned char *gate, int w, int h,
                                          int blockW, int blockH, int forceTh) {
    int total = w * h;
    int offset = otsu_threshold(gate, total);

    int threshold;
    if (forceTh >= 0) {
        threshold = forceTh;
    } else {
        long long diffHist[256];
        difference_histogram(gate, w, h, diffHist);
        threshold = auto_threshold(diffHist);
        if (threshold > 255) threshold = 255;
    }

    unsigned char *filterGate = (unsigned char *)malloc(total);
    box_average_fast(gate, filterGate, w, h, blockW, blockH);

    for (int i = 0; i < total; i++) {
        unsigned char g = gate[i];
        int diff = (int)filterGate[i] - (int)g;
        if (abs(diff) > threshold)
            g = (unsigned char)(diff > 0 ? 0 : 255);
        gate[i] = (unsigned char)(g > offset ? 255 : 0);
    }
    free(filterGate);
}

/* AdaptiveThreshold with overridable DT threshold for testing */
void EXPORT AdaptiveThresholdTest(
    const unsigned char *gray, unsigned char *result,
    int w, int h, int blockW, int blockH, int contrast, int brightness,
    int forceDTThreshold)
{
    int total = w * h;
    set_fpu_extended();

    int invertedBrightness;
    if (brightness < 0)
        invertedBrightness = otsu_threshold(gray, total);
    else {
        invertedBrightness = 255 - brightness;
        if (invertedBrightness > 255) invertedBrightness = 255;
    }

    unsigned char *sobelMag = (unsigned char *)malloc(total);
    sobel_magnitude_l1(gray, sobelMag, w, h);

    unsigned char *gateImage = (unsigned char *)malloc(total);
    box_average_fast(sobelMag, gateImage, w, h, blockW, blockH);
    free(sobelMag);

    int gate;
    if (contrast < 0 && contrast != -2) {
        gate = 128;
        dynamic_threshold_on_gate_ex(gateImage, w, h, blockW, blockH, forceDTThreshold);
    } else if (contrast == -2) {
        gate = otsu_threshold(gateImage, total);
    } else {
        gate = contrast;
        if (gate > 255) gate = 255;
    }

    unsigned char *filterImage = (unsigned char *)malloc(total);
    box_average_fast(gray, filterImage, w, h, blockW + 2, blockH + 2);

    memcpy(result, gray, total);
    for (int i = 0; i < total; i++) {
        if (gateImage[i] >= gate)
            result[i] = (unsigned char)(result[i] < filterImage[i] ? 0 : 255);
    }
    free(gateImage);
    free(filterImage);

    for (int i = 0; i < total; i++)
        result[i] = (unsigned char)(result[i] > invertedBrightness ? 255 : 0);
}

/* Diagnostic version: expose DynamicThreshold internals */
void EXPORT DynamicThresholdDiag(
    unsigned char *gate, unsigned char *out_filterGate, int *out_params,
    int w, int h, int blockW, int blockH)
{
    int total = w * h;
    set_fpu_extended();
    int offset = otsu_threshold(gate, total);

    long long diffHist[256];
    difference_histogram(gate, w, h, diffHist);
    int threshold = auto_threshold(diffHist);
    if (threshold > 255) threshold = 255;

    /* Also compute Otsu on diffHist for comparison */
    long long sumAll = 0, totalDH = 0;
    for (int i = 0; i < 256; i++) { sumAll += (long long)i * diffHist[i]; totalDH += diffHist[i]; }
    long long sumB = 0, wB = 0;
    double maxVar = 0; int otsuDH = 0;
    for (int t = 0; t < 256; t++) {
        wB += diffHist[t]; if (wB == 0) continue;
        long long wF = totalDH - wB; if (wF == 0) break;
        sumB += (long long)t * diffHist[t];
        double mB = (double)sumB / (double)wB;
        double mF = (double)(sumAll - sumB) / (double)wF;
        double d = mB - mF;
        double v = (double)wB * (double)wF * d * d;
        if (v > maxVar) { maxVar = v; otsuDH = t; }
    }

    out_params[0] = offset;
    out_params[1] = threshold;
    out_params[2] = otsuDH;
    /* Store first 64 diffHist entries packed into out_params[3..66] */
    for (int i = 0; i < 64; i++)
        out_params[3 + i] = (int)diffHist[i];

    box_average_fast(gate, out_filterGate, w, h, blockW, blockH);

    for (int i = 0; i < total; i++) {
        int diff = (int)out_filterGate[i] - (int)gate[i];
        if (abs(diff) > threshold)
            gate[i] = (unsigned char)(diff > 0 ? 0 : 255);
    }

    for (int i = 0; i < total; i++)
        gate[i] = (unsigned char)(gate[i] > offset ? 255 : 0);
}

/* ══════════════════════════════════════════════════════════════════════
 * AdaptiveThresholdAverage — full pipeline
 *
 * Input:  gray[w*h] — grayscale pixel data
 * Output: result[w*h] — binary 0/255
 *
 * Matches DLL 0xa79fd8 (internal) / 0xb9031c (export)
 * ══════════════════════════════════════════════════════════════════════ */

void EXPORT AdaptiveThresholdAverage(
    const unsigned char *gray, unsigned char *result,
    int w, int h, int blockW, int blockH, int contrast, int brightness)
{
    int total = w * h;
    set_fpu_extended();

    /* 1. invertedBrightness */
    int invertedBrightness;
    if (brightness < 0)
        invertedBrightness = otsu_threshold(gray, total);
    else {
        invertedBrightness = 255 - brightness;
        if (invertedBrightness > 255) invertedBrightness = 255;
    }

    /* 2. Sobel L1 edge magnitude (parallel rows when OMP available) */
    unsigned char *sobelMag = (unsigned char *)malloc(total);
    SOBEL_IMPL(gray, sobelMag, w, h);

    /* 3. Box average of Sobel → gateImage (parallel interior rows) */
    unsigned char *gateImage = (unsigned char *)malloc(total);
    box_average_fast_par(sobelMag, gateImage, w, h, blockW, blockH);
    free(sobelMag);

    /* 4. Contrast → gate threshold */
    int gate;
    if (contrast < 0 && contrast != -2) {
        gate = 128;
        /* Inline DT-on-gate with parallel box avg */
        {
            int offset = otsu_threshold(gateImage, total);
            long long diffHist[256];
            difference_histogram(gateImage, w, h, diffHist);
            int threshold = auto_threshold(diffHist);
            if (threshold > 255) threshold = 255;
            unsigned char *filterGate = (unsigned char *)malloc(total);
            box_average_fast_par(gateImage, filterGate, w, h, blockW, blockH);

            #ifdef _OPENMP
            #pragma omp parallel for schedule(static)
            #endif
            for (int i = 0; i < total; i++) {
                int diff = (int)filterGate[i] - (int)gateImage[i];
                if (abs(diff) > threshold)
                    gateImage[i] = (unsigned char)(diff > 0 ? 0 : 255);
            }
            free(filterGate);

            #ifdef _OPENMP
            #pragma omp parallel for schedule(static)
            #endif
            for (int i = 0; i < total; i++)
                gateImage[i] = (unsigned char)(gateImage[i] > offset ? 255 : 0);
        }
    } else if (contrast == -2) {
        gate = otsu_threshold(gateImage, total);
    } else {
        gate = contrast;
        if (gate > 255) gate = 255;
    }

    /* 5. Box average of grayscale (wider window) → filterImage */
    unsigned char *filterImage = (unsigned char *)malloc(total);
    box_average_fast_par(gray, filterImage, w, h, blockW + 2, blockH + 2);

    /* 6+7. Fused: gate comparison + post-binarize in one pass */
    #ifdef _OPENMP
    #pragma omp parallel for schedule(static)
    #endif
    for (int i = 0; i < total; i++) {
        unsigned char r = gray[i];
        if (gateImage[i] >= gate)
            r = (unsigned char)(r < filterImage[i] ? 0 : 255);
        result[i] = (unsigned char)(r > invertedBrightness ? 255 : 0);
    }
    free(gateImage);
    free(filterImage);
}

/* ══════════════════════════════════════════════════════════════════════
 * box_average_fast — Interior-fast box average
 *
 * Border pixels use exact x87 div, interior uses precomputed reciprocal
 * multiply (still x87 extended precision).
 * For a 7x7 window on a 3094x4314 image, border = ~0.3% of pixels.
 * ══════════════════════════════════════════════════════════════════════ */

static void box_average_fast(const unsigned char *input, unsigned char *output,
                              int w, int h, int blockW, int blockH)
{
    set_fpu_extended();
    int halfW = (blockW + 1) >> 1;
    if (halfW < 1) halfW = 1;
    int halfH = (blockH + 1) >> 1;
    if (halfH < 1) halfH = 1;

    long long *integral = compute_integral(input, w, h);
    if (!integral) return;
    int iw = w + 1;

    /* Interior region: x in [halfW, w-1-halfW], y in [halfH, h-1-halfH].
     * In this zone clamps never fire, so x1=x-halfW, x2=x+halfW, y1=y-halfH,
     * y2=y+halfH, and count = (2*halfW+1)*(2*halfH+1) is a compile-time constant. */
    int fixedCount = (2 * halfW + 1) * (2 * halfH + 1);
    long double invCount = 1.0L / (long double)fixedCount;

    /* Interior y bounds */
    int yStart = halfH;
    int yEnd   = h - 1 - halfH;  /* inclusive */
    int xStart = halfW;
    int xEnd   = w - 1 - halfW;  /* inclusive */

    /* Helper lambda-style macro to compute one pixel with full x87 division */
#define BOX_PIXEL_EXACT(py, px)                                             \
    do {                                                                    \
        int x1 = (px) - halfW; if (x1 < 0) x1 = 0;                        \
        int x2 = (px) + halfW; if (x2 > w - 1) x2 = w - 1;               \
        int y1 = (py) - halfH; if (y1 < 0) y1 = 0;                        \
        int y2 = (py) + halfH; if (y2 > h - 1) y2 = h - 1;               \
        long long sum = integral[(y2+1)*iw+(x2+1)]                         \
                      - integral[y1*iw+(x2+1)]                             \
                      - integral[(y2+1)*iw+x1]                             \
                      + integral[y1*iw+x1];                                \
        int cnt = (y2-y1+1)*(x2-x1+1);                                     \
        int val = bankers_round((long double)sum / (long double)cnt);       \
        if (val < 0) val = 0; if (val > 255) val = 255;                    \
        output[(py)*w+(px)] = (unsigned char)val;                          \
    } while(0)

    /* Top border rows */
    for (int y = 0; y < yStart && y < h; y++)
        for (int x = 0; x < w; x++)
            BOX_PIXEL_EXACT(y, x);

    /* Middle rows: left border, interior, right border */
    for (int y = yStart; y <= yEnd; y++) {
        int yr1 = y - halfH, yr2 = y + halfH;
        long long rowBase1 = (long long)yr1 * iw;
        long long rowBase2 = (long long)(yr2 + 1) * iw;

        /* Left border: x in [0, xStart-1] */
        for (int x = 0; x < xStart && x < w; x++)
            BOX_PIXEL_EXACT(y, x);

        /* Interior: fixed count, use reciprocal multiply */
        for (int x = xStart; x <= xEnd; x++) {
            int xl = x - halfW, xr = x + halfW + 1;
            long long sum = integral[rowBase2 + xr]
                          - integral[rowBase1 + xr]
                          - integral[rowBase2 + xl]
                          + integral[rowBase1 + xl];
            int val = bankers_round((long double)sum * invCount);
            if (val < 0) val = 0;
            if (val > 255) val = 255;
            output[y * w + x] = (unsigned char)val;
        }

        /* Right border: x in [xEnd+1, w-1] */
        for (int x = xEnd + 1; x < w; x++)
            BOX_PIXEL_EXACT(y, x);
    }

    /* Bottom border rows */
    for (int y = yEnd + 1; y < h; y++)
        for (int x = 0; x < w; x++)
            BOX_PIXEL_EXACT(y, x);

#undef BOX_PIXEL_EXACT
    free(integral);
}

#ifdef _OPENMP
/* Parallel Sobel: each row is independent. */
static void sobel_magnitude_l1_par(const unsigned char *gray, unsigned char *sobel,
                                    int w, int h)
{
    #pragma omp parallel for schedule(static)
    for (int y = 0; y < h; y++) {
        int yp = y > 0 ? y - 1 : 0;
        int yn = y < h - 1 ? y + 1 : h - 1;
        for (int x = 0; x < w; x++) {
            int xp = x > 0 ? x - 1 : 0;
            int xn = x < w - 1 ? x + 1 : w - 1;
            int tl = gray[yp*w+xp], tc = gray[yp*w+x], tr = gray[yp*w+xn];
            int cl = gray[y *w+xp],                      cr = gray[y *w+xn];
            int bl = gray[yn*w+xp], bc = gray[yn*w+x], br = gray[yn*w+xn];
            int gx = (tr + 2*cr + br) - (tl + 2*cl + bl);
            int gy = (bl + 2*bc + br) - (tl + 2*tc + tr);
            int mag = abs(gx) + abs(gy);
            sobel[y*w+x] = (unsigned char)(mag < 255 ? mag : 255);
        }
    }
}
#endif

/* ── Parallel box_average (OMP interior rows) ─────────────────────── */

static void box_average_fast_par(const unsigned char *input, unsigned char *output,
                                  int w, int h, int blockW, int blockH)
{
    set_fpu_extended();
    int halfW = (blockW + 1) >> 1;
    if (halfW < 1) halfW = 1;
    int halfH = (blockH + 1) >> 1;
    if (halfH < 1) halfH = 1;

    long long *integral = compute_integral(input, w, h);
    if (!integral) return;
    int iw = w + 1;

    int fixedCount = (2 * halfW + 1) * (2 * halfH + 1);
    long double invCount = 1.0L / (long double)fixedCount;

    int yStart = halfH;
    int yEnd   = h - 1 - halfH;
    int xStart = halfW;
    int xEnd   = w - 1 - halfW;

#define BOX_PIXEL_EXACT_P(py, px)                                         \
    do {                                                                    \
        int x1 = (px) - halfW; if (x1 < 0) x1 = 0;                        \
        int x2 = (px) + halfW; if (x2 > w - 1) x2 = w - 1;               \
        int y1 = (py) - halfH; if (y1 < 0) y1 = 0;                        \
        int y2 = (py) + halfH; if (y2 > h - 1) y2 = h - 1;               \
        long long sum = integral[(y2+1)*iw+(x2+1)]                         \
                      - integral[y1*iw+(x2+1)]                             \
                      - integral[(y2+1)*iw+x1]                             \
                      + integral[y1*iw+x1];                                \
        int cnt = (y2-y1+1)*(x2-x1+1);                                     \
        int val = bankers_round((long double)sum / (long double)cnt);       \
        if (val < 0) val = 0; if (val > 255) val = 255;                    \
        output[(py)*w+(px)] = (unsigned char)val;                          \
    } while(0)

    /* Border rows (sequential — few rows) */
    for (int y = 0; y < yStart && y < h; y++)
        for (int x = 0; x < w; x++)
            BOX_PIXEL_EXACT_P(y, x);
    for (int y = yEnd + 1; y < h; y++)
        for (int x = 0; x < w; x++)
            BOX_PIXEL_EXACT_P(y, x);

    /* Interior rows: parallel */
    #ifdef _OPENMP
    #pragma omp parallel for schedule(static)
    #endif
    for (int y = yStart; y <= yEnd; y++) {
        set_fpu_extended(); /* per-thread x87 init */
        int yr1 = y - halfH, yr2 = y + halfH;
        long long rowBase1 = (long long)yr1 * iw;
        long long rowBase2 = (long long)(yr2 + 1) * iw;

        /* Left border pixels */
        for (int x = 0; x < xStart && x < w; x++)
            BOX_PIXEL_EXACT_P(y, x);

        /* Interior: reciprocal multiply */
        for (int x = xStart; x <= xEnd; x++) {
            int xl = x - halfW, xr = x + halfW + 1;
            long long sum = integral[rowBase2 + xr]
                          - integral[rowBase1 + xr]
                          - integral[rowBase2 + xl]
                          + integral[rowBase1 + xl];
            int val = bankers_round((long double)sum * invCount);
            if (val < 0) val = 0;
            if (val > 255) val = 255;
            output[y * w + x] = (unsigned char)val;
        }

        /* Right border pixels */
        for (int x = xEnd + 1; x < w; x++)
            BOX_PIXEL_EXACT_P(y, x);
    }

#undef BOX_PIXEL_EXACT_P
    free(integral);
}

/* ══════════════════════════════════════════════════════════════════════
 * Diagnostic: dump intermediate products for debugging
 * ══════════════════════════════════════════════════════════════════════ */

void EXPORT AdaptiveThresholdDiag(
    const unsigned char *gray, unsigned char *out_sobel, unsigned char *out_gate,
    unsigned char *out_filter, int *out_params,
    int w, int h, int blockW, int blockH, int contrast, int brightness)
{
    int total = w * h;
    set_fpu_extended();

    /* 1. invertedBrightness */
    int invertedBrightness;
    if (brightness < 0)
        invertedBrightness = otsu_threshold(gray, total);
    else {
        invertedBrightness = 255 - brightness;
        if (invertedBrightness > 255) invertedBrightness = 255;
    }
    out_params[0] = invertedBrightness;

    /* 2. Sobel */
    sobel_magnitude_l1(gray, out_sobel, w, h);

    /* 3. Box average Sobel → gateImage */
    box_average_fast(out_sobel, out_gate, w, h, blockW, blockH);

    /* 4. gate threshold */
    int gate;
    if (contrast < 0 && contrast != -2) {
        gate = 128;
        /* Save gate before DynamicThreshold modifies it */
        int gateOtsu = otsu_threshold(out_gate, total);
        out_params[2] = gateOtsu;
        dynamic_threshold_on_gate(out_gate, w, h, blockW, blockH);
    } else if (contrast == -2) {
        gate = otsu_threshold(out_gate, total);
    } else {
        gate = contrast;
    }
    out_params[1] = gate;

    /* 5. filterImage */
    box_average_fast(gray, out_filter, w, h, blockW + 2, blockH + 2);
}

/* ══════════════════════════════════════════════════════════════════════
 * DynamicThresholdAverage — full pipeline
 *
 * Input:  gray[w*h] — grayscale pixel data
 * Output: result[w*h] — binary 0/255
 *
 * Matches DLL's DT_DynamicThresholdAverage
 * ══════════════════════════════════════════════════════════════════════ */

void EXPORT DynamicThresholdAverage(
    const unsigned char *gray, unsigned char *result,
    int w, int h, int blockW, int blockH, int contrast, int brightness)
{
    int total = w * h;
    set_fpu_extended();

    /* Resolve auto contrast: AutoThreshold on 4-neighbor DiffHist of gray */
    if (contrast == -1) {
        long long neighDiffHist[256];
        memset(neighDiffHist, 0, sizeof(neighDiffHist));
        for (int row = 1; row <= h - 2; row++)
            for (int col = 1; col <= w - 2; col++) {
                int val = gray[row * w + col];
                neighDiffHist[abs(val - gray[(row+1)*w + col])]++;
                neighDiffHist[abs(val - gray[(row-1)*w + col])]++;
                neighDiffHist[abs(val - gray[row*w + col+1])]++;
                neighDiffHist[abs(val - gray[row*w + col-1])]++;
            }
        contrast = 255 - auto_threshold(neighDiffHist);
    }

    /* Resolve auto brightness: Otsu on grayscale (both -1 and -2) */
    if (brightness < 0) {
        int hist256[256];
        memset(hist256, 0, sizeof(hist256));
        for (int i = 0; i < total; i++) hist256[gray[i]]++;
        long long sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += (long long)i * hist256[i];
        long long sumB = 0, wB = 0;
        double maxVar = 0; int bestT = 0;
        for (int t = 0; t < 256; t++) {
            wB += hist256[t]; if (wB == 0) continue;
            long long wF = total - wB; if (wF == 0) break;
            sumB += (long long)t * hist256[t];
            double mB = (double)sumB / (double)wB;
            double mF = (double)(sumAll - sumB) / (double)wF;
            double diff = mB - mF;
            double var = (double)wB * (double)wF * diff * diff;
            if (var > maxVar) { maxVar = var; bestT = t; }
        }
        brightness = 255 - bestT;
    }

    /* Apply DynamicThreshold: box average → compare → threshold */
    int effContrast = 255 - contrast;
    int effThreshold = 255 - brightness;

    unsigned char *boxMean = (unsigned char *)malloc(total);
    box_average_fast(gray, boxMean, w, h, blockW, blockH);

    for (int i = 0; i < total; i++) {
        int pixel = gray[i];
        int mean = boxMean[i];
        int absDiff = abs(mean - pixel);
        if (absDiff > effContrast)
            result[i] = (unsigned char)(pixel >= mean ? 255 : 0);
        else
            result[i] = (unsigned char)(pixel > effThreshold ? 255 : 0);
    }
    free(boxMean);
}

/* ══════════════════════════════════════════════════════════════════════
 * HSL ↔ RGB (x87 extended precision)
 * Matches DLL's RGBtoHSL / HSLtoRGB exactly
 * ══════════════════════════════════════════════════════════════════════ */

static const long double ONE_THIRD = 1.0L / 3.0L;
static const long double TWO_THIRDS = 2.0L / 3.0L;

static double hue_to_rgb(double p, double q, double t) {
    if (t < 0.0) t = t + 1.0;
    if (t > 1.0) t = t - 1.0;
    long double six_t = 6.0L * (long double)t;
    if ((double)six_t < 1.0)
        return (double)((long double)p + ((long double)q - (long double)p) * 6.0L * (long double)t);
    long double two_t = 2.0L * (long double)t;
    if ((double)two_t < 1.0)
        return q;
    long double three_t = 3.0L * (long double)t;
    if ((double)three_t < 2.0)
        return (double)((long double)p + ((long double)q - (long double)p) * (TWO_THIRDS - (long double)t) * 6.0L);
    return p;
}

static void hsl_to_rgb(double H, double S, double L,
                        unsigned char *R, unsigned char *G, unsigned char *B) {
    set_fpu_extended();
    if (S == 0.0) {
        int v = bankers_round((long double)L * 255.0L);
        if (v < 0) v = 0; if (v > 255) v = 255;
        *R = *G = *B = (unsigned char)v;
        return;
    }
    double q = L < 0.5
        ? (double)((long double)L * (1.0L + (long double)S))
        : (double)((long double)L + (long double)S - (long double)L * (long double)S);
    double p = (double)(2.0L * (long double)L - (long double)q);
    double t_r = (double)((long double)H + ONE_THIRD);
    double t_g = H;
    double t_b = (double)((long double)H - ONE_THIRD);
    int ri = bankers_round((long double)hue_to_rgb(p, q, t_r) * 255.0L);
    int gi = bankers_round((long double)hue_to_rgb(p, q, t_g) * 255.0L);
    int bi = bankers_round((long double)hue_to_rgb(p, q, t_b) * 255.0L);
    if (ri < 0) ri = 0; if (ri > 255) ri = 255;
    if (gi < 0) gi = 0; if (gi > 255) gi = 255;
    if (bi < 0) bi = 0; if (bi > 255) bi = 255;
    *R = (unsigned char)ri; *G = (unsigned char)gi; *B = (unsigned char)bi;
}

static void rgb_to_hsl(int R, int G, int B, double *H, double *S, double *L) {
    set_fpu_extended();
    float f255 = 255.0f;
    double rf = (double)((long double)R / (long double)f255);
    double gf = (double)((long double)G / (long double)f255);
    double bf = (double)((long double)B / (long double)f255);
    double cMax = rf > gf ? (rf > bf ? rf : bf) : (gf > bf ? gf : bf);
    double cMin = rf < gf ? (rf < bf ? rf : bf) : (gf < bf ? gf : bf);
    double Lv = (double)(((long double)cMax + (long double)cMin) / 2.0L);
    if (cMax == cMin) { *H = 0; *S = 0; *L = Lv; return; }
    double delta = (double)((long double)cMax - (long double)cMin);
    double Sv = Lv < 0.5
        ? (double)((long double)delta / ((long double)cMax + (long double)cMin))
        : (double)((long double)delta / (2.0L - (long double)cMax - (long double)cMin));
    double Hv;
    if (rf == cMax)      Hv = (double)(((long double)gf - (long double)bf) / (long double)delta);
    else if (gf == cMax) Hv = (double)(((long double)bf - (long double)rf) / (long double)delta + 2.0L);
    else                 Hv = (double)(((long double)rf - (long double)gf) / (long double)delta + 4.0L);
    Hv = (double)((long double)Hv / 6.0L);
    if (Hv < 0.0) Hv = (double)((long double)Hv + 1.0L);
    *H = Hv; *S = Sv; *L = Lv;
}

/* ══════════════════════════════════════════════════════════════════════
 * RemoveBleedThroughFast — optimized version
 *
 * Same algorithm as RemoveBleedThrough but:
 * 1. L_index computed via integer LUT (no FPU) for histogram passes
 * 2. Full HSL computed only once per pixel, only for bright pixels
 * 3. hsl_to_rgb called only for qualifying pixels (~13% at tol=1)
 *
 * Produces identical output to RemoveBleedThrough.
 * ══════════════════════════════════════════════════════════════════════ */

/* Compute L_index = bankers_round(L * 255) using exact x87 FPU path.
 * L = (cMax + cMin) / 2 where cMax = max/255.0f, cMin = min/255.0f.
 * Must match rgb_to_hsl's precision exactly. */
static int fast_lightness_index(int cMaxVal, int cMinVal) {
    float f255 = 255.0f;
    /* Use the exact same FPU operations as rgb_to_hsl */
    double cMax = (double)((long double)cMaxVal / (long double)f255);
    double cMin = (double)((long double)cMinVal / (long double)f255);
    double Lv = (double)(((long double)cMax + (long double)cMin) / 2.0L);
    int li = bankers_round((long double)Lv * (long double)f255);
    if (li < 0) li = 0; if (li > 255) li = 255;
    return li;
}

/* Precomputed L_index[max][min] for all (max, min) pairs 0..255.
 * Exactly matches the x87 path through rgb_to_hsl. */
static unsigned char g_lindex_lut[256][256];
static int g_lindex_init = 0;

static void init_lindex_lut(void) {
    if (g_lindex_init) return;
    set_fpu_extended();
    for (int M = 0; M < 256; M++) {
        for (int m = 0; m <= M; m++) {
            unsigned char li = (unsigned char)fast_lightness_index(M, m);
            g_lindex_lut[M][m] = li;
            g_lindex_lut[m][M] = li;
        }
    }
    g_lindex_init = 1;
}

/* ══════════════════════════════════════════════════════════════════════
 * Fast HSL helpers — standard double math, no x87 long double overhead.
 * Accepts minor rounding differences vs the x87 versions.
 * ══════════════════════════════════════════════════════════════════════ */

static void fast_rgb_to_hsl(int R, int G, int B, double *H, double *S, double *L) {
    static const double inv255 = 1.0 / 255.0;
    double rf = R * inv255, gf = G * inv255, bf = B * inv255;
    double cMax = rf > gf ? (rf > bf ? rf : bf) : (gf > bf ? gf : bf);
    double cMin = rf < gf ? (rf < bf ? rf : bf) : (gf < bf ? gf : bf);
    double Lv = (cMax + cMin) * 0.5;
    if (cMax == cMin) { *H = 0; *S = 0; *L = Lv; return; }
    double delta = cMax - cMin;
    *S = Lv < 0.5 ? delta / (cMax + cMin) : delta / (2.0 - cMax - cMin);
    double Hv;
    if (rf == cMax)      Hv = (gf - bf) / delta;
    else if (gf == cMax) Hv = (bf - rf) / delta + 2.0;
    else                 Hv = (rf - gf) / delta + 4.0;
    Hv /= 6.0;
    if (Hv < 0.0) Hv += 1.0;
    *H = Hv; *L = Lv;
}

static double fast_hue_to_rgb(double p, double q, double t) {
    if (t < 0.0) t += 1.0;
    if (t > 1.0) t -= 1.0;
    if (6.0 * t < 1.0) return p + (q - p) * 6.0 * t;
    if (2.0 * t < 1.0) return q;
    if (3.0 * t < 2.0) return p + (q - p) * (2.0/3.0 - t) * 6.0;
    return p;
}

static void fast_hsl_to_rgb(double H, double S, double L,
                             unsigned char *R, unsigned char *G, unsigned char *B) {
    if (S == 0.0) {
        int v = (int)(L * 255.0 + 0.5);
        if (v < 0) v = 0; if (v > 255) v = 255;
        *R = *G = *B = (unsigned char)v;
        return;
    }
    double q = L < 0.5 ? L * (1.0 + S) : L + S - L * S;
    double p = 2.0 * L - q;
    int ri = (int)(fast_hue_to_rgb(p, q, H + 1.0/3.0) * 255.0 + 0.5);
    int gi = (int)(fast_hue_to_rgb(p, q, H) * 255.0 + 0.5);
    int bi = (int)(fast_hue_to_rgb(p, q, H - 1.0/3.0) * 255.0 + 0.5);
    if (ri < 0) ri = 0; if (ri > 255) ri = 255;
    if (gi < 0) gi = 0; if (gi > 255) gi = 255;
    if (bi < 0) bi = 0; if (bi > 255) bi = 255;
    *R = (unsigned char)ri; *G = (unsigned char)gi; *B = (unsigned char)bi;
}

/* Split version: compute background color only (passes 1+2).
 * Returns background H/S/L as integer indices (0-255) and Otsu threshold. */
void EXPORT RemoveBleedThroughGetBackground(
    const unsigned char *bgr, int w, int h, int stride,
    int *outBgH, int *outBgS, int *outBgL, int *outOtsu)
{
    init_lindex_lut();
    int total = w * h;

    /* Pass 1: L histogram using integer LUT */
    long long lHist[256];
    memset(lHist, 0, sizeof(lHist));
    for (int y = 0; y < h; y++) {
        const unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            int cMax = rv > gv ? (rv > bv ? rv : bv) : (gv > bv ? gv : bv);
            int cMin = rv < gv ? (rv < bv ? rv : bv) : (gv < bv ? gv : bv);
            lHist[g_lindex_lut[cMax][cMin]]++;
        }
    }

    /* Otsu */
    long long sumAll = 0;
    for (int i = 0; i < 256; i++) sumAll += (long long)i * lHist[i];
    long long sumB = 0, wB = 0;
    double maxVar = 0; int medianThreshold = 0;
    for (int t = 0; t < 256; t++) {
        wB += lHist[t]; if (wB == 0) continue;
        long long wF = total - wB; if (wF == 0) break;
        sumB += (long long)t * lHist[t];
        double mB = (double)sumB / (double)wB;
        double mF = (double)(sumAll - sumB) / (double)wF;
        double d = mB - mF;
        double v = (double)wB * (double)wF * d * d;
        if (v > maxVar) { maxVar = v; medianThreshold = t; }
    }

    /* Pass 2: HSL histograms for bright pixels */
    long long histH[256], histS[256], histL2[256];
    memset(histH, 0, sizeof(histH));
    memset(histS, 0, sizeof(histS));
    memset(histL2, 0, sizeof(histL2));

    for (int y = 0; y < h; y++) {
        const unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            int cMax = rv > gv ? (rv > bv ? rv : bv) : (gv > bv ? gv : bv);
            int cMin = rv < gv ? (rv < bv ? rv : bv) : (gv < bv ? gv : bv);
            int li = g_lindex_lut[cMax][cMin];
            if (li < medianThreshold) continue;
            double pH, pS, pL;
            fast_rgb_to_hsl(rv, gv, bv, &pH, &pS, &pL);
            int si = (int)(pS * 255.0 + 0.5);
            int hi = (int)(pH * 255.0 + 0.5);
            if (li >= 0 && li <= 255) histL2[li]++;
            if (si >= 0 && si <= 255) histS[si]++;
            if (hi >= 0 && hi <= 255) histH[hi]++;
        }
    }

    int bestH=0, bestS=0, bestL=0;
    long long bHc=0, bSc=0, bLc=0;
    for (int i = 0; i < 256; i++) {
        if (histH[i] > bHc) { bHc = histH[i]; bestH = i; }
        if (histS[i] > bSc) { bSc = histS[i]; bestS = i; }
        if (histL2[i] > bLc) { bLc = histL2[i]; bestL = i; }
    }

    *outBgH = bestH; *outBgS = bestS; *outBgL = bestL; *outOtsu = medianThreshold;
}

/* Split version: apply bleedthrough using pre-computed background.
 * Process rows [startY, endY) only. Can be called in parallel on different row ranges. */
void EXPORT RemoveBleedThroughApplyRows(
    unsigned char *bgr, int w, int h, int stride, int tolerance,
    int bgH_idx, int bgS_idx, int bgL_idx, int startY, int endY)
{
    init_lindex_lut();

    double bgH = bgH_idx / 255.0;
    double bgS = bgS_idx / 255.0;
    double bgL = bgL_idx / 255.0;
    double lThreshold = ((255.0 - tolerance) / 255.0) * bgL;

    int lMinIdx = (int)(lThreshold * 255.0);
    if (lMinIdx < 0) lMinIdx = 0;
    int lMaxIdx = bgL_idx;

    for (int y = startY; y < endY; y++) {
        unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            int cMax = rv > gv ? (rv > bv ? rv : bv) : (gv > bv ? gv : bv);
            int cMin = rv < gv ? (rv < bv ? rv : bv) : (gv < bv ? gv : bv);
            int li = g_lindex_lut[cMax][cMin];
            if (li < lMinIdx || li > lMaxIdx + 1) continue;

            double pH, pS, pL;
            fast_rgb_to_hsl(rv, gv, bv, &pH, &pS, &pL);
            if (fabs(pS - bgS) >= 0.15) continue;
            if (fabs(pH - bgH) >= 0.08) continue;
            if (bgL < pL) continue;
            if (lThreshold > pL) continue;
            unsigned char nR, nG, nB;
            fast_hsl_to_rgb(pH, pS, bgL, &nR, &nG, &nB);
            row[x*3] = nB; row[x*3+1] = nG; row[x*3+2] = nR;
        }
    }
}

/* ══════════════════════════════════════════════════════════════════════
 * RemoveBleedThrough
 *
 * Uses fast double-precision HSL helpers and integer LUT for lightness.
 * ══════════════════════════════════════════════════════════════════════ */

void EXPORT RemoveBleedThrough(
    unsigned char *bgr, int w, int h, int stride, int tolerance)
{
    init_lindex_lut();
    int total = w * h;

    /* Pass 1: Build lightness histogram using integer LUT (no FPU) */
    long long lHist[256];
    memset(lHist, 0, sizeof(lHist));

    for (int y = 0; y < h; y++) {
        const unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            int cMax = rv > gv ? (rv > bv ? rv : bv) : (gv > bv ? gv : bv);
            int cMin = rv < gv ? (rv < bv ? rv : bv) : (gv < bv ? gv : bv);
            lHist[g_lindex_lut[cMax][cMin]]++;
        }
    }

    /* Otsu on lightness histogram */
    long long sumAll = 0;
    for (int i = 0; i < 256; i++) sumAll += (long long)i * lHist[i];
    long long sumB = 0, wB = 0;
    double maxVar = 0; int medianThreshold = 0;
    for (int t = 0; t < 256; t++) {
        wB += lHist[t]; if (wB == 0) continue;
        long long wF = total - wB; if (wF == 0) break;
        sumB += (long long)t * lHist[t];
        double mB = (double)sumB / (double)wB;
        double mF = (double)(sumAll - sumB) / (double)wF;
        double d = mB - mF;
        double v = (double)wB * (double)wF * d * d;
        if (v > maxVar) { maxVar = v; medianThreshold = t; }
    }

    /* Pass 2: Accumulate HSL histograms for bright pixels using fast double math */
    long long histH[256], histS[256], histL[256];
    memset(histH, 0, sizeof(histH));
    memset(histS, 0, sizeof(histS));
    memset(histL, 0, sizeof(histL));

    for (int y = 0; y < h; y++) {
        const unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            int cMax = rv > gv ? (rv > bv ? rv : bv) : (gv > bv ? gv : bv);
            int cMin = rv < gv ? (rv < bv ? rv : bv) : (gv < bv ? gv : bv);
            int li = g_lindex_lut[cMax][cMin];
            if (li < medianThreshold) continue;
            double pH, pS, pL;
            fast_rgb_to_hsl(rv, gv, bv, &pH, &pS, &pL);
            int si = (int)(pS * 255.0 + 0.5);
            int hi = (int)(pH * 255.0 + 0.5);
            if (li >= 0 && li <= 255) histL[li]++;
            if (si >= 0 && si <= 255) histS[si]++;
            if (hi >= 0 && hi <= 255) histH[hi]++;
        }
    }

    /* Mode of each histogram -> background color */
    int bestH=0, bestS=0, bestL=0;
    long long bHc=0, bSc=0, bLc=0;
    for (int i = 0; i < 256; i++) {
        if (histH[i] > bHc) { bHc = histH[i]; bestH = i; }
        if (histS[i] > bSc) { bSc = histS[i]; bestS = i; }
        if (histL[i] > bLc) { bLc = histL[i]; bestL = i; }
    }

    double bgH = bestH / 255.0;
    double bgS = bestS / 255.0;
    double bgL = bestL / 255.0;
    double lThreshold = ((255.0 - tolerance) / 255.0) * bgL;

    int lMinIdx = (int)(lThreshold * 255.0);
    if (lMinIdx < 0) lMinIdx = 0;
    int lMaxIdx = bestL;

    /* Pass 3: Process qualifying pixels using fast double HSL */
    for (int y = 0; y < h; y++) {
        unsigned char *row = bgr + y * stride;
        for (int x = 0; x < w; x++) {
            int bv = row[x*3], gv = row[x*3+1], rv = row[x*3+2];
            int cMax = rv > gv ? (rv > bv ? rv : bv) : (gv > bv ? gv : bv);
            int cMin = rv < gv ? (rv < bv ? rv : bv) : (gv < bv ? gv : bv);
            int li = g_lindex_lut[cMax][cMin];
            if (li < lMinIdx || li > lMaxIdx + 1) continue;

            double pH, pS, pL;
            fast_rgb_to_hsl(rv, gv, bv, &pH, &pS, &pL);
            if (fabs(pS - bgS) >= 0.15) continue;
            if (fabs(pH - bgH) >= 0.08) continue;
            if (bgL < pL) continue;
            if (lThreshold > pL) continue;
            unsigned char nR, nG, nB;
            fast_hsl_to_rgb(pH, pS, bgL, &nR, &nG, &nB);
            row[x*3] = nB; row[x*3+1] = nG; row[x*3+2] = nR;
        }
    }
}

/* ══════════════════════════════════════════════════════════════════════
 * RefineThreshold
 *
 * binary[w*h]: 0/255 binary image (modified in place)
 * gray[w*h]: original grayscale
 * tolerance: edge contrast threshold
 *
 * Matches DLL's ImgRefineThreshold (0xa8047c) exactly:
 *   1. BoxAverage(gray, 3, 3) → smoothed (5x5 window due to halfW=2)
 *   2. Sobel L1 on smoothed → gradient
 *   3. Column-major scan (x outer, y inner) over all pixels
 *   4. For each unvisited pixel, DFS flood-fill to discover CC and compute
 *      edge statistics (IsBorder: image edge OR 4-connected different color)
 *   5. If edgeCount > 0 AND avgGrad < tolerance → FillCC2
 *   6. FillCC2 uses direction-flag iterative walk with persistent fill bitmap:
 *      - Expands only through pixels == 0 (foreground/black)
 *      - Off-by-one boundaries: x>=2 for left, y>=2 for up
 *      - Fill bitmap persists across all FillCC2 calls (masking effect)
 *      - For black CC: sets pixels to 255 (white), expansion through CC
 *      - For white CC: sets seed to 0 (black), expansion bleeds into
 *        adjacent black region, marking those pixels in fill bitmap
 *
 * Verified 100% pixel-perfect match against DLL on 3 test images at
 * tolerances 1-200.
 * ══════════════════════════════════════════════════════════════════════ */

/* FillCC2: direction-flag iterative walk matching DLL 0xa7fc90.
 * Expands through pixels with bin[]==0 and fillBitmap[]==0.
 * Direction flags (low nibble): LEFT=0x01, DOWN=0x02, RIGHT=0x04, UP=0x08
 * Came-from flags (high nibble): FROM_LEFT=0x10, FROM_BELOW=0x20,
 *                                 FROM_RIGHT=0x40, FROM_ABOVE=0x80 */
static void fillcc2(unsigned char *bin, unsigned char *fillBitmap,
                     int w, int h, int startX, int startY,
                     unsigned char targetColor) {
    int maxX = w - 1, maxY = h - 1;
    int x = startX, y = startY;
    for (;;) {
        int idx = y * w + x;
        unsigned char flags = fillBitmap[idx];

        /* Color pixel on first visit (LEFT direction not yet checked) */
        if (!(flags & 0x01))
            bin[idx] = targetColor;

        /* Try LEFT (off-by-one: x >= 2) */
        if (x >= 2 && !(flags & 0x01)) {
            int nb = idx - 1;
            if (bin[nb] == 0 && fillBitmap[nb] == 0) {
                fillBitmap[idx] = flags | 0x01;
                x--;
                fillBitmap[y * w + x] = 0x40; /* came from right */
                continue;
            }
        }

        /* Try RIGHT */
        if (x < maxX && !(flags & 0x04)) {
            int nb = idx + 1;
            if (bin[nb] == 0 && fillBitmap[nb] == 0) {
                fillBitmap[idx] = flags | 0x04;
                x++;
                fillBitmap[y * w + x] = 0x10; /* came from left */
                continue;
            }
        }

        /* Try UP (off-by-one: y >= 2) */
        if (y >= 2 && !(flags & 0x08)) {
            int nb = idx - w;
            if (bin[nb] == 0 && fillBitmap[nb] == 0) {
                fillBitmap[idx] = flags | 0x08;
                y--;
                fillBitmap[y * w + x] = 0x20; /* came from below */
                continue;
            }
        }

        /* Try DOWN */
        if (y < maxY && !(flags & 0x02)) {
            int nb = idx + w;
            if (bin[nb] == 0 && fillBitmap[nb] == 0) {
                fillBitmap[idx] = flags | 0x02;
                y++;
                fillBitmap[y * w + x] = 0x80; /* came from above */
                continue;
            }
        }

        /* Backtrack by came-from flag */
        unsigned char cf = fillBitmap[idx] & 0xF0;
        if      (cf == 0x10) x--;
        else if (cf == 0x20) y++;
        else if (cf == 0x40) x++;
        else if (cf == 0x80) y--;
        else break; /* no came-from = seed pixel, done */
    }
}

void EXPORT RefineThreshold(
    unsigned char *binary, const unsigned char *gray,
    int w, int h, int tolerance)
{
    if (tolerance <= 0) return;
    int total = w * h;

    /* Step 1: BoxAverage(gray, 3, 3) → smoothed, then Sobel → gradient */
    unsigned char *smoothed = (unsigned char *)malloc(total);
    box_average_fast(gray, smoothed, w, h, 3, 3);
    unsigned char *gradient = (unsigned char *)malloc(total);
    sobel_magnitude_l1(smoothed, gradient, w, h);
    free(smoothed);

    /* Step 2: CC analysis — column-major scan, both colors */
    unsigned char *visited = (unsigned char *)calloc(total, 1);
    unsigned char *fillBitmap = (unsigned char *)calloc(total, 1);

    int stackCap = 65536;
    int *stack = (int *)malloc(stackCap * sizeof(int));

    int wm1 = w - 1, hm1 = h - 1;

    for (int col = 0; col < w; col++) {
        for (int row = 0; row < h; row++) {
            int startIdx = row * w + col;
            if (visited[startIdx]) continue;

            unsigned char pixelColor = binary[startIdx];
            visited[startIdx] = 1;

            /* DFS flood-fill to discover CC and compute edge stats */
            /* Stack stores packed (x,y): (y << 16) | x */
            int stackTop = 0;
            long edgeSum = 0;
            int edgeCount = 0;

            stack[stackTop++] = (row << 16) | col;

            while (stackTop > 0) {
                int packed = stack[--stackTop];
                int x = packed & 0xFFFF;
                int y = (unsigned)packed >> 16;
                int idx = y * w + x;

                /* IsBorder: image edge OR any 4-connected neighbor differs */
                int isEdge = (x == 0 || y == 0 || x == wm1 || y == hm1 ||
                              binary[idx-1] != pixelColor ||
                              binary[idx+1] != pixelColor ||
                              binary[idx-w] != pixelColor ||
                              binary[idx+w] != pixelColor);
                if (isEdge) {
                    edgeSum += gradient[idx];
                    edgeCount++;
                }

                /* Expand through same-color neighbors */
                if (x > 0 && !visited[idx-1] && binary[idx-1] == pixelColor) {
                    visited[idx-1] = 1;
                    if (stackTop >= stackCap) {
                        stackCap *= 2;
                        stack = (int *)realloc(stack, stackCap * sizeof(int));
                    }
                    stack[stackTop++] = (y << 16) | (x - 1);
                }
                if (x < wm1 && !visited[idx+1] && binary[idx+1] == pixelColor) {
                    visited[idx+1] = 1;
                    if (stackTop >= stackCap) {
                        stackCap *= 2;
                        stack = (int *)realloc(stack, stackCap * sizeof(int));
                    }
                    stack[stackTop++] = (y << 16) | (x + 1);
                }
                if (y > 0 && !visited[idx-w] && binary[idx-w] == pixelColor) {
                    visited[idx-w] = 1;
                    if (stackTop >= stackCap) {
                        stackCap *= 2;
                        stack = (int *)realloc(stack, stackCap * sizeof(int));
                    }
                    stack[stackTop++] = ((y - 1) << 16) | x;
                }
                if (y < hm1 && !visited[idx+w] && binary[idx+w] == pixelColor) {
                    visited[idx+w] = 1;
                    if (stackTop >= stackCap) {
                        stackCap *= 2;
                        stack = (int *)realloc(stack, stackCap * sizeof(int));
                    }
                    stack[stackTop++] = ((y + 1) << 16) | x;
                }
            }

            /* Flip if low contrast: edgeCount > 0 AND avgGrad < tolerance */
            if (edgeCount > 0 && edgeSum < (long)tolerance * edgeCount) {
                unsigned char targetColor = (pixelColor == 0) ? 255 : 0;
                fillcc2(binary, fillBitmap, w, h, col, row, targetColor);
            }
        }
    }

    free(stack); free(visited); free(fillBitmap); free(gradient);
}

/* ══════════════════════════════════════════════════════════════════════
 * Despeckle — remove small connected components from packed 1bpp buffer
 *
 * buf:    packed 1bpp pixel data (0-bit = black, 1-bit = white)
 * stride: bytes per row
 * w, h:   image dimensions in pixels
 * maxW, maxH: max bounding-box size for removal
 *
 * Returns number of removed components.
 * 8-connected flood-fill. CCs whose bounding box fits within maxW×maxH
 * are set to white.
 * ══════════════════════════════════════════════════════════════════════ */

/* Single despeckle pass — optimized with packed x,y coords, generation
 * counter, row indexing, byte-level skip, split flood fill, precomputed
 * row offsets, and unsigned bounds checks.
 * Returns number of CCs removed.  Sets *pixelsChanged nonzero if any buf
 * byte was actually modified during erasure. */
static int despeckle_single_pass(unsigned char *buf, int stride, int w, int h,
                                 int maxW, int maxH,
                                 unsigned char *visited, unsigned char generation,
                                 int **pStack, int *stackCap,
                                 int **pComp,  int *compCap,
                                 const int *activeRows, int activeRowCount,
                                 int *pixelsChanged)
{
    int removed = 0;
    int *stack = *pStack;
    int *comp  = *pComp;
    int sCap = *stackCap, cCap = *compCap;
    int changed = 0;
    const unsigned int uw = (unsigned int)w;
    const unsigned int uh = (unsigned int)h;
    const int byteW = (w + 7) >> 3;

/* Macro: try neighbor at (visited-index ni, buf-row br, pixel-x nx, packed-coord pc) */
#define DS_TRY(ni, br, nx, pc) \
    do { \
        if (visited[ni] != generation && \
            (buf[(br) + ((nx) >> 3)] & (0x80 >> ((nx) & 7))) == 0) { \
            visited[ni] = generation; \
            if (stackTop >= sCap) { \
                sCap *= 2; \
                stack = (int *)realloc(stack, sCap * sizeof(int)); \
                *pStack = stack; *stackCap = sCap; \
            } \
            stack[stackTop++] = (pc); \
        } \
    } while(0)

    for (int ri = 0; ri < activeRowCount; ri++) {
        int y = activeRows[ri];
        int rowOff = y * stride;
        for (int bIdx = 0; bIdx < byteW; bIdx++) {
            if (buf[rowOff + bIdx] == 0xFF) continue; /* skip all-white byte */
            int xBase = bIdx << 3;
            int xEnd = xBase + 8;
            if (xEnd > w) xEnd = w;
            for (int x = xBase; x < xEnd; x++) {
                if ((buf[rowOff + (x >> 3)] & (0x80 >> (x & 7))) != 0) continue;
                int idx = y * w + x;
                if (visited[idx] == generation) continue;

                /* Phase 1: flood fill tracking bbox + component list */
                int compSize = 0, stackTop = 0;
                stack[stackTop++] = (y << 16) | x;
                visited[idx] = generation;
                int minX = x, maxX2 = x, minY = y, maxY2 = y;
                int tooLarge = 0;

                while (stackTop > 0) {
                    int ci = stack[--stackTop];
                    int cx = ci & 0xFFFF;
                    int cy = (unsigned)ci >> 16;

                    if (compSize >= cCap) {
                        cCap *= 2;
                        comp = (int *)realloc(comp, cCap * sizeof(int));
                        *pComp = comp; *compCap = cCap;
                    }
                    comp[compSize++] = ci;
                    if (cx < minX) minX = cx;
                    if (cx > maxX2) maxX2 = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY2) maxY2 = cy;
                    if (maxX2 - minX >= maxW || maxY2 - minY >= maxH) {
                        tooLarge = 1;
                        break;
                    }

                    /* Precomputed row offsets for 8 neighbors */
                    int viC = cy * (int)uw;
                    int brC = cy * stride;
                    int cyM1 = cy - 1, cyP1 = cy + 1;
                    int viM = viC - (int)uw, brM = brC - stride;
                    int viP = viC + (int)uw, brP = brC + stride;
                    int cxM1 = cx - 1, cxP1 = cx + 1;
                    int hasUp    = (unsigned)cyM1 < uh;
                    int hasDown  = (unsigned)cyP1 < uh;
                    int hasLeft  = (unsigned)cxM1 < uw;
                    int hasRight = (unsigned)cxP1 < uw;

                    if (hasUp) {
                        if (hasLeft)  DS_TRY(viM+cxM1, brM, cxM1, (cyM1<<16)|cxM1);
                                      DS_TRY(viM+cx,   brM, cx,   (cyM1<<16)|cx);
                        if (hasRight) DS_TRY(viM+cxP1, brM, cxP1, (cyM1<<16)|cxP1);
                    }
                    if (hasLeft)  DS_TRY(viC+cxM1, brC, cxM1, (cy<<16)|cxM1);
                    if (hasRight) DS_TRY(viC+cxP1, brC, cxP1, (cy<<16)|cxP1);
                    if (hasDown) {
                        if (hasLeft)  DS_TRY(viP+cxM1, brP, cxM1, (cyP1<<16)|cxM1);
                                      DS_TRY(viP+cx,   brP, cx,   (cyP1<<16)|cx);
                        if (hasRight) DS_TRY(viP+cxP1, brP, cxP1, (cyP1<<16)|cxP1);
                    }
                }

                /* Phase 2: if tooLarge, continue flood fill just marking visited */
                if (tooLarge) {
                    while (stackTop > 0) {
                        int ci = stack[--stackTop];
                        int cx2 = ci & 0xFFFF;
                        int cy2 = (unsigned)ci >> 16;

                        int viC = cy2 * (int)uw;
                        int brC = cy2 * stride;
                        int cyM1 = cy2 - 1, cyP1 = cy2 + 1;
                        int viM = viC - (int)uw, brM = brC - stride;
                        int viP = viC + (int)uw, brP = brC + stride;
                        int cxM1 = cx2 - 1, cxP1 = cx2 + 1;
                        int hasUp    = (unsigned)cyM1 < uh;
                        int hasDown  = (unsigned)cyP1 < uh;
                        int hasLeft  = (unsigned)cxM1 < uw;
                        int hasRight = (unsigned)cxP1 < uw;

                        if (hasUp) {
                            if (hasLeft)  DS_TRY(viM+cxM1, brM, cxM1, (cyM1<<16)|cxM1);
                                          DS_TRY(viM+cx2,  brM, cx2,  (cyM1<<16)|cx2);
                            if (hasRight) DS_TRY(viM+cxP1, brM, cxP1, (cyM1<<16)|cxP1);
                        }
                        if (hasLeft)  DS_TRY(viC+cxM1, brC, cxM1, (cy2<<16)|cxM1);
                        if (hasRight) DS_TRY(viC+cxP1, brC, cxP1, (cy2<<16)|cxP1);
                        if (hasDown) {
                            if (hasLeft)  DS_TRY(viP+cxM1, brP, cxM1, (cyP1<<16)|cxM1);
                                          DS_TRY(viP+cx2,  brP, cx2,  (cyP1<<16)|cx2);
                            if (hasRight) DS_TRY(viP+cxP1, brP, cxP1, (cyP1<<16)|cxP1);
                        }
                    }
                }

                /* Erase small CC using component list */
                if (!tooLarge) {
                    for (int i = 0; i < compSize; i++) {
                        int px = comp[i] & 0xFFFF;
                        int py = (unsigned)comp[i] >> 16;
                        int byteOff = py * stride + (px >> 3);
                        unsigned char mask = (unsigned char)(0x80 >> (px & 7));
                        unsigned char old = buf[byteOff];
                        buf[byteOff] = old | mask;
                        if (old != (old | mask)) changed = 1;
                    }
                    removed++;
                }
            }
        }
    }
#undef DS_TRY
    *pixelsChanged = changed;
    return removed;
}

/* Despeckle — remove small connected components from packed 1bpp buffer.
 *
 * Multi-pass: each pass removes all small CCs visible from the current
 * buffer state.  Passes repeat until convergence (no more removals).
 * This matches the original Delphi DLL's cascade behaviour: removing one
 * CC can expose previously-connected pixels as new small CCs.
 *
 * In practice converges in 2–3 passes for typical document images.
 */
int EXPORT Despeckle(unsigned char *buf, int stride, int w, int h,
                     int maxW, int maxH) {
    int total = w * h;
    unsigned char *visited = (unsigned char *)malloc(total);
    if (!visited) return 0;
    memset(visited, 0, total);

    int stackCap = 4096;
    int *stack = (int *)malloc(stackCap * sizeof(int));
    int compCap = 4096;
    int *comp = (int *)malloc(compCap * sizeof(int));
    int *activeRows = (int *)malloc(h * sizeof(int));
    int removed = 0;
    unsigned char generation = 0;

    int passRemoved;
    do {
        /* Generation counter: increment instead of memset */
        generation++;
        if (generation == 0) {
            memset(visited, 0, total);
            generation = 1;
        }

        /* Row indexing: find rows with any black pixels (8 bytes at a time) */
        int activeRowCount = 0;
        int byteW = (w + 7) >> 3;
        for (int y = 0; y < h; y++) {
            int rowOff = y * stride;
            int hasBlack = 0;
            int b = 0;
            for (; b + 8 <= byteW; b += 8) {
                unsigned long long chunk;
                __builtin_memcpy(&chunk, buf + rowOff + b, 8);
                if (chunk != 0xFFFFFFFFFFFFFFFFULL) { hasBlack = 1; break; }
            }
            if (!hasBlack) {
                for (; b < byteW; b++) {
                    if (buf[rowOff + b] != 0xFF) { hasBlack = 1; break; }
                }
            }
            if (hasBlack) activeRows[activeRowCount++] = y;
        }

        int pixelsChanged = 0;
        passRemoved = despeckle_single_pass(buf, stride, w, h, maxW, maxH,
                                            visited, generation,
                                            &stack, &stackCap,
                                            &comp, &compCap,
                                            activeRows, activeRowCount,
                                            &pixelsChanged);
        removed += passRemoved;
        if (passRemoved > 0 && !pixelsChanged) break;
    } while (passRemoved > 0);

    free(activeRows); free(comp); free(stack); free(visited);
    return removed;
}

/* ══════════════════════════════════════════════════════════════════════
 * RemoveBlackWires — remove isolated black pixels from packed 1bpp
 *
 * Single-pass algorithm using the ORIGINAL (unmodified) buffer state:
 *   For each black pixel, check if it is vertically isolated (no black
 *   pixel directly above or below) OR horizontally isolated (no black
 *   pixel directly left or right).  If either condition holds, the pixel
 *   is a noise/wire artifact and is removed (set to white).
 *
 * All neighbour checks are performed against the ORIGINAL input state —
 * not the progressively-modified output — so the removal set is
 * determined atomically from the original image.
 * ══════════════════════════════════════════════════════════════════════ */

void EXPORT RemoveBlackWires(unsigned char *buf, int stride, int w, int h) {
    /* Allocate a shadow copy for neighbour lookups (original state) */
    int bytes = stride * h;
    unsigned char *orig = (unsigned char *)malloc(bytes);
    if (!orig) return;
    memcpy(orig, buf, bytes);

    for (int y = 0; y < h; y++) {
        int rowOff = y * stride;

        for (int x = 0; x < w; x++) {
            int byteCol = x >> 3;
            unsigned char mask = (unsigned char)(0x80 >> (x & 7));

            if ((orig[rowOff + byteCol] & mask) != 0) continue; /* white in original */

            /* Vertical isolation: no black directly above AND below.
             * Boundary (y=0 or y=h-1) counts as "has neighbor" (not isolated). */
            int no_above = (y > 0)   && ((orig[(y-1) * stride + byteCol] & mask) != 0);
            int no_below = (y < h-1) && ((orig[(y+1) * stride + byteCol] & mask) != 0);
            if (no_above && no_below) {
                buf[rowOff + byteCol] |= mask; /* set to white */
                continue;
            }

            /* Horizontal isolation: no black directly left AND right.
             * Boundary (x=0 or x=w-1) counts as "has neighbor" (not isolated). */
            int lx = x - 1, rx = x + 1;
            int no_left  = (x > 0)   && ((orig[rowOff + (lx >> 3)] & (unsigned char)(0x80 >> (lx & 7))) != 0);
            int no_right = (x < w-1) && ((orig[rowOff + (rx >> 3)] & (unsigned char)(0x80 >> (rx & 7))) != 0);
            if (no_left && no_right) {
                buf[rowOff + byteCol] |= mask; /* set to white */
            }
        }
    }

    free(orig);
}

/* ══════════════════════════════════════════════════════════════════════
 * RemoveVerticalLines — remove vertical lines from packed 1bpp
 *
 * Per-column analysis: count black pixels, segments (contiguous runs),
 * span (first to last black). If span ≥ minVLen, breaks ≤ maxVBreaks,
 * and blackCount ≥ minBlack → remove thin pixels (hRun ≤ 5).
 *
 * Returns the number of columns cleaned.
 * ══════════════════════════════════════════════════════════════════════ */

int EXPORT RemoveVerticalLines(unsigned char *buf, int stride, int w, int h,
                                int minVLen, int maxVBreaks, int minBlack) {
    int removed = 0;

    for (int x = 0; x < w; x++) {
        int byteCol = x >> 3;
        unsigned char mask = (unsigned char)(0x80 >> (x & 7));

        int blackCount = 0, segments = 0;
        int inBlack = 0;
        int firstBlack = -1, lastBlack = -1;

        for (int y = 0; y < h; y++) {
            int isBlack = (buf[y * stride + byteCol] & mask) == 0;
            if (isBlack) {
                blackCount++;
                if (firstBlack < 0) firstBlack = y;
                lastBlack = y;
                if (!inBlack) { segments++; inBlack = 1; }
            } else
                inBlack = 0;
        }

        if (firstBlack < 0) continue;
        int span = lastBlack - firstBlack + 1;
        int breaks = segments - 1;

        if (span >= minVLen && breaks <= maxVBreaks && blackCount >= minBlack) {
            for (int y = firstBlack; y <= lastBlack; y++) {
                if ((buf[y * stride + byteCol] & mask) != 0) continue;

                /* Only remove if horizontal run is thin (line, not text) */
                int hRun = 1;
                for (int lx = x - 1; lx >= 0 &&
                     (buf[y * stride + (lx >> 3)] & (0x80 >> (lx & 7))) == 0; lx--)
                    hRun++;
                for (int rx = x + 1; rx < w &&
                     (buf[y * stride + (rx >> 3)] & (0x80 >> (rx & 7))) == 0; rx++)
                    hRun++;

                if (hRun <= 5)
                    buf[y * stride + byteCol] |= mask;
            }
            removed++;
        }
    }

    return removed;
}

/* ══════════════════════════════════════════════════════════════════════
 * AutoThreshold — global threshold using Otsu, Kittler, or Ridler-Calvard
 *
 * gray:   grayscale pixel data (1 byte per pixel)
 * w, h:   image dimensions
 * stride: bytes per row (may differ from w for padded buffers)
 * algo:   0=Otsu, 1=Kittler-Illingworth, 2=Ridler-Calvard
 *
 * Returns the computed threshold value.
 * Uses x87 extended precision for FP operations.
 * ══════════════════════════════════════════════════════════════════════ */

static int otsu_threshold_hist(const long long *hist, int total) {
    long long sumAll = 0;
    for (int i = 0; i < 256; i++) sumAll += (long long)i * hist[i];

    long long sumB = 0, wB = 0;
    double maxVar = 0;
    int bestT = 0;
    for (int t = 0; t < 256; t++) {
        wB += hist[t];
        if (wB == 0) continue;
        long long wF = total - wB;
        if (wF == 0) break;
        sumB += (long long)t * hist[t];
        double mB = (double)sumB / (double)wB;
        double mF = (double)(sumAll - sumB) / (double)wF;
        double diff = mB - mF;
        double var = (double)wB * (double)wF * diff * diff;
        if (var > maxVar) { maxVar = var; bestT = t; }
    }
    return bestT;
}

static int ridler_calvard(const long long *hist, int total) {
    set_fpu_extended();
    if (total == 0) return 127;

    long long globalSum = 0;
    for (int i = 0; i < 256; i++) globalSum += (long long)i * hist[i];
    int T = bankers_round((long double)globalSum / (long double)total);

    for (int iter = 0; iter < 255; iter++) {
        long long sum1 = 0, count1 = 0, sum2 = 0, count2 = 0;
        for (int i = 0; i <= T; i++)
            { sum1 += (long long)i * hist[i]; count1 += hist[i]; }
        for (int i = T + 1; i < 256; i++)
            { sum2 += (long long)i * hist[i]; count2 += hist[i]; }
        if (count1 == 0 || count2 == 0) break;
        long double m1 = (long double)sum1 / (long double)count1;
        long double m2 = (long double)sum2 / (long double)count2;
        int newT = bankers_round((m1 + m2) / 2.0L);
        if (newT == T) break;
        T = newT;
    }
    return T;
}

static int kittler_illingworth(const long long *hist, int total) {
    set_fpu_extended();
    if (total == 0) return 127;

    long long globalSum = 0;
    for (int i = 0; i < 256; i++) globalSum += (long long)i * hist[i];
    int T = bankers_round((long double)globalSum / (long double)total);

    for (int iter = 0; iter < 255; iter++) {
        long long n1 = 0, n2 = 0, s1 = 0, s2 = 0;
        long long sq1 = 0, sq2 = 0;
        for (int i = 0; i <= T; i++)
            { n1 += hist[i]; s1 += (long long)i * hist[i]; sq1 += (long long)i * i * hist[i]; }
        for (int i = T + 1; i < 256; i++)
            { n2 += hist[i]; s2 += (long long)i * hist[i]; sq2 += (long long)i * i * hist[i]; }
        if (n1 == 0 || n2 == 0) break;

        long double m1 = (long double)s1 / (long double)n1;
        long double m2 = (long double)s2 / (long double)n2;
        long double v1 = (long double)sq1 / (long double)n1 - m1 * m1;
        long double v2 = (long double)sq2 / (long double)n2 - m2 * m2;
        long double wcv = ((long double)n1 * v1 + (long double)n2 * v2) / (long double)total;
        long double meanSum = m1 + m2;
        if (meanSum < 1e-10L && meanSum > -1e-10L) break;

        long double logRatio = logl((long double)n2 / (long double)n1);
        int newT = bankers_round(meanSum / 2.0L + wcv * logRatio / meanSum);
        if (newT < 0) newT = 0;
        if (newT > 255) newT = 255;
        if (newT == T) break;
        T = newT;
    }
    if (T == 0 || T == 255) T = 127;
    return T;
}

int EXPORT AutoThreshold(const unsigned char *gray, int w, int h,
                         int stride, int algo) {
    set_fpu_extended();
    int total = w * h;

    /* Build histogram from strided buffer */
    long long hist[256];
    memset(hist, 0, sizeof(hist));
    for (int y = 0; y < h; y++) {
        const unsigned char *row = gray + y * stride;
        for (int x = 0; x < w; x++)
            hist[row[x]]++;
    }

    switch (algo) {
        case 1:  return kittler_illingworth(hist, total);
        case 2:  return ridler_calvard(hist, total);
        default: return otsu_threshold_hist(hist, total);
    }
}

/* ══════════════════════════════════════════════════════════════════════
 * FindBlackBorder — find black border from a given side
 *
 * buf:    raw pixel data (packed 1bpp, 8bpp, or 24bpp BGR)
 * stride: bytes per row
 * w, h:   image dimensions in pixels
 * bpp:    bits per pixel (1, 8, or 24)
 * side:   0=Left, 1=Right, 2=Top, 3=Bottom
 * minBlackPct: percentage of pixels that must be black for border line
 * maxHoles:    allowed non-border lines before stopping
 *
 * Returns border position (same semantics as C# FindBlackBorder).
 * ══════════════════════════════════════════════════════════════════════ */

int EXPORT FindBlackBorder(const unsigned char *buf, int stride, int w, int h,
                           int bpp, int side, double minBlackPct, int maxHoles) {
    set_fpu_extended();
    init_luts();

    double minWhiteFrac = (100.0 - minBlackPct) / 100.0;

    int blackCutoff = 128;
    if (bpp == 8) {
        /* Otsu threshold / 2 for black cutoff */
        long long hist8[256];
        memset(hist8, 0, sizeof(hist8));
        for (int y = 0; y < h; y++) {
            const unsigned char *row = buf + y * stride;
            for (int x = 0; x < w; x++)
                hist8[row[x]]++;
        }
        blackCutoff = otsu_threshold_hist(hist8, w * h) / 2;
    }

    int scanColumns = (side == 0 || side == 1); /* Left/Right scan columns */
    int lineCount = scanColumns ? w : h;
    int pixelsPerLine = scanColumns ? h : w;
    int minWhitePixels = (int)(pixelsPerLine * minWhiteFrac);

    int borderPos = 0;
    int holeCount = 0;
    int reverse = (side == 1 || side == 3); /* Right/Bottom */

    for (int i = 0; i < lineCount; i++) {
        int lineIdx = reverse ? (lineCount - 1 - i) : i;

        int whiteCount = 0;
        if (bpp == 1) {
            if (scanColumns) {
                int byteCol = lineIdx >> 3;
                unsigned char mask = (unsigned char)(0x80 >> (lineIdx & 7));
                for (int p = 0; p < pixelsPerLine; p++)
                    if ((buf[p * stride + byteCol] & mask) != 0) whiteCount++;
            } else {
                int rowOff = lineIdx * stride;
                for (int p = 0; p < pixelsPerLine; p++)
                    if ((buf[rowOff + (p >> 3)] & (0x80 >> (p & 7))) != 0) whiteCount++;
            }
        } else if (bpp == 8) {
            if (scanColumns)
                for (int p = 0; p < pixelsPerLine; p++)
                    { if (buf[p * stride + lineIdx] > blackCutoff) whiteCount++; }
            else
                for (int p = 0; p < pixelsPerLine; p++)
                    { if (buf[lineIdx * stride + p] > blackCutoff) whiteCount++; }
        } else { /* 24bpp */
            if (scanColumns)
                for (int p = 0; p < pixelsPerLine; p++) {
                    int off = p * stride + lineIdx * 3;
                    int lum = g_lutB[buf[off]] + g_lutG[buf[off + 1]] + g_lutR[buf[off + 2]];
                    if (lum > blackCutoff) whiteCount++;
                }
            else
                for (int p = 0; p < pixelsPerLine; p++) {
                    int off = lineIdx * stride + p * 3;
                    int lum = g_lutB[buf[off]] + g_lutG[buf[off + 1]] + g_lutR[buf[off + 2]];
                    if (lum > blackCutoff) whiteCount++;
                }
        }

        int isBorderLine = (whiteCount <= minWhitePixels);
        if (isBorderLine) {
            borderPos = i + 1;
            holeCount = 0;
        } else {
            holeCount++;
            if (holeCount > maxHoles) break;
        }
    }

    /* For Right/Bottom, convert back to image coordinates */
    if (reverse) {
        if (borderPos > 0)
            borderPos = lineCount - borderPos - 1;
        else
            borderPos = lineCount - 1;
    }
    return borderPos;
}

/* ══════════════════════════════════════════════════════════════════════
 * FindBlackBorderBatch — run multiple FindBlackBorder calls on the same
 * buffer with a single pass for projection building (cache-efficient).
 *
 * For 1bpp images this builds colBlack[w] and rowBlack[h] projection arrays
 * in a single row-major scan (cache-friendly), then answers all border queries
 * from the projections in O(w+h) time.
 *
 * For 8bpp and 24bpp images it falls back to individual FindBlackBorder calls.
 *
 * nCalls: number of (side, minBlackPct, maxHoles) triples in the calls array.
 * calls:  packed array of [side, minBlackPctDouble, maxHoles] structs —
 *         laid out as: int side, double minBlackPct, int maxHoles, [4-byte pad]
 *         Use FindBlackBorderBatchCall struct from the caller.
 * results: output array of nCalls ints.
 * ══════════════════════════════════════════════════════════════════════ */

/* Call descriptor: side, minBlackPct, maxHoles.
 * Use explicit offsets to match C# StructLayout(Sequential) without Pack.
 * C#: int(4) + double(8) + int(4) = 16 bytes, no padding between fields.
 * We use a packed struct in C to match. */
#pragma pack(push, 1)
typedef struct { int side; double minBlackPct; int maxHoles; } FBBCall;
#pragma pack(pop)

/* minBlack threshold matching FindBlackBorder's minWhitePixels logic exactly:
 * minWhitePixels = (int)(pixelsPerLine * (100-pct)/100)
 * isBorderLine = whiteCount <= minWhitePixels
 *              = (pixelsPerLine - blackCount) <= minWhitePixels
 *              = blackCount >= pixelsPerLine - minWhitePixels
 * So minBlack = pixelsPerLine - minWhitePixels                          */
static int find_border_from_projection(
    const int *lineBlack, int lineCount, int pixelsPerLine,
    int side, double minBlackPct, int maxHoles)
{
    double minWhiteFrac = (100.0 - minBlackPct) / 100.0;
    int minWhitePixels = (int)(pixelsPerLine * minWhiteFrac);
    int minBlack = pixelsPerLine - minWhitePixels;
    int reverse = (side == 1 || side == 3);
    int borderPos = 0, holeCount = 0;

    for (int i = 0; i < lineCount; i++) {
        int lineIdx = reverse ? (lineCount - 1 - i) : i;
        int isBorderLine = (lineBlack[lineIdx] >= minBlack);
        if (isBorderLine) {
            borderPos = i + 1;
            holeCount = 0;
        } else {
            holeCount++;
            if (holeCount > maxHoles) break;
        }
    }

    if (reverse)
        borderPos = borderPos > 0 ? lineCount - borderPos - 1 : lineCount - 1;
    return borderPos;
}

void EXPORT FindBlackBorderBatch(
    const unsigned char *buf, int stride, int w, int h, int bpp,
    int nCalls, const FBBCall *calls, int *results)
{
    set_fpu_extended();
    init_luts();

    if (bpp != 1) {
        /* Fallback: call individual FindBlackBorder for non-1bpp */
        for (int c = 0; c < nCalls; c++) {
            results[c] = FindBlackBorder(buf, stride, w, h, bpp,
                calls[c].side, calls[c].minBlackPct, calls[c].maxHoles);
        }
        return;
    }

    /* Build projections in a single row-major pass (cache-friendly) */
    int *colBlack = (int *)calloc(w, sizeof(int));
    int *rowBlack = (int *)calloc(h, sizeof(int));
    if (!colBlack || !rowBlack) {
        free(colBlack); free(rowBlack);
        /* Emergency fallback */
        for (int c = 0; c < nCalls; c++) {
            results[c] = FindBlackBorder(buf, stride, w, h, bpp,
                calls[c].side, calls[c].minBlackPct, calls[c].maxHoles);
        }
        return;
    }

    /* 1bpp: 0 = black, 1 = white.
     * Walk rows (cache-friendly), accumulate colBlack and rowBlack. */
    int fullBytes = w >> 3;
    int remBits = w & 7;

    for (int y = 0; y < h; y++) {
        const unsigned char *row = buf + y * stride;
        int blackInRow = 0;
        for (int b = 0; b < fullBytes; b++) {
            unsigned char byte = row[b];
            /* Count black (0) bits = 8 - popcount(byte) */
            unsigned char v = byte;
            v = v - ((v >> 1) & 0x55u);
            v = (v & 0x33u) + ((v >> 2) & 0x33u);
            int whiteBits = (v + (v >> 4)) & 0x0Fu;
            blackInRow += 8 - whiteBits;

            /* Accumulate per-column using bit extraction */
            int base = b << 3;
            if (!(byte & 0x80)) colBlack[base + 0]++;
            if (!(byte & 0x40)) colBlack[base + 1]++;
            if (!(byte & 0x20)) colBlack[base + 2]++;
            if (!(byte & 0x10)) colBlack[base + 3]++;
            if (!(byte & 0x08)) colBlack[base + 4]++;
            if (!(byte & 0x04)) colBlack[base + 5]++;
            if (!(byte & 0x02)) colBlack[base + 6]++;
            if (!(byte & 0x01)) colBlack[base + 7]++;
        }
        /* Remaining bits (partial last byte) */
        if (remBits > 0) {
            unsigned char byte = row[fullBytes];
            int base = fullBytes << 3;
            for (int bit = 0; bit < remBits; bit++) {
                unsigned char mask = (unsigned char)(0x80 >> bit);
                if (!(byte & mask)) { colBlack[base + bit]++; blackInRow++; }
            }
        }
        rowBlack[y] = blackInRow;
    }

    /* Answer each query from projections */
    for (int c = 0; c < nCalls; c++) {
        int side = calls[c].side;
        int scanCols = (side == 0 || side == 1);
        if (scanCols)
            results[c] = find_border_from_projection(colBlack, w, h,
                side, calls[c].minBlackPct, calls[c].maxHoles);
        else
            results[c] = find_border_from_projection(rowBlack, h, w,
                side, calls[c].minBlackPct, calls[c].maxHoles);
    }

    free(colBlack);
    free(rowBlack);
}
