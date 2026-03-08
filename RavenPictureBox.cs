using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.Mathematics;
using WIC = Vortice.WIC;

namespace RAVEN
{
    public class RavenPictureBox : Panel
    {
        // === Direct2D / WIC objects ===
        private ID2D1Factory? _d2dFactory;
        private ID2D1HwndRenderTarget? _renderTarget;
        private WIC.IWICImagingFactory? _wicFactory;
        private ID2D1Bitmap? _bitmap;

        // Image dimensions (original, pre-rotation)
        private int _imgW, _imgH;
        // Displayed dimensions (after rotation)
        private int _dispW, _dispH;

        // Current rotation: 0, 90, 180, 270
        private int _rotation;

        // Viewport in IMAGE coordinates (what's currently visible)
        private float _viewLeft, _viewTop, _viewRight, _viewBottom;

        // Selection in IMAGE coordinates (rubber-band result)
        private int _selLeft, _selTop, _selRight, _selBottom;

        // Cursor position in WINDOW coordinates
        private int _cursorX, _cursorY;

        // Mouse drag tracking
        private bool _dragging;
        private Point _dragStart;
        private Point _dragCurrent;
        private MouseButtons _dragButton;

        // Annotations: list of (left, top, right, bottom, color) in image coords
        private List<(int left, int top, int right, int bottom, int color)> _annotations = new();

        // Save-status overlay
        private enum SaveStatus { None, Saving, Saved }
        private SaveStatus _saveStatus = SaveStatus.None;
        private float _saveProgress = 0f; // 0..1 for progress bar animation
        private System.Windows.Forms.Timer _saveAnimTimer;
        private DateTime _savedAt = DateTime.MinValue;

        // Current loaded file path
        private string _currentFile = "";

        // === Image Prefetch Cache (static, shared across all instances) ===
        private struct PrefetchEntry
        {
            public byte[] Pixels;  // 32bppPBGRA
            public int Width;
            public int Height;
        }

        private static readonly ConcurrentDictionary<string, PrefetchEntry> _prefetchCache
            = new(StringComparer.OrdinalIgnoreCase);
        private static CancellationTokenSource _prefetchCts = new();

        // === Public Events ===
        public event EventHandler? DoClickEvent;
        public event EventHandler? OnDoRightClickEvent;
        public event EventHandler? OnDoLeftClickEvent;       // stub: not fired by real impl
        public event EventHandler? OnDoDoubleLeftClickEvent;
        public event EventHandler? OnDoDoubleRightClickEvent;

        // === Public Properties ===
        public DateTime LastZoomed { get; set; } = DateTime.Now;

        public new byte[]? Image
        {
            get
            {
                if (string.IsNullOrEmpty(_currentFile)) return null;
                try { return File.ReadAllBytes(_currentFile); }
                catch { return null; }
            }
            set
            {
                if (value == null) return;
                // Write to a temp file and load it
                string tmp = Path.Combine(Path.GetTempPath(), $"RavenPB_{GetHashCode()}.tmp");
                try
                {
                    File.WriteAllBytes(tmp, value);
                    Load_Image(tmp);
                }
                catch { }
            }
        }

        // --- Stub fields/properties required by Main.cs (not used by D2D viewer) ---
        public int LastLeftRegion, LastTopRegion, LastRightRegion, LastBottomRegion, LastRegion;
        public int LastLeftZoom, LastTopZoom, LastRightZoom, LastBottomZoom;
        public string IMG = "";
        public int PageCount { get; set; }
        public bool IsLoaded => _bitmap != null;

        public RavenPictureBox()
        {
            DoubleBuffered = false; // We handle painting via D2D
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Opaque, true);
            ResizeRedraw = true;
        }

        // ============================================================
        // Direct2D initialization
        // ============================================================
        private void EnsureD2D()
        {
            if (_d2dFactory == null)
                _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>();

            if (_wicFactory == null)
                _wicFactory = new WIC.IWICImagingFactory();

            if (_renderTarget == null && IsHandleCreated && Width > 0 && Height > 0)
                CreateRenderTarget();
        }

        private void CreateRenderTarget()
        {
            _renderTarget?.Dispose();
            _renderTarget = null;

            if (_d2dFactory == null || !IsHandleCreated || Width <= 0 || Height <= 0) return;

            var rtProps = new RenderTargetProperties(
                RenderTargetType.Default,
                new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                0, 0,
                RenderTargetUsage.None,
                FeatureLevel.Default);

            var hwndProps = new HwndRenderTargetProperties
            {
                Hwnd = Handle,
                PixelSize = new Vortice.Mathematics.SizeI(Width, Height),
                PresentOptions = PresentOptions.None
            };

            _renderTarget = _d2dFactory.CreateHwndRenderTarget(rtProps, hwndProps);
        }

        private void DisposeD2D()
        {
            _bitmap?.Dispose(); _bitmap = null;
            _renderTarget?.Dispose(); _renderTarget = null;
            _wicFactory?.Dispose(); _wicFactory = null;
            _d2dFactory?.Dispose(); _d2dFactory = null;
        }

        // ============================================================
        // Image Loading
        // ============================================================
        public void Load_Image(string path)
        {
            if (!File.Exists(path)) return;

            EnsureD2D();
            if (_renderTarget == null || _wicFactory == null) return;

            // Dispose old bitmap
            _bitmap?.Dispose();
            _bitmap = null;

            try
            {
                // Check prefetch cache — skip file I/O + WIC decode if already decoded
                if (_prefetchCache.TryRemove(path, out var cached))
                {
                    int stride = cached.Width * 4;
                    var props = new BitmapProperties(new Vortice.DCommon.PixelFormat(
                        Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));
                    var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                        cached.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        _bitmap = _renderTarget.CreateBitmap(
                            new SizeI(cached.Width, cached.Height),
                            handle.AddrOfPinnedObject(), (uint)stride, props);
                    }
                    finally { handle.Free(); }

                    _imgW = cached.Width;
                    _imgH = cached.Height;
                    _currentFile = path;
                    _rotation = 0;
                    UpdateDisplayDimensions();
                    FitToPanel();
                    ClearSelection();
                    Render();
                    return;
                }

                // Normal path: load from file
                // Load file bytes into memory so WIC never holds a file lock.
                // Prevents "file is open in another process" when threshold writes the same TIF.
                byte[] fileBytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(fileBytes);
                using var decoder = _wicFactory.CreateDecoderFromStream(
                    ms, WIC.DecodeOptions.CacheOnLoad);
                using var frame = decoder.GetFrame(0);
                using var converter = _wicFactory.CreateFormatConverter();
                converter.Initialize(frame, WIC.PixelFormat.Format32bppPBGRA,
                    WIC.BitmapDitherType.None, null, 0, WIC.BitmapPaletteType.Custom);

                _bitmap = _renderTarget.CreateBitmapFromWicBitmap(converter);

                var size = _bitmap.PixelSize;
                _imgW = size.Width;
                _imgH = size.Height;
                _currentFile = path;
                _rotation = 0;
                UpdateDisplayDimensions();
                FitToPanel();
                ClearSelection();
                Render();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load_Image failed: {ex.Message}");
            }
        }

        /// <summary>Show a green progress bar sweeping across the bottom during save.</summary>
        public void ShowSaving()
        {
            _saveStatus = SaveStatus.Saving;
            _saveProgress = 0f;
            if (_saveAnimTimer == null)
            {
                _saveAnimTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
                _saveAnimTimer.Tick += (s, e) =>
                {
                    if (_saveStatus == SaveStatus.Saving)
                    {
                        _saveProgress = Math.Min(1f, _saveProgress + 0.05f);
                        Render();
                    }
                    else if (_saveStatus == SaveStatus.Saved && (DateTime.Now - _savedAt).TotalMilliseconds > 1500)
                    {
                        _saveStatus = SaveStatus.None;
                        _saveAnimTimer.Stop();
                        Render();
                    }
                };
            }
            _saveAnimTimer.Start();
            Render();
        }

        /// <summary>Switch from progress bar to green checkmark (auto-fades after 1.5s).</summary>
        public void ShowSaved()
        {
            _saveStatus = SaveStatus.Saved;
            _saveProgress = 1f;
            _savedAt = DateTime.Now;
            Render();
            // Timer keeps running to auto-clear after 1.5s
            if (_saveAnimTimer != null && !_saveAnimTimer.Enabled)
                _saveAnimTimer.Start();
        }

        /// <summary>Show green checkmark immediately (e.g., when loading image from file).</summary>
        public void ShowFileOk()
        {
            _saveStatus = SaveStatus.Saved;
            _saveProgress = 1f;
            _savedAt = DateTime.Now;
            if (_saveAnimTimer == null)
            {
                _saveAnimTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _saveAnimTimer.Tick += (s, e) =>
                {
                    if (_saveStatus == SaveStatus.Saved && (DateTime.Now - _savedAt).TotalMilliseconds > 1500)
                    {
                        _saveStatus = SaveStatus.None;
                        _saveAnimTimer.Stop();
                        Render();
                    }
                };
            }
            _saveAnimTimer.Start();
            Render();
        }

        /// <summary>
        /// Update the display from raw 8-bit grayscale pixels already in memory.
        /// Skips file I/O and Group 4 decode — just WIC format conversion + render.
        /// Preserves current zoom/viewport.
        /// </summary>
        public void LoadFromGrayscalePixels(byte[] pixels, int width, int height)
        {
            EnsureD2D();
            if (_renderTarget == null) return;

            _bitmap?.Dispose();
            _bitmap = null;

            try
            {
                // Convert 8-bit grayscale → 32-bit BGRA in parallel
                int stride = width * 4;
                byte[] bgra = new byte[height * stride];
                System.Threading.Tasks.Parallel.For(0, height, y =>
                {
                    int src = y * width;
                    int dst = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        byte v = pixels[src + x];
                        int d = dst + x * 4;
                        bgra[d]     = v;   // B
                        bgra[d + 1] = v;   // G
                        bgra[d + 2] = v;   // R
                        bgra[d + 3] = 255; // A
                    }
                });

                // Create D2D bitmap directly from BGRA — no WIC, no file decode
                var props = new BitmapProperties(new Vortice.DCommon.PixelFormat(
                    Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied));
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(bgra, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    _bitmap = _renderTarget.CreateBitmap(
                        new SizeI(width, height), handle.AddrOfPinnedObject(), (uint)stride, props);
                }
                finally { handle.Free(); }

                _imgW = width;
                _imgH = height;
                UpdateDisplayDimensions();
                // Preserve current zoom — just re-render with the new bitmap
                Render();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFromGrayscalePixels failed: {ex.Message}");
            }
        }

        // ============================================================
        // Zoom / Viewport
        // ============================================================
        public int GetZoomLeft() => (int)_viewLeft;
        public int GetZoomTop() => (int)_viewTop;
        public int GetZoomRight() => (int)_viewRight;
        public int GetZoomBottom() => (int)_viewBottom;

        public void ZoomPanImage1(int left, int top, int right, int bottom)
        {
            _viewLeft = left;
            _viewTop = top;
            _viewRight = right;
            _viewBottom = bottom;
            LastZoomed = DateTime.Now;
            Render();
        }

        public void RotateImage(int rotation)
        {
            // Support 0, 90, 180, 270. The param 2=180 in the original API.
            // We'll interpret: 0=0, 1=90, 2=180, 3=270
            switch (rotation)
            {
                case 0: _rotation = 0; break;
                case 1: _rotation = 90; break;
                case 2: _rotation = 180; break;
                case 3: _rotation = 270; break;
                default: _rotation = rotation; break; // allow raw degrees
            }
            UpdateDisplayDimensions();
            FitToPanel();
            Render();
        }

        // ============================================================
        // Selection
        // ============================================================
        public int GetSelectedLeft() => _selLeft;
        public int GetSelectedTop() => _selTop;
        public int GetSelectedRight() => _selRight;
        public int GetSelectedBottom() => _selBottom;

        public void SetSelectedImageArea(int left, int top, int right, int bottom)
        {
            if (left == 0 && top == 0 && right == 0 && bottom == 0)
                ClearSelection();
            else
            {
                _selLeft = left; _selTop = top;
                _selRight = right; _selBottom = bottom;
            }
            Render();
        }

        public int GetCursorX() => _cursorX;
        public int GetCursorY() => _cursorY;

        public Tuple<int, int> ConvertWindowToImageCoordinates(int windowX, int windowY)
        {
            WindowToImage(windowX, windowY, out int ix, out int iy);
            return Tuple.Create(ix, iy);
        }

        // ============================================================
        // Annotations
        // ============================================================
        public void SetTransparentRect(int left, int top, int right, int bottom, int color)
        {
            _annotations.Add((left, top, right, bottom, color));
            Render();
        }

        public void SetTransparentRectNoRef(int left, int top, int right, int bottom, int color)
        {
            _annotations.Add((left, top, right, bottom, color));
            // No refresh
        }

        public void SetTransparentRectNoRef_Refresh()
        {
            Render();
        }

        public void RemoveAnnotation(int a, int b, int c)
        {
            // Always clears all annotations (params ignored)
            _annotations.Clear();
            Render();
        }

        // ============================================================
        // Coordinate transforms
        // ============================================================

        // Scale + offset that maps image coords to window coords
        private float _scale = 1f;
        private float _offsetX, _offsetY;

        private void ComputeTransform()
        {
            if (Width <= 0 || Height <= 0 || _dispW <= 0 || _dispH <= 0) return;

            float vw = _viewRight - _viewLeft;
            float vh = _viewBottom - _viewTop;
            if (vw <= 0 || vh <= 0) return;

            float sx = Width / vw;
            float sy = Height / vh;
            _scale = Math.Min(sx, sy);

            // Center the image in the panel
            float drawW = vw * _scale;
            float drawH = vh * _scale;
            _offsetX = (Width - drawW) / 2f - _viewLeft * _scale;
            _offsetY = (Height - drawH) / 2f - _viewTop * _scale;
        }

        private void ImageToWindow(float ix, float iy, out float wx, out float wy)
        {
            ComputeTransform();
            wx = ix * _scale + _offsetX;
            wy = iy * _scale + _offsetY;
        }

        private void WindowToImage(float wx, float wy, out int ix, out int iy)
        {
            ComputeTransform();
            if (_scale == 0) { ix = 0; iy = 0; return; }
            ix = (int)((wx - _offsetX) / _scale);
            iy = (int)((wy - _offsetY) / _scale);
        }

        private void ClearSelection()
        {
            _selLeft = _selTop = _selRight = _selBottom = 0;
        }

        private void FitToPanel()
        {
            _viewLeft = 0; _viewTop = 0;
            _viewRight = _dispW; _viewBottom = _dispH;
        }

        private void UpdateDisplayDimensions()
        {
            if (_rotation == 90 || _rotation == 270)
            {
                _dispW = _imgH; _dispH = _imgW;
            }
            else
            {
                _dispW = _imgW; _dispH = _imgH;
            }
        }

        // ============================================================
        // Rendering
        // ============================================================
        private void Render()
        {
            if (_renderTarget == null) return;
            ComputeTransform();

            _renderTarget.BeginDraw();
            _renderTarget.Clear(new Color4(0.2f, 0.2f, 0.2f, 1f)); // dark gray bg

            if (_bitmap != null && _dispW > 0 && _dispH > 0)
            {
                // Source rect in the actual bitmap (always full image for now)
                var srcRect = new Vortice.RawRectF(0, 0, _imgW, _imgH);

                // Destination rect: map the visible viewport to window coords
                ImageToWindow(_viewLeft, _viewTop, out float dx1, out float dy1);
                // Recompute for bottom-right using the full displayed image extent
                ImageToWindow(_viewRight, _viewBottom, out float dx2, out float dy2);

                // But we want to draw the FULL image, just scaled/offset so the viewport
                // fills the panel. Compute where 0,0 and dispW,dispH map to.
                ImageToWindow(0, 0, out float x0, out float y0);
                ImageToWindow(_dispW, _dispH, out float x1, out float y1);

                // Apply rotation transform
                var center = new System.Drawing.PointF((x0 + x1) / 2f, (y0 + y1) / 2f);

                if (_rotation != 0)
                {
                    // For rotation, we need to adjust source/dest mapping
                    // D2D approach: set a transform on the render target
                    var m = System.Numerics.Matrix3x2.CreateRotation(
                        _rotation * (float)Math.PI / 180f,
                        new System.Numerics.Vector2(center.X, center.Y));

                    _renderTarget.Transform = m;

                    // Adjust the destination rect for rotated images
                    if (_rotation == 90 || _rotation == 270)
                    {
                        // Swap the draw area dimensions
                        float cX = center.X, cY = center.Y;
                        float hw = (x1 - x0) / 2f, hh = (y1 - y0) / 2f;
                        // Draw into the swapped rect
                        var destRect = new Vortice.RawRectF(
                            cX - hh, cY - hw, cX + hh, cY + hw);
                        _renderTarget.DrawBitmap(_bitmap, destRect, 1f,
                            Vortice.Direct2D1.BitmapInterpolationMode.Linear, srcRect);
                    }
                    else
                    {
                        var destRect = new Vortice.RawRectF(x0, y0, x1, y1);
                        _renderTarget.DrawBitmap(_bitmap, destRect, 1f,
                            Vortice.Direct2D1.BitmapInterpolationMode.Linear, srcRect);
                    }

                    _renderTarget.Transform = System.Numerics.Matrix3x2.Identity;
                }
                else
                {
                    var destRect = new Vortice.RawRectF(x0, y0, x1, y1);
                    _renderTarget.DrawBitmap(_bitmap, destRect, 1f,
                        Vortice.Direct2D1.BitmapInterpolationMode.Linear, srcRect);
                }

                // Draw annotations (semi-transparent colored rects)
                foreach (var ann in _annotations)
                {
                    ImageToWindow(ann.left, ann.top, out float ax1, out float ay1);
                    ImageToWindow(ann.right, ann.bottom, out float ax2, out float ay2);

                    // Color 2 = red (default), anything else also red for now
                    using var brush = _renderTarget.CreateSolidColorBrush(
                        new Color4(1f, 0f, 0f, 0.3f)); // semi-transparent red
                    _renderTarget.FillRectangle(
                        new Vortice.RawRectF(ax1, ay1, ax2, ay2), brush);
                    using var borderBrush = _renderTarget.CreateSolidColorBrush(
                        new Color4(1f, 0f, 0f, 0.8f));
                    _renderTarget.DrawRectangle(
                        new Vortice.RawRectF(ax1, ay1, ax2, ay2), borderBrush, 1f);
                }

                // Draw selection rectangle (rubber-band) during drag
                if (_dragging)
                {
                    float rx1 = Math.Min(_dragStart.X, _dragCurrent.X);
                    float ry1 = Math.Min(_dragStart.Y, _dragCurrent.Y);
                    float rx2 = Math.Max(_dragStart.X, _dragCurrent.X);
                    float ry2 = Math.Max(_dragStart.Y, _dragCurrent.Y);

                    using var selBrush = _renderTarget.CreateSolidColorBrush(
                        new Color4(0f, 0.5f, 1f, 0.25f));
                    _renderTarget.FillRectangle(
                        new Vortice.RawRectF(rx1, ry1, rx2, ry2), selBrush);
                    using var selBorder = _renderTarget.CreateSolidColorBrush(
                        new Color4(0f, 0.5f, 1f, 0.9f));
                    _renderTarget.DrawRectangle(
                        new Vortice.RawRectF(rx1, ry1, rx2, ry2), selBorder, 1.5f);
                }
                // Draw stored selection (if not currently dragging and selection exists)
                else if (_selLeft != 0 || _selTop != 0 || _selRight != 0 || _selBottom != 0)
                {
                    ImageToWindow(_selLeft, _selTop, out float sx1, out float sy1);
                    ImageToWindow(_selRight, _selBottom, out float sx2, out float sy2);

                    using var selBorder = _renderTarget.CreateSolidColorBrush(
                        new Color4(0f, 1f, 0f, 0.9f));
                    _renderTarget.DrawRectangle(
                        new Vortice.RawRectF(sx1, sy1, sx2, sy2), selBorder, 1.5f);
                }
            }

            // Draw save-status overlay
            if (_saveStatus == SaveStatus.Saving)
            {
                // Green progress bar at bottom of panel
                float barH = 4f;
                float barW = Width * _saveProgress;
                using var barBrush = _renderTarget.CreateSolidColorBrush(
                    new Color4(0.2f, 0.8f, 0.2f, 0.9f));
                _renderTarget.FillRectangle(
                    new Vortice.RawRectF(0, Height - barH, barW, Height), barBrush);
            }
            else if (_saveStatus == SaveStatus.Saved)
            {
                // Green checkmark indicator — small circle + check at bottom-right
                float cx = Width - 20f, cy = Height - 20f, r = 10f;
                using var circleBrush = _renderTarget.CreateSolidColorBrush(
                    new Color4(0.15f, 0.7f, 0.15f, 0.85f));
                _renderTarget.FillEllipse(new Ellipse(new Vector2(cx, cy), r, r), circleBrush);
                // Draw checkmark as two lines
                using var checkBrush = _renderTarget.CreateSolidColorBrush(
                    new Color4(1f, 1f, 1f, 1f));
                _renderTarget.DrawLine(
                    new Vector2(cx - 5f, cy),
                    new Vector2(cx - 1f, cy + 5f),
                    checkBrush, 2f);
                _renderTarget.DrawLine(
                    new Vector2(cx - 1f, cy + 5f),
                    new Vector2(cx + 6f, cy - 4f),
                    checkBrush, 2f);
            }

            _renderTarget.EndDraw();
        }

        // ============================================================
        // Mouse handling
        // ============================================================
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _cursorX = e.X; _cursorY = e.Y;

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                _dragging = true;
                _dragStart = e.Location;
                _dragCurrent = e.Location;
                _dragButton = e.Button;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _cursorX = e.X; _cursorY = e.Y;

            if (_dragging)
            {
                _dragCurrent = e.Location;
                Render();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _cursorX = e.X; _cursorY = e.Y;

            if (_dragging)
            {
                _dragging = false;
                int dx = Math.Abs(e.X - _dragStart.X);
                int dy = Math.Abs(e.Y - _dragStart.Y);
                bool wasDrag = dx > 3 || dy > 3;

                if (wasDrag)
                {
                    // Convert rubber-band corners to image coordinates
                    int wx1 = Math.Min(_dragStart.X, e.X);
                    int wy1 = Math.Min(_dragStart.Y, e.Y);
                    int wx2 = Math.Max(_dragStart.X, e.X);
                    int wy2 = Math.Max(_dragStart.Y, e.Y);

                    WindowToImage(wx1, wy1, out _selLeft, out _selTop);
                    WindowToImage(wx2, wy2, out _selRight, out _selBottom);

                    // Clamp to image bounds
                    _selLeft = Math.Max(0, Math.Min(_selLeft, _dispW));
                    _selTop = Math.Max(0, Math.Min(_selTop, _dispH));
                    _selRight = Math.Max(0, Math.Min(_selRight, _dispW));
                    _selBottom = Math.Max(0, Math.Min(_selBottom, _dispH));
                }

                if (_dragButton == MouseButtons.Left)
                {
                    if (wasDrag && _selRight > _selLeft && _selBottom > _selTop)
                    {
                        // Left-drag = zoom into the selected area
                        _viewLeft = _selLeft;
                        _viewTop = _selTop;
                        _viewRight = _selRight;
                        _viewBottom = _selBottom;
                        LastZoomed = DateTime.Now;
                        ClearSelection();
                    }
                    else if (!wasDrag)
                    {
                        // Single left-click = zoom out to full image
                        FitToPanel();
                        LastZoomed = DateTime.Now;
                    }
                    DoClickEvent?.Invoke(this, EventArgs.Empty);
                }
                else if (_dragButton == MouseButtons.Right)
                {
                    if (wasDrag)
                        OnDoRightClickEvent?.Invoke(this, EventArgs.Empty);
                    else
                        ClearSelection(); // click outside = deselect
                }

                Render();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            _cursorX = e.X; _cursorY = e.Y;

            if (e.Button == MouseButtons.Left)
                OnDoDoubleLeftClickEvent?.Invoke(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Right)
                OnDoDoubleRightClickEvent?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_bitmap == null) return;

            // Zoom in/out by 20% per notch
            float factor = e.Delta > 0 ? 0.8f : 1.25f;

            float vw = _viewRight - _viewLeft;
            float vh = _viewBottom - _viewTop;
            float cx = _viewLeft + vw / 2f;
            float cy = _viewTop + vh / 2f;

            float nw = vw * factor;
            float nh = vh * factor;

            // Don't zoom out past full image
            if (nw > _dispW) nw = _dispW;
            if (nh > _dispH) nh = _dispH;

            _viewLeft = cx - nw / 2f;
            _viewTop = cy - nh / 2f;
            _viewRight = cx + nw / 2f;
            _viewBottom = cy + nh / 2f;

            // Clamp
            if (_viewLeft < 0) { _viewRight -= _viewLeft; _viewLeft = 0; }
            if (_viewTop < 0) { _viewBottom -= _viewTop; _viewTop = 0; }
            if (_viewRight > _dispW) { _viewLeft -= (_viewRight - _dispW); _viewRight = _dispW; }
            if (_viewBottom > _dispH) { _viewTop -= (_viewBottom - _dispH); _viewBottom = _dispH; }
            _viewLeft = Math.Max(0, _viewLeft);
            _viewTop = Math.Max(0, _viewTop);

            LastZoomed = DateTime.Now;
            Render();
        }

        // ============================================================
        // WinForms overrides
        // ============================================================
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnsureD2D();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_renderTarget != null && Width > 0 && Height > 0)
            {
                _renderTarget.Resize(new Vortice.Mathematics.SizeI(Width, Height));
                Render();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureD2D();
            Render();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress default background painting; D2D handles it
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeD2D();
            base.Dispose(disposing);
        }

        // ============================================================
        // Stub methods — required by Main.cs but not yet implemented
        // in the D2D viewer. These are no-ops or return defaults.
        // ============================================================

        /// <summary>Stub: zoom by a fixed amount (not yet wired to D2D viewport).</summary>
        public void ZoomImage(int amount) { }

        /// <summary>Stub: reset zoom level.</summary>
        public void ResetZoom(int level) { FitToPanel(); Render(); }

        /// <summary>Stub: pan the viewport by a pixel offset.</summary>
        public void Pan(int x, int y) { }

        /// <summary>Stub: same as SetTransparentRect (alias used by some call sites).</summary>
        public void SetTransparentRectEx(int l, int t, int r, int b, int color)
        {
            SetTransparentRect(l, t, r, b, color);
        }

        /// <summary>Stub: set a colored border around the window.</summary>
        public void SetWindowColorBorder(int color, int width) { }

        /// <summary>Stub: restrict display to a sub-region of the image.</summary>
        public void SetDisplayImageArea(int x1, int y1, int x2, int y2) { }

        /// <summary>Stub: crop image region and save to disk.</summary>
        public void CropAndSave(string input, string output, int x1, int y1, int x2, int y2) { }

        /// <summary>Stub: remove a dirty vertical/horizontal line from the image.</summary>
        public void RemoveDirtyLine(string file, int page, string output, int thickness, int col) { }

        /// <summary>Stub: deskew the image.</summary>
        public void Deskew(string input, string temp, string output) { }

        /// <summary>Stub: check if a file is a valid image the viewer can handle.</summary>
        public bool ValidImageFile(string path) => true;

        /// <summary>Stub: show an error/placeholder image.</summary>
        public void Error_Image() { }

        /// <summary>Clear the displayed image and release resources.</summary>
        public void Clear_Image()
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _currentFile = "";
            _annotations.Clear();
            ClearSelection();
            Render();
        }

        /// <summary>Stub: move the selected annotation by an offset.</summary>
        public void MoveSelectAnnotation(int x, int y) { }

        /// <summary>Stub: select an annotation by index.</summary>
        public void SelectAnnotation(int z) { }

        /// <summary>Stub: get the color of an annotation by index.</summary>
        public int GetColorRect(int index) => 0;

        /// <summary>Programmatically fire the DoClickEvent.</summary>
        public void Do_Click() { DoClickEvent?.Invoke(this, EventArgs.Empty); }

        // ============================================================
        // Image Prefetch — decode images on background threads
        // so Load_Image only needs a fast memcpy to D2D bitmap.
        // ============================================================

        /// <summary>
        /// Pre-decode an image file to BGRA pixels on a background thread.
        /// The result is cached so Load_Image can skip file I/O + WIC decode.
        /// </summary>
        public static void PrefetchImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (_prefetchCache.ContainsKey(path)) return; // already cached

            var cts = _prefetchCts; // capture current CTS
            Task.Run(() =>
            {
                try
                {
                    if (cts.IsCancellationRequested) return;

                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                    byte[] fileBytes = File.ReadAllBytes(path);
                    if (cts.IsCancellationRequested) return;

                    // Each background thread gets its own WIC factory (COM free-threaded)
                    using var wicFactory = new WIC.IWICImagingFactory();
                    using var ms = new MemoryStream(fileBytes);
                    using var decoder = wicFactory.CreateDecoderFromStream(
                        ms, WIC.DecodeOptions.CacheOnLoad);
                    using var frame = decoder.GetFrame(0);
                    using var converter = wicFactory.CreateFormatConverter();
                    converter.Initialize(frame, WIC.PixelFormat.Format32bppPBGRA,
                        WIC.BitmapDitherType.None, null, 0, WIC.BitmapPaletteType.Custom);

                    if (cts.IsCancellationRequested) return;

                    var size = converter.Size;
                    uint stride = (uint)(size.Width * 4);
                    byte[] pixels = new byte[stride * size.Height];
                    converter.CopyPixels(stride, pixels);

                    if (!cts.IsCancellationRequested)
                    {
                        _prefetchCache[path] = new PrefetchEntry
                        {
                            Pixels = pixels,
                            Width = size.Width,
                            Height = size.Height
                        };
                    }
                }
                catch { /* prefetch failure is non-fatal — Load_Image falls back to normal path */ }
            });
        }

        /// <summary>
        /// Prefetch multiple images. Paths already in cache are skipped.
        /// </summary>
        public static void PrefetchImages(IEnumerable<string> paths)
        {
            foreach (var path in paths)
                PrefetchImage(path);
        }

        /// <summary>
        /// Cancel all in-flight prefetch work and clear the cache.
        /// Call before CPU-intensive work (e.g. thresholding) to free cores.
        /// </summary>
        public static void CancelAllPrefetch()
        {
            var oldCts = Interlocked.Exchange(ref _prefetchCts, new CancellationTokenSource());
            oldCts.Cancel();
            oldCts.Dispose();
            _prefetchCache.Clear();
        }

        /// <summary>
        /// Cancel in-flight prefetch and evict entries NOT in the keep set.
        /// Call on navigation to retain still-valid cached images.
        /// </summary>
        public static void RefreshPrefetch(HashSet<string> keepPaths)
        {
            var oldCts = Interlocked.Exchange(ref _prefetchCts, new CancellationTokenSource());
            oldCts.Cancel();
            oldCts.Dispose();

            foreach (var key in _prefetchCache.Keys)
            {
                if (!keepPaths.Contains(key))
                    _prefetchCache.TryRemove(key, out _);
            }
        }
    }
}
