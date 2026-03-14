using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Data.Common;
using System.Drawing;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
//Think this got added on accident?
using System.Security.Cryptography.X509Certificates;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

// Link to Read http://msdn.microsoft.com/msdnmag/issues/02/08/CQA/  for [MarshalAs(UnmanagedType.LPStr)] we might have to use that on several buffers in the USVWIN stuff!

// xxx
namespace RAVEN
{
    public partial class KeyPicture : Panel
    {
        // Define an event
        public event EventHandler DoClickEvent;
        public event EventHandler OnDoRightClickEvent;
        public event EventHandler OnDoLeftClickEvent;
        public event EventHandler OnDoDoubleLeftClickEvent;
        public event EventHandler OnDoDoubleRightClickEvent;



        // Method to safely raise the event
        protected virtual void OnDoClick()
        {
            DoClickEvent?.Invoke(this, EventArgs.Empty);
        }

        // Sloppy but I think will work
        protected virtual void OnDoRightClick()
        {
            OnDoRightClickEvent?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnDoLeftClick()
        {
            OnDoLeftClickEvent?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnDoDBLeftClick()
        {
            OnDoDoubleLeftClickEvent?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnDoDBRightClick()
        {
            OnDoDoubleRightClickEvent?.Invoke(this, EventArgs.Empty);
        }





        // This allows us to generate a Unique name across all Image Controls
        private static int InternalControlCounter = 0;
        // This is the Handle to the WinViw Control
        private Int32 ImageHandle = -1;


        private string ToBeLoaded = "", CurrentFile = "";
        public int PageCount = 0;
        private int CurrentPage = 1;
        private int iRedactLineRate = 32;
        private int iPrintCropLeft = -1, iPrintCropRight = -1, iPrintCropTop = -1, iPrintCropBottom = -1;
        private int iAutoInvert = 0;

        public int LastLeftRegion = 0, LastTopRegion = 0, LastRightRegion = 0, LastBottomRegion = 0, LastRegion = 0, LastLeftZoom = 0, LastTopZoom, LastRightZoom, LastBottomZoom;

        // NS 202402 - Sure there's a better way to do this but need to keep track of what Date/Time this image was last zoomed
        public DateTime LastZoomed = DateTime.Now;

        public enum RightMouseMode : int { Magnifier = 0, PrintCrop = 1, Redact = 2 };
        public enum RedactionMode : int { Off = 0, Opaque = 1, Transparent = 2, Editable = 3, UNKNOWN_6 = 6, UNKNOWN_7 = 7 };
        public enum DisplayMode : int { Entire = 0, Width = 1, Normal = 2, Two2One = 3, Four2One = 4, Height = 5, NormalNoAspect = 6 };

        private RedactionMode iRedactMode = RedactionMode.Editable;
        private RightMouseMode iRightMouseMode = RightMouseMode.Magnifier;
        private DisplayMode iDisplayMode = DisplayMode.Entire;

        private string Redaction_File = "";
        private string PrintCrop_File = "";
        private string Temporary_File = "";


        // Used for Image mapping
        public string IMG = "";
        private string Image_File = "";
        bool From_Memory = false;
        bool In_Byte_Array = false;
        bool In_Clear_Error = false;

        // GetSelectedImageTop(Int32 Spare )

        public bool IsLoaded
        {
            get { return (!String.IsNullOrEmpty(CurrentFile)); }
        }


        private int iRedactPossible = 0;

        private USVWin.TiffRedactInfo TI;
        private int SizeTI = 0;
        private int iPrintCropLocal = 0;
        private USVWin.USVWinCallback RMB_Hook;
        private USVWin.USVWinCallback LMB_Hook;
        private USVWin.USVWinCallback DBRMB_Hook;
        private USVWin.USVWinCallback DBLMB_Hook;

        private byte[] iImage = null;
        public byte[] Image
        {
            get
            {
                if (String.IsNullOrEmpty(CurrentFile) && From_Memory == false)
                {
                    return (null);
                }

                if (!In_Byte_Array)
                {
                    try
                    {
                        FileStream FS = new FileStream(CurrentFile, FileMode.Open, FileAccess.Read);
                        iImage = null;
                        iImage = new Byte[FS.Length];
                        FS.Read(iImage, 0, (int)FS.Length);
                        FS.Close();
                        FS = null;
                        In_Byte_Array = true;
                    }
#pragma warning disable 168
                    catch (Exception e)
                    {
                        iImage = null;
                        In_Byte_Array = false;
                        Error_Image();
                    }
#pragma warning restore 168
                }

                return (iImage);
            }

            set
            {

                if (iImage != null && iImage.Equals(value)) return;
                if (iImage != null && iImage.Length == value.Length) return;
                iImage = value;
                In_Byte_Array = true;

                if (iImage == null)
                {
                    Clear_Image();
                    return;
                }

                try
                {
                    System.IO.FileStream FS = new System.IO.FileStream(Image_File, System.IO.FileMode.Create);
                    System.IO.BinaryWriter BW = new System.IO.BinaryWriter(FS);
                    BW.Write(iImage);
                    BW.Close();
                    FS.Close();
                    BW = null;
                    FS = null;
                    From_Memory = true;
                    Load_Image(Image_File);
                }
#pragma warning disable 168
                catch (Exception e)
                {
                    From_Memory = false;
                    Error_Image();
                    return;
                }
#pragma warning restore 168
            }
        }

        public int GetSelectedTop()
        {
            int STop = USVWin.GetSelectedImageTop(0);
            //USVWin.GetSelectedImageTop
            return STop;


        }

        public int GetSelectedBottom()
        {
            int STop = USVWin.GetSelectedImageBottom(0);
            //USVWin.GetSelectedImageTop
            return STop;
        }

        public int GetSelectedLeft()
        {
            int STop = USVWin.GetSelectedImageLeft(0);

            //USVWin.GetSelectedImageTop
            return STop;
        }

        public int GetSelectedRight()
        {
            int STop = USVWin.GetSelectedImageRight(0);
            //USVWin.GetSelectedImageTop
            return STop;
        }

        public void Deskew(string inputfilename, string tempfilename, string outputfilename)
        {
            USVWin.VWDeskewBlackBorders(inputfilename, tempfilename, outputfilename);
        }

        public void SetTransparentRectEx(int left, int top, int right, int bottom, int color)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.SetColorRectEx(left, top, right, bottom, 2, 0, 0);


            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        public void SetDisplayImageArea(int X1, int Y1, int X2, int Y2)
        {
            USVWin.SetDisplayImageArea(X1, Y1, X2, Y2);
        }

        public Tuple<int, int> ConvertWindowToImageCoordinates(int windowX, int windowY)
        {
            int imageX = 0;
            int imageY = 0;


            // Call the external function
            int result = USVWin.WindowToImage(windowX, windowY, ref imageX, ref imageY);

            return Tuple.Create(imageX, imageY);

        }

        //Added by NS 202402
        public void SetTransparentRect(int left, int top, int right, int bottom, int color)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.SetColorRect(left, top, right, bottom, color);


            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        public void SetTransparentRectNoRef(int left, int top, int right, int bottom, int color)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.SetColorRect(left, top, right, bottom, 2);
        }

        public void SetTransparentRectNoRef_Refresh()
        {
            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        public int GetColorRect(int index)
        {
            USVWin.SetImageWindow(ImageHandle);
            int filler = 0;
            int xindex = USVWin.VWGetColorRect(0, ref filler, ref filler, ref filler, ref filler, ref filler);
            return xindex;
        }

        public void RemoveAnnotation(int rectangles, int Othr1, int Othr2)
        {
            USVWin.RemoveAnnotation(rectangles, Othr1, Othr2);
        }

        public void MoveSelectAnnotation(int X, int Y)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.MoveSelectAnnotation(X, Y);
            USVWin.PaintImage(0);

        }

        public void SelectAnnotation(int z)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.SelectAnnotation(z);

        }

        public int GetZoomLeft()
        {
            int left = USVWin.GetWindowCurrentStatus(7);
            return left;
        }

        public int GetZoomRight()
        {
            int right = USVWin.GetWindowCurrentStatus(5);
            return right;
        }

        public int GetZoomTop()
        {
            int top = USVWin.GetWindowCurrentStatus(8);
            return top;
        }

        public int GetZoomBottom()
        {
            int bottom = USVWin.GetWindowCurrentStatus(6);
            return bottom;
        }
        public int GetCursorX()
        {
            int retval = USVWin.GetWindowCurrentStatus(25);
            
            return retval;
        }
        public int GetCursorY()
        {
            int retval = USVWin.GetWindowCurrentStatus(26);
            return retval;
        }


        public void CropAndSave(string Input, string Output, int x1, int y1, int x2, int y2)
        {
            // var watch = System.Diagnostics.Stopwatch.StartNew();
            USVWin.VWCropFileEx(Input, 1, Output, x1, y1, x2, y2);
            // watch.Stop();
            // var elapsedMs = watch.ElapsedMilliseconds;

        }





        public void Error_Image()
        {
            From_Memory = false;
            In_Byte_Array = false;
            iImage = null;
            CurrentFile = "";
            if (In_Clear_Error) return;  // Make sure we don't get into a "Load" Loop
            In_Clear_Error = true;
            In_Clear_Error = false;
            CurrentFile = "";
        }

        public void Clear_Image()
        {
            USVWin.SetImageWindow(ImageHandle);

            From_Memory = false;
            In_Byte_Array = false;
            iImage = null;
            CurrentFile = "";
            if (In_Clear_Error) return; // Make sure we don't get into a "Load" Loop
            In_Clear_Error = true;
            In_Clear_Error = false;
            CurrentFile = "";
            USVWin.PaintImage(0);

            //USVWin.CloseImageWindow(ImageHandle);

        }

        public KeyPicture()
        {
            SetStyle(ControlStyles.UserPaint, !this.DesignMode);
            SizeTI = Marshal.SizeOf(TI);
            RMB_Hook = new USVWin.USVWinCallback(RMB_Callback);
            LMB_Hook = new USVWin.USVWinCallback(LMB_Callback);
            DBRMB_Hook = new USVWin.USVWinCallback(DBRMB_Callback);
            DBLMB_Hook = new USVWin.USVWinCallback(DBLMB_Callback);
            InitializeComponent();
            InternalControlCounter++;
            this.BorderStyle = BorderStyle.Fixed3D;
            this.Width = 10;
            this.Height = 10;

        }

        public void Do_Click()
        {
            OnDoClick();


        }

        protected override void OnCreateControl()
        {

            base.OnCreateControl();

            // Added by NS 20240205
            // Check if the control is in design mode
            if (this.DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            {
                // In design mode, do not attempt to use USVWin32.dll functionality
                return;
            }

            string UniqueName = "kellpro" + InternalControlCounter.ToString();



            // Create Window is in On Show Event
            CreateImageWindow();

        }



        unsafe private bool LMB_Callback(Int32 Hwnd, UInt32 iMessage, UInt32 wParam, Int32 lParam)
        {
            
            USVWin.SetImageWindow(ImageHandle);
            LastRegion = 1;
            // This is selected area
            LastLeftRegion = USVWin.GetSelectedImageLeft(0);
            LastTopRegion = USVWin.GetSelectedImageTop(0);
            LastRightRegion = USVWin.GetSelectedImageRight(0);
            LastBottomRegion = USVWin.GetSelectedImageBottom(0);

            //This is zoom area - added by NS 202402
            LastLeftZoom = USVWin.GetWindowCurrentStatus(7);
            LastTopZoom = USVWin.GetWindowCurrentStatus(8);
            LastRightZoom = USVWin.GetWindowCurrentStatus(5);
            LastBottomZoom = USVWin.GetWindowCurrentStatus(6);

            OnDoLeftClick(); 

            LastZoomed = DateTime.Now;
            Do_Click();
            

            return (false);
        }

        unsafe private bool DBRMB_Callback(Int32 Hwnd, UInt32 iMessage, UInt32 wParam, Int32 lParam)
        {

            USVWin.SetImageWindow(ImageHandle);
            LastRegion = 1;
            // This is selected area
            LastLeftRegion = USVWin.GetSelectedImageLeft(0);
            LastTopRegion = USVWin.GetSelectedImageTop(0);

            // Do_Double_Right_Click();
            OnDoDBRightClick();
            return (false);
        }

        unsafe private bool DBLMB_Callback(Int32 Hwnd, UInt32 iMessage, UInt32 wParam, Int32 lParam)
        {

            USVWin.SetImageWindow(ImageHandle);
            LastRegion = 1;
            // This is selected area
            LastLeftRegion = USVWin.GetSelectedImageLeft(0);
            LastTopRegion = USVWin.GetSelectedImageTop(0);

            OnDoDBLeftClick();

            return (false);
        }


        unsafe private bool RMB_Callback(Int32 Hwnd, UInt32 iMessage, UInt32 wParam, Int32 lParam)
        {
            USVWin.SetImageWindow(ImageHandle);

            int Left, Top, Right, Bottom;
            int i;
            int iIndex;
            int ret;

            LastRegion = 2;
            Left = LastLeftRegion = USVWin.GetSelectedImageLeft(0);
            Top = LastTopRegion = USVWin.GetSelectedImageTop(0);
            Right = LastRightRegion = USVWin.GetSelectedImageRight(0);
            Bottom = LastBottomRegion = USVWin.GetSelectedImageBottom(0);

            //Added by NS 20240216 trying to capture right click on images as an event. Commented out Do_Click();

            OnDoRightClick();
            if (this.iRightMouseMode == RightMouseMode.Magnifier) return (false);

            // Do_Click();

            if (iRightMouseMode == RightMouseMode.PrintCrop)   /* PrintCropArea */
            {
                if (Left + 8 > Right || Top + 8 >= Bottom)
                {
                    Left = Top = Right = Bottom = -1;
                }

                if (iPrintCropLocal != 0)
                {
                    iPrintCropBottom = Bottom;
                    iPrintCropLeft = Left;
                    iPrintCropRight = Right;
                    iPrintCropTop = Top;
                    SetImagePagePrintToggleArea(CurrentFile, CurrentPage, 1);
                    return (false);
                }



                ret = AddPrintCropTag(CurrentFile, CurrentPage, PrintCrop_File, Left, Top, Right, Bottom);
                if (ret == 0)
                {
                    System.IO.File.Move(PrintCrop_File, CurrentFile);
                    CurrentFile = PrintCrop_File;
                    CurrentPage = 1;
                    SetImagePage(CurrentFile, CurrentPage);
                }
            }
            if (iRightMouseMode == RightMouseMode.Redact)   /* Redact Image Mode */
            {
                if (Left + 8 > Right || Top + 8 >= Bottom)  // Select Point - Deleting?
                {
                    iIndex = -1;

                    fixed (USVWin.TiffRedactInfo* p = &TI)
                        for (i = 0; i < p->iCount; i++)
                        {
                            if ((int)p->iLeft[i] <= Left &&
                               (int)p->iTop[i] <= Top &&
                               (int)p->iRight[i] >= Right &&
                               (int)p->iBottom[i] >= Bottom)
                                iIndex = i;
                        }
                    if (iIndex > -1)
                    {
                        fixed (USVWin.TiffRedactInfo* p = &TI)
                            for (; iIndex + 1 < p->iCount; iIndex++)
                            {
                                p->iLeft[iIndex + 0] = p->iLeft[iIndex + 1];
                                p->iTop[iIndex + 0] = p->iTop[iIndex + 1];
                                p->iRight[iIndex + 0] = p->iRight[iIndex + 1];
                                p->iBottom[iIndex + 0] = p->iBottom[iIndex + 1];
                            }
                        TI.iCount--;

                        ret = AddRedactTag(Redaction_File, CurrentPage, Redaction_File);
                        if (ret == 0)
                        {
                            SetImagePage(Redaction_File, CurrentPage);
                        }
                    }
                    else
                    {
                        MessageBox.Show("No Redaction at that location", "Warning");
                    }
                    return (false);
                }
                else  // Adding a Redact Area
                {
                    fixed (USVWin.TiffRedactInfo* p = &TI)
                        if (p->iCount < 50)
                        {
                            p->iLeft[TI.iCount] = (uint)Left;
                            p->iTop[TI.iCount] = (uint)Top;
                            p->iRight[TI.iCount] = (uint)Right;
                            p->iBottom[TI.iCount] = (uint)Bottom;
                            p->iCount++;

                            ret = AddRedactTag(Redaction_File, CurrentPage, Redaction_File);
                            if (ret == 0)
                            {
                                SetImagePage(CurrentFile, CurrentPage);
                            }
                        }
                }
            }
            return (false);
        }

        int AddPrintCropTag(String ImageFileName, int iPage, String OutputFileName, int iLeft, int iTop, int iRight, int iBottom)
        {
            int Width = 0, Height = 0;
            int ImageOffset = 0;
            int Xres = 0, Yres = 0, CompressionMode = 0, CompressionDirection = 0, ImageNegativeStored = 0;
            int ret;

            ret = USVWin.GetImageInfoExtended(ImageFileName, iPage, ref Width, ref Height,
               ref ImageOffset, ref Xres, ref Yres, ref CompressionMode, ref CompressionDirection,
               ref ImageNegativeStored);

            USVWin.TiffPrintCropInfo PCI = new USVWin.TiffPrintCropInfo();
            PCI.iLeft = iLeft;
            PCI.iTop = iTop;
            PCI.iRight = iRight;
            PCI.iBottom = iBottom;

            if (CompressionMode >= 1024) return (400);

            if (CompressionMode >= 256) /* Stripped Image */
            {
                int NullHndl = 0;
                USVWin.ConvertPageofImageFile(ref NullHndl, ImageFileName, iPage, Temporary_File);
                ret = USVWin.SetTiffStart(Temporary_File, 1);
                if (ret != 0) return (1);

                if (iLeft < 0)
                {
                    USVWin.RemoveTiffTag(0x872c);
                }
                else
                {
                    ret = USVWin.SetTiffTag(0x872c, 4, 4, 0, ref PCI);
                    if (ret != 0) ret = USVWin.ReSetTiffTag(0x872c, 4, 4, 0, ref PCI);
                }
                ret = USVWin.SetTiffEnd(OutputFileName);
                if (ret != 0) return (2);
            }
            else
            {
                ret = USVWin.SetTiffStart(ImageFileName, iPage);
                if (ret != 0) return (1);

                if (iLeft < 0)
                {
                    USVWin.RemoveTiffTag(0x872c);
                }
                else
                {
                    ret = USVWin.SetTiffTag(0x872c, 4, 4, 0, ref PCI);
                    if (ret != 0) ret = USVWin.ReSetTiffTag(0x872c, 4, 4, 0, ref PCI);
                }
                ret = USVWin.SetTiffEnd(OutputFileName);
                if (ret != 0) return (2);
            }
            return (0);
        }

        private void CreateImageWindow()
        {



            /// <summary>
            /// Sets how the mouse buttons will work when over the image window.
            /// </summary>
            /// <param name="Mode">0 = Standard Zoom
            ///    2 = Allow callbacks
            ///    6 = Select box
            ///    7 = Magnafier
            ///    8 = Hold and Pan</param>
            /// <param name="MouseKey">0 = Left Mouse Button
            ///    1 = Right Mouse Button
            ///    2 = Left Double Click
            ///    3 = Right Double Click</param>
            /// <returns>0 = Mouse mode set correctly.
            /// 1 = No image window or memory error.</returns>
            /// <remarks></remarks>


            // Added by NS 20240205

            if (LicenseManager.UsageMode != LicenseUsageMode.Runtime)
            {
                return; // Exit the method if we're in design mode, avoiding the rest of the method
            }

            if (USVWin.StartImageWindow(this.Handle, 1, 0, 0, 0, 0) == 0)
            {
                ImageHandle = USVWin.GetImageWindowHandle();
                SetMouseMode(0);
                USVWin.SetCallback(4, RMB_Hook);
                USVWin.SetCallback(3, LMB_Hook);
                USVWin.SetCallback(7, DBRMB_Hook);
                USVWin.SetCallback(6, DBLMB_Hook);
                USVWin.AutoSetNegativeFlagOnDisplay(iAutoInvert);
                USVWin.SetScrolLessFlash(1);
                USVWin.SetImageScaling(4);

                USVWin.SetMaxAnnotations(25, 25, 25, 25);


                USVWin.SetMouseMode(6, 3);
                USVWin.SetMouseMode(0, 0);  // Left  - Zoom Mode 

                USVWin.SetMouseMode(12, 2);  // Full Image - Square Pixels 


            }
            else
            {
                Debug.Print("Unable to Create USVWin Image Window");
            }

            if (!String.IsNullOrEmpty(ToBeLoaded)) Load_Image(ToBeLoaded);

        }

        public bool ValidImageFile(string FileName)
        {
            int Width = 0, Height = 0;
            int ret;

            ret = USVWin.GetImageInfo(FileName, 1, ref Width, ref Height);

            if (ret != 0) return (false);
            return (true);
        }

        private int SetImagePage(String ImageFileName, int iPage)
        {
            int ret = 0;
            int Width = 0, Height = 0;
            int ImageOffset = 0;
            int Xres = 0, Yres = 0, CompressionMode = 0, CompressionDirection = 0, ImageNegativeStored = 0;

            // Set to off
            iRedactPossible = 0;

            USVWin.SetImageWindow(ImageHandle);

            USVWin.SetScreenForeBackColor(-1, -1, 0x00A0A0A0);

            // NS20240205 Setting redact mode off to fix my Tif loads without double loading (I think)
            this.iRedactMode = RedactionMode.Off;

            if (!(this.iRedactMode == RedactionMode.Off))
            {
                // Check to see if we are 
                if (!CurrentFile.Equals(ImageFileName, StringComparison.OrdinalIgnoreCase) || iPage != CurrentPage)
                {
                    ret = USVWin.GetImageInfoExtended(ImageFileName, iPage, ref Width, ref Height,
                          ref ImageOffset, ref Xres, ref Yres, ref CompressionMode, ref CompressionDirection,
                          ref ImageNegativeStored);

                    PageCount = USVWin.GetTiffPages(ImageFileName);
                    if (CompressionMode < 512) // Old Code has it limited to 1 page.  iPage == 1 && PageCount == 1)
                    {
                        iRedactPossible = 1;

                        CurrentFile = ImageFileName;
                        CurrentPage = iPage;

                        ret = USVWin.GetTiffTagData(ImageFileName, iPage, 0x872d, ref TI, SizeTI);
                        if (ret == 0 && USVWin.GetTiffTagType() == 1 && USVWin.GetTiffTagCount() == SizeTI)
                        {
                            ret = SetKellproRedact(ImageFileName, iPage, Redaction_File, (int)iRedactMode, TI);
                        }
                        else
                        {
                            TI.iCount = 0;

                            int NullHandle = 0;
                            // If we fail to export a page, we copy the file into the Resaction_File

                        }
                    }
                    else  // Color Tiff
                    {
                        iRedactPossible = 0;
                    }
                }
                else // We are viewing the Same Page, we don't need to re-export the data
                {
                    ret = SetKellproRedact(CurrentFile, CurrentPage, Redaction_File, (int)iRedactMode, TI);
                }

                if (iRedactPossible != 0)
                {
                    ret = USVWin.SetImageMultPage(Redaction_File, 1);

                    if (ret == 0 && iRightMouseMode == RightMouseMode.PrintCrop) SetImagePagePrintToggleArea(ImageFileName, iPage, 1);
                    if (iDisplayMode != DisplayMode.Entire) USVWin.SetDisplayImage((int)iDisplayMode);
                    return (ret);
                }
            }

            iRedactPossible = 0;

            ret = USVWin.SetImageMultPage(ImageFileName, iPage);
            if (ret == 0)
            {
                CurrentFile = ImageFileName;
                CurrentPage = iPage;
                if (iRightMouseMode == RightMouseMode.PrintCrop) SetImagePagePrintToggleArea(ImageFileName, iPage, 1);
                if (iDisplayMode != DisplayMode.Entire) USVWin.SetDisplayImage((int)iDisplayMode);
            }

            else
            {
                Error_Image();
            }
            SetMouseMode(0);

            Application.DoEvents();
            return (ret);
        }

        public void RotateImage(int Scale)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.RotateImage(Scale);
            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        public void ResetZoom(int WhatLevel)
        {
            USVWin.SetDisplayImage(WhatLevel);
            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        public void ZoomPanImage1(int Left, int Top, int XScale, int YScale)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.ZoomPanImage(Left, Top, XScale, YScale);
            USVWin.PaintImage(0);
            Application.DoEvents();

        }

        public void SetSelectedImageArea(int Left, int Top, int XScale, int YScale)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.SetSelectedImageArea(Left, Top, XScale, YScale);
            // Application.DoEvents();

        }

        /// <summary>
        /// Removes dirty lines from an image file by calling the USVWin.RemoveDirtyLine method.
        /// This method serves as a wrapper to abstract the external library call and can be extended for future custom logic.
        /// </summary>
        /// <param name="ImageFileName">Name of the image file.</param>
        /// <param name="iPage">Page number to process.</param>
        /// <param name="OutputFileName">Name of the output file.</param>
        /// <param name="MaxLineThickness">Maximum thickness of lines to remove.</param>
        /// <param name="Column">Column number to process.</param>
        public void RemoveDirtyLine(string ImageFileName, int iPage, string OutputFileName, int MaxLineThickness, int Column)
        {
            USVWin.RemoveDirtyLine(ImageFileName, iPage, OutputFileName, MaxLineThickness, Column);
        }


        public void ZoomImage(int ZoomAmount)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.ZoomImage(ZoomAmount);
            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        public void SetWindowColorBorder(int Color, int Width)
        {
            USVWin.SetWindowColorBorder(Color, Width);
        }

        public void Pan(int X, int Y)
        {
            USVWin.SetImageWindow(ImageHandle);
            USVWin.MoveImage(X, Y);
            USVWin.PaintImage(0);

            Application.DoEvents();
        }

        public void Load_Image(string ImageName)
        {
            if (!System.IO.File.Exists(ImageName))
            {
                Clear_Image();
                return;
            }
            if (!ValidImageFile(ImageName))
            {
                Error_Image();
                return;
            }

            if (ImageHandle > 0)
            {
                USVWin.SetImageWindow(ImageHandle);
                //NS20240205 - Think I can check my TemThink I added this for trouble shooting and it's not needed.
                //20240205 - Old Code

                int temptest = SetImagePage(ImageName, 1);
                if (temptest != 0)
                {
                    Error_Image();
                }

                else
                {
                    //NS20240205 - I don't think I need to re-paint the image. 
                    // USVWin.PaintImage(0);

                    PageCount = USVWin.GetTiffPages(ImageName);
                    CurrentPage = 1;
                    Application.DoEvents();
                }
            }
            else
            {
                ToBeLoaded = ImageName;
            }

        }

        unsafe int SetKellproRedact(String ImageFileName, int iPage, string RedactFileName, int TheRedactMode, USVWin.TiffRedactInfo TI)
        {
            int Width = 0, Height = 0;
            int ImageOffset = 0;
            int Xres = 0, Yres = 0, CompressionMode = 0, CompressionDirection = 0, ImageNegativeStored = 0;
            int ret;
            int i;


            ret = USVWin.GetImageInfoExtended(ImageFileName, iPage, ref Width, ref Height,
                  ref ImageOffset, ref Xres, ref Yres, ref CompressionMode, ref CompressionDirection,
                  ref ImageNegativeStored);


            if (ret != 0 || CompressionMode >= 512) //  || iPage != 1) 
            {
                iRedactPossible = 0;
                return (1);
            }

            ret = USVWin.PrintNWMultStart(4, Width, Height, 4, RedactFileName);
            if (ret != 0)
            {
                iRedactPossible = 0;
                return (3);
            }
            ret = USVWin.PrintNWMultMergeImage(ImageFileName, iPage,
               -1, -1, -1, -1, // Whole Image 
               -1, -1, -1, -1, 0, 0);

            if (TheRedactMode == 1) TheRedactMode = 6;
            else TheRedactMode = 7;

            USVWin.SetRedactedLineOutCount(iRedactLineRate);

            for (i = 0; i < TI.iCount; i++)
            {
                USVWin.PrintNWMultMergeSetRect(
                   (int)TI.iLeft[i],
                   (int)TI.iTop[i],
                   (int)TI.iRight[i],
                   (int)TI.iBottom[i],
                   TheRedactMode);
            }

            USVWin.PrintNWMultPrint(0);
            return (0);
        }

        int AddRedactTag(String ImageFileName, int iPage, String OutputFileName)
        {
            int ret;

            ret = USVWin.SetTiffStart(ImageFileName, iPage);
            if (ret != 0) return (1);

            ret = USVWin.SetTiffTag(0x872d, 1, SizeTI, 0, ref TI);
            if (ret != 0)
            {
                ret = USVWin.ReSetTiffTag(0x872d, 1, SizeTI, 0, ref TI);
            }
            ret = USVWin.SetTiffEnd(OutputFileName);
            if (ret != 0) return (2);
            return (0);
        }

        void SetImagePagePrintToggleArea(String ImageFileName, int iPage, int iPrintMode)
        {
            int ret;

            iPrintCropLeft = -1;
            iPrintCropTop = -1;
            iPrintCropRight = -1;
            iPrintCropBottom = -1;

            USVWin.RemoveAnnotation(1, 1, 1);

            if (iPrintMode != 0)
            {
                GetTiffTag872c(ImageFileName, iPage,
                   ref iPrintCropLeft, ref iPrintCropTop, ref iPrintCropRight, ref iPrintCropBottom);
            }

            if (iPrintCropLeft >= 0)
            {
                ret = USVWin.SetColorRect(iPrintCropLeft, iPrintCropTop, iPrintCropLeft + 2, iPrintCropBottom, 2);
                ret = USVWin.SetColorRect(iPrintCropLeft, iPrintCropTop, iPrintCropRight, iPrintCropTop + 2, 2);
                ret = USVWin.SetColorRect(iPrintCropRight - 2, iPrintCropTop, iPrintCropRight, iPrintCropBottom, 2);
                ret = USVWin.SetColorRect(iPrintCropLeft, iPrintCropBottom - 2, iPrintCropRight, iPrintCropBottom, 2);
            }
            USVWin.PaintImage(0);
            Application.DoEvents();
        }

        void SetMouseMode(int Mode)
        {
            if (ImageHandle <= 0) return;

            USVWin.SetImageWindow(ImageHandle);
            switch (Mode)
            {
                case 0:
                    iRightMouseMode = RightMouseMode.Magnifier;
                    USVWin.SetMouseMode(6, 1);   /* Right - Mag Glass */
                    USVWin.SetMouseMode(13, 3);  /* Right Double Click - Full WIdth */
                    SetImagePagePrintToggleArea(CurrentFile, CurrentPage, 0);
                    break;

                case 1:
                    iRightMouseMode = RightMouseMode.Redact;
                    USVWin.SetMouseMode(1, 1);  /* Right - Select Mode */
                    USVWin.SetMouseMode(3, 3);  /* Right Double Click - Nothing */
                    SetImagePagePrintToggleArea(CurrentFile, CurrentPage, 0);
                    break;

                case 2:
                    iRightMouseMode = RightMouseMode.PrintCrop;
                    USVWin.SetMouseMode(1, 1);  /* Right - Select Mode */
                    USVWin.SetMouseMode(3, 3);  /* Right Double Click - Nothing */
                    SetImagePagePrintToggleArea(CurrentFile, CurrentPage, 1);
                    break;
            }
        }



        void GetTiffTag872c(string ImageFileName, int iPage, ref int iLeft, ref int iTop, ref int iRight, ref int iBottom)
        {
            int ret;

            if (iPrintCropLocal != 0)
            {
                iLeft = -1;
                iTop = -1;
                iRight = -1;
                iBottom = -1;

                if (iPage == 1)
                {
                    iLeft = iPrintCropLeft;
                    iTop = iPrintCropTop;
                    iRight = iPrintCropRight;
                    iBottom = iPrintCropBottom;
                }
                return;
            }

            USVWin.TiffPrintCropInfo PCI;
            PCI.iLeft = iLeft;
            PCI.iRight = iRight;
            PCI.iTop = iTop;
            PCI.iBottom = iBottom;
            PCI.unknown_1 = PCI.unknown_2 = PCI.unknown_3 = PCI.unknown_4 = 0;

            ret = USVWin.GetTiffTagData(ImageFileName, iPage, 0x872c, ref PCI, 32);  // 32 = Sizeof(PCI)
            if (ret == 0 && USVWin.GetTiffTagType() == 4 && USVWin.GetTiffTagCount() == 4)
            {
                iLeft = PCI.iLeft;
                iTop = PCI.iTop;
                iRight = PCI.iRight;
                iBottom = PCI.iBottom;
            }
            else
            {
                iLeft = iTop = iRight = iBottom = -1;
            }
        }


        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            // Destroy Window
            if (ImageHandle > 0)
            {
                USVWin.SetImageWindow(ImageHandle);
                USVWin.ClearCallback(4);
                USVWin.ClearCallback(3);
                USVWin.CloseImageWindow(0);
                LMB_Hook = null;
                RMB_Hook = null;
                DBRMB_Hook = null;
                DBLMB_Hook = null;
                Application.DoEvents();

                ImageHandle = -1;
            }

            base.Dispose(disposing);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ImageHandle > 0)
            {
                USVWin.SetImageWindow(ImageHandle);
                USVWin.SizeImageWindow(0);
                Application.DoEvents();
            }
            // Debug.Print("On Resize");

        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            Debug.Print("On Location Changed");
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Debug.Print("On Size Change");

        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            // TODO: Add custom paint code here

            // Calling the base class OnPaint
            if (!this.DesignMode)
            {
                base.OnPaint(pe);
                // Your painting code here
            }
        }



    }

    #region Imaging externs from USVWin32
    public class USVWin
    {

        #region Kellpro Specific Structure

        // This is Stored in Tiff Tag: 0x872d
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public unsafe struct TiffRedactInfo
        {
            public int iCount;
            public fixed UInt32 iLeft[50];
            public fixed UInt32 iTop[50];
            public fixed UInt32 iRight[50];
            public fixed UInt32 iBottom[50];
        };

        // This is Stored in Tiff Tag: 0x872c
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct TiffPrintCropInfo
        {
            public Int32 iLeft;
            public Int32 iTop;
            public Int32 iRight;
            public Int32 iBottom;
            public Int32 unknown_1;
            public Int32 unknown_2;
            public Int32 unknown_3;
            public Int32 unknown_4;
        };
        #endregion

        #region " Imaging Functions from USVWin32 "
        //************************************************************
        // Kernel32 functions to load and free the USVWin Library.
        [DllImport("KERNEL32.DLL", EntryPoint = "LoadLibraryA",
             SetLastError = true, CharSet = CharSet.Ansi,
             ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        public extern static IntPtr LoadLibrary(string LibraryName);
        //Used to load the USVWIN32.DLL.

        [DllImport("KERNEL32.DLL", EntryPoint = "FreeLibrary",
             SetLastError = true, CharSet = CharSet.Ansi,
             ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        public extern static Int32 FreeLibrary(IntPtr LibaryHandle);
        //Used to free the USVWin32.DLL.

        //************************************************************

        //USVWin Functions

        /*[return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(HandleRef hWnd, uint Msg, IntPtr wParam, IntPtr lParam); */


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int PostMessage(int hWnd, int msg, int wParam, IntPtr lParam);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_Version",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 USVWinVersion(Int32 Spare);
        //Gets the version of the USVWin32.DLL file.
        //Must be divided by 100 to get version.


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetDisplayImageArea",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetDisplayImageArea(Int32 Left, Int32 Top, Int32 Right, Int32 Bottom);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ImageToWindow",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ImageToWindow(ref int ImageX, ref int ImageY, int WindowX, int WindowY);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_WindowToImage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 WindowToImage(int WindowX, int WindowY, ref int ImageX, ref int ImageY);


        /// <summary>
        /// Starts the image window.
        /// </summary>
        /// <param name="Handle"></param>
        /// <param name="ChildWindowIdentifier"> 0 = Child Window within Parent.
        ///    &lt;=0 = Pop up window with caption bar.
        ///    -1701 = Pop up window without caption bar.</param>
        /// <param name="ImageLeft"></param>
        /// <param name="ImageTop"></param>
        /// <param name="ImageRight"></param>
        /// <param name="ImageBottom"></param>
        /// <returns>0 = Image Window Created.
        /// 1 = Unable to allocate Memory.
        /// 2 = Trying to open more than 32 Image Windows.
        /// 3 = Cannot be used from a Callback function.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_StartImageWindow",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 StartImageWindow(IntPtr Handle,
             Int32 ChildWindowIdentifier, Int32 ImageLeft,
             Int32 ImageTop, Int32 ImageRight,
             Int32 ImageBottom);


        /// <summary>
        /// Closes the currently set open image window.
        /// Image to be set must be set first with VWSetImageWindow if 
        /// more than one window is open.
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns>0 = Image Window closed.
        /// 1 = No valid image window to close.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_CloseImageWindow",
                  CallingConvention = CallingConvention.StdCall,
                  SetLastError = true, ExactSpelling = true)]
        public extern static Int32 CloseImageWindow(Int32 Spare);


        /// <summary>
        /// Retrieves the MS Windows handle to the image window.
        /// </summary>
        /// <returns>0 = No image window handle.
        /// Else = Handle to image window.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetImageWindowHandle",
                  CallingConvention = CallingConvention.StdCall,
                  SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetImageWindowHandle();


        /// <summary>
        /// Sets the current image window for the library functions to work with.
        /// </summary>
        /// <param name="Handle"></param>
        /// <returns>0 = Image Window handle set.
        /// 1 = Not a valid Image Window.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetImageWindow",
                  CallingConvention = CallingConvention.StdCall,
                  SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetImageWindow(Int32 Handle);



        /// <summary>
        /// Removes the Annotation.
        /// </summary>
        /// <param name="RemoveColorRect">1 = Remove, 0 - Leave</param>
        /// <param name="RemoveIcons">1 = Remove, 0 - Leave</param>
        /// <param name="RemoveFreeHand">1 = Remove, 0 - Leave</param>
        /// <returns> 0 - Okay
        /// 1 - Calling Order Error </returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_RemoveAnnotation",
                   CallingConvention = CallingConvention.StdCall,
                   SetLastError = true, ExactSpelling = true)]
        public extern static Int32 RemoveAnnotation(Int32 RemoveColorRect,
        Int32 RemoveIcons, Int32 RemoveFreeHand);


        //
        /// <summary>
        /// Moves Selected Annotationd
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_MoveSelectAnnotation",
                   CallingConvention = CallingConvention.StdCall,
                   SetLastError = true, ExactSpelling = true)]
        public extern static Int32 MoveSelectAnnotation(Int32 X, Int32 Y);

        /// <summary>
        /// Gets the number of pages in the TIFF file.
        /// </summary>
        /// <param name="FileName"></param>
        /// <returns>&gt;0 = Number of pages.
        /// -1 = Invalid file name or not a TIFF (or DCS) File.
        /// -2 = Memory Allocation Error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SelectAnnotation",
         CallingConvention = CallingConvention.StdCall,
         SetLastError = true, ExactSpelling = true)]
        public extern static int SelectAnnotation(Int32 type);



        /// <summary>
        /// Gets the number of pages in the TIFF file.
        /// </summary>
        /// <param name="FileName"></param>
        /// <returns>&gt;0 = Number of pages.
        /// -1 = Invalid file name or not a TIFF (or DCS) File.
        /// -2 = Memory Allocation Error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "GetTiffPages",
         CallingConvention = CallingConvention.StdCall,
         SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetTiffPages(String FileName);


        /// <summary>
        /// Displays a single page TIFF or page one of a multi page TIFF.
        /// </summary>
        /// <param name="FileName"></param>
        /// <returns>0 = Single Page TIFF displayed correctly.
        /// 1 = No image window or memory error.
        /// 2 = Not a valid Image file.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetImagePage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetImagePage(String FileName);


        /// <summary>
        /// Displays a single page of a multi page tiff file.
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="Page"></param>
        /// <returns>0 = Single Page TIFF displayed correctly.
        /// 1 = No image window or memory error.
        /// 2 = Not a valid Image file.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetImageMultPage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetImageMultPage(String FileName, Int32 Page);


        /// <summary>
        /// Sets how the image will be displayed.
        /// </summary>
        /// <param name="DisplayType">
        ///0 = Full Image
        ///1 = Entire Width
        ///2 = 1 to 1 Scaling
        ///3 = 2 to 1 Scaling
        ///4 = 4 to 1 Scaling
        ///5 = Entire Height
        ///6 = Full Image - Non Square Display</param>
        /// <returns>
        /// 0 = Display type set correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetDisplayImage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetDisplayImage(Int32 DisplayType);


        /// <summary>
        /// Sets the type of Scaling to be done on the image being displayed.
        /// </summary>
        /// <param name="ScalingType">
        /// 0 = No Scaling Options
        /// 1 = Vertical Scaling
        /// 2 = Horizontal Scaling
        /// 3 = V and H Scaling
        /// 4 = GrayScale
        /// 5 = V and GrayScale
        /// 6 = H and GrayScale
        /// 7 = V, H and GrayScale
        /// </param>
        /// <returns>
        /// 0 = Scaling set correctly.
        /// 1 = No image window or memory error.
        /// </returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetImageScaling",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetImageScaling(Int32 ScalingType);


        /// <summary>
        /// Sizes the image window to the container.
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns>0 = Image window sized correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SizeImageWindow",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SizeImageWindow(Int32 Spare);


        /// <summary>
        /// Forces the image to redraw.
        /// </summary>
        /// <param name="Speed">1701 = Screen not cleared first.
        ///    4321 = Screen repainted immediately.
        ///    Else = Clear the screen and then repaint during idle time.</param>
        /// <returns>0 = Image window repainted correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PaintImage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PaintImage(Int32 Speed);


        /// <summary>
        /// Rotates the image
        /// </summary>
        /// <param name="RotationValue">0 = None
        ///    1 = Clockwise 90 
        ///    2 = 180 Rotation
        ///    3 = 90 Counter Clockwise
        ///   4 = Set Rotation to 0 degrees
        ///    5 = Set Rotation to 90 degrees
        ///    6 = Set Rotation to 180 degrees
        ///    7 = Set Rotation to 90 degrees counter clockwise
        ///    8 = Set Rotation to 0 from image default
        ///    9 = Set rotation to 90 from image default
        ///    10= Set rotation to 180 from image default
        ///    11= Set rotation to 270 from image default</param>
        /// <returns>0 = Image rotated correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_RotateImage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 RotateImage(Int32 RotationValue);


        /// <summary>
        /// Inverts the pixels in a TIFF Image
        /// </summary>
        /// <param name="InvertType">0 = Black on White
        ///    1 = White on Black
        ///    2 = Invert Previous Displayed colors</param>
        /// <returns>0 = Image pixels inverted correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_InvertImage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 InvertImage(Int32 InvertType);


        /// <summary>
        /// Zooms the displayed image
        /// </summary>
        /// <param name="ZoomValue">2304 = Zoom In 
        /// 1792 = Zoom Out</param>
        /// <returns>0 = Image zoomed correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ZoomImage",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ZoomImage(Int32 ZoomValue);

        // Added by NS 202402
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetDisplayImageOffsetScale",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]

        public extern static Int32 ZoomPanImage(Int32 Left, Int32 Top, Int32 XScale, Int32 YScale);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetSelectedImageArea",
         CallingConvention = CallingConvention.StdCall,
         SetLastError = true, ExactSpelling = true)]

        public extern static Int32 SetSelectedImageArea(Int32 Left, Int32 Top, Int32 XScale, Int32 YScale);

        /// <summary>
        /// Sets a border around the image being displayed.
        /// </summary>
        /// <param name="Color">0 = Black, 1 = Red, 2 = Blue, 3 = Green 
        ///    4 = Yellow, 5 = Purple, 6 = Aqua, 7 = White
        ///    10 = Light Gray, 16 = Medium Gray, 22 = Dark Gray</param>
        /// <param name="Width">Width in Pixels of the border</param>
        /// <returns> 0 = Border set correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetWindowColorBorder",
         CallingConvention = CallingConvention.StdCall,
         SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetWindowColorBorder(Int32 Color, Int32 Width);



        // This is a delegate for the SetCallback 
        public delegate bool USVWinCallback(Int32 Hwnd, UInt32 iMessage, UInt32 wParam, Int32 lParam);

        /// <summary>
        /// Allows functions to be called from the DLL when an event happens.
        /// </summary>
        /// <param name="CallingEvent">0 = Mouse Move Event
        ///    1 = Left Mouse Button Down Event
        ///    2 = Right Mouse Button Down Event
        ///    3 = Left Mouse Button Up Event
        ///    4 = Right Mouse Button Up Event
        ///    5 = Image Window Painted
        ///    6 = Left Dbl Click Mouse Down Event
        ///    7 = Right Dbl Click Mouse Down Event</param>
        /// <param name="Process">Pointer to the function to run (AddressOf)</param>
        /// <returns>0 = CallBack succeeded.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetCallback",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetCallback(Int32 CallingEvent, USVWinCallback Process);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ClearCallback",
                 CallingConvention = CallingConvention.StdCall,
                 SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ClearCallback(Int32 CallingEvent);


        /// <summary>
        /// Sets how the mouse buttons will work when over the image window.
        /// </summary>
        /// <param name="Mode">0 = Standard Zoom
        ///    2 = Allow callbacks
        ///    6 = Select box
        ///    7 = Magnafier
        ///    8 = Hold and Pan</param>
        /// <param name="MouseKey">0 = Left Mouse Button
        ///    1 = Right Mouse Button
        ///    2 = Left Double Click
        ///    3 = Right Double Click</param>
        /// <returns>0 = Mouse mode set correctly.
        /// 1 = No image window or memory error.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetMouseMode",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetMouseMode(Int32 Mode, Int32 MouseKey);


        /// <summary>
        /// Returns the width of the current image in pixels.
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns>The width of the current image in pixels.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetImageWidth",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetImageWidth(Int32 Spare);


        /// <summary>
        /// Returns: The Height of the current image in pixels.
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns>The Height of the current image in pixels.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetImageHeight",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetImageHeight(Int32 Spare);


        /// <summary>
        /// 
        /// </summary>
        /// <param name="Type">13 = Image Compression Mode
        /// 14 = Image Compression Direction</param>
        /// <returns>Misc information about the current image window
        /// Type 13:
        ///    0 = Group 3
        ///    1 = Group 4
        ///    2 = Group 3 2D(Modified Read)
        ///    3 = Raw/Uncompressed
        ///    4 = IBM MMR
        ///    5 = Group 3 No EP:S
        ///    7 = PCX
        ///    8 = Group 4 Wrap
        ///    10= PackBits
        /// Type 14:
        ///    0 = Normal
        ///    1 = Reverse
        /// </returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetWindowCurrentStatus",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetWindowCurrentStatus(Int32 Type);


        /// <summary>
        /// Sets the magnify window size.
        /// </summary>
        /// <param name="Width">Value between 32 and 512</param>
        /// <param name="Height">Value between 32 and 512</param>
        /// <returns>Misc information about the current image window</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetMaxMagnifyWindowSize",
             CallingConvention = CallingConvention.StdCall,
             SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetMaxMagnifyWindowSize(Int32 Width, Int32 Height);


        /// <summary>
        /// Moves the image up, down, left and right by number of pixels specified.
        /// </summary>
        /// <param name="XOffset">Number of pixels to move the image in a horizontal direction.
        ///       Negative values move the image left and positive move the image right.</param>
        /// <param name="YOffset">Number of pixels to move the image in a horizontal direction.
        ///       Negative values move the image up and positive move the image down.</param>
        /// <returns>0 = OK
        /// 1 = No valid image window</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_MoveImage",
         CallingConvention = CallingConvention.StdCall,
         SetLastError = true, ExactSpelling = true)]
        public extern static Int32 MoveImage(Int32 XOffset, Int32 YOffset);


        /// <summary>
        /// Gets extended TIFF properties at once.
        /// </summary>
        /// <param name="InputFile">Input file.</param>
        /// <param name="Page">Which page in file to open.</param>
        /// <param name="Width">Width of the image</param>
        /// <param name="Height">Height of the image</param>
        /// <param name="ImageOffset">Offset within the file to the image</param>
        /// <param name="XRes">X Resolution in inches</param>
        /// <param name="YRes">Y Resolution in inches</param>
        /// <param name="CompressionMode">0 = Group 3
        ///    1 = Group 4
        ///    2 = Group 3 2D(Modified Read)
        ///    3 = Raw/Uncompressed
        ///    4 = IBM MMR
        ///    5 = Group 3 No EP:S
        ///    7 = PCX
        ///    8 = Group 4 Wrap
        ///    10= PackBits
        ///    Other = Unknown or error</param>
        /// <param name="CompressionDirection">0 = Bits stored normal
        ///    1 = Bits stored in reverse order</param>
        /// <param name="ImageNegativeStored">0 = Black on white
        ///    1 = White on black</param>
        /// <returns>0 = OK
        /// 1 = Unable to open image or unknown format.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VWGetImageInfoExtended" /* No _ in VW_ */ ,
         CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetImageInfoExtended(String InputFile,
        Int32 Page, ref Int32 Width, ref Int32 Height,
        /* Int64? */ ref Int32 ImageOffset, ref Int32 XRes, ref Int32 YRes,
        ref Int32 CompressionMode, ref Int32 CompressionDirection,
        ref Int32 ImageNegativeStored);


        /// <summary>
        /// Gets Image properties.
        /// </summary>
        /// <param name="InputFile">Input file.</param>
        /// <param name="Page">Page of Tiff (1 = first)</param>
        /// <param name="Width">Width of the image</param>
        /// <param name="Height">Height of the image</param>
        /// <returns>0 = OK
        /// 1 = Unable to open image or unknown format.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VWGetImageInfo" /* No _ in VW_ */ ,
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetImageInfo(String InputFile, Int32 Page,
        ref Int32 Width, ref Int32 Height);

        /// <summary>
        /// Forces all images to be displayed as grayscale image.
        /// </summary>
        /// <param name="Mode">Mode = 0 - Off(default), 1 - On.</param>
        /// <returns>0 = OK
        /// 1 = Not OK</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ForceColorToGray",
         CallingConvention = CallingConvention.StdCall,
         SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWForceColorToGray(Int32 Mode);


        /// <summary>
        /// Draws a colored rectangle on the image.
        /// </summary>
        /// <param name="Left">Coordinates of the rectangle to draw.</param>
        /// <param name="Top">Coordinates of the rectangle to draw.</param>
        /// <param name="Right">Coordinates of the rectangle to draw.</param>
        /// <param name="Bottom">Coordinates of the rectangle to draw.</param>
        /// <param name="Color">
        ///   0 = Black
        ///   1 = Red
        ///   2 = Blue
        ///   3 = Green
        ///   4 = Yellow
        ///   5 = Purple
        ///   6 = Aqua
        ///   7 = White</param>
        /// <returns>0 = OK
        /// 1 = Valid image window not available or function cannot be called from a callback function.
        /// 2 = More than 50 rectangles specified for the current image.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetColorRect",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetColorRect(Int32 Left, Int32 Top, Int32 Right, Int32 Bottom, Int32 Color);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetColorRectEx",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetColorRectEx(Int32 Left, Int32 Top, Int32 Right, Int32 Bottom, Int32 Fore, Int32 Back, int Mode);


        /// <summary>
        /// Draws colored text on the image.
        /// </summary>
        /// <param name="TextHeight">Height of text in pixels</param>
        /// <param name="Text">The text to display</param>
        /// <param name="Bold">0 - Not bold, 1 - Bold</param>
        /// <param name="Italic">0 - Not italic, 1 - Italic</param>
        /// <param name="Font">Windows font to use (eg. "Arial")</param>
        /// <param name="Color">0 = Black
        ///       1 = Red
        ///       2 = Blue
        ///      3 = Green
        ///      4 = Yellow
        ///       5 = Purple
        ///       6 = Aqua</param>
        /// <param name="MergeMode">0 - Transparent, 1 - Overwrite</param>
        /// <param name="XCoord">X coordinate to place on the image</param>
        /// <param name="YCoord">Y coordinate to place on the image</param>
        /// <param name="Scale">Associated scale value (2048 for 1 to 1 or current scale [VWGetWindowCurrentStatus(5)])</param>
        /// <param name="Rotation">Rotation value(0 or current rotation [VWGetWindowCurrentStatus(15)])</param>
        /// <param name="AdjustCurrentRotation">0 - Absolute location, 1 - Adjust text position for current rotation</param>
        /// <returns>0 = OK
        /// 1 = No associated window
        /// 2 = Memory error</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetAnnotationText",
    CallingConvention = CallingConvention.StdCall,
    SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWSetAnnotationText(Int32 TextHeight, String Text, Int32 Bold,
    Int32 Italic, String Font, Int32 Color, Int32 MergeMode,
    Int32 XCoord, Int32 YCoord, Int32 Scale, Int32 Rotation,
    Int32 AdjustCurrentRotation);


        /// <summary>
        /// Deletes currently selected annotation.
        /// </summary>
        /// <returns>0 = OK
        /// 1 = No current image window
        /// 2 = Annotation could not be deleted</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_DeleteSelectAnnotation",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWDeleteSelectAnnotation();


        /// <summary>
        /// Gets currently selected annotation type.
        /// </summary>
        /// <returns>-1 = Error
        ///   0 = No Annotation
        ///   1 = Rectangle
        ///   2 = Ellipse
        ///   3 = Icon
        ///   4 = FreeHand
        ///   5 = Text</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetCurrentAnnotationType",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWGetCurrentAnnotationType();


        /// <summary>
        /// Gets the specs for the selected or any rectangle.
        /// </summary>
        /// <param name="Index">-1 for currently selected, 0 to n for specific rectangle by index.</param>
        /// <param name="Left">Coordinates of the rectangle drawn.</param>
        /// <param name="Top">Coordinates of the rectangle drawn.</param>
        /// <param name="Right">Coordinates of the rectangle drawn.</param>
        /// <param name="Bottom">Coordinates of the rectangle drawn.</param>
        /// <param name="Color">0 = Black
        ///   1 = Red
        ///   2 = Blue
        ///   3 = Green
        ///   4 = Yellow
        ///   5 = Purple
        ///   6 = Aqua
        ///   7 = White</param>
        /// <returns>0 = OK
        ///   1 = Error.
        ///   2 = Invalid index value.
        ///   3 = Selected annotation is not a rectangle</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetColorRect",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWGetColorRect(Int32 Index, ref Int32 Left, ref Int32 Top,
        ref Int32 Right, ref Int32 Bottom, ref Int32 Color);


        /// <summary>
        /// Get the properties for the currently selected or any text annotation.
        /// </summary>
        /// <param name="Index">-1 for the currently selected text or 0 to n for indexed text.</param>
        /// <param name="TextHeight">Height of text in pixels</param>
        /// <param name="Text">The text to display</param>
        /// <param name="Bold">0 - Not bold, 1 - Bold</param>
        /// <param name="Italic">0 - Not italic, 1 - Italic</param>
        /// <param name="Font">Windows font to use (eg. "Arial")</param>
        /// <param name="Color">0 = Black
        ///       1 = Red
        ///       2 = Blue
        ///       3 = Green
        ///       4 = Yellow
        ///       5 = Purple
        ///       6 = Aqua</param>
        /// <param name="MergeMode">0 - Transparent, 1 - Overwrite</param>
        /// <param name="XCoord">X coordinate to place on the image</param>
        /// <param name="YCoord">Y coordinate to place on the image</param>
        /// <param name="Scale">Associated scale value (2048 for 1 to 1 or current scale [VWGetWindowCurrentStatus(5)])</param>
        /// <param name="Rotation">Rotation value(0 or current rotation [VWGetWindowCurrentStatus(15)])</param>
        /// <returns>0 = OK
        ///   1 = Error.
        ///   2 = Invalid index value.
        ///   3 = Selected annotation is not text.</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetAnnotationText",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWGetAnnotationText(Int32 Index, ref Int32 TextHeight, ref String Text, ref Int32 Bold,
        ref Int32 Italic, ref String Font, ref Int32 Color, ref Int32 MergeMode,
        ref Int32 XCoord, ref Int32 YCoord, ref Int32 Scale, ref Int32 Rotation);


        /// <summary>
        /// Specifies an area to XOR with Light Blue
        /// </summary>
        /// <param name="Left">Coordinates of edges in pixels</param>
        /// <param name="Top">Coordinates of edges in pixels</param>
        /// <param name="Right">Coordinates of edges in pixels</param>
        /// <param name="Bottom">Coordinates of edges in pixels</param>
        /// <returns>0 = OK
        ///   1 = Current image window not available
        ///   2 = Too many areas specified</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetInvertRect",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWSetInvertRect(Int32 Left, Int32 Top,
        Int32 Right, Int32 Bottom);


        /// <summary>
        /// Un-Specifies an all areas from being XOR//ed.
        /// </summary>
        /// <param name="Spare">not used</param>
        /// <returns>0 = OK
        /// 1 = Current image window not available</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_UnSetInvertRects",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWUnSetInvertRects(Int32 Spare);


        // Importing the VW_SetScreenForeBackColor function from the USVWIN32.DLL.
        // This function sets the foreground and background colors of the screen and the color 
        // displayed when no image is loaded.
        // lFore: The color to be used for the foreground.
        // lBack: The color to be used for the background.
        // lNoImage: The color to be used when no image is loaded.
        // Returns a value indicating the success of the operation.      
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetScreenForeBackColor",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]

        public extern static Int32 SetScreenForeBackColor(Int32 lFore, Int32 lBack, Int32 lNoImage);



        /// <summary>
        /// Specifies an area to XOR with Light Blue
        /// </summary>
        /// <param name="Mode">0 - White out all areas
        ///       1 - Black out all areas
        ///       2 - Frame white out areas
        ///       3 - Stamped the word "REDACTED" after white out
        ///       4 - Stamped the word "REDACTED" after black out
        ///       5 - X redact image
        ///       6 - Framed white out areas with lines every 8 lines
        ///       7 - Framed transparent out areas with lines every 8 lines
        ///       8 - Framed white out areas with a line from each opposite corner
        ///       9 - Framed transparent out areas with a line from each opposite corner</param>
        /// <param name="OutputFile">Full path name for output file.</param>
        /// <returns>0 = OK
        ///   1 = No image available
        ///   2 = No redacts available or bad mode.
        ///   3 = Error opening InputImage
        ///   4 = Error opening Output image</returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "RedactImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 RedactImage(Int32 Mode, String OutputFile);




        #endregion

        #region " TIFF Tag Functions from USVWin32 "

        //***************************************************
        //Start TIFF tag manipulation calls
        //***************************************************



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetRedactedLineOutCount",
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetRedactedLineOutCount(Int32 Page);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetTiffStart",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetTiffStart(String FileName, Int32 Page);
        //Specifies 
        //Parameters:
        //   FileName = TIFF file name; should always be single page, Group 4
        //   Page = Page number; should always be 1
        //Returns: 
        //   0 = OK
        //   1 = Bad input image
        //   2 = Memory allocation error.
        //   3 = Not a TIFF file.
        //   4 = Not a valid page in TIFF file.
        //   5 = Too few or too many TIFF tags.
        //   6 = Unsupported TIFF tag type.
        //   7 = Not enough memory for TIFF tag data.
        //   8 = TIFF data not in file.
        //   9 = Images cannot be stripped or tiled


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetTiffTag",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetTiffTag(Int32 Tag, Int32 FieldType, Int32 Length,
            /* Int64? */ Int32 FieldOffset, ref TiffRedactInfo TRI);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetTiffTag",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetTiffTag(Int32 Tag, Int32 FieldType, Int32 Length,
            /* Int64? */ Int32 FieldOffset, ref TiffPrintCropInfo TPCI);


        //Sets tiff tag to specific value
        //Parameters:
        //   Tag = TIFF tag number
        //   FieldType:
        //       1 = Bytewise data
        //       2 = ASCII data
        //       3 = Short Int.
        //       4 = Long int.
        //   FieldOffset = The value of the tiff tag, unless more than 4 bytes needed 
        //   to store the value.
        //   Data = External buffer holding TIFF tag data when more than 4 bytes is 
        //   associated with TIFF tag.
        //Returns: 
        //   0 = OK
        //   1 = Must call VWSetTiffTagStart first
        //   2 = Can set only 35 tags per header
        //   3 = Tag already set
        //   4 = Invalid field type
        //   5 = Data not passed when needed
        //

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ReSetTiffTag",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ReSetTiffTag(Int32 Tag, Int32 FieldType, Int32 Length,
            /* Int64? */ Int32 FieldOffset, ref TiffRedactInfo TRI);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ReSetTiffTag",
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ReSetTiffTag(Int32 Tag, Int32 FieldType, Int32 Length,
                /* Int64? */ Int32 FieldOffset, ref TiffPrintCropInfo TPCI);


        //Sets tiff tag to specific value
        //Parameters:
        //   Tag = TIFF tag number
        //   FieldType:
        //       1 = Bytewise data
        //       2 = ASCII data
        //       3 = Short Int.
        //       4 = Long int.
        //   FieldOffset = The value of the tiff tag, unless more than 4 bytes needed 
        //   to store the value.
        //   Data = External buffer holding TIFF tag data when more than 4 bytes is 
        //   associated with TIFF tag.
        //Returns: 
        //   0 = OK
        //   1 = Must call VWSetTiffTagStart first
        //   2 = Can set only 35 tags per header
        //   3 = Tag already set
        //   4 = Invalid field type
        //   5 = Data not passed when needed


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_RemoveTiffTag",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 RemoveTiffTag(Int32 Tag);
        //Removes a TIFF tag by tag number
        //Parameters:
        //   Tag = TIFF tag number
        //Returns: 
        //   0 = OK
        //   1 = Must call VWSetTiffTagStart first
        //   2 = Tag not found


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetTiffEnd",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetTiffEnd(String OutputFileName);
        //Removes a TIFF tag by tag number
        //Parameters:
        //   OutputFileName = Resulting output file
        //Returns: 
        //   0 = OK
        //   1 = Must call VWSetTiffTagStart first
        //   2 = Could not open input file
        //   3 = Input image is not a TIFF
        //   4 = Could not open input image.
        //   5 = Memory allocation error
        //   6 = Error writing image data
        //   7 = TIFF format error

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetTiffTagList",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetTiffTagList(String FileName,
        Int32 Page, ref Int32 TagCount, StringBuilder Buffer,
        Int32 BufferSize);
        //Gets list of TIFF tags. JL - Had to do this function with declare instead of DLLImport
        //because of buffer error.
        //Parameters:
        //   FileName = TIFF image file
        //   Page = Page in TIFF file
        //   TagCount = Number of TIFF tags found in header
        //   Buffer = Place to store tags in short int format
        //   BufferSize = Size of buffer to store TIFF data in.
        //Returns: 
        //   0 = OK
        //   1 = Bad input image
        //   2 = Memory Allocation error
        //   3 = Not a TIFF image
        //   4 = Not a valid page number


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetTiffTagData",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetTiffTagData(String FileName,
        Int32 Page, Int32 SeekTag, ref TiffRedactInfo TRI,
        Int32 BufferSize);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetTiffTagData",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetTiffTagData(String FileName,
        Int32 Page, Int32 SeekTag, ref TiffPrintCropInfo TPCI,
        Int32 BufferSize);


        //Gets TIFF tag data. JL - Had to do this function with declare instead of DLLImport
        //Parameters:
        //   FileName = TIFF image file
        //   Page = Page in TIFF file
        //   SeekTag = Number of TIFF tags found in header
        //   Buffer = Place to store tags in short int format
        //   BufferSize = Size of buffer to store TIFF data in.
        //Returns: 
        //   0 = OK
        //   1 = Bad input image
        //   2 = Memory Allocation error
        //   3 = Not a TIFF image
        //   4 = Not a valid page in TIFF file.
        //   5 = TIFF tag not found
        //   6 = Unsupported TIFF tag type

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetTiffTagType",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetTiffTagType();
        //Gets tag type after calling VWGetTiffTagData.
        //   0 = Call GetTagData() first
        //   1 = Byte
        //   2 = ASCII
        //   3 = Short
        //   4 = Long
        //   5 = Rational


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetTiffTagCount",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetTiffTagCount();

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_AutoSetNegativeFlagOnDisplay",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 AutoSetNegativeFlagOnDisplay(Int32 Mode);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetScrolLessFlash",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetScrolLessFlash(Int32 iMode);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetMaxAnnotations",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetMaxAnnotations(Int32 iMaxInvertRects,
                    Int32 iMaxColorRects, Int32 iMaxColorEllipses, Int32 iMaxIcons);

        //***************************************************
        //End TIFF tag manipulation calls
        //***************************************************


        #endregion

        #region " Printing Functions from USVWin32 "

        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Handle"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintImageStartNoWindowHDC",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintImageStartNoWindowHDC(IntPtr Handle);
        //Specifies 
        //Parameters:
        //   Handle = Handle to printer device context.
        //Returns: 
        //   0 = OK
        //   1 = Function cannot be called from a callback function
        //   2 = Printer startup error


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="PrinterName"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintImageStartNoWindowPrinter",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintImageStartNoWindowPrinter(String PrinterName);
        //Specifies 
        //Parameters:
        //   Handle = Handle to printer device context.
        //Returns: 
        //   0 = OK
        //   1 = Function cannot be called from a callback function
        //   2 = Printer startup error


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultPrint",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultPrint(Int32 Spare);
        //Specifies 
        //Parameters:
        //   Spare = 0 (not used)
        //Returns: 
        //   0 = OK
        //   1 = Must start a complex page first
        //   2 = Error printing the image


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultAbort",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultAbort(Int32 Spare);
        //Specifies 
        //Parameters:
        //   Spare = 0 (not used)
        //Returns: 
        //   0 = OK
        //   1 = Must start a complex page first
        //   2 = Error aborting image


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="PageSize"></param>
        /// <param name="PrinterOptions"></param>
        /// <param name="LPT"></param>
        /// <param name="PrintMode"></param>
        /// <param name="PrintFileName"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultStart",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultStart(Int32 PageSize, Int32 PrinterOptions,
        Int32 LPT, Int32 PrintMode, String PrintFileName);
        //Specifies beginning of print job.
        //Parameters:
        //   See manual
        //Returns: 
        //   0 = OK
        //   1 = No image available
        //   2 = Bad device number
        //   3 = Bad LPT number
        //   4 = Bad print mode
        //   5 = Unable to open current image file
        //   6 = Unable to initialize printer or open OutputFile
        //   7 = Error while printing or writing to output file
        //   8 = Image decompression error


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultGetWidth",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultGetWidth(Int32 Spare);
        //Specifies 
        //Parameters:
        //   Spare = 0 (not used)
        //Returns: 
        //   0 = Must start a complex page first
        //   Other = Width of the complex print page


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultGetHeight",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultGetHeight(Int32 Spare);
        //Specifies 
        //Parameters:
        //   Spare = 0 (not used)
        //Returns: 
        //   0 = Must start a complex page first
        //   Other = Height of the complex print page


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="Page"></param>
        /// <param name="ImageLeft"></param>
        /// <param name="ImageTop"></param>
        /// <param name="ImageRight"></param>
        /// <param name="ImageBottom"></param>
        /// <param name="PageLeft"></param>
        /// <param name="PageTop"></param>
        /// <param name="PageRight"></param>
        /// <param name="PageBottom"></param>
        /// <param name="Rotate"></param>
        /// <param name="Negative"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultMergeImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultMergeImage(String FileName, Int32 Page,
        Int32 ImageLeft, Int32 ImageTop, Int32 ImageRight, Int32 ImageBottom,
        Int32 PageLeft, Int32 PageTop, Int32 PageRight, Int32 PageBottom,
        Int32 Rotate, Int32 Negative);
        //Specifies 
        //Parameters:
        //   FileName = Image file name
        //   Page = Page number within the file
        //   Image Left, Top, Bottom and Right = Coordinate on the image. (-1 use entire image)
        //   Page Left, Top, Bottom and Right = Coordinate on the page. (-1 use entire page)
        //   Rotate
        //       0 = No rotation
        //       1 = 90 degree rotation
        //       2 = 180 degree rotation
        //       3 = 270 degree rotation
        //   Negative
        //       0 = Black on white
        //       1 = White on black
        //Returns: 
        //   0 = OK
        //   1 = Must start a complex page first
        //   2 = Bad page coordinate
        //   3 = Error opening or using InputFile
        //   4 = Bad image coordinates



        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Mode"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMemoryMode",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMemoryMode(Int32 Mode);
        //Specifies 
        //Parameters:
        //   Mode
        //       0 = Opaque(Default)
        //       1 = Transparent
        //Returns:
        //   0 = OK
        //   1 = Must start a complex page first
        //   2 = Mode not 0 or 1


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Mode"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWTextDitherMode",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWTextDitherMode(Int32 Mode);
        //Sets whether text will be dithered or outlined before writing
        //Parameters:
        //   Mode
        //       0 = No dither (Default)
        //       1 = Every other bit in every character
        //       2 = Keep every 4th bit in every char
        //       3 = Keep every 9th bit
        //       4 = Keep every 16th bit
        //       8 = 1 pixel wide border
        //       16 = 2 pixel wide border
        //       24 = Three pixel wide border
        //       32 = 4 pixel wide border
        //Returns:
        //   0 = OK


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="TextHeight"></param>
        /// <param name="Text"></param>
        /// <param name="Bold"></param>
        /// <param name="Italic"></param>
        /// <param name="Font"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetMaxTextWidth",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetMaxTextWidth(Int32 TextHeight, String Text,
        Int32 Bold, Int32 Italic, String Font);
        //Returns the actual width used by the longest line in the text string.
        //Parameters:
        //   Height = Height of the text in pixels
        //   Text = Text string with embedded linefeeds and carriage returns.
        //   Bold = 0 - Not bold, 1 - Bold
        //   Italic = 0 - Not italic, 1 - Italic
        //   Font = MS-Windows font to use
        //Returns:
        //   -1 = No window defined
        //   Otherwise = Width of the text


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Text"></param>
        /// <param name="TextHeight"></param>
        /// <param name="TextLeft"></param>
        /// <param name="TextTop"></param>
        /// <param name="Rotate"></param>
        /// <param name="Bold"></param>
        /// <param name="Italic"></param>
        /// <param name="Font"></param>
        /// <param name="DisplayAllText"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultMergeTextExtended",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultMergeTextExtended(String Text, Int32 TextHeight,
        Int32 TextLeft, Int32 TextTop, Int32 Rotate, Int32 Bold,
        Int32 Italic, String Font, Int32 DisplayAllText);
        //Merges text onto a complex print page.
        //Parameters:
        //   Text = Text string with embedded linefeeds and carriage returns.
        //   TextHeight = Text height to be used.
        //   TextLeft = Left coordinate on the page for printing
        //   TextTop = Top coordinate on the page for printing
        //   Bold = 0 - Not bold, 1 - Bold
        //   Italic = 0 - Not italic, 1 - Italic
        //   Font = MS-Windows font to use
        //   Display all text = 0 - Do nothing, 1 - if the text fails the bottom or right edge, shift
        //   shift the text left and up
        //Returns:
        //   0 = OK
        //   1 = Must call VWPrintNWMultStart first


        /// <summary>
        /// deprecated
        /// </summary>
        /// <param name="Spare"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintImageEndNoWindow",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintImageEndNoWindow(Int32 Spare);
        //
        //Parameters:
        //   Spare = 0 (not used)
        //Returns: 
        //   0 = OK
        //   1 = Function cannot be called from a callback function
        //   2 = Printer startup error


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PrintNWMultMergeSetRect",
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true, ExactSpelling = true)]
        public extern static Int32 PrintNWMultMergeSetRect(
        Int32 Left, Int32 Top, Int32 Right, Int32 Bottom, Int32 Color);


        #endregion

        #region " Printer Handle from winspool.drv"

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("winspool.drv", EntryPoint = "OpenPrinterA",
            SetLastError = true, CharSet = CharSet.Ansi,
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        public extern static Boolean OpenPrinter(String pPrinterName,
            ref IntPtr phPrinter,
            Int32 pDefault);



        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("winspool.drv", EntryPoint = "ClosePrinter",
        SetLastError = true,
        ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall)]
        public extern static Boolean ClosePrinter(IntPtr hPrinter);



        #endregion

        #region " Scanning Functions from USVWin32 "

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_GetSCSIScannerInfo",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWGetSCSIScannerInfo(String vendor, String product,
                            String revision, ref Int32 scanner, ref Int32 adapter,
                            ref Int32 targetid);



        [DllImport("USVWIN32.DLL", EntryPoint = "SetScannerUp",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetScannerUp(Int32 Width, Int32 height, Int32 XOffset,
        Int32 Yoffset, Int32 Xres, Int32 Yres, Int32 ADFEnabled,
        Int32 Brightness, Int32 Threshold, Int32 Contrast, Int32 ScannerType,
        Int32 ScannerOption);



        [DllImport("USVWIN32.DLL", EntryPoint = "SetScannerUpDuplex",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SetScannerUpDuplex(Int32 Width, Int32 height, Int32 XOffset,
          Int32 Yoffset, Int32 Xres, Int32 Yres, Int32 ADFEnabled,
          Int32 FrontBrightness, Int32 FrontThreshold, Int32 FrontContrast,
          Int32 BackBrightness, Int32 BackThreshold, Int32 BackContrast,
          Int32 ScannerType, Int32 ScannerOption);



        [DllImport("USVWIN32.DLL", EntryPoint = "ScanPage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ScanPage(String FileName);




        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetScanPhotoMode",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWSetScanPhotoMode(Int32 iValue);



        /*  Public Declare Function VWScanGrayImages Lib "Usvwin32.dll" (Int32 spath As String, 
               Int32 Width As Integer, Int32 height As Integer, Int32 XOffset As Integer, 
                Int32 Yoffset As Integer, Int32 Xres As Integer, Int32 Yres As Integer, Int32 ADFEnabled As Integer, 
                Int32 ScannerType As Integer, Int32 ScannerOption As Integer, Int32 lcompression As Integer, Int32 lQuality ); As Integer
          //JL - Had to revert to old call for this function.
              */

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ScanGrayImages",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWScanGrayImages(String spath,
        Int32 Width, Int32 height, Int32 XOffset,
        Int32 Yoffset, Int32 Xres, Int32 Yres, Int32 ADFEnabled,
        Int32 ScannerType, Int32 ScannerOption, Int32 Compression,
        Int32 Quality);




        /* Public Declare Function VWScanColorImages Lib "Usvwin32.dll" (Int32 spath As String, 
          Int32 Width As Integer, Int32 height As Integer, Int32 XOffset As Integer, 
           Int32 Yoffset As Integer, Int32 Xres As Integer, Int32 Yres As Integer, Int32 ADFEnabled As Integer, 
           Int32 ScannerType As Integer, Int32 ScannerOption As Integer, Int32 lcompression As Integer, Int32 lQuality ); As Integer
             */

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ScanColorImages",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWScanColorImages(String spath,
        Int32 Width, Int32 height, Int32 XOffset,
        Int32 Yoffset, Int32 Xres, Int32 Yres,
        Int32 ADFEnabled, Int32 ScannerType, Int32 ScannerOption,
        Int32 icompression, Int32 iQuality);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SplitGrayColorImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWSplitGrayColorImage(String sinput, Int32 iPage,
        String soutput1, String soutput2, Int32 ioffset);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ConvertJPEGToTIFF",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWConvertJPEGToTIFF(String JPEGFileName, String TiffFileName);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ConvertTIFFToJPEG",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWConvertTIFFToJPEG(String TiffFileName, Int32 Page,
        String JPGFileName, Int32 Mode);



        [DllImport("USVWIN32.DLL", EntryPoint = "COMBINETIFFS",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 COMBINETIFFS(String OutputFile, String AppendFile);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_CombineMultipleTiffPages",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VW_CombineMultipleTiffPages(String OutputFile, String AppendFile, Int32 Mode);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_CombineMultiplePageTiffs",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VW_CombineMultiplePageTiffs(String File1, String File2, String Outputfile);






        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetImageThumbNail",
    CallingConvention = CallingConvention.StdCall,
    SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWSetImageThumbNail(String filename, Int32 page,
    Int32 x, Int32 y, Int32 rotate,
    Int32 Left, Int32 Top, Int32 Right, Int32 Bottom);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_SetDisplayImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWSetDisplayImage(Int32 iType);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_PaintImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWPaintImage(Int32 Spare);



        [DllImport("USVWIN32.DLL", EntryPoint = "GetSelectedImageTop",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetSelectedImageTop(Int32 Spare);



        [DllImport("USVWIN32.DLL", EntryPoint = "GetSelectedImageLeft",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetSelectedImageLeft(Int32 Spare);



        [DllImport("USVWIN32.DLL", EntryPoint = "GetSelectedImageRight",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetSelectedImageRight(Int32 Spare);



        [DllImport("USVWIN32.DLL", EntryPoint = "GetSelectedImageBottom",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 GetSelectedImageBottom(Int32 Spare);



        [DllImport("USVWIN32.DLL", EntryPoint = "VWinSeekBarCode3of9Ex",   // this is a NON VW_ function don't try to fix
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWinSeekBarCode3of9Ex(String filein, Int32 nPage,
        Int32 sleft, Int32 sztop, Int32 sright, Int32 sbottom,
        Int32 minthick, Int32 srange, StringBuilder Buffer);



        #endregion

        #region " TWAIN Scanning Functions from BTWAIN32 "

        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWGetImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWGetImage(Int32 ShowUI, Int32 wPixTypes);
        // Call this function to obtain an image handle that refers to whatever is
        // currently in the scanner. If ShowUI is TRUE, the scanner//s TWAIN driver
        // will show its user-interface dialog. If FALSE, you//re asking that the
        // dialog not appear. Not all TWAIN drivers honor such requests, however.  
        //#define BWTWANYTYPE 0x0000  // any of the following:
        //#define BWTWBW      0x0001  // 1-bit per pixel, B&W     (== TWPTBW)
        //#define BWTWGRAY    0x0002  // 1,4, or 8-bit grayscale  (== TWPTGRAY)
        //#define BWTWRGB     0x0004  // 24-bit RGB color         (== TWPTRGB)
        //#define BWTWPALETTE 0x0008  // 1,4, or 8-bit palette    (== TWPTPALETTE)
        //

        public const Int32 BWTWANYTYPE = 0;
        public const Int32 BWTWBW = 1;
        public const Int32 BWTWGRAY = 2;
        public const Int32 BWTWRGB = 4;
        public const Int32 BWTWPALETTE = 8;

        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWSaveImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWSaveImage(String OutputFile, Int32 DisplayUI, Int32 StartPage);
        //Public Declare Function BWTWSaveImage Lib "btwain32.dll" 
        //(Int32 OutputFile As String, Int32 ShowUI As Integer, 
        //Int32 StartPage ); As Integer
        // Call this function scan a Black/White image into a TIFF Group 4 file.
        // This function returns the number of pages scanned.  If multiple pages
        // are in the Automatic Document Feeder and StartPage is from 0 to 999,
        // then the extension is changed to .ddd and each page is stored in an
        // indivudual image file.  If StartPage is -1, then all pages are scanned
        // into the same file.


        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWFreeImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static void BWTWFreeImage(Int32 handle);
        // Releases the memory allocated to a native format image, as returned by
        // BWTWGetImage.
        // If you don//t free the returned image handle, it stays around taking up
        // Windows (virtual) memory until your application terminates.  Memory
        // required per square inch:
        //             1 bit B&W       8-bit grayscale     24-bit color
        // 100 dpi      1.25KB              10KB               30KB
        // 200 dpi        5KB               40KB              120KB
        // 300 dpi      11.25KB             90KB              270KB
        // 400 dpi       20KB              160KB              480KB
        //End Sub

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWSelectImageSource",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Boolean BWTWSelectImageSource(Int32 handle);

        //// This is the routine to call when the user chooses the "Select Source..."
        //// menu command from your application//s File menu.  Your app has one of
        //// these, right?  The TWAIN spec calls for this feature to be available in
        //// your user interface, preferably as described.
        //// Note: If only one TWAIN device is installed on a system, it is selected
        //// automatically, so there is no need for the user to do Select Source.
        //// You should not require your users to do Select Source before Acquire.
        ////
        //// This function posts the Source Manager//s Select Source dialog box.
        //// It returns after the user either OK//s or CANCEL//s that dialog.
        //// A return of TRUE indicates OK, FALSE indicates one of the following:
        ////   a) The user cancelled the dialog
        ////   b) The Source Manager found no data sources installed
        ////   c) There was a failure before the Select Source dialog could be posted

        //// In the standard implementation of "Select Source...", your application
        //// doesn//t need to do anything except make this one call.
        //End Sub

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWIsAvailable",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Boolean BWTWIsAvailable();
        //// Call this function any time to find out if TWAIN is installed on the
        //// system.  It takes a little time on the first call, after that it//s fast,
        //// just testing a flag.  It returns TRUE if the TWAIN Source Manager is
        //// installed & can be loaded, FALSE otherwise.
        //

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWVersion",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Boolean BWTWVersion();
        //// Returns the version number of BWTWAIN.DLL, multiplied by 100.
        //

        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWRegisterApp",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static void BWTWRegisterApp(ref Int32 nMajorNum, ref Int32 nMinorNum,
        ref Int32 nLanguage, ref Int32 nCountry, ref StringBuilder lpszVersion,
        ref StringBuilder lpszMfg, ref StringBuilder lpszFamily, ref StringBuilder lpszProduct);
        ////    BWTWRegisterApp can be called *AS THE FIRST CALL*, to register the
        ////    application. If this function is not called, the application is given a
        ////    //generic// registration by BWTWAIN.
        ////    Registration only provides this information to the Source Manager and any
        ////    sources you may open - it is used for debugging, and (frankly) by some
        ////    sources to give special treatment to certain applications.

        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWRegisterApp",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWRegisterApp(Int32 iImageType);



        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWGetCurrentResolution",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWGetCurrentResolution();



        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWSetCurrentResolution",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWSetCurrentResolution(Int32 iRes);



        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWSetJPEGQuality",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWSetJPEGQuality(Int32 iQuality);



        [DllImport("BTWAIN32.DLL", EntryPoint = "BWTWSingleImageMode",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 BWTWSingleImageMode(Int32 iMode);



        #endregion

        #region " Image Correction Functions from USVWin32 "

        [DllImport("USVWIN32.DLL", EntryPoint = "VWGetImageSize",   // THIS is a NON VW_command it is VWcommand
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWGetImageSize(String InputFile, Int32 Page);



        [DllImport("USVWIN32.DLL", EntryPoint = "SKEWCORRECT",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 SKEWCORRECT(String InputFile, String OutputFile);



        [DllImport("USVWIN32.DLL", EntryPoint = "AutoCropBorders",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 AutoCropBorders(String FileName, Int32 Left, Int32 Top,
        Int32 Right, Int32 Bottom);



        [DllImport("USVWIN32.DLL", EntryPoint = "ConvertPageofImageFile",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ConvertPageofImageFile(ref Int32 hWnd, String FileNameIn,
        Int32 Page, String FileNameOut);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_DeskewBlackBorders",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWDeskewBlackBorders(String InputFile, String TempFile,
        String OutputFile);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_Rotate90Ex",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWRotate90Ex(String InputFile, Int32 Page,
        String OutputFile, Int32 Rotate);



        [DllImport("USVWIN32.DLL", EntryPoint = "CleanImageSetup",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 CleanImageSetup(String InputImage, String OutputImage,
        Int32 DespeckleWidth, Int32 DespeckleHeight, Int32 DespeckleMaxPixels,
        Int32 DespeckleOn, Int32 BordersOn, Int32 HolesOn);



        [DllImport("USVWIN32.DLL", EntryPoint = "ChangeImageResolution",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 ChangeImageResolution(String InputImage, String OutputImage,
        Int32 ScaleX, Int32 ScaleY);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_CropFileEx",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWCropFileEx(String InputFile, Int32 nPage,
        String OutputFile, Int32 Left, Int32 Top, Int32 Right,
        Int32 Bottom);



        [DllImport("USVWIN32.DLL", EntryPoint = "AutoCropKeepEdges",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 AutoCropKeepEdges(Int32 Left, Int32 Top, Int32 Right,
        Int32 Bottom);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_DeskewBlackBordersEdgeErrorMargin",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWDeskewBlackBordersEdgeErrorMargin(Int32 Value);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_DeskewBlackBordersCropImages",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWDeskewBlackBordersCropImages(Int32 Mode);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ConvertImageFormat",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWConvertImageFormat(String FileIn, Int32 Page,
        String FileOut, Int32 PageMode);
        //PageMode =
        //   0 - TIFF Group 3
        //   1 - PCX
        //   2 - TIFF Group 4
        //   3 - BMP
        //   4 - BMP (pixels reversed)


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ReverseOutBorders",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWReverseOutBorders(String FileIn, String FileOut,
        Int32 Left, Int32 Top, Int32 Right, Int32 Bottom);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ClearEdgesReverse",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWClearEdgesReverse(String FileIn, String FileOut,
        ref Int32 Left, ref Int32 Top, ref Int32 Right, ref Int32 Bottom,
        ref Int32 Left2, ref Int32 Top2, ref Int32 Right2, ref Int32 Bottom2);



        [DllImport("USVWIN32.DLL", EntryPoint = "AutoCropFile",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 AutoCropFile(String FileIn, String FileOut, Int32 Margin);
        //Margin = 0 to 25 as percent


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ConvertTextToTiff",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWConvertTextToTiff(String TextFile, String ImageFile,
        Int32 MaxLinesPerPage, Int32 Bold, Int32 Res, Int32 Width,
        Int32 Height, String Font, Int32 TopMargin, Int32 LeftMargin);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_DitherTextImage",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWDitherTextImage(String InputFile, Int32 Page, String OutputFile,
        Int32 DitherMode, String Message);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_CombineMultiplePageTiffs",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWCombineMultiplePageTiffs(String File1, String File2, String OutputFile);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ForceImageSize",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWForceImageSize(String InputFile, Int32 Page, String OutputFile,
        Int32 Width, Int32 Height);
        //Width and Height - 
        //If less than 256, is assumed to be 10th of an inch, if more, is assumed
        //to be actual pixel size.


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ClearBWFoldedCorners",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWClearBWFoldedCorners(String InputFile, Int32 Page, String OutputFile);



        [DllImport("USVWIN32.DLL", EntryPoint = "VW_ClearBWBlobs",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWClearBWBlobs(String InputFile, Int32 Page,
        String OutputFile, Int32 LeftEdge, Int32 TopEdge, Int32 RightEdge,
        Int32 BottomEdge, Int32 MinWidth, Int32 MinHeight, Int32 MaxWidth,
        Int32 MaxHeight);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_MergeImageOffset",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWMergeImageOffset(String InputFile, Int32 Page,
        String MergeFile, Int32 Page2, String OutputFile, int LeftEdge, Int32 TopEdge);

        [DllImport("USVWIN32.DLL", EntryPoint = "VW_MergeImageRect",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 VWMergeImageRect(String MergeFileBase, Int32 Page,
        String MergeFileTop, Int32 Page2, String OutputFile, int Left, Int32 Top, Int32 Right, Int32 Bottom);



        [DllImport("USVWIN32.DLL", EntryPoint = "EraseIn",
    CallingConvention = CallingConvention.StdCall,
    SetLastError = true, ExactSpelling = true)]
        public extern static Int32 EraseIn(String FileNameIn, String FileNameOut, Int16 Left, Int16 Top, Int16 Right, Int16 Bottom);

        [DllImport("USVWIN32.DLL", EntryPoint = "EraseOut",
CallingConvention = CallingConvention.StdCall,
SetLastError = true, ExactSpelling = true)]
        public extern static Int32 EraseOut(String FileNameIn, String FileNameOut, Int16 Left, Int16 Top, Int16 Right, Int16 Bottom);


        [DllImport("USVWIN32.DLL", EntryPoint = "VW_RemoveDirtLine",
        CallingConvention = CallingConvention.StdCall,
        SetLastError = true, ExactSpelling = true)]
        public extern static Int32 RemoveDirtyLine(string ImageFileName, int iPage, string OutputFileName, int MaxLineThickness, int Column);


        #endregion


    }
    #endregion


}
