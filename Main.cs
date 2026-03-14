using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Drawing.Imaging;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Threading.Tasks;
using IniParser.Model;
using IniParser;
using BitMiracle.LibTiff.Classic;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.CompilerServices.RuntimeHelpers;
using System.Reflection;
using ImageMagick;
using static RAVEN.Form1;
using System.Threading;


namespace RAVEN
{

    public delegate void ScannerCallback(int DIBHandle);

    public partial class Form1 : Form
    {


        // List of settings for conversions 
        public class ConversionSettings
        {


            public string Type { get; set; }
            public bool NegativeImage { get; set; }
            public int Contrast { get; set; }
            public int Brightness { get; set; }
            public int Despeckle { get; set; }
            public int FilterThresholdStepup { get; set; }
            public int Tolerance { get; set; }
            public int DespeckleFilter { get; set; }

            public void CopyValuesFrom(ConversionSettings other)
            {
                this.Type = other.Type;
                this.NegativeImage = other.NegativeImage;
                this.Contrast = other.Contrast;
                this.Brightness = other.Brightness;
                this.Despeckle = other.Despeckle;
                this.FilterThresholdStepup = other.FilterThresholdStepup;
                this.Tolerance = other.Tolerance;
                this.DespeckleFilter = other.DespeckleFilter;
            }

            public ConversionSettings()
            {
                Type = "Dynamic"; // Default value
            }

            public void WriteINI(string sectionName, string filePath = null)
            {
                if (filePath == null)
                {
                    filePath = (Path.Combine(System.Windows.Forms.Application.StartupPath, "settings.ini"));
                }

                var parser = new FileIniDataParser();
                IniData data;

                if (File.Exists(filePath))
                {
                    data = parser.ReadFile(filePath);
                }
                else
                {
                    data = new IniData();
                }

                // Ensure the section exists
                if (!data.Sections.ContainsSection(sectionName))
                {
                    data.Sections.AddSection(sectionName);
                }

                foreach (PropertyInfo prop in this.GetType().GetProperties())
                {
                    string value = prop.GetValue(this)?.ToString() ?? "";

                    if (prop.PropertyType == typeof(bool))
                    {
                        value = (bool)prop.GetValue(this) ? "Y" : "N";
                    }

                    if (prop.PropertyType == typeof(ComboBox))
                    {
                        ComboBox comboBox = (ComboBox)prop.GetValue(this);
                        value = comboBox.SelectedItem?.ToString() ?? "Dynamic"; 
                    }

                    // Only update if the value has changed or doesn't exist
                    if (!data[sectionName].ContainsKey(prop.Name) || data[sectionName][prop.Name] != value)
                    {
                        data[sectionName][prop.Name] = value;
                    }
                }
                parser.WriteFile(filePath, data);
            }

            public void ReadINI(string sectionName, string filePath = "settings.ini")
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"INI file not found: {filePath}");
                }

                var parser = new FileIniDataParser();
                IniData data = parser.ReadFile(filePath);

                if (!data.Sections.ContainsSection(sectionName))
                {
                    Console.WriteLine($"Warning: Section '{sectionName}' not found in the INI file.");
                    return; // Exit the method, leaving properties at their default values
                }

                foreach (PropertyInfo prop in this.GetType().GetProperties())
                {
                    if (data[sectionName].ContainsKey(prop.Name))
                    {
                        string value = data[sectionName][prop.Name];

                        if (prop.PropertyType == typeof(bool))
                        {
                            prop.SetValue(this, value.ToUpper() == "Y");
                        }
                        else if (prop.PropertyType == typeof(int))
                        {
                            if (int.TryParse(value, out int intValue))
                            {
                                prop.SetValue(this, intValue);
                            }
                        }
                        else if (prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(this, value);
                        }
                        // Add more type checks here if needed
                    }
                }

            }
        }

        public class CropCordinates
        {
            // Add this copy constructor
            public CropCordinates(CropCordinates other)
            {
                X1 = other.X1;
                Y1 = other.Y1;
                X2 = other.X2;
                Y2 = other.Y2;
            }

            public int X1 { get; set; }
            public int Y1 { get; set; }
            public int X2 { get; set; }
            public int Y2 { get; set; }

            // Read-only properties for width and height
            public int Width => X2 - X1;
            public int Height => Y2 - Y1;

            public CropCordinates(int x1, int y1, int x2, int y2)
            {
                X1 = x1;
                Y1 = y1;
                X2 = x2;
                Y2 = y2;
            }

            public void ResetCord()
            {
                X1 = 0; Y1 = 0; X2 = 0; Y2 = 0; 
            }

            public void UpdateCoordinates(int newX1, int newY1)
            {
                // Save the original width and height
                int originalWidth = Width;
                int originalHeight = Height;

                // Update X1 and Y1 to the new values
                X1 = newX1;
                Y1 = newY1;

                // Adjust X2 and Y2 based on the new X1/Y1 and original dimensions
                X2 = X1 + originalWidth;
                Y2 = Y1 + originalHeight;
            }
        }


        public ConversionSettings GetSettingsForKey(Keys key)
        {
            switch (key)
            {
                case Keys.F2:
                    return F2Settings;
                case Keys.F3:
                    return F3Settings;
                case Keys.F4:
                    return F4Settings;
                case Keys.F5:
                    return F5Settings;
                case Keys.F6:
                    return F6Settings;
                default:
                    return null;
            }
        }

        private CropCordinates xCropCordinatesEven = new CropCordinates(0, 0, 0, 0); // Original Shift+X
        private CropCordinates xCropCordinatesOdd = new CropCordinates(0, 0, 0, 0); // Original Shift+X
        private CropCordinates sCropCordinates = new CropCordinates(0, 0, 0, 0); // Previous image's crop (S key)
        private CropCordinates dCropCordinates = new CropCordinates(0, 0, 0, 0); // Two images back (D key)
        private CropCordinates workingCropCordinates = new CropCordinates(0, 0, 0, 0); // Active crop for display/movement
        private string cropBoxSource = null; // Tracks if cropbox came from "D", "S" or null

        public ConversionSettings F2Settings { get; private set; }
        public ConversionSettings F3Settings { get; private set; }
        public ConversionSettings F4Settings { get; private set; }

        public ConversionSettings F5Settings { get; private set; }

        public ConversionSettings F6Settings { get; private set; }

        public ConversionSettings LastConversionSettings { get; private set; }

        private void InitializeConversionSettings()
        {
            // Auto turn on line mode if set for Canadian in the INI 
            if (Special_CanadianSetLine)
            {
                Special_DrawLineMode = true;
                UpdateLinePositionFromTag();
                DrawTop();
            }

            string iniFilePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "settings.ini");

            F2Settings = new ConversionSettings();
            F3Settings = new ConversionSettings();
            F4Settings = new ConversionSettings();
            F5Settings = new ConversionSettings();
            F6Settings = new ConversionSettings();

            LastConversionSettings = new ConversionSettings(); 

            F2Settings.ReadINI("F2");
            F3Settings.ReadINI("F3");
            F4Settings.ReadINI("F4");
            F5Settings.ReadINI("F5");
            F6Settings.ReadINI("F6");



            LoadINISettings();
        }

        // Keeps track if we load from a file or a directory (slightly different logic for inserting blanks)
        public ImageSourceType ImageSource { get; private set; }
        public enum ImageSourceType
        {
            Text,
            Directory
        }

        public MultipageModifyMode vMultipageModifyMode { get; set; } = MultipageModifyMode.Uninitialized;
        public List<Tuple<int, int, int, int, ConversionSettings>> vMultipageModifyList { get; private set; } = new List<Tuple<int, int, int, int, ConversionSettings>>();


        public enum MultipageModifyMode
        {
            Uninitialized, 
            // Every Page
            InitializedEveryPage,
            // Every Other Page
            InitializedEveryOtherPage
        }      

        public bool BatchWhiteoutMode = false;
        // Turn this on to print the bulk crop on the screen - then when active and press "W" - do BatchWhiteout
        public bool BatchWhiteoutActive = false;

        // Turn this on - changes crop mode to "whiteout outside this area" mode
        public bool isWhiteoutMode = false;

        public List<Tuple<int, int, int, int>> vEvenWhiteoutList { get; private set; } = new List<Tuple<int, int, int, int>>();
        public List<Tuple<int, int, int, int>> vOddWhiteoutList { get; private set; } = new List<Tuple<int, int, int, int>>();







        // Keeps track of our image list w attributes
        public List<(string JPG, string TIF, int BX1, int BY1, int BX2, int BY2)> ImagePairs { get; private set; } = new List<(string JPG, string TIF, int BX1, int BY1, int BX2, int BY2)>();
        public int currentImageIndex = 0;

        //Cached JPG / TIF so I don't have to reload them after a single conversion. Delete them on the page next. 
        private int CachedJPG = 0;
        private int CachedTIF = 0;

        private int CachedPartialImageHandle = 0;
        private (int PartialSelectionX1, int PartialSelectionY1, int PartialSelectionX2, int PartialSelectionY2) CachedPartialImageDimensions = (0, 0, 0, 0);

        private bool isThresholdMeRunning = false;
      
        // Field to track the current image index - made public to be able to convert rest of the book forward 


        // Class-level variable to track the load requests
        private int loadVersion = 0;

        // Ability to disable the ThresholdSettings changes with wheel (will use it for fine rotation & maybe other things)

        public enum WheelModes
        {
            ThresholdSettings = 0,
            FineRotation = 1,
            // Other modes can follow, with values like 4, 8, 16, etc.
        }

        WheelModes currentWheelMode = WheelModes.ThresholdSettings;




        private string actionStatus, selectionStatus, modeStatus, autolineStatus;
        private string _lastThresholdDetail;

        //Initialize Jump to Index
        public int InitialJumpToIndex = 0; 

        //Cropbox
        bool activecropbox = false;

        //JPG Visible
        public bool isJPGVisible = false;


        //Zoom variable
        public int ZoomFactor;

        //Set crop box cordinates 
        private int CropLeftEven = 0;
        private int CropTopEven = 0;
        private int CropLeftOdd = 0;
        private int CropTopOdd = 0;

        private int CropHeight = 0;
        private int CropWidth = 0;


        // Special settings
        public bool Special_InsertBlankAbility = false;
        public bool Special_BatchWhiteoutAbility = false;

        public bool Special_DrawLineMode = false;
        public decimal Special_DrawLineValue = 0;

        public bool Special_CanadianSetLine = false;
        public bool Special_CropJPG = false;


        public bool AutoLineRemovalmode = false;
        public List<int> LineRemovalList;

        public Form1()
        {
            isJPGVisible = true;
            Console.WriteLine("Form1 constructor started");
            LogEnvironmentInfo();



            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath);

            // Set the working directory to the executable's directory
            Directory.SetCurrentDirectory(exeDir);
            Console.WriteLine($"Set working directory to: {exeDir}");


            InitializeComponent();
            InitializeConversionSettings();

            int warmupHandle = RavenImaging.ImgCreate(100, 100, 2, 300);
            RavenImaging.ImgDelete(warmupHandle); 

            this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(Form1_KeyDown);
            this.MouseWheel += new MouseEventHandler(Form1_MouseWheel);
            keyPicture1.DoClickEvent += KeyPicture1_DoClickEvent;
            keyPicture2.DoClickEvent += KeyPicture2_DoClickEvent;

            


            keyPicture2.OnDoRightClickEvent += KeyPicture2_OnDoRightClickEvent;

            LineRemovalList = new List<int>();
            this.Shown += new System.EventHandler(this.Form_Shown);

            //To Test with the string path and put file name in turn testfilelaunch = true; 

            bool testfilelaunch = false;
            

            // If launched with a command line / directory - use that and disable the ability to move books
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 || testfilelaunch == true)
            {


                string path = ""; 

                if (args.Length <= 1)
                {
                    path = @"I:\KEY\dsn\NSFileList.txt";
                }
                else
                {
                    path = args[1];
                }

                if (Directory.Exists(path))
                {
                    // It's a directory
                    this.textBox1.Text = path;
                    this.textBox1.ReadOnly = true;
                }
                else if (File.Exists(path))
                {
                    // It's a text file - jump to this image
                    if (!path.EndsWith(".txt"))
                    {
                        // Check if it's a TIF file and has a matching JPG
                        if (Path.GetExtension(path).Equals(".tif", StringComparison.OrdinalIgnoreCase))
                        {
                            path = GetMatchingJpgIfExists(path);
                        }

                        // It's a file
                        this.textBox1.Text = Path.GetDirectoryName(path);
                        this.textBox1.ReadOnly = true;

                        // Load the image pairs from the directory


                        // Find the index of the file - this may be loading directory twice
                        LoadImagePairsFromDirectory(Path.GetDirectoryName(path));

                        int index = ImagePairs.FindIndex(pair => pair.TIF.Equals(path, StringComparison.OrdinalIgnoreCase) || pair.JPG.Equals(path, StringComparison.OrdinalIgnoreCase));

                        // Sloppy - should be able to just assign index but doesn't work for some reason so will call the JumpTo in the Form_Shown

                        if (index >= 0)
                        {
                            this.InitialJumpToIndex = index;
                        }
                        else
                        {
                            MessageBox.Show($"File {path} not found in the directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        textBox1.Text = path; 
                    }
                }
                

            }
            else
            {
                textBox1.Text = @"C:\temp\1a";
            }

            // Attach to Load event of Form1
            // this.Load += Form1_Load;
        }

        private void LogEnvironmentInfo()
        {
            Console.WriteLine($"Current Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"Command Line: {Environment.CommandLine}");
            Console.WriteLine($"Is 64-bit Process: {Environment.Is64BitProcess}");
            Console.WriteLine($"OS Version: {Environment.OSVersion}");
            Console.WriteLine($"System Directory: {Environment.SystemDirectory}");
            Console.WriteLine($"User Interactive: {Environment.UserInteractive}");
            Console.WriteLine($"Working Set: {Environment.WorkingSet}");
        }

        private string GetMatchingJpgIfExists(string tifPath)
        {
            string jpgPath = Path.ChangeExtension(tifPath, ".jpg");
            return File.Exists(jpgPath) ? jpgPath : tifPath;
        }


        private void Form_Shown(Object sender, EventArgs e)
        {
            OpenSettings();
            this.Activate();

            if (this.InitialJumpToIndex > 0)
            {
                JumpTo(InitialJumpToIndex); 
            }

            

            // If launched w a file - jump to the correct index. 
        }

        private void OpenSettings()
        {
            if (thresholdSettingsForm == null || thresholdSettingsForm.IsDisposed)
            {
                // Create a new instance of the form.
                thresholdSettingsForm = new Settings();

                // Set the current form (this) as the owner of the ThresholdSettings form.
                thresholdSettingsForm.Show(this); // Using Show(IWin32Window owner)
                thresholdSettingsForm.SetFirstContrast();
            }
            else
            {
                // If the form is minimized, bring it to normal state.
                if (thresholdSettingsForm.WindowState == FormWindowState.Minimized)
                {
                    thresholdSettingsForm.WindowState = FormWindowState.Normal;
                }

                // The form is already created and not disposed, so just bring it to front.
                thresholdSettingsForm.Show();
                thresholdSettingsForm.Activate();
                thresholdSettingsForm.SetFirstContrast();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Maximized;

            keyPicture2.Location = new Point(1, 35);
            keyPicture1.Location = new Point(1400, 35);

            int swidth = this.Width / 2;

            keyPicture2.Height = this.Height - 90;
            keyPicture1.Height = this.Height - 90;
            keyPicture2.Width = 1350;
            keyPicture1.Width = 1350;

            button_go.PerformClick();

            // Checks INI
            LoadINISettings();

            // Wire up background save callback — updates title bar + green checkmark when save completes
            OpenThresholdBridge.OnSaveCompleted = (writeMs) =>
            {
                if (InvokeRequired)
                    BeginInvoke(() => OnBackgroundSaveCompleted(writeMs));
                else
                    OnBackgroundSaveCompleted(writeMs);
            };
        }

        private void OnBackgroundSaveCompleted(long writeMs)
        {
            // Update title bar: replace "save:bg" with actual save time
            if (this.Text.Contains("save:bg"))
                this.Text = this.Text.Replace("save:bg", $"save:{writeMs}");
            keyPicture2.ShowSaved();
        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            /// Come back here 
            if (currentWheelMode == WheelModes.ThresholdSettings)
            {
                if (thresholdSettingsForm != null && !thresholdSettingsForm.IsDisposed)
                {
                    thresholdSettingsForm.Settings_MouseWheel(sender, e);
                }
                ThresholdMe(e.Delta);
            }
            else if (currentWheelMode == WheelModes.FineRotation)
            {
                double rotationAmount = e.Delta > 0 ? -.4 : .4;
                

                FineRotate(this.ImagePairs[currentImageIndex].TIF, (float)rotationAmount);
                // This works - but need to use later after figure out a way to cache how much rotated by and rotate / deskew tif all at once
                // FineRotateAlt(this.ImagePairs[currentImageIndex].TIF, (float)rotationAmount);
                // MessageBox.Show($"FineRotate executed in {stopwatch.ElapsedMilliseconds} ms", "Execution Time");

                RefreshDisplayImage();
            }
        }
        public string LineListToString() =>
    $"{string.Join(", ", LineRemovalList.Take(5))}{(LineRemovalList.Count > 5 ? " ..." : "")}";



        private void KeyPicture2_OnDoRightClickEvent(object sender, EventArgs e)
        {
            // Is Ctrl pressed? 
            bool isShiftPress = (ModifierKeys & Keys.Shift) == Keys.Shift;

            // Get selection coordinates
            int left = keyPicture2.GetSelectedLeft();
            int top = keyPicture2.GetSelectedTop();
            int right = keyPicture2.GetSelectedRight();
            int bottom = keyPicture2.GetSelectedBottom();

            if (activecropbox)
            {
                // CTRL modifier moves the closest corner of the cropbox
                if (isShiftPress)
                {
                    // Calculate squared distances to each corner
                    double distTL = Math.Pow(left - workingCropCordinates.X1, 2) + Math.Pow(top - workingCropCordinates.Y1, 2);
                    double distTR = Math.Pow(left - workingCropCordinates.X2, 2) + Math.Pow(top - workingCropCordinates.Y1, 2);
                    double distBL = Math.Pow(left - workingCropCordinates.X1, 2) + Math.Pow(top - workingCropCordinates.Y2, 2);
                    double distBR = Math.Pow(left - workingCropCordinates.X2, 2) + Math.Pow(top - workingCropCordinates.Y2, 2);

                    // Find the smallest distance
                    double minDist = Math.Min(Math.Min(distTL, distTR), Math.Min(distBL, distBR));

                    // Adjust the closest corner to the click position
                    if (minDist == distTL)
                    {
                        workingCropCordinates.X1 = left;  // Top-left corner: set X1 and Y1
                        workingCropCordinates.Y1 = top;
                    }
                    else if (minDist == distTR)
                    {
                        workingCropCordinates.X2 = left;  // Top-right corner: set X2 and Y1
                        workingCropCordinates.Y1 = top;
                    }
                    else if (minDist == distBL)
                    {
                        workingCropCordinates.X1 = left;  // Bottom-left corner: set X1 and Y2
                        workingCropCordinates.Y2 = top;
                    }
                    else if (minDist == distBR)
                    {
                        workingCropCordinates.X2 = left;  // Bottom-right corner: set X2 and Y2
                        workingCropCordinates.Y2 = top;
                    }

                    // Update the crop box display
                    SetCropbox(workingCropCordinates, true);

                    // Update our D/S locations
                    if (cropBoxSource == "D")
                    {
                        dCropCordinates.X1 = workingCropCordinates.X1;
                        dCropCordinates.Y1 = workingCropCordinates.Y1;
                        dCropCordinates.X2 = workingCropCordinates.X2;
                        dCropCordinates.Y2 = workingCropCordinates.Y2;
                    }
                    else if (cropBoxSource == "S")
                    {
                        sCropCordinates.X1 = workingCropCordinates.X1;
                        sCropCordinates.Y1 = workingCropCordinates.Y1;
                        sCropCordinates.X2 = workingCropCordinates.X2;
                        sCropCordinates.Y2 = workingCropCordinates.Y2;
                    }
                }
                else
                {
                    UpdateCropBox(left, top);
                }
            }
            else
            {
                if (left != right)
                {
                    this.selectionStatus = $"X1:{left}, Y1:{top}, X2:{right} Y2:{bottom}";
                    StatusUpdate();
                }
                else if (Special_DrawLineMode)
                {
                    UpdateLinePosition(top);
                }
                else
                {
                    RemoveAndRedisplayLine(left);
                }
            }
        }

        // Updates the crop box position based on a click
        private void UpdateCropBox(int x, int y)
            {
            bool isEven = currentImageIndex % 2 == 0;

            if (AreCoordinatesEqual(xCropCordinatesEven, workingCropCordinates))
            {
                workingCropCordinates.UpdateCoordinates(x, y);
                xCropCordinatesEven.UpdateCoordinates(x, y);
            }
            else if (AreCoordinatesEqual(xCropCordinatesOdd, workingCropCordinates))
            {
                workingCropCordinates.UpdateCoordinates(x, y);
                xCropCordinatesOdd.UpdateCoordinates(x, y);
            }
            else
            {
                workingCropCordinates.UpdateCoordinates(x, y);
            }

            SetCropbox(workingCropCordinates, true);
        }

        // Helper method to compare coordinate objects
        private bool AreCoordinatesEqual(CropCordinates a, CropCordinates b)
        {
            return a.X1 == b.X1 && a.Y1 == b.Y1 && a.X2 == b.X2 && a.Y2 == b.Y2;
        }

        // Updates the line position in DrawLine mode
        private void UpdateLinePosition(int top)
        {
            Special_DrawLineValue = top / 300m; // Convert pixels to inches at 300 DPI
            DrawTop(); // Redraw the line at new position
        }

        // Removes a line and redisplays the image
        private void RemoveAndRedisplayLine(int left)
        {
            RemoveLine(this.ImagePairs[currentImageIndex].TIF, left);
            DisplayImages(string.Empty, this.ImagePairs[currentImageIndex].TIF, 2, true);
        }




        private void RemoveLine(string Image, int X)
        {
            RavenImaging.RemoveDirtyLine(Image, 1, Image, 3, X);

            if (this.AutoLineRemovalmode == true)
            {
                this.LineRemovalList.Add(X);
                autolineStatus = "L:" + LineListToString();
                StatusUpdate();
            }

        }

        // May need to disable keypicture1 zoom event in the future 
        private void KeyPicture1_DoClickEvent(object sender, EventArgs e)
        {
            int Left = keyPicture1.GetZoomLeft();
            int Top = keyPicture1.GetZoomTop();
            int Right = keyPicture1.GetZoomRight();
            int Bottom = keyPicture1.GetZoomBottom();

            // Match the zoom of the unzoomed image to the zoomed one
            if (keyPicture1.LastZoomed > keyPicture2.LastZoomed)
            {

                keyPicture2.ZoomPanImage1(Left, Top, Right, Bottom);
            }
        }




        private void KeyPicture2_DoClickEvent(object sender, EventArgs e)
        {
            // At one point I had changed the two lines below - but I don't understand why. Put it back to what made sense. 
            /*
             int Right = keyPicture2.GetSelectedRight();
             int Bottom = keyPicture2.GetSelectedBottom();
            */
            int Left = keyPicture2.GetZoomLeft();
            int Top = keyPicture2.GetZoomTop();
            int Right = keyPicture2.GetZoomRight();
            int Bottom = keyPicture2.GetZoomBottom();

            if (keyPicture2.LastZoomed > keyPicture1.LastZoomed)
            {
                keyPicture1.ZoomPanImage1(Left, Top, Right, Bottom);
            }
        }

        private Settings thresholdSettingsForm = null;


        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (!textBox1.Focused)
            {
                // Check if the Page Down key is pressed
                if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.N || e.KeyCode == Keys.Space) // 'Next' represents the Page Down key
                {
                    // Perform the button click action
                    NextImage.PerformClick();

                    // Optionally, set Handled to true to prevent furthernu processing of this key
                    e.Handled = true;
                }
                // Check if the Page Down key is pressed
                if (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.U) // 'Next' represents the Page Down key
                {
                    // Perform the button click action
                    PrevImage.PerformClick();


                    // Optionally, set Handled to true to prevent further processing of this key
                    e.Handled = true;
                }
                if (e.KeyCode == Keys.F2 || e.KeyCode == Keys.F3 || e.KeyCode == Keys.F4 || e.KeyCode == Keys.F5 || e.KeyCode == Keys.F6)
                {
                    if (vMultipageModifyMode == MultipageModifyMode.InitializedEveryPage || vMultipageModifyMode == MultipageModifyMode.InitializedEveryOtherPage)
                    {
                        MultipageModifyAlt(e.KeyCode);
                    }
                    else
                    {
                        // With this:
                        var settings = GetSettingsForKey(e.KeyCode);
                        if (settings != null)
                        {
                            ThresholdMe(settings);
                        }
                    }
                }
                if (e.KeyCode == Keys.W)
                {
                    if (this.BatchWhiteoutActive == true)
                    {
                        BatchWhiteout(ImagePairs[currentImageIndex].TIF);
                        this.BatchWhiteoutActive = false;
                    }
                    else
                    {
                        int _left = keyPicture2.GetSelectedLeft();
                        int _top = keyPicture2.GetSelectedTop();

                        // Adding 1 because it misses far right pixel on edges
                        int _right = keyPicture2.GetSelectedRight() + 1;
                        int _bottom = keyPicture2.GetSelectedBottom();

                        if (ModifierKeys == Keys.Shift)
                        {
                            whiteout(ImagePairs[currentImageIndex].TIF, _left, _top, _right, _bottom, true);
                        }
                        else
                        {
                            whiteout(ImagePairs[currentImageIndex].TIF, _left, _top, _right, _bottom);
                        }
                    }
                }
                if (e.KeyCode == Keys.R)
                {
                    toggle(ImagePairs[currentImageIndex].TIF, keyPicture2.GetSelectedLeft(), keyPicture2.GetSelectedTop(), keyPicture2.GetSelectedRight(), keyPicture2.GetSelectedBottom());

                }
                if (e.KeyCode == Keys.Z)
                {
                    if (e.Modifiers == Keys.Shift)
                    {
                        LoadNextCheckpoint(ImagePairs[currentImageIndex].TIF);
                    }
                    else
                    {
                        LoadPrevCheckpoint(ImagePairs[currentImageIndex].TIF);
                    }
                }
                if (e.Modifiers == Keys.Shift && e.KeyCode == Keys.X)
                {
                    bool isEven = currentImageIndex % 2 == 0;

                    if (isEven)
                    {
                        xCropCordinatesEven.X1 = keyPicture2.GetSelectedLeft();
                        xCropCordinatesEven.Y1 = keyPicture2.GetSelectedTop();
                        xCropCordinatesEven.X2 = keyPicture2.GetSelectedRight();
                        xCropCordinatesEven.Y2 = keyPicture2.GetSelectedBottom();

                        // We need to set Odd crop box to be same width / height, just 0, 0 starting
                        xCropCordinatesOdd.X1 = 0;
                        xCropCordinatesOdd.Y1 = 0;
                        xCropCordinatesOdd.X2 = keyPicture2.GetSelectedRight() - keyPicture2.GetSelectedLeft();
                        xCropCordinatesOdd.Y2 = keyPicture2.GetSelectedBottom() - keyPicture2.GetSelectedTop();

                        workingCropCordinates.X1 = xCropCordinatesEven.X1;
                        workingCropCordinates.Y1 = xCropCordinatesEven.Y1;
                        workingCropCordinates.X2 = xCropCordinatesEven.X2;
                        workingCropCordinates.Y2 = xCropCordinatesEven.Y2;                      

                    }
                    else
                    {
                        xCropCordinatesOdd.X1 = keyPicture2.GetSelectedLeft();
                        xCropCordinatesOdd.Y1 = keyPicture2.GetSelectedTop();
                        xCropCordinatesOdd.X2 = keyPicture2.GetSelectedRight();
                        xCropCordinatesOdd.Y2 = keyPicture2.GetSelectedBottom();

                        xCropCordinatesEven.X1 = 0;
                        xCropCordinatesEven.Y1 = 0; 
                        xCropCordinatesEven.X2 = keyPicture2.GetSelectedRight() - keyPicture2.GetSelectedLeft();
                        xCropCordinatesEven.Y2 = keyPicture2.GetSelectedBottom() - keyPicture2.GetSelectedTop();

                        workingCropCordinates.X1 = xCropCordinatesOdd.X1;
                        workingCropCordinates.Y1 = xCropCordinatesOdd.Y1;
                        workingCropCordinates.X2 = xCropCordinatesOdd.X2;
                        workingCropCordinates.Y2 = xCropCordinatesOdd.Y2;


                    }
                    SetCropbox(workingCropCordinates);


                    // We should no longer need the set variables logic on SetCropbox - remove all references to it. Refactor code (it will still have all the screen display stuff)                  
                    e.Handled = true;
                }
                if (e.KeyCode == Keys.X && e.Modifiers != Keys.Shift)
                {
                    // Load cropbox from SavedCordinates
                    bool isEven = currentImageIndex % 2 == 0;
                    if (isEven)
                    {                     
                        workingCropCordinates.X1 = xCropCordinatesEven.X1;
                        workingCropCordinates.Y1 = xCropCordinatesEven.Y1;
                        workingCropCordinates.X2 = xCropCordinatesEven.X2;
                        workingCropCordinates.Y2 = xCropCordinatesEven.Y2; 
                    }
                    else
                    {
                        workingCropCordinates.X1 = xCropCordinatesOdd.X1;
                        workingCropCordinates.Y1 = xCropCordinatesOdd.Y1;
                        workingCropCordinates.X2 = xCropCordinatesOdd.X2;
                        workingCropCordinates.Y2 = xCropCordinatesOdd.Y2;
                    }
                    SetCropbox(workingCropCordinates);
                    
                }
                if (e.KeyCode == Keys.F) // f is for flip could use f6 instead for consistency with indexing tool
                {
                    Rotate90(this.ImagePairs[currentImageIndex].TIF);
                    Rotate90(this.ImagePairs[currentImageIndex].JPG);

                    RefreshDisplayImage(true);
                }
                if (e.KeyCode == Keys.C)
                {
                    // Eventually move this to other function "GetCropCord()"?
                    int x1 = 0;
                    int y1 = 0;
                    int x2 = 0;
                    int y2 = 0;

                    // Think the activecropbox isn't being set (or the cropwidth / cropheight)
                    if (activecropbox == true)
                    {
                        x1 = workingCropCordinates.X1;
                        y1 = workingCropCordinates.Y1;
                        x2 = workingCropCordinates.X2;
                        y2 = workingCropCordinates.Y2;
                    }
                    else
                    {
                        x1 = keyPicture1.GetSelectedLeft();
                        y1 = keyPicture1.GetSelectedTop();
                        x2 = keyPicture1.GetSelectedRight();
                        y2 = keyPicture1.GetSelectedBottom();
                    }


                    // Update D/S cordinates - keeps crop box size
                    if (activecropbox == true)
                    {
                        if (cropBoxSource == "D")
                        {
                            // Check if moved from original D location
                            if (x1 != dCropCordinates.X1 || y1 != dCropCordinates.Y1 ||
                                x2 != dCropCordinates.X2 || y2 != dCropCordinates.Y2)
                            {
                                dCropCordinates.X1 = x1;
                                dCropCordinates.Y1 = y1;
                                dCropCordinates.X2 = x2;
                                dCropCordinates.Y2 = y2;
                            }
                        }
                        else if (cropBoxSource == "S")
                        {
                            // Check if moved from original S location
                            if (x1 != sCropCordinates.X1 || y1 != sCropCordinates.Y1 ||
                                x2 != sCropCordinates.X2 || y2 != sCropCordinates.Y2)
                            {
                                sCropCordinates.X1 = x1;
                                sCropCordinates.Y1 = y1;
                                sCropCordinates.X2 = x2;
                                sCropCordinates.Y2 = y2;
                            }
                        }
                        cropBoxSource = null; 
                    }

                    //Handle if we are using 

                    if (x1 < x2 && y1 < y2)
                    {
                        // Calculate current width and height
                        int currentWidth = x2 - x1;
                        int currentHeight = y2 - y1;

                        // If using D/S - we update their location & activecropbox, has that box been moved from existing location? If so update it and let rest of logic continue. 
                        if (activecropbox)
                        {


                        }


                        // Update D/S whiteout variables if not using active cropbox.
                        if (!activecropbox)
                        {
                            sCropCordinates.X1 = dCropCordinates.X1;
                            sCropCordinates.Y1 = dCropCordinates.Y1;
                            sCropCordinates.X2 = dCropCordinates.X2;
                            sCropCordinates.Y2 = dCropCordinates.Y2;

                            // Update dCropCordinates with current coordinates
                            dCropCordinates.X1 = x1;
                            dCropCordinates.Y1 = y1;
                            dCropCordinates.X2 = x2;
                            dCropCordinates.Y2 = y2;
                        }

                        

                        if (this.isWhiteoutMode == true)
                        {
                            // How do we set x1, y1, x2, y2 to the other variables if D/S were pushed? 
                            whiteout(ImagePairs[currentImageIndex].TIF, x1, y1, x2, y2, false, true);

                        }
                        else
                        {
                            crop(ImagePairs[currentImageIndex].TIF, x1, y1, x2, y2);
                           
                        }

                        keyPicture2.RemoveAnnotation(1, 1, 1);
                        activecropbox = false;
                    }
                }
                if (e.KeyCode == Keys.J)
                {
                    JumpTo jumpToForm = new JumpTo(); // Create a new instance of the JumpTo form
                    DialogResult result = jumpToForm.ShowDialog(); // Show the form as a modal dialog and capture the result

                    if (result == DialogResult.OK && jumpToForm.JumpToPage.HasValue)
                    {
                        // Show the JumpToPage value after the form is closed
                        //Jump to specified index 
                        int jumpto = jumpToForm.JumpToPage.Value - 1; // Our index starts at 0, 1 = page 1

                        JumpTo(jumpto);
                    }

                }
                if (e.KeyCode == Keys.Home)
                {
                    JumpTo(0);
                }
                if (e.KeyCode == Keys.L)
                {
                    if (AutoLineRemovalmode == false)
                    {

                        AutoLineRemovalmode = true;
                        autolineStatus = "L:";
                        StatusUpdate();
                    }

                    else
                    {
                        //Clear our line removals
                        AutoLineRemovalmode = false;
                        // As long as L is pushed - it removes lines on the next pages. Exiting line removal mode in a book (pushing L) deletes these spots and goes back to normal. 
                        this.LineRemovalList.Clear();
                        autolineStatus = "";
                        StatusUpdate();
                    }
                }
                if (e.KeyCode == Keys.Q)
                {
                    // If Shift is held down, call AutosetCropbox with LocationOnly = false
                    // Otherwise, call it with LocationOnly = true
                    AutosetCropbox(Control.ModifierKeys == Keys.Shift ? false : true);
                }
                if (e.KeyCode == Keys.Escape)
                {
                    System.Windows.Forms.Application.Exit();
                }
                if (e.KeyCode == Keys.Enter)
                {
                    button_go.PerformClick();
                }
                if (e.KeyCode == Keys.Y)
                {
                    deskew(ImagePairs[currentImageIndex].TIF);
                }
                if (e.KeyCode == Keys.Oemtilde)
                {
                    ToggleJPG();
                }
                if (e.KeyCode == Keys.F1)
                {
                    KeyboardShortcutHelp();
                }
                if (e.KeyCode == Keys.A)
                {
                    if ((e.Modifiers & Keys.Shift) == Keys.Shift)
                    {
                        thresholdSettingsForm.goPrevControl();
                    }
                    else
                    {
                        thresholdSettingsForm.goNextControl();
                    }
                    e.Handled = true;
                    return; // Prevents further processing
                }

                else if (e.KeyCode == Keys.End)
                {
                    JumpTo(ImagePairs.Count - 1);
                }
                else if (e.KeyCode == Keys.D1)
                {
                    ChangeWheelMode();
                }
                else if (e.KeyCode == Keys.Insert && (e.Modifiers & Keys.Shift) == Keys.Shift)
                {
                    TryAddRemoveBlank();
                }
                else if (e.KeyCode == Keys.Insert)
                {
                    string currentImagePath = ImagePairs[currentImageIndex].TIF; // Assuming this is how you access the current image path
                    Memo memoForm = new Memo(currentImagePath); // Create a new instance of Memo with the current image path
                    memoForm.Show(); // Show the Memo form

                }
                else if (e.KeyCode == Keys.D5)
                {
                    if (this.Special_BatchWhiteoutAbility == true)
                    {
                        BatchWhiteoutStatusToggle();
                    }
                }
                else if (e.KeyCode == Keys.D6)
                {
                    DrawLineStatusToggle();
                }
                else if (e.KeyCode == Keys.E)
                {
                    // Not convinced this batch whiteout works correctly.
                    if (this.BatchWhiteoutMode == true)
                    {
                        BatchWhiteoutActivateToggle();
                    }
                }
                else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.D3)
                {
                    // Runs customizable powershell scripts that have access to image file, image path & program path variables 
                    // Currently used for irfanview & CASJ

                    // Output current variables to Variables.txt
                    string currentAppPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                    string currentTIFFile = ImagePairs[currentImageIndex].TIF;
                    string currentTIFFPath = Path.GetDirectoryName(currentTIFFile).TrimEnd(Path.DirectorySeparatorChar);

                    // Path to Variables.txt
                    string variablesFilePath = Path.Combine(currentAppPath, "Variables.txt");

                    // Write variables to Variables.txt
                    using (StreamWriter writer = new StreamWriter(variablesFilePath, false))
                    {
                        writer.WriteLine($"ImgFile={currentTIFFile}");
                        writer.WriteLine($"ImgPath={currentTIFFPath}");
                        writer.WriteLine($"RAVENPath={currentAppPath}");
                    }

                    string psFile = "";

                    if (e.KeyCode == Keys.D2)
                    {
                        psFile = Path.Combine(currentAppPath, "2.ps1");
                        if (!string.IsNullOrEmpty(psFile))
                        {
                            RunPowerShellScript(psFile);
                        }
                    }

                    if (e.KeyCode == Keys.D3)
                    {
                        psFile = Path.Combine(currentAppPath, "3.ps1");
                        if (!string.IsNullOrEmpty(psFile))
                        {
                            RunPowerShellScript(psFile);
                        }
                    }
                    e.Handled = true; 
                }
                else if (e.KeyCode == Keys.D4)
                {
                    // Switch program to "WhiteoutCrop" mode. This results in 
                    // A - All crop features work the same EXCEPT that instead of "cropping" the TIF image - it whites out all the areas outside the selected area. 
                    // Get the dimensions that need to be whited out - outside of the drawn box
                    // Crude way would be current selected rectangle - everything left of that
                    // Everything above that
                    // Everything to the right of that
                    // Everything to the bottom of that 
                    // 4 Rectangles
                    // Whiteout uses RavenImaging.EraseOut (C# implementation)
                    isWhiteoutMode = !isWhiteoutMode;
                    string modeText = isWhiteoutMode ? "Whiteout" : "Normal";
                    StatusUpdate($"Switched to {modeText} mode");

                }
                if (e.KeyCode == Keys.OemOpenBrackets || e.KeyCode == Keys.OemCloseBrackets)
                {
                    MultipageModifyAlt(e.KeyCode);
                }
                if (e.KeyCode == Keys.S || e.KeyCode == Keys.D)
                {
                    // 20250203 - For Canadian 
                    // If whiteout mode activated, save the last two images crop area (whiteout area)

                    if (isWhiteoutMode)
                    {
                        CropCordinates coordsToUse = null;
                        if (e.KeyCode == Keys.D && dCropCordinates.X2 > 0) // Check if coordinates are set
                        {
                            cropBoxSource = "D"; 
                            coordsToUse = new CropCordinates(dCropCordinates);
                        }
                        else if (e.KeyCode == Keys.S && sCropCordinates.X2 > 0) // Check if coordinates are set
                        {
                            cropBoxSource = "S"; 
                            coordsToUse = new CropCordinates(sCropCordinates);
                        }
                        // Skip if no valid coordinates are set
                        if (coordsToUse == null || coordsToUse.X1 >= coordsToUse.X2 || coordsToUse.Y1 >= coordsToUse.Y2)
                        {
                            return; // Do nothing if coordinates are unset or invalid
                        }

                        // If crop box is active and matches these coordinates, toggle it off
                        if (activecropbox &&
                            workingCropCordinates.X1 == coordsToUse.X1 &&
                            workingCropCordinates.Y1 == coordsToUse.Y1 &&
                            workingCropCordinates.X2 == coordsToUse.X2 &&
                            workingCropCordinates.Y2 == coordsToUse.Y2)
                        {
                            SetCropbox(coordsToUse, false); // Use existing toggle-off path
                        }
                        else
                        {
                            SetCropbox(coordsToUse, true); // Force it on with new/changed coordinates
                        }
                    }
                    else
                    {
                        int _modify = 0;
                        if (e.KeyCode == Keys.S)
                        {
                            _modify = -1;
                        }
                        else
                        {
                            _modify = +1;
                        }

                        if (LastConversionSettings.Contrast == 0)
                        {
                            LastConversionSettings.CopyValuesFrom(F2Settings);
                        }
                        LastConversionSettings.Contrast = LastConversionSettings.Contrast + _modify;
                        ThresholdMe(LastConversionSettings);
                    }
                    
                }
                e.Handled = true; 
            }

        }


        // Alternate multipage modify — area-based conversion across pages.
        private void MultipageModifyAlt(Keys pressedKey)
        {
            // [ = Initialize / Uninitialize & Clear Cordinates
            // ] = Run & then Uninitialize 

            //Was [ key pressed (initialize) & initialize not already set?

            bool IsDynamicOrML(string type) => type == "Dynamic" || type == "RDynamic" || type == "Refine" || type == "ML1" || type == "ML2";

            if (pressedKey == Keys.OemOpenBrackets)
            {
                if (vMultipageModifyMode == MultipageModifyMode.Uninitialized)
                {
                    this.vMultipageModifyMode = MultipageModifyMode.InitializedEveryPage;
                    AddStatusUpdate("Area Conversion InitializeEveryPage");
                }
                else if (vMultipageModifyMode == MultipageModifyMode.InitializedEveryPage)
                {
                    this.vMultipageModifyMode = MultipageModifyMode.InitializedEveryOtherPage;
                    AddStatusUpdate("Area Conversion InitializeEveryOtherPage");
                }
                else
                {
                    this.vMultipageModifyMode = MultipageModifyMode.Uninitialized;
                    AddStatusUpdate("Area Conversion Uninitialized", true);
                    vMultipageModifyList.Clear();
                }
            }

            // If we are initialized & an F2/F3/F4 key has been pressed, we will check dimensions & TIF tag for fine rotate. If they match - we apply the F key settings to the cordinates & add to a list. 
            if ((this.vMultipageModifyMode == MultipageModifyMode.InitializedEveryPage || this.vMultipageModifyMode == MultipageModifyMode.InitializedEveryOtherPage) && (pressedKey == Keys.F2 || pressedKey == Keys.F3 || pressedKey == Keys.F4 || pressedKey == Keys.F5 || pressedKey == Keys.F6))
            {
                if (!VerifyMatchingDimensions(ImagePairs[currentImageIndex].TIF, ImagePairs[currentImageIndex].JPG))
                {
                    MessageBox.Show("TIF/JPG Dimensions don't match!");
                }
                else
                {
                    var settings = GetSettingsForKey(pressedKey);
                    if (settings != null && !IsDynamicOrML(settings.Type))
                    {
                        MessageBox.Show("This feature only works for Dynamic & SBB Thresholding.");
                    }
                    else
                    {
                        int _left = keyPicture2.GetSelectedLeft();
                        int _top = keyPicture2.GetSelectedTop();
                        int _right = keyPicture2.GetSelectedRight();
                        int _bottom = keyPicture2.GetSelectedBottom();

                        AddStatusUpdate("Drawbox Dimensions" + _left.ToString() + "," + _top.ToString() + "," + _right.ToString() + _bottom.ToString() + "," + pressedKey.ToString());

                        // With this:
                        var settings1 = GetSettingsForKey(pressedKey);
                        if (settings1 != null)
                        {
                            vMultipageModifyList.Add(Tuple.Create(_left, _top, _right, _bottom, settings1));
                        }
                    }
                }
                ClearSelection();
            }


            // Move starting here 

            if ((this.vMultipageModifyMode == MultipageModifyMode.InitializedEveryPage || this.vMultipageModifyMode == MultipageModifyMode.InitializedEveryOtherPage) && pressedKey == Keys.OemCloseBrackets)
            {
                if (vMultipageModifyList.Count == 0)
                {
                    AddStatusUpdate("No modifications to apply.");
                    return; // This will exit the entire method
                }

                bool hasSBB = false;
                bool hasDynamic = false;

                foreach (var item in vMultipageModifyList)
                {
                    string type = item.Item5.Type; 
                    if (type == "ML1" || type == "ML2")
                    {
                        hasSBB = true;
                    }
                    if (type == "Dynamic" || type == "RDynamic" || type == "Refine")
                    {
                        hasDynamic = true;
                    }
                }

                if (hasDynamic && hasSBB)
                {
                    MessageBox.Show("Currently only supports doing one conversion type.");
                    return;
                }
                else if (hasDynamic)
                {
                    MultipageModifyAlt_Dynamic();
                }
                else if (hasSBB)
                {
                    MultipageModifyAlt_SBB();
                }
                this.vMultipageModifyMode = MultipageModifyMode.Uninitialized;
                AddStatusUpdate("Area Conversion Uninitialized", true);
                RefreshDisplayImage();
            }
            // Move ending here 
        }

        private void MultipageModifyAlt_SBB()
        {
            // Loop thru each additional image in book
            var _imagePairs = this.ImagePairs;
            int increment = (this.vMultipageModifyMode == MultipageModifyMode.InitializedEveryOtherPage) ? 2 : 1;
            for (int i = currentImageIndex; i < _imagePairs.Count; i += increment)
            {
                // Check if JPG & TIF dimensions match 
                if (!VerifyMatchingDimensions(_imagePairs[i].TIF, _imagePairs[i].JPG))
                {
                    AddStatusUpdate($"{_imagePairs[i].JPG} Not Matching TIF/JPG Dimensions");
                    continue;  // Skip this file if dimensions don't match
                }
                foreach (var item in vMultipageModifyList)
                {
                    int x1 = item.Item1;
                    int y1 = item.Item2;
                    int x2 = item.Item3;
                    int y2 = item.Item4;
                    ConversionSettings _conversionSettings = item.Item5 as ConversionSettings;

                    ThresholdMe(_conversionSettings, x1, y1, x2, y2, _imagePairs[i].JPG, _imagePairs[i].TIF, false, true);
//                     ThresholdMe(_ConvSetting, left, top, right, bottom, _imagePairs[i].JPG, _imagePairs[i].TIF, false, true);

                }
            }
                // Loop thru each setting, convert that area.         
        }

        // Flag to prevent overlapping batch conversions
        private volatile bool _batchConvertRunning = false;

        /// <summary>
        /// Multi-threaded batch conversion for Dynamic/RDynamic/Refine types.
        /// Processes all remaining images from currentImageIndex using the byte-array
        /// pipeline directly (thread-safe, no shared static cache or handle API).
        /// Runs in the background so the UI stays responsive.
        /// </summary>
        private void MultipageModifyAlt_Dynamic()
        {
            if (_batchConvertRunning)
            {
                AddStatusUpdate("Batch conversion already in progress.");
                return;
            }

            // Snapshot everything we need before going async
            var modifyList = vMultipageModifyList
                .Select(t => (x1: t.Item1, y1: t.Item2, x2: t.Item3, y2: t.Item4, settings: t.Item5))
                .ToList();

            // Separate full-page entries (coords <= 0) from area entries (positive coords)
            var areaEntries = modifyList.Where(a => a.x1 < a.x2 && a.y1 < a.y2).ToList();
            var fullPageEntries = modifyList.Where(a => a.x1 <= 0 && a.y1 <= 0 && a.x2 <= 0 && a.y2 <= 0).ToList();

            if (areaEntries.Count == 0 && fullPageEntries.Count == 0)
            {
                AddStatusUpdate("No valid conversions to apply.");
                vMultipageModifyList.Clear();
                return;
            }

            // Check if any full-page entry uses the photostat pipeline
            bool anyPhotostat = fullPageEntries.Any(fp =>
                fp.settings.NegativeImage &&
                (fp.settings.Type == "RDynamic" || fp.settings.Type == "Dynamic"));

            var imagePairs = this.ImagePairs;
            int startIndex = this.currentImageIndex;
            int increment = (this.vMultipageModifyMode == MultipageModifyMode.InitializedEveryOtherPage) ? 2 : 1;

            var indices = new List<int>();
            for (int i = startIndex; i < imagePairs.Count; i += increment)
                indices.Add(i);

            if (indices.Count == 0)
            {
                AddStatusUpdate("No images to process.");
                vMultipageModifyList.Clear();
                return;
            }

            int totalImages = indices.Count;
            int maxParallelism = Math.Max(1, Environment.ProcessorCount - 2);

            _batchConvertRunning = true;
            AddStatusUpdate($"Batch converting {totalImages} images ({maxParallelism} threads)...");

            // Ensure any pending single-image save completes before batch starts
            OpenThresholdBridge.WaitForPendingSave();

            // Clear the list now — we've captured everything we need
            vMultipageModifyList.Clear();

            Task.Run(() =>
            {
                int completedCount = 0;
                int skippedCount = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = maxParallelism };

                    Parallel.ForEach(indices, options, imageIndex =>
                    {
                        string jpgPath = imagePairs[imageIndex].JPG;
                        string tifPath = imagePairs[imageIndex].TIF;

                        try
                        {
                            // Verify dimensions match (reads file headers only, thread-safe)
                            int jpgW = 0, jpgH = 0, tifW = 0, tifH = 0;
                            RavenImaging.GetImageInfo(jpgPath, 1, ref jpgW, ref jpgH);
                            RavenImaging.GetImageInfo(tifPath, 1, ref tifW, ref tifH);
                            if (jpgW != tifW || jpgH != tifH)
                            {
                                Interlocked.Increment(ref skippedCount);
                                return;
                            }

                            // Decode JPEG to grayscale byte arrays (thread-local, no shared state)
                            byte[] grayLut, grayAvg;
                            byte[] bgr = null;
                            int bgrStride = 0;
                            int fullWidth, fullHeight;

                            if (anyPhotostat)
                            {
                                // Photostat needs BGR data for bleed-through removal
                                (grayLut, bgr, bgrStride, fullWidth, fullHeight) =
                                    RavenImaging.LoadImageAsGrayscaleAndBgr(jpgPath);
                                // Compute avg grayscale from BGR for Refine support
                                grayAvg = new byte[fullWidth * fullHeight];
                                Parallel.For(0, fullHeight, y =>
                                {
                                    int srcRow = y * bgrStride;
                                    int dstRow = y * fullWidth;
                                    for (int x = 0; x < fullWidth; x++)
                                    {
                                        int off = srcRow + x * 3;
                                        grayAvg[dstRow + x] = (byte)((bgr[off + 2] + bgr[off + 1] + bgr[off] + 1) / 3);
                                    }
                                });
                            }
                            else
                            {
                                (grayLut, grayAvg, fullWidth, fullHeight) =
                                    RavenImaging.LoadImageAsGrayscaleDual(jpgPath);
                            }

                            // Process full-page entries first (if any)
                            // Last full-page entry wins (replaces entire TIF)
                            if (fullPageEntries.Count > 0)
                            {
                                var fp = fullPageEntries[fullPageEntries.Count - 1];
                                bool refine = fp.settings.Type == "Refine";
                                int contrast = refine
                                    ? fp.settings.Contrast + fp.settings.FilterThresholdStepup
                                    : fp.settings.Contrast;
                                int brightness = fp.settings.Brightness;
                                int despeckle = refine ? fp.settings.DespeckleFilter : fp.settings.Despeckle;
                                int tolerance = fp.settings.Tolerance;
                                bool negative = fp.settings.NegativeImage;

                                byte[] binary;

                                if (negative && (fp.settings.Type == "RDynamic" || fp.settings.Type == "Dynamic"))
                                {
                                    // Full photostat pipeline with border detection + bleed-through
                                    binary = BatchConvertPhotostatFull(bgr, bgrStride,
                                        grayLut, fullWidth, fullHeight, contrast, brightness, despeckle);
                                }
                                else
                                {
                                    byte[] srcGray = grayLut;
                                    byte[] srcAvg = grayAvg;

                                    if (negative)
                                    {
                                        // Simple negative: clone + invert
                                        srcGray = new byte[fullWidth * fullHeight];
                                        srcAvg = grayAvg != null ? new byte[fullWidth * fullHeight] : null;
                                        for (int i = 0; i < srcGray.Length; i++)
                                        {
                                            srcGray[i] = (byte)(255 - grayLut[i]);
                                            if (srcAvg != null) srcAvg[i] = (byte)(255 - grayAvg[i]);
                                        }
                                    }

                                    binary = RavenImaging.DynamicThresholdApply(
                                        srcGray, fullWidth, fullHeight, 7, 7, contrast, brightness);
                                    if (refine && srcAvg != null)
                                        binary = RavenImaging.RefineThresholdApply(
                                            binary, srcAvg, fullWidth, fullHeight, tolerance);
                                    if (despeckle > 0)
                                        RavenImaging.DespeckleBytes(binary, fullWidth, fullHeight,
                                            despeckle, despeckle);
                                }

                                RavenImaging.SaveAsCcitt4Tif(binary, fullWidth, fullHeight, tifPath);
                            }

                            // Process area entries (composite onto existing TIF)
                            if (areaEntries.Count > 0)
                            {
                                // Load existing TIF pixels for compositing
                                byte[] tifPixels;
                                int tw, th;
                                if (File.Exists(tifPath))
                                {
                                    (tifPixels, tw, th) = RavenImaging.LoadImageAsGrayscale(tifPath);
                                }
                                else
                                {
                                    tw = fullWidth; th = fullHeight;
                                    tifPixels = new byte[tw * th];
                                    Array.Fill(tifPixels, (byte)255);
                                }

                                foreach (var area in areaEntries)
                                {
                                    // Clamp coordinates to image bounds
                                    int ax1 = Math.Max(0, Math.Min(area.x1, fullWidth));
                                    int ay1 = Math.Max(0, Math.Min(area.y1, fullHeight));
                                    int ax2 = Math.Max(0, Math.Min(area.x2, fullWidth));
                                    int ay2 = Math.Max(0, Math.Min(area.y2, fullHeight));
                                    int cropW = ax2 - ax1, cropH = ay2 - ay1;
                                    if (cropW <= 0 || cropH <= 0) continue;

                                    bool refine = area.settings.Type == "Refine";
                                    int contrast = refine
                                        ? area.settings.Contrast + area.settings.FilterThresholdStepup
                                        : area.settings.Contrast;
                                    int brightness = area.settings.Brightness;
                                    int despeckle = refine
                                        ? area.settings.DespeckleFilter
                                        : area.settings.Despeckle;
                                    int tolerance = area.settings.Tolerance;
                                    bool negative = area.settings.NegativeImage;

                                    // Extract crop from LUT grayscale
                                    byte[] cropGray = new byte[cropW * cropH];
                                    for (int row = 0; row < cropH; row++)
                                        Buffer.BlockCopy(grayLut,
                                            (ay1 + row) * fullWidth + ax1,
                                            cropGray, row * cropW, cropW);

                                    if (negative)
                                    {
                                        for (int i = 0; i < cropGray.Length; i++)
                                            cropGray[i] = (byte)(255 - cropGray[i]);
                                    }

                                    // Extract crop from avg grayscale (for Refine)
                                    byte[] cropGrayAvg = null;
                                    if (refine && grayAvg != null)
                                    {
                                        cropGrayAvg = new byte[cropW * cropH];
                                        for (int row = 0; row < cropH; row++)
                                            Buffer.BlockCopy(grayAvg,
                                                (ay1 + row) * fullWidth + ax1,
                                                cropGrayAvg, row * cropW, cropW);
                                        if (negative)
                                        {
                                            for (int i = 0; i < cropGrayAvg.Length; i++)
                                                cropGrayAvg[i] = (byte)(255 - cropGrayAvg[i]);
                                        }
                                    }

                                    // Threshold the crop
                                    byte[] binary = RavenImaging.DynamicThresholdApply(
                                        cropGray, cropW, cropH, 7, 7, contrast, brightness);
                                    if (refine && cropGrayAvg != null)
                                        binary = RavenImaging.RefineThresholdApply(
                                            binary, cropGrayAvg, cropW, cropH, tolerance);
                                    if (despeckle > 0)
                                        RavenImaging.DespeckleBytes(binary, cropW, cropH,
                                            despeckle, despeckle);

                                    // Composite: paste thresholded crop into TIF at (ax1, ay1)
                                    for (int row = 0; row < cropH; row++)
                                        Buffer.BlockCopy(binary, row * cropW,
                                            tifPixels, (ay1 + row) * tw + ax1, cropW);
                                }

                                // Save the composited TIF
                                RavenImaging.SaveAsCcitt4Tif(tifPixels, tw, th, tifPath);
                            }

                            int done = Interlocked.Increment(ref completedCount);
                            if (done % 5 == 0 || done == totalImages)
                            {
                                this.BeginInvoke(new Action(() =>
                                    AddStatusUpdate($"Batch converting... {done}/{totalImages}")));
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref skippedCount);
                            System.Diagnostics.Debug.WriteLine(
                                $"Batch convert error on {jpgPath}: {ex.Message}");
                        }
                    });

                    sw.Stop();
                    string msg = $"Batch complete: {completedCount} converted, {skippedCount} skipped" +
                                 $" in {sw.Elapsed.TotalSeconds:F1}s";

                    this.BeginInvoke(new Action(() =>
                    {
                        AddStatusUpdate(msg, true);
                        RefreshDisplayImage();
                        _batchConvertRunning = false;
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        AddStatusUpdate($"Batch conversion failed: {ex.Message}");
                        _batchConvertRunning = false;
                    }));
                }
            });
        }

        /// <summary>
        /// Thread-safe full photostat conversion using byte-array pipeline only.
        /// Replicates OpenThresholdBridge.ApplyThresholdToFilePhotostat logic
        /// without touching any static cache state. All operations use local arrays.
        /// </summary>
        private static byte[] BatchConvertPhotostatFull(byte[] bgr, int bgrStride,
            byte[] grayLut, int W, int H, int contrast, int brightness, int despeckle)
        {
            // Clone BGR since bleed-through removal modifies it in-place
            byte[] bgrCopy = new byte[bgr.Length];
            Buffer.BlockCopy(bgr, 0, bgrCopy, 0, bgr.Length);

            // Phase 1: Border detection + BT background detection (overlapped)
            int T = 0;
            int aLeft = 0, aTop = 0, aRight = 0, aBottom = 0;
            int bLeft = 0, bTop = 0, bRight = 0, bBottom = 0;
            int cLeft = 0, cTop = 0, cRight = 0, cBottom = 0;
            int aW = 0, aH = 0, bW = 0, bH = 0, contentW = 0, contentH = 0;
            byte[] aGray = null;
            int btBgH = 0, btBgS = 0, btBgL = 0, btOtsu = 0;
            bool borderFailed = false;

            Parallel.Invoke(
                () =>
                {
                    T = RavenImaging.NativeAutoThresholdPublic(grayLut, W, H, W, 2);
                    byte[] borderPacked = RavenImaging.ThresholdAndPack1bpp(grayLut, W, H, T);
                    int byteW = (W + 7) / 8;

                    aLeft   = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 0, 90.0, 1);
                    aTop    = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 2, 90.0, 1);
                    aRight  = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 1, 90.0, 1);
                    aBottom = RavenImaging.FindBlackBorderDirect(borderPacked, byteW, W, H, 1, 3, 90.0, 1);
                    if (aLeft > aRight || aTop > aBottom) { borderFailed = true; return; }

                    aW = aRight - aLeft + 1; aH = aBottom - aTop + 1;
                    aGray = RavenImaging.ExtractGrayscaleSubRegion(grayLut, W, aLeft, aTop, aRight, aBottom);
                    byte[] aCropPacked = RavenImaging.ThresholdAndPack1bpp(aGray, aW, aH, T);
                    int aCropByteW = (aW + 7) / 8;
                    for (int i = 0; i < aCropPacked.Length; i++) aCropPacked[i] = (byte)~aCropPacked[i];

                    bLeft   = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 0, 99.0, 1);
                    bTop    = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 2, 99.0, 1);
                    bRight  = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 1, 99.0, 1);
                    bBottom = RavenImaging.FindBlackBorderDirect(aCropPacked, aCropByteW, aW, aH, 1, 3, 99.0, 1);
                    bLeft += 20; bRight -= 20;
                    if (bLeft > bRight || bTop > bBottom) { borderFailed = true; return; }

                    bW = bRight - bLeft + 1; bH = bBottom - bTop + 1;
                    byte[] bGrayForBorder = RavenImaging.ExtractGrayscaleSubRegion(aGray, aW, bLeft, bTop, bRight, bBottom);
                    byte[] bCropPacked = RavenImaging.ThresholdAndPack1bpp(bGrayForBorder, bW, bH, T);
                    int bCropByteW = (bW + 7) / 8;
                    for (int i = 0; i < bCropPacked.Length; i++) bCropPacked[i] = (byte)~bCropPacked[i];

                    cLeft   = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 0, 80.0, 30);
                    cTop    = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 2, 80.0, 100);
                    cRight  = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 1, 80.0, 30);
                    cBottom = RavenImaging.FindBlackBorderDirect(bCropPacked, bCropByteW, bW, bH, 1, 3, 80.0, 100);
                    contentW = cRight - cLeft + 1; contentH = cBottom - cTop + 1;
                },
                () =>
                {
                    RavenImaging.RemoveBleedThroughGetBackground(bgrCopy, W, H, bgrStride,
                        out btBgH, out btBgS, out btBgL, out btOtsu);
                }
            );

            if (borderFailed)
                return new byte[W * H]; // White image on border detection failure

            // Phase 2: Bleed-through removal (modifies bgrCopy in-place, row-parallel)
            int nThreads = Environment.ProcessorCount;
            int rowsPerThread = (H + nThreads - 1) / nThreads;
            Parallel.For(0, nThreads, t =>
            {
                int startY = t * rowsPerThread;
                int endY = Math.Min(startY + rowsPerThread, H);
                if (startY < endY)
                    RavenImaging.RemoveBleedThroughApplyRows(bgrCopy, W, H, bgrStride, 1,
                        btBgH, btBgS, btBgL, startY, endY);
            });

            // Phase 3: Post-BT grayscale conversion
            int absContentLeft  = aLeft + bLeft + cLeft;
            int absContentTop   = aTop  + bTop  + cTop;
            int absContentRight = aLeft + bLeft + cRight;
            int absContentBottom = aTop + bTop + cBottom;
            int absBLeft = aLeft + bLeft, absBTop = aTop + bTop;
            int absBRight = aLeft + bRight, absBBottom = aTop + bBottom;

            byte[] grayPost = new byte[W * H];
            byte[] contentGray = null;

            Parallel.Invoke(
                () =>
                {
                    Parallel.For(0, H, y =>
                    {
                        int srcRow = y * bgrStride;
                        int dstRow = y * W;
                        for (int x = 0; x < W; x++)
                        {
                            int off = srcRow + x * 3;
                            grayPost[dstRow + x] = (byte)(RavenImaging.LutR[bgrCopy[off + 2]]
                                + RavenImaging.LutG[bgrCopy[off + 1]] + RavenImaging.LutB[bgrCopy[off]]);
                        }
                    });
                },
                () =>
                {
                    contentGray = RavenImaging.ExtractGrayscaleFromBgr(bgrCopy, bgrStride,
                        absContentLeft, absContentTop, absContentRight, absContentBottom, invert: true);
                }
            );

            byte[] aGrayPost = RavenImaging.ExtractGrayscaleSubRegion(grayPost, W, aLeft, aTop, aRight, aBottom);
            byte[] bGrayPost = RavenImaging.ExtractGrayscaleSubRegion(grayPost, W, absBLeft, absBTop, absBRight, absBBottom);

            // Phase 4: Parallel AT + content DT + cleanup
            byte[] fullResult = new byte[W * H];
            byte[] aResult = new byte[aW * aH];
            byte[] bResult = new byte[bW * bH];
            byte[] contentBinary = null;

            Parallel.Invoke(
                () => RavenImaging.AdaptiveThresholdApply(grayPost, fullResult, W, H, 7, 7, -1, -1),
                () => RavenImaging.AdaptiveThresholdApply(aGrayPost, aResult, aW, aH, 7, 7, 40, 230),
                () => RavenImaging.AdaptiveThresholdApply(bGrayPost, bResult, bW, bH, 7, 7, 40, 230),
                () =>
                {
                    contentBinary = RavenImaging.DynamicThresholdApply(contentGray, contentW, contentH,
                        7, 7, contrast, brightness);
                    byte[] contentPacked = RavenImaging.PackTo1bpp(contentBinary, contentW, contentH);
                    int cByteW = (contentW + 7) / 8;
                    if (despeckle > 0)
                        RavenImaging.DespeckleApply(contentPacked, cByteW, contentW, contentH, despeckle, despeckle);
                    RavenImaging.RemoveBlackWiresDirect(contentPacked, cByteW, contentW, contentH);
                    int PhHeight = contentH - 10;
                    int PhRatio = PhHeight / 5;
                    int phBreaks = PhHeight - 15000;
                    RavenImaging.RemoveVerticalLinesDirect(contentPacked, cByteW, contentW, contentH,
                        PhHeight, phBreaks, PhRatio);
                    RavenImaging.UnpackFrom1bpp(contentPacked, contentBinary, contentW, contentH);
                }
            );

            // Composite: content -> page -> overscan -> full
            RavenImaging.CompositeBytes(contentBinary, contentW, contentH, bResult, bW, cLeft, cTop);
            RavenImaging.CompositeBytes(bResult, bW, bH, aResult, aW, bLeft, bTop);
            RavenImaging.CompositeBytes(aResult, aW, aH, fullResult, W, aLeft, aTop);

            return fullResult;
        }


        private void ClearSelection()
        {
            keyPicture2.SetSelectedImageArea(0, 0, 0, 0);
            RefreshDisplayImage();
        }


        private void AddStatusUpdate(string StatusUpdate, bool clearstatus = false)
        {
            thresholdSettingsForm.AddStatusUpdate(StatusUpdate, clearstatus);
        }




        private void RunPowerShellScript(string scriptFile)
        {
            try
            {
                // Path to the PowerShell script
                // @"C:\path\to\2.ps1";

                // Create a new process to run the PowerShell script
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFile}\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                // Start the process
                using (Process process = Process.Start(psi))
                {
                    // Read the output
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    // Wait for the process to exit
                    process.WaitForExit();

                    // Handle the output and error if necessary
                    if (!string.IsNullOrEmpty(output))
                    {
                        Console.WriteLine(output);
                    }

                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine(error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running PowerShell script: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChangeWheelMode()
        {
            // Get all values of the WheelModes enum
            var modes = Enum.GetValues(typeof(WheelModes)).Cast<WheelModes>().ToList();

            // Find the index of the current mode
            int currentIndex = modes.IndexOf(currentWheelMode);

            // Increment the index to move to the next mode, wrapping around if necessary
            int nextIndex = (currentIndex + 1) % modes.Count;

            // Update the current mode to the next one
            currentWheelMode = modes[nextIndex];

            modeStatus = $"Wheel Mode: {currentWheelMode}";
            StatusUpdate();
        }

        // Need to update the list 
        // 

        private void TryAddRemoveBlank()
        {
            if (Special_InsertBlankAbility != true)
            {
                StatusUpdate("Insert blank option not turned on in INI settings.");
                return;
            }

            if (this.ImageSource == ImageSourceType.Text)
            {
                StatusUpdate("Unable to insert blanks when images are loaded from a text file."); 
                return;
            }


            string _tif = ImagePairs[currentImageIndex].TIF;
            string _filename = Path.GetFileNameWithoutExtension(_tif);
            string _path = Path.GetDirectoryName(_tif); 

            if (ImagePairs[currentImageIndex].TIF.EndsWith("_Blank.tif", StringComparison.OrdinalIgnoreCase))
            {
                // Delete blank, go back to prev image & update status bar. 
                File.Delete(ImagePairs[currentImageIndex].TIF);
                File.Delete(ImagePairs[currentImageIndex].JPG);

                // Remove the blank image pair from the list
                ImagePairs.RemoveAt(currentImageIndex);

                // Adjust the current image index to the previous image if possible
                if (currentImageIndex > 0)
                {
                    currentImageIndex--;
                }

                // Refresh the display
                if (ImagePairs.Any())
                {
                    DisplayImages(ImagePairs[currentImageIndex].JPG, ImagePairs[currentImageIndex].TIF, 0, true);
                }
                StatusUpdate("Deleted " + _filename);
            }

            else

            {
               // Not a blank image - so we insert one. 
               int _ImageCopyHandle = RavenImaging.ImgOpen(_tif, 0);
               var (_width, _height) = GetDimensions(_tif);
               RavenImaging.ImgDelete(_ImageCopyHandle);
               int _NewImg = RavenImaging.ImgCreate(_width, _height, 1, 300);

                

                
               int _NewImg2 = RavenImaging.ImgCreate(_width, _height, 8, 300);

               string blankTif = _filename + "_Blank.tif";
               string blankJpg = _filename + "_Blank.jpg";

                RavenImaging.ImgDrawRectangle(_NewImg, 0, 0, _width, _height, 1, true);
                // May want to add "Image Intentionally left blank" at some point. 

                // RavenImaging.ImgDrawText(_NewImg, 150, 150, 1, 1, 1, 1, "This Image Intentionally Left Blank.");
                // RavenImaging.ImgDrawText(_NewImg, "Hello, World!", 50, 50, "Arial", 12, 0,  0, 1); // 0 represents black, 1 represents white in a bitonal image

                RavenImaging.ImgSaveAsTif(_NewImg, Path.Combine(_path, _filename + "_Blank.tif"), 5, 0);

                RavenImaging.ImgDrawRectangle(_NewImg2, 0, 0, _width, _height, 255, true);
                RavenImaging.ImgSaveAsJpg(_NewImg2, Path.Combine(_path, _filename + "_Blank.jpg"), 80);

                // Insert the new blank image pair into the list
                ImagePairs.Insert(currentImageIndex + 1, (Path.Combine(_path, _filename + "_Blank.jpg"), Path.Combine(_path, _filename + "_Blank.tif"), 0, 0, 0, 0));

                // Move to the new blank image
                currentImageIndex++;

                // Refresh the display
                DisplayImages(ImagePairs[currentImageIndex].JPG, ImagePairs[currentImageIndex].TIF, 0, true);
                StatusUpdate("Inserted " + _filename + "_Blank");
                RavenImaging.ImgDelete(_NewImg);
                RavenImaging.ImgDelete(_NewImg2);
            }

        }

       




        private void Rotate90(string image)
        {
            //Works w JPG or TIF images 

            int _Rotatehandle = RavenImaging.ImgOpen(image, 0);
            RavenImaging.ImgRotate(_Rotatehandle, 90);

            if (image.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
            {
                bool ImgSaved = SaveImageVerified(_Rotatehandle, image);
            }

            if (image.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || image.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                bool ImgSaved = SaveImageVerified(_Rotatehandle, image); 
            }
            

            //add a section to flip the jpg — RavenImaging.ImgRotate works for jpgs, may not play well with checkpoint system

            // using finerotate tag currently may not be needed if jpg is also flipped 
            RavenImaging.ImgDelete(_Rotatehandle);

            if (!IsFineRotated(image))
            {
                WriteFineRotationTag(image, true);
            }
        }

        /// <summary>
        /// Fast rotate w Alternate IMGU library
        /// </summary>
        /// <param name="image">The image to rotate.</param>
        /// <param name="degrees">The degrees to rotate.</param>
        private void FineRotateAlt(string image, float degrees)
        {
            RotateWithSameCanvasSize(image, (float)degrees, image);
            bool isRotated = IsFineRotated(image);

            if (!isRotated)
            {
                WriteFineRotationTag(image, true);
            }

            actionStatus = "FR: " + degrees.ToString();
            StatusUpdate();


        }



        private void FineRotate(string image, float degrees)
        {

            int _Rotatehandle = RavenImaging.ImgOpen(image, 0);

            RavenImaging.ImgCorrectDeformation1(_Rotatehandle, degrees, degrees, true);

            int newHeight = RavenImaging.ImgGetHeight(_Rotatehandle); 
            int newWidth = RavenImaging.ImgGetWidth(_Rotatehandle) + 1;

            // Change using TIF tag to adding column of white. 
            RavenImaging.ImgResize(_Rotatehandle, newWidth, newHeight, 0); 

            bool ImgSaved = SaveImageVerified(_Rotatehandle, image);

            // We use adding a pixel now instead of rotate tag. Safer. 
            // bool isRotated = IsFineRotated(image);

            // Conditionally measure WriteFineRotationTag
            
            /*
            if (!isRotated)
            {
                WriteFineRotationTag(image, true);
            }
            */ 
          
            RavenImaging.ImgDelete(_Rotatehandle);

            // Update action status
            actionStatus = "FR: " + degrees.ToString();
            StatusUpdate();


        }

        // I don't trust being able to track this with tags as we move to eventually rotate JPGs


        public void WriteFineRotationTag(string tiffPath, bool rotated)
        {
            using (Tiff image = Tiff.Open(tiffPath, "r+"))
            {
                if (image == null) return;

                // Use a standard tag to store the rotation info. Here we use IMAGEDESCRIPTION for simplicity.
                string rotationFlag = rotated ? "Y" : "N";
                image.SetField(TiffTag.IMAGEDESCRIPTION, $"FineRotated: {rotationFlag}");

                image.CheckpointDirectory(); // Writes the current directory and prepares for the next.
            }
        }

        public bool IsFineRotated(string tiffPath)
        {
            using (Tiff image = Tiff.Open(tiffPath, "r"))
            {
                if (image == null) return false;

                // Retrieve the IMAGEDESCRIPTION tag value
                FieldValue[] fieldValues = image.GetField(TiffTag.IMAGEDESCRIPTION);
                if (fieldValues != null && fieldValues.Length > 0)
                {
                    string description = fieldValues[0].ToString();
                    if (description.Contains("FineRotated: Y"))
                    {
                        return true; // The image has been marked as rotated

                    }
                }
            }
            return false; // Default to false if not rotated or tag not found
        }


        private void RefreshDisplayImage(bool ForceJPGRefresh = false)
        {
            if (ForceJPGRefresh == true)
            {
                DisplayImages(this.ImagePairs[currentImageIndex].JPG, this.ImagePairs[currentImageIndex].TIF, 0, true);
            }
            else
            {
                DisplayImages(string.Empty, this.ImagePairs[currentImageIndex].TIF, 2, true);
            }
            
            
        }


        // Verified save via RavenImaging — writes to temp then replaces original.
        // Ensures file is deleted before overwrite and verifies final file exists.
        // 5 ms slower than suggested GPT but I like more saving at the very end.
        private static bool SaveImageVerified(int ImgHandle, string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            if (!new[] { ".tif", ".jpg", ".jpeg" }.Contains(extension))
            {
                MessageBox.Show("Unsupported file format. Only TIF and JPG are supported.");
                return false;
            }

            string tmpFile = Path.ChangeExtension(filePath, ".tmp");

            try
            {
                // Clean up any existing temp file
                if (File.Exists(tmpFile)) File.Delete(tmpFile);

                // Try to save the image, with one retry on failure
                Action saveImage = () => {
                    if (extension == ".tif")
                        RavenImaging.ImgSaveAsTif(ImgHandle, tmpFile, 5, 0);
                    else
                        RavenImaging.ImgSaveAsJpg(ImgHandle, tmpFile, 80);
                };

                try { saveImage(); }
                catch (System.Runtime.InteropServices.SEHException)
                {
                    Thread.Sleep(1000);
                    saveImage();
                }

                // Replace the target file with our temp file
                File.Move(tmpFile, filePath, overwrite: true);

                return File.Exists(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save image: {ex.Message}");
                return false;
            }
            finally
            {
                // Clean up temp file if it still exists
                try { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private static bool VerifyDelete(string _file)
        {
            if (File.Exists(_file))
            {
                try
                {
                    File.Delete(_file);
                }
                catch (IOException)
                {
                    // Log or handle the error as needed
                    MessageBox.Show("Unable to delete file!");
                    return false;
                }
            }

            return true;
        }





        private void ToggleJPG()
        {
            // This needs to be refactored taking into account the proc that displays images - won't display a jpg if turned off etc and I have to push a blank JPG into it - can all be redone much better. 
            if (this.isJPGVisible == true)
            {
                this.isJPGVisible = false;
                RefreshJPGVisibility();
            }
            else 
            {
                this.isJPGVisible = true;
                RefreshJPGVisibility();
            }
        }

        public void deskew(string tifimage)
        {   
            CreateCheckpoint(tifimage);

            RavenImaging.SKEWCORRECT(tifimage, tifimage);
            DisplayImages(string.Empty, tifimage, 2, true);
            activecropbox = false;
        }
		



        public void RotateWithSameCanvasSize(string imagePath, double angle, string outputPath)
        {
            // GDI+ implementation — no Emgu.CV dependency
            using (var src = new Bitmap(imagePath))
            {
                int width  = src.Width;
                int height = src.Height;

                // Render into 32bppArgb with white background, apply rotation
                using (var canvas = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);
                    g.TranslateTransform(width / 2f, height / 2f);
                    g.RotateTransform((float)-angle);
                    g.TranslateTransform(-width / 2f, -height / 2f);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.DrawImage(src, 0, 0);

                    // Extract pixels as grayscale, binarize at threshold 128
                    var bmpData = canvas.LockBits(
                        new Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    byte[] gray = new byte[width * height];
                    unsafe
                    {
                        byte* ptr = (byte*)bmpData.Scan0.ToPointer();
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int idx = y * bmpData.Stride + x * 4;
                                byte b = ptr[idx];
                                byte gv = ptr[idx + 1];
                                byte r = ptr[idx + 2];
                                // Luminance; binarize immediately
                                byte lum = (byte)(0.299 * r + 0.587 * gv + 0.114 * b);
                                gray[y * width + x] = lum < 128 ? (byte)0 : (byte)255;
                            }
                        }
                    }
                    canvas.UnlockBits(bmpData);

                    RavenImaging.SaveAsCcitt4Tif(gray, width, height, outputPath);
                }
            }
        }







        private void StatusUpdate(string Append = "")
        {
            string status = "RAVEN2 | " + this.ImagePairs[currentImageIndex].TIF + " | " + this.actionStatus + " | " + selectionStatus + " | " + modeStatus + " | " + autolineStatus;
            if (Append != "") { status = status + Append; }

            this.Text = status; 
        }

        // I can pass in crop cordinates manually with the imageCropDimOverride 
        private void SetCropbox(CropCordinates CropDim, bool force_active = false)
        
        {
            //Remove cropbox if exists
            keyPicture2.RemoveAnnotation(1, 1, 1);
            if (activecropbox == false || force_active == true)
            {
                // Determine if the page is even or odd
                bool isEven = currentImageIndex % 2 == 0;
                actionStatus = $"Crop X:{CropDim.X1} Y:{CropDim.Y1} W:{CropDim.X2}, H:{CropDim.Y2}";
                activecropbox = true;
                keyPicture2.SetTransparentRect(CropDim.X1, CropDim.Y1, CropDim.X2, CropDim.Y2, 2);

                // This lets our actual crop work - if crop box is active 
                this.workingCropCordinates = CropDim;
            }

            else
            {
                DisplayImages("", ImagePairs[currentImageIndex].TIF, 2);
                activecropbox = false;
            }
            
            StatusUpdate();
        }

        private void AutosetCropbox(bool LocationOnly = false)
        {
            if (F2Settings.NegativeImage != true) { return; }

            // If LocationOnly == True & we Have X/Y values, shortcut slow code
            // Very sloppy needs to be redone. 
            if (LocationOnly && ImagePairs[currentImageIndex].BX1 > 0 && ImagePairs[currentImageIndex].BY1 > 0 && ImagePairs[currentImageIndex].BX2 > 0 && ImagePairs[currentImageIndex].BY2 > 0)

            {
                // Assuming CropLeftEven, CropTopEven, CropLeftOdd, CropTopOdd are class properties
                // and currentImageIndex helps to decide between Even and Odd
                bool isEven = currentImageIndex % 2 == 0;
                int newLeft = ImagePairs[currentImageIndex].BX1;
                int newTop = ImagePairs[currentImageIndex].BY1;
                int newRight = ImagePairs[currentImageIndex].BX2;
                int newBottom = ImagePairs[currentImageIndex].BY2;

                // Calculate new width and height based on BX1, BY1, BX2, BY2
                int newWidth = newRight - newLeft;
                int newHeight = newBottom - newTop;

                if (isEven)
                {
                    this.xCropCordinatesEven.X1 = newLeft - 80;
                    this.xCropCordinatesEven.Y1 = newTop - 80;
                    this.xCropCordinatesEven.X2 = newRight + 160;
                    this.xCropCordinatesEven.Y2 = newBottom + 160;
                    
                    
                    // Shouldn't need these anymore - leaving them because may save me a support programming session to leave till later
                    this.CropLeftEven = newLeft - 80;
                    this.CropTopEven = newTop - 80;
                    SetCropbox(xCropCordinatesEven); // Apply the new crop box settings
                }
                else
                {
                    this.xCropCordinatesOdd.X1 = newLeft - 80;
                    this.xCropCordinatesOdd.Y1 = newTop - 80;
                    this.xCropCordinatesOdd.X2 = newRight + 160;
                    this.xCropCordinatesOdd.Y2 = newBottom + 160;


                    // Shouldn't need these anymore - leaving them because may save me a support programming session to leave till later
                    this.CropLeftOdd = newLeft - 80;
                    this.CropTopOdd = newTop - 80; ;
                    SetCropbox(xCropCordinatesOdd); // Apply the new crop box settings
                }
                // Assuming CropWidth and CropHeight should be updated based on new values
                // Shouldn't need these 
                this.CropWidth = newWidth + 160;
                this.CropHeight = newHeight + 160;
                
                return; // Exit the method early as we only needed to update the crop box
            }

            if (F2Settings.NegativeImage != true) { return; }

            int ImageHandle = RavenImaging.ImgOpen(ImagePairs[currentImageIndex].JPG, 0);
            int TifHandle = RavenImaging.ImgOpen(ImagePairs[currentImageIndex].TIF, 0);

            int jpgWidth = RavenImaging.ImgGetWidth(ImageHandle);
            int jpgHeight = RavenImaging.ImgGetHeight(ImageHandle);
            int tifWidth = RavenImaging.ImgGetWidth(TifHandle);
            int tifHeight = RavenImaging.ImgGetHeight(TifHandle);

            if (jpgWidth != tifWidth || jpgHeight != tifHeight)
            {
                StatusUpdate("TIF & JPG Size Don't Match!");
                RavenImaging.ImgDelete(ImageHandle);
                RavenImaging.ImgDelete(TifHandle);
                return;
            }
            // Copied from photostat script
            // A is the over scan
            // B is the page border outside the photostat
            // C is the Photostat

            int CopyOfImage = RavenImaging.ImgDuplicate(ImageHandle);
            // stopwatch.Start();


            // RavenImaging.ImgAdaptiveThresholdAverage(CopyOfImage, 7, 7, -1, -1);
            // ImgAutoThreshold(_CurrentImage, 1);
            RavenImaging.ImgAutoThreshold(CopyOfImage, 1);
            // 
            // Round A: single LockBits+Copy for all 4 border calls (vs 4 separate copies)
            var aRes = RavenImaging.ImgFindBlackBorderBatch(CopyOfImage,
                new[] { (0, 90.0, 1), (2, 90.0, 1), (1, 90.0, 1), (3, 90.0, 1) });
            int aLeft = aRes[0]; int aTop = aRes[1]; int aRight = aRes[2]; int aBottom = aRes[3];

            if ((aLeft <= aRight) && (aTop <= aBottom))
            {
                RavenImaging.ImgCropBorder(CopyOfImage, aLeft, aTop, aRight, aBottom);

                //Copy of image has black interior photostat bitonal - we will invert this.
                RavenImaging.ImgInvert(CopyOfImage);

                // Round B: single LockBits+Copy for all 4 border calls
                var bRes = RavenImaging.ImgFindBlackBorderBatch(CopyOfImage,
                    new[] { (0, 99.0, 1), (2, 99.0, 1), (1, 99.0, 1), (3, 99.0, 1) });
                int bLeft = bRes[0]; int bTop = bRes[1]; int bRight = bRes[2]; int bBottom = bRes[3];

                bLeft = bLeft + 20;
                bRight = bRight - 20;

                if (bLeft <= bRight && bTop <= bBottom)
                {
                    RavenImaging.ImgCropBorder(CopyOfImage, bLeft, bTop, bRight, bBottom);

                    // Round C: single LockBits+Copy for all 4 border calls
                    var cRes = RavenImaging.ImgFindBlackBorderBatch(CopyOfImage,
                        new[] { (0, 80.0, 30), (2, 80.0, 100), (1, 80.0, 30), (3, 80.0, 100) });
                    int cLeft = cRes[0]; int cTop = cRes[1]; int cRight = cRes[2]; int cBottom = cRes[3];

                    // don't understand the math here but think works
                    int left = aLeft + (bLeft + 20) + cLeft;
                    int top = aTop + bTop + cTop;

                    int bottom = cBottom + aTop + bTop;
                    int right = cRight + aLeft + bLeft;

                    bool isEven = currentImageIndex % 2 == 0;

                    // This is for setting new cropbox size
                    if (LocationOnly == false)
                    {
                        this.CropHeight = bottom - top + 160;
                        this.CropWidth = right - left + 160;

                        if (isEven)
                        {
                            this.CropLeftEven = left - 80;
                            this.CropTopEven = top - 80;
                            xCropCordinatesEven.X1 = left - 80;
                            xCropCordinatesEven.Y1 = top - 80;
                            xCropCordinatesEven.X2 = right + 160;
                            xCropCordinatesEven.Y2 = bottom + 160;
                            SetCropbox(xCropCordinatesEven);
                        }
                        else
                        {
                            this.CropLeftOdd = left - 80;
                            this.CropTopOdd = top - 80;
                            xCropCordinatesOdd.X1 = left - 80;
                            xCropCordinatesOdd.Y1 = top - 80;
                            xCropCordinatesOdd.X2 = right + 160;
                            xCropCordinatesOdd.Y2 = bottom + 160;
                            SetCropbox(xCropCordinatesOdd);
                        }
                    }
                    else
                    {
                        // Check if the crop variables are set and valid
                        bool cropVariablesSet = isEven ? (this.CropLeftEven >= 0 && this.CropTopEven >= 0 && this.CropWidth > 0 && this.CropHeight > 0) : (this.CropLeftOdd >= 0 && this.CropTopOdd >= 0 && this.CropWidth > 0 && this.CropHeight > 0);

                        if (!cropVariablesSet)
                        {
                            // Crop variables are not properly set, exit the function
                            return;
                        }

                        // New detected box dimensions
                        int detectedWidth = right - left;
                        int detectedHeight = bottom - top;

                        // Calculate the new starting points for the cropbox (to center it)
                        // Adjust these starting points to center the existing cropbox within the new detected area
                        int newCropLeft = left + (detectedWidth - this.CropWidth) / 2;
                        int newCropTop = top + (detectedHeight - this.CropHeight) / 2;

                        // Set the new positions, ensuring they remain within image bounds
                        if (isEven)
                        {
                            xCropCordinatesEven.X1 = Math.Max(0, left - 80);
                            xCropCordinatesEven.Y1 = Math.Max(0, top - 80);
                            xCropCordinatesEven.Y2 = Math.Max(0, bottom + 160);
                            xCropCordinatesEven.X2 = Math.Max(0, right + 160);
                            SetCropbox(xCropCordinatesEven);
                            // Should be able to remove this leaving for now
                            this.CropLeftEven = Math.Max(0, Math.Min(newCropLeft, tifWidth - this.CropWidth));
                            this.CropTopEven = Math.Max(0, Math.Min(newCropTop, tifHeight - this.CropHeight));
                            
                        }
                        else
                        {
                            xCropCordinatesOdd.X1 = Math.Max(0, left - 80);
                            xCropCordinatesOdd.Y1 = Math.Max(0, top - 80);
                            xCropCordinatesOdd.Y2 = Math.Max(0, bottom + 160);
                            xCropCordinatesOdd.X2 = Math.Max(0, right + 160);
                            SetCropbox(xCropCordinatesOdd);
                            // Should be able to remove
                            this.CropLeftOdd = Math.Max(0, Math.Min(newCropLeft, tifWidth - this.CropWidth));
                            this.CropTopOdd = Math.Max(0, Math.Min(newCropTop, tifHeight - this.CropHeight));
                        }
                    }
                }

                
                RavenImaging.ImgDelete(ImageHandle);
                RavenImaging.ImgDelete(TifHandle);
                RavenImaging.ImgDelete(CopyOfImage);
            }
        }



        // Build Crop
        // Input Tif -> Saves to original image. Calling function saves / overwrites it. 

        // Eventually make work w JPG or TIF to crop JPGs (microfilm for missouri state archives etc)
        public void crop(string tifimage, int X1, int Y1, int X2, int Y2)
        {
            // Move auto crop logic out of this function
            
            // When cropping JPG, clear all checkpoints to avoid undo conflicts
            if (Special_CropJPG)
            {
                ClearCheckpoints();
            }
            else
            {
                CreateCheckpoint(tifimage);
            }

            // Open TIF if exists
            int TifHandle = File.Exists(tifimage) ? RavenImaging.ImgOpen(tifimage, 0) : 0;         

            // Perform the crop
            RavenImaging.ImgCropBorder(TifHandle, X1, Y1, X2, Y2);
            SaveImageVerified(TifHandle, tifimage);                
            RavenImaging.ImgDelete(TifHandle); // Clean up TIF image handle

            // Crop JPG if Special_CropJPG is enabled
            if (Special_CropJPG)
            {
                string jpgImage = Path.ChangeExtension(tifimage, ".jpg");
                if (File.Exists(jpgImage))
                {
                    int JpgHandle = RavenImaging.ImgOpen(jpgImage, 0);
                    if (JpgHandle != 0)
                    {
                        RavenImaging.ImgCropBorder(JpgHandle, X1, Y1, X2, Y2);
                        RavenImaging.ImgSaveAsJpg(JpgHandle, jpgImage, 80);
                        RavenImaging.ImgDelete(JpgHandle);
                    }
                }
            }
        
            // Refresh the displayed image - reload both TIF and JPG if CropJPG is enabled
            if (Special_CropJPG)
            {
                string jpgImage = Path.ChangeExtension(tifimage, ".jpg");
                DisplayImages(jpgImage, tifimage, 0, true);
            }
            else
            {
                DisplayImages(string.Empty, tifimage, 2, true);
            }
        }

        private void buttonx_Click(object sender, EventArgs e)
        {
            OpenSettings();
        }
        private void ThresholdMe(int mouseWheelDelta)
        {
            // The Settings_MouseWheel method already handles updating the values and UI
            // We just need to apply the current F2Settings after the UI has been updated
            if (ImagePairs.Count > currentImageIndex && thresholdSettingsForm?.currentlyFocusedControl?.Name.Contains("F2") == true)
            {
                var (jpgImage, tifImage, _, _, _, _) = ImagePairs[currentImageIndex];
                int _left = keyPicture2.GetSelectedLeft();
                int _top = keyPicture2.GetSelectedTop();
                int _right = keyPicture2.GetSelectedRight();
                int _bottom = keyPicture2.GetSelectedBottom();
                
                // Apply the threshold with current F2Settings (which were just updated by Settings_MouseWheel)
                ThresholdMe(F2Settings, _left, _top, _right, _bottom, jpgImage, jpgImage.ToLower().Replace(".jpg", ".tif"));
            }
        }

        public void ThresholdMe(ConversionSettings _ConversionSettings, int left = -1, int top = -1, int right = -1, int bottom = -1, string jpg ="", string tif = "", bool refreshscreen = true, bool Forcecacherefresh = true)
         {
            bool SBB = false;

            //The cache of the prev conversion worked - except that we were mixing photostat / non photostat settings
            //And it sometimes stores a negative color/jpg and inverts it or visa versa - would need to do testing to verify 
            //It was better to invert on the fly and saves significant time
            //For now defaulting forcecacherefresh

            // Set LastConversionSettings
            LastConversionSettings = new ConversionSettings();
            LastConversionSettings.CopyValuesFrom(_ConversionSettings); 

            // We update variables on paging - not here
            // thresholdSettingsForm.SetVariables(); 
            // Exit function if running. 


            if (isThresholdMeRunning == true) return;

            isThresholdMeRunning = true;

            //If overrides are set - we use those instead of selected area

            int _left = (left != -1) ? left : keyPicture2.GetSelectedLeft();
            int _top = (top != -1) ? top : keyPicture2.GetSelectedTop();
            int _right = (right != -1) ? right : keyPicture2.GetSelectedRight();
            int _bottom = (bottom != -1) ? bottom : keyPicture2.GetSelectedBottom();


            try
            {

                if (ImagePairs.Count > currentImageIndex)
                {
                    var (jpgImage, tifImage, _, _, _, _) = ImagePairs[currentImageIndex];
                    // Correctly access the current pair

                    // If overwrite is specified overwrite TIF & JPG
                    if (!string.IsNullOrEmpty(jpg))
                    {
                        jpgImage = jpg; 
                    }
                    if (!string.IsNullOrEmpty(tifImage))
                    {
                        tifImage = tif;
                    }

                    int contrast = 0;
                    int brightness = 0;
                    int despeckle = 0;
                    bool refinethreshold = false;
                    int tolerance = 0;
                    bool negative = false;

                    if (_ConversionSettings.Type == "Dynamic" || _ConversionSettings.Type == "RDynamic")
                    {
                        refinethreshold = false;
                        contrast = _ConversionSettings.Contrast;
                        brightness = _ConversionSettings.Brightness;
                        despeckle = _ConversionSettings.Despeckle;
                        negative = _ConversionSettings.NegativeImage;
                    }
                    else if (_ConversionSettings.Type == "Refine")
                    {
                        refinethreshold = true;
                        contrast = _ConversionSettings.Contrast + _ConversionSettings.FilterThresholdStepup;
                        brightness = _ConversionSettings.Brightness;
                        despeckle = _ConversionSettings.DespeckleFilter; 
                        tolerance = _ConversionSettings.Tolerance;
                        negative = _ConversionSettings.NegativeImage; 
                    }
                    else if (_ConversionSettings.Type == "ML1" || _ConversionSettings.Type == "ML2")
                    {
                        SBB = true;
                        string mlFile = jpgImage.ToUpper().Replace(".jpg", "." + _ConversionSettings.Type);
                        if (!File.Exists(mlFile))
                        {
                            MessageBox.Show($"No {_ConversionSettings.Type} File!");
                            return;
                        }                       
                    }
                  
                    // Call the threshold method with the current JPG image.
                    // This all really needs to be cleaned up - adding a third threshold type as a completely diff parameter (SBB) - which completely ignores all values and just looks for converted .sbb file. 

                    threshold(jpgImage, contrast, brightness, _left, _top, _right, _bottom, negative, refinethreshold, despeckle, tolerance, Forcecacherefresh, SBB, _ConversionSettings);


                    if (refreshscreen == true)
                    {
                        // Better than including in threshold
                        // If cropping JPG and area processing was done, reload both images
                        if (Special_CropJPG && _left < _right && _top < _bottom)
                        {
                            DisplayImages(jpgImage, jpgImage.ToLower().Replace(".jpg", ".tif"), 0, true);
                        }
                        else if (_ConversionSettings?.Type == "RDynamic" || _ConversionSettings?.Type == "Refine"
                            ? OpenThresholdBridge.TryGetDisplayPixels(out byte[] tifPixels, out int tifW, out int tifH)
                            : false)
                        {
                            // Display from cached pixels — skip disk read + Group 4 decode
                            keyPicture2.LoadFromGrayscalePixels(tifPixels, tifW, tifH);
                            if (OpenThresholdBridge.IsSaveInProgress)
                                keyPicture2.ShowSaving();
                        }
                        else
                        {
                            // Fallback to disk reload
                            DisplayImages(string.Empty, jpgImage.ToLower().Replace(".jpg", ".tif"), 2, true);
                            keyPicture2.ShowFileOk();
                        }

                        // Re-select
                        if (_right > 0 && _bottom > 0)
                        {
                            keyPicture2.SetSelectedImageArea(_left, _top, _right, _bottom);
                        }
                    }

                    string timePart = _lastThresholdDetail != null ? $" | {_lastThresholdDetail}" : "";
                    _lastThresholdDetail = null;

                    if (refinethreshold == true)
                    {
                        actionStatus = $"Refine: C{contrast} B{brightness} D{despeckle} T{tolerance}{timePart}";
                    }
                    else
                    {
                        actionStatus = $"{_ConversionSettings?.Type ?? "Dynamic"}: C{contrast} B{brightness} D{despeckle}{timePart}";
                    }
                    StatusUpdate();
                }
                else
                {
                    MessageBox.Show("Invalid image selection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                isThresholdMeRunning = false;
            }

        }


        private void button5_Click(object sender, EventArgs e)
        {
            SaveCurrentPageSettings();
            ClearJPGCache();
            ClearCheckpoints();

            //Need to test the text file to verify Undo, Next / Prev / Jump image works
            if (Path.GetExtension(textBox1.Text).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                // If it's a text file, load image pairs from the text file
                LoadImagePairsFromTextFile(textBox1.Text);
            }
            else
            {
                string directoryPath = textBox1.Text;
                LoadImagePairsFromDirectory(directoryPath);
            }

            if (Special_DrawLineMode)
            {
                UpdateLinePositionFromTag();
                DrawTop();
            }


        }


        private void LoadImagePairsFromTextFile(string filePath)
        {

            this.ImageSource = ImageSourceType.Text;
            // Clears the existing list of image pairs
            ImagePairs.Clear();
            // Resets the index for the current image to 0
            currentImageIndex = 0;

            // Checks if the text file exists at the provided file path
            if (!File.Exists(filePath))
            {
                // Displays a message box indicating that the file does not exist
                MessageBox.Show($"File does not exist: {filePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return; // Exits the function if the file does not exist
            }

            // Reads all lines from the file into an array, each line presumably containing one TIF file path
            var lines = File.ReadAllLines(filePath);

            // Get rid of \\ & " - makes file compatible w sqlexport
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Replace(@"\\", @"\").Trim().Trim('"');
            }


            foreach (var line in lines) // Loops through each line in the text file
            {

                var tifFile = line.Trim(); // Trims whitespace from the file path

                // Checks if the file exists and is a TIF file
                if (File.Exists(tifFile) && Path.GetExtension(tifFile).Equals(".tif", StringComparison.OrdinalIgnoreCase))
                {
                    var jpgFile = Path.ChangeExtension(tifFile, ".jpg"); // Changes the extension of the TIF file to JPG

                    // Checks if the corresponding JPG file exists
                    if (File.Exists(jpgFile))
                    {
                        // Adds the JPG and TIF file pair along with placeholder values for coordinates to the list
                        ImagePairs.Add((jpgFile, tifFile, 0, 0, 0, 0));
                    }
                }
            }

            // Checks if any image pairs were added to the list
            if (ImagePairs.Any())
            {
                // Loads the first image pair to display
                string titleUpdate = ImagePairs[currentImageIndex].JPG; // Prepares the title update with the current JPG file name
                DisplayImages(ImagePairs[currentImageIndex].JPG, ImagePairs[currentImageIndex].TIF, 0, true); // Displays the images from the first pair
            }
            else
            {
                // Displays a message box if no valid image pairs were found
                MessageBox.Show("No matching TIF and JPG image pairs found.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void LoadImagePairsFromDirectory(string directoryPath)
        {
            this.ImageSource = ImageSourceType.Directory;
            ImagePairs.Clear();
            currentImageIndex = 0; 

            // Ensure the directory exists
            if (!Directory.Exists(directoryPath))
            {
                MessageBox.Show($"Directory does not exist: {directoryPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Get all JPG files in the directory and order them by name
            var jpgFiles = Directory.GetFiles(directoryPath, "*.jpg", SearchOption.TopDirectoryOnly)
                                    .OrderBy(file => file) // This orders the files alphabetically by their full path
                                    .ToArray();

            foreach (var jpgFile in jpgFiles)
            {
                var tifFile = Path.ChangeExtension(jpgFile, ".tif");

                // Check if the corresponding TIF file exists
                if (File.Exists(tifFile))
                {
                    ImagePairs.Add((jpgFile, tifFile, 0, 0, 0, 0));
                }
            }

            // Update UI or logic to reflect the loaded images
            if (ImagePairs.Any())
            {
                // Load the first pair of images
                string TitleUpdate = ImagePairs[currentImageIndex].JPG;
                DisplayImages(ImagePairs[currentImageIndex].JPG, ImagePairs[currentImageIndex].TIF, 0, true);
            }
            else
            {
                MessageBox.Show("No matching JPG and TIF image pairs found.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public void RefreshJPGVisibility()
        {
            // Clear image if we are not showing JPG / reload jpg if we are showing JPG
            if (isJPGVisible == false)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string blankJPG = Path.Combine(baseDir, "BlankJPG.jpg");

                DisplayImages(blankJPG, "", 1);
            }
            else
            { DisplayImages(ImagePairs[currentImageIndex].JPG, ImagePairs[currentImageIndex].TIF); }
        }

        // Legacy code that may need to be cleaned up - removing keyPicture1 events - but may want to refresh the JPG in some instances while not supporting event functions (IE - Crop = Crop JPG too)

        // Try double clicking after doing an area threshold to get back to main image - then it tries to zoomtolast and breaks. 
        private void DisplayImages(string jpgFile, string tifFile, int imageType = 0, bool zoomToLast = false)
        {
            int currentLoadVersion = ++loadVersion;
            switch (imageType)
            {
                case 1: // Load only the JPG image - hacky way of including BlankJPG to clear last one. 
                    if (isJPGVisible || Path.GetFileName(jpgFile) == "BlankJPG.jpg")
                    {
                        if (zoomToLast)
                        {
                            // Disabling this on 20240926 - don't understand why I need and this may make things a bit faster.
                            
                            int leftZoom = keyPicture1.GetZoomLeft();
                            int topZoom = keyPicture1.GetZoomTop();
                            int rightZoom = keyPicture1.GetZoomRight();
                            int bottomZoom = keyPicture1.GetZoomBottom();
                            keyPicture1.Load_Image(jpgFile);
                            
                            if (rightZoom > 0 && bottomZoom > 0 && rightZoom > leftZoom && bottomZoom > topZoom)
                            {
                                keyPicture1.ZoomPanImage1(leftZoom, topZoom, rightZoom, bottomZoom);
                            }
                             
                        }
                        else
                        {
                            
                            keyPicture1.Load_Image(jpgFile);
                             

                        }
                    }
                    break;
                case 2: // Load only the TIF image
                    if (zoomToLast)
                    {
                        int leftZoom = keyPicture2.GetZoomLeft();
                        int topZoom = keyPicture2.GetZoomTop();
                        int rightZoom = keyPicture2.GetZoomRight();
                        int bottomZoom = keyPicture2.GetZoomBottom();

                        keyPicture2.Load_Image(tifFile);

                        // && rightZoom > leftZoom && bottomZoom > topZoom
                        // This fixed bug where wasn't zooming in right when re-zooming (I think)
                        if (rightZoom > 0 && bottomZoom > 0 )
                        {
                            keyPicture2.ZoomPanImage1(leftZoom, topZoom, rightZoom, bottomZoom);
                        }



                    }
                    else
                    {
                        keyPicture2.Load_Image(tifFile);
                    }
                    break;
                case 0: // Load both images
                default:
                    if (zoomToLast)
                    {
                        int leftZoom = keyPicture2.GetZoomLeft();
                        int topZoom = keyPicture2.GetZoomTop();
                        int rightZoom = keyPicture2.GetZoomRight();
                        int bottomZoom = keyPicture2.GetZoomBottom();
                        

                        if (isJPGVisible)
                        {
                            keyPicture1.Load_Image(jpgFile);
                            if (rightZoom > 0 && bottomZoom > 0)
                            {
                                keyPicture1.ZoomPanImage1(leftZoom, topZoom, rightZoom, bottomZoom);
                            }
                        }
                        keyPicture2.Load_Image(tifFile);
                        if (rightZoom > 0 && bottomZoom > 0)
                        {
                            keyPicture2.ZoomPanImage1(leftZoom, topZoom, rightZoom, bottomZoom);
                        }
                    }
                    else
                    {
                        keyPicture2.Load_Image(tifFile);
                        if (isJPGVisible)
                        {
                            keyPicture1.Load_Image(jpgFile);
                        }

                    }
                    
                    break;
            }

            // Specific to Canadian of rotate every other image
            if (Special_CanadianSetLine && currentImageIndex % 2 == 1)
            {
                keyPicture2.RotateImage(2);
            }

            StatusUpdate();
            // keyPicture2.SetWindowColorBorder(2, 50);
            // keyPicture2.ResetZoom(0);
        }


        public void BatchWhiteoutActivateToggle()
        {
            // BatchWhiteoutActivate just activates the stuff on the screen (pressing E)
            // To clear it - we push "5" / call BatchWhiteoutStatusToggle
            // Need to build this function 
            // keyPicture2.ClearAnnotations(); // Assuming such a method exists

            if (this.BatchWhiteoutActive == true) 
            { 
                this.BatchWhiteoutActive = false;
                keyPicture2.RemoveAnnotation(1, 1, 1);
                RefreshDisplayImage();
            }
            else {  this.BatchWhiteoutActive = true; BatchWhiteoutDraw(); }

            
        }

        public void BatchWhiteoutlistClear()
        {
            
            vOddWhiteoutList.Clear();
            vEvenWhiteoutList.Clear();

            this.modeStatus = $"Batch Whiteout: {this.BatchWhiteoutMode.ToString()}";
        }

        public void BatchWhiteoutDraw()
        {
            keyPicture2.RemoveAnnotation(1, 1, 1);

            RefreshDisplayImage();

            bool isEven = currentImageIndex % 2 == 0;

            List<Tuple<int, int, int, int>> vWorkingList;

            if (isEven)
            {
                 vWorkingList = vEvenWhiteoutList; 
            }
            else
            {
                vWorkingList = vOddWhiteoutList; 
            }

            foreach (var box in vWorkingList)
            {
                int x1 = box.Item1;
                int y1 = box.Item2;
                int x2 = box.Item3;
                int y2 = box.Item4;

                
                keyPicture2.SetTransparentRectNoRef(x1, y1, x2, y2, 2);
            }

            keyPicture2.SetTransparentRectNoRef_Refresh(); 


        }

        private void DrawLineStatusToggle()
        {
            // Toggle Special_DrawLineMode
            Special_DrawLineMode = !Special_DrawLineMode;

            if (Special_DrawLineMode)
            {
                // Try to load the existing value from the metadata
                string tiffPath = ImagePairs[currentImageIndex].TIF; // Current TIFF file path
                string metadataPath = Path.ChangeExtension(tiffPath, ".linepos.json");

                if (File.Exists(metadataPath))
                {
                    try
                    {
                        // Deserialize the JSON file to get the line position
                        string json = File.ReadAllText(metadataPath);
                        var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);

                        if (metadata != null && metadata.ContainsKey("LinePosition"))
                        {
                            Special_DrawLineValue = metadata["LinePosition"];
                            // Use the loaded line position value
                        }
                    }
                    catch (Exception ex)
                    {
                        // You may log this exception if you have a logging mechanism in your application.
                    }
                }

                // Redraw the line with the loaded position
                DrawTop();
            }
            else
            {
                // Logic to handle disabling the draw line mode if needed
                keyPicture2.RemoveAnnotation(1, 1, 1);  // Clear any existing line
                RefreshDisplayImage();
            }

            // Update the status like the original code
            this.modeStatus = $"DrawLineTopMode: {this.Special_DrawLineMode}";
            StatusUpdate();
        }


        private void DrawTop()
        {
            int _toploc = (int)(Special_DrawLineValue * 300);
            var (imageWidth, _) = GetDimensions(this.ImagePairs[currentImageIndex].TIF);
            keyPicture2.RemoveAnnotation(1, 1, 1);

            // Check if this position came from TIF tag
            decimal? savedPosition = ReadLinePositionTag(ImagePairs[currentImageIndex].TIF);

            // Use different color parameter based on source
            // Assuming SetTransparentRect accepts a color parameter, you may need to modify this
            int colorParam = (savedPosition.HasValue && savedPosition.Value == Special_DrawLineValue) ? 2 : 3;

            keyPicture2.SetTransparentRect(0, _toploc, imageWidth, _toploc + 5, colorParam);
        }

        public void WriteLinePositionTag(string tiffPath, decimal position)
        {
            try
            {
                // Create metadata file path by replacing .tif with .linepos.json
                string metadataPath = Path.ChangeExtension(tiffPath, ".linepos.json");

                // Create simple object to serialize
                var metadata = new { LinePosition = position };

                // Write to JSON file
                string json = System.Text.Json.JsonSerializer.Serialize(metadata);
                File.WriteAllText(metadataPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing line position: {ex.Message}");
            }
        }



        public decimal? ReadLinePositionTag(string tiffPath)
        {
            try
            {
                string metadataPath = Path.ChangeExtension(tiffPath, ".linepos.json");
                if (!File.Exists(metadataPath))
                    return null;

                string json = File.ReadAllText(metadataPath);
                var metadata = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                return metadata.GetProperty("LinePosition").GetDecimal();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading line position: {ex.Message}");
                return null;
            }
        }


        public void BatchWhiteoutStatusToggle()
        {
            if (this.BatchWhiteoutMode == true)
            {
                this.BatchWhiteoutMode = false;
                BatchWhiteoutlistClear();
                BatchWhiteoutDraw();
            }
            else
            {
                this.BatchWhiteoutMode = true;
            }
            this.modeStatus = $"Batch Whiteout: {this.BatchWhiteoutMode.ToString()}";
            StatusUpdate();
        }

        // Add to the whiteout list (having whiteout on - drawing without bulkprep being ready to whiteout. 
        public void BatchWhiteoutAdd(int X1, int Y1, int X2, int Y2)
        {
            bool isEven = currentImageIndex % 2 == 0;
            List<Tuple<int, int, int, int>> vWorkingList;

            if (isEven)  
            {
                vWorkingList = vEvenWhiteoutList;
            }
            else
            {
                vWorkingList = vOddWhiteoutList;
            }

            vWorkingList.Add(Tuple.Create(X1, Y1, X2, Y2));


            string result = string.Join("/", vWorkingList.Select(t => $"{t.Item1}:{t.Item2}:{t.Item3}:{t.Item4}"));
            this.modeStatus = $"Batch Whiteout: {result}";
            // Should be done once per box vs redrawing all - but have to refresh image each time so doubt it makes difference
            BatchWhiteoutDraw(); 
            // public List<Tuple<int, int, int, int>> vWhiteoutList { get; private set; } = new List<Tuple<int, int, int, int>>();
        }


        public void BatchWhiteout(string tifimage)
        {
            CreateCheckpoint(tifimage);

            bool isEven = currentImageIndex % 2 == 0;
            List<Tuple<int, int, int, int>> vWorkingList;

            if (isEven)
            {
                vWorkingList = vEvenWhiteoutList;
            }
            else
            {
                vWorkingList = vOddWhiteoutList;
            }

            var (width, height) = GetDimensions(tifimage); 


            foreach (var box in vWorkingList)
            {
                int x1 = box.Item1;
                int y1 = box.Item2;
                int x2 = box.Item3;
                int y2 = box.Item4;

                // Sometimes this box can be off the image - if I am going to a cropped image. 


                // Only run if starting point is on image somewhere
                if ((x1<width || y1<height) && x1 < x2 && y1 < y2)
                {                   
                    //Set end point to width / height if < image width / height
                    if (x2>width) 
                    { 
                        x2 = width; 
                    }
                    if (y2>height) 
                    { 
                        y2 = height; 
                    }

                    string TempFile = SaveTemp(tifimage); 

                    // HORRIBLE - should replace this w new library if it's faster - prob is. Going overwrite file EACH time
                    RavenImaging.EraseIn(tifimage, TempFile, ((short)x1), ((short)y1), ((short)x2), ((short)y2));

                    if (VerifyDelete(tifimage))
                    {
                        File.Copy(TempFile, tifimage, true);
                    }                   
                }

                // keyPicture2.SetTransparentRect(x1, y1, x2, y2, 2);
            }

            keyPicture2.RemoveAnnotation(1, 1, 1);
            RefreshDisplayImage();

        }

        public void whiteout(string tifimage, int X1, int Y1, int X2, int Y2, bool AddRegionBatchWhiteout = false, bool EraseOut = false)
        {
            

            if (X1 >= X2 || Y1 >= Y2)
            {
                return;
            }


            if (!File.Exists(tifimage))
            {
                return;
            }

            CreateCheckpoint(tifimage);

            // Calculate width and height
            int width = X2 - X1;
            int height = Y2 - Y1;

            // Corrected call with width and height
            // RavenImaging.EraseIn(tifimage, tifimage + ".tif", ((short)X1), ((short)Y1), ((short)width), ((short)height));
            
            // May need to do a dedicated filereplace

            string TempFile = SaveTemp(tifimage);

            if (EraseOut == true)
            {
                RavenImaging.EraseOut(tifimage, TempFile, ((short)X1), ((short)Y1), ((short)X2), ((short)Y2));
            }
            else
            {
                RavenImaging.EraseIn(tifimage, TempFile, ((short)X1), ((short)Y1), ((short)X2), ((short)Y2));
            }
            if (VerifyDelete(tifimage))
            {
                File.Copy(TempFile, tifimage, true);
            }

            // If we are doing a batch whiteout - we add it to the list
            
            DisplayImages(string.Empty, tifimage, 2, true);

            // Need to make this only happen on the first image. But I don't know how. 
            if (BatchWhiteoutMode == true && AddRegionBatchWhiteout == true)
            {
                BatchWhiteoutAdd(X1, Y1, X2, Y2);

            }
        }

        // Saves a file to a temp file, returns tempfile name
        public string SaveTemp(string filename)
        {
            // Get the current application directory
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // Construct the temporary file path
            string tempFileName = Path.GetFileNameWithoutExtension(filename) + ".tmp";
            string tempFilePath = Path.Combine(appDirectory, tempFileName);

            // Delete the existing temporary file if it exists
            if (!VerifyDelete(tempFilePath))
            {
                // Handle the case where the file cannot be deleted, maybe return null or throw an exception
                MessageBox.Show("Failed to clean up old temp file.");
                return null;
            }

            // Assuming you want to copy the original file to a temp file
            try
            {
                File.Copy(Path.Combine(appDirectory, filename), tempFilePath, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating temp file: {ex.Message}");
                return null;
            }

            // Return the path of the temp file
            return tempFilePath;
        }



        public void toggle(string tifimage, int X1, int Y1, int X2, int Y2)
        {
            // Leave function unless a box is provided where Right > Left & Bottom > Top
            if (X1 >= X2 || Y1 >= Y2)
            {
                return;
            }

            int TifHandle = File.Exists(tifimage) ? RavenImaging.ImgOpen(tifimage, 0) : 0; // Open TIF if exists

            // If Tifhandle not loaded, exit function
            if (TifHandle <= 0)
            {
                return;
            }
            CreateCheckpoint(tifimage);
            // Calculate width and height
            int width = X2 - X1;
            int height = Y2 - Y1;

            // Corrected call with width and height
            int TifHandlePartial = RavenImaging.ImgCopy(TifHandle, X1, Y1, X2, Y2);
            RavenImaging.ImgInvert(TifHandlePartial);
            RavenImaging.ImgAddCopy(TifHandle, TifHandlePartial, X1, Y1);
            if (TifHandlePartial != 0) RavenImaging.ImgDelete(TifHandlePartial); // Clean up TIF image handle if it was used 

            // Save the final thresholded TIF image
            SaveImageVerified(TifHandle, tifimage); 

            if (TifHandle != 0) RavenImaging.ImgDelete(TifHandle); // Clean up TIF image handle if it was used

            DisplayImages(string.Empty, tifimage, 2, true);

            keyPicture2.SetSelectedImageArea(X1, Y1, X2, Y2);
        }


        private (int Width, int Height) GetDimensions(string filePath, int page = 1)
        {
            int width = 0;
            int height = 0;

            // Assume RavenImaging.GetImageInfo is a method that sets the width and height based on the image file
            RavenImaging.GetImageInfo(filePath, page, ref width, ref height);

            return (width, height);
        }

        private bool VerifyMatchingDimensions(string _1, string _2)
        {
            var (_1width, _1height) = GetDimensions(_1);
            var (_2width, _2height) = GetDimensions(_2);

            if (_1width == _2width && _1height == _2height) { return true; } else { return false; }
        }

        private void button1_Click_2(object sender, EventArgs e)
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;

            string message = $"Base Directory:\n{exeDirectory}\n\n" +
                             $"Executable Path:\n{exePath}\n\n" +
                             $"Assembly Location:\n{assemblyLocation}";

            MessageBox.Show(message, "Executable Information", MessageBoxButtons.OK, MessageBoxIcon.Information);


            // deskew(ImagePairs[currentImageIndex].TIF);

        }




        // Before threshold mod
        // Threshold my actual JPG -> Output to TIF I SURE WISH I COULD USE LICENSED SOFTWARE
        public void threshold(string inputJPG, int contrast, int brightness, int X1, int Y1, int X2, int Y2, bool NegativeImage = false, bool RefineThreshold = false, int despeckle = 0, int refinetolerance = 10, bool ForceClearCache = false, bool SBB = false, ConversionSettings conversionSettings = null)
        {
            // Cancel background image prefetch to free CPU cores for thresholding
            RavenPictureBox.CancelAllPrefetch();

            // Ensure any previous background TIF save finishes before we touch the same .tmp file
            OpenThresholdBridge.WaitForPendingSave();

            if (ForceClearCache == true)
            {
                ClearJPGCache();
            }

            inputJPG = inputJPG.ToLower();
            
            // Used to store SBB of converted TIF file from SBB conv
            string inputSBB = inputJPG.ToUpper().Replace(".JPG", "." + conversionSettings.Type.ToUpper());

            string outputTIF = inputJPG.Replace(".jpg", ".tif"); // Ensure the TIF version is the one we're working with
            string TempTifFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thresholded.tif");
            string TempPartialTifFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thresholded_Partial.tif");


            // Fast path: full non-negative conversion for RDynamic/Dynamic/Refine
            // Skips ImgOpen/ImgDuplicate — the byte-array pipeline doesn't need GDI+ handles
            if (X1 <= 0 && Y1 <= 0 && X2 <= 0 && Y2 <= 0
                && !NegativeImage && !SBB
                && (conversionSettings?.Type == "RDynamic" || conversionSettings?.Type == "Dynamic" || conversionSettings?.Type == "Refine"))
            {
                CreateCheckpoint(outputTIF);
                int dynDespeckle = despeckle;
                OpenThresholdBridge.ApplyThresholdToFile(inputJPG, outputTIF, 7, 7, contrast, brightness,
                    RefineThreshold, refinetolerance, dynDespeckle);
                OpenThresholdBridge.WaitForPendingSave();
                long total = OpenThresholdBridge.LastDecodeMs + OpenThresholdBridge.LastThresholdMs + Math.Max(0, OpenThresholdBridge.LastWriteMs);
                string bits = Environment.Is64BitProcess ? "64-bit" : "32-bit";
                _lastThresholdDetail = $"{total}ms {bits} (decode:{OpenThresholdBridge.LastDecodeMs} thresh:{OpenThresholdBridge.LastThresholdMs} save:{OpenThresholdBridge.LastWriteMs})";
                return;
            }

            // Fast path: full photostat (negative) conversion for RDynamic/Dynamic
            // Byte-array pipeline with parallel AT — no GDI+ handles
            if (X1 <= 0 && Y1 <= 0 && X2 <= 0 && Y2 <= 0
                && NegativeImage && !SBB
                && (conversionSettings?.Type == "RDynamic" || conversionSettings?.Type == "Dynamic"))
            {
                CreateCheckpoint(outputTIF);
                OpenThresholdBridge.ApplyThresholdToFilePhotostat(inputJPG, outputTIF, 7, 7, contrast, brightness, despeckle);
                OpenThresholdBridge.WaitForPendingSave();
                long total = OpenThresholdBridge.LastDecodeMs + OpenThresholdBridge.LastThresholdMs + Math.Max(0, OpenThresholdBridge.LastWriteMs);
                string bits = Environment.Is64BitProcess ? "64-bit" : "32-bit";
                _lastThresholdDetail = $"{total}ms {bits} (photostat decode:{OpenThresholdBridge.LastDecodeMs} thresh:{OpenThresholdBridge.LastThresholdMs} save:{OpenThresholdBridge.LastWriteMs})";
                return;
            }

            int ImageHandle = 0;

            if (!SBB)
            {
                // Is this image loaded / cached already? (We will delete it on "Next Image"
                if (CachedJPG == 0 || ForceClearCache == true)
                {
                    ImageHandle = RavenImaging.ImgOpen(inputJPG, 0); // Open original image
                    CachedJPG = ImageHandle; // Save the handle for later.
                }
                else
                {
                    ImageHandle = CachedJPG;
                }
            }
            else
            {
                ImageHandle = RavenImaging.ImgOpen(inputJPG, 0); // Open original image
                CachedJPG = ImageHandle; // Save the handle for later.

            }

            // Think I can get rid of this in some cases (full conversion) since I do my width / height w phreview
            int TifHandle = File.Exists(outputTIF) ? RavenImaging.ImgOpen(outputTIF, 0) : 0; // Open TIF if exists (copies area into the TIF if area)
            int tImageHandle = 0;

            // Full conversion
            if (X1 <= 0 && Y1 <= 0 && X2 <= 0 && Y2 <= 0)
            {
                CreateCheckpoint(outputTIF);

                // If isNegative set (Photostat) & full conversion run this script to deal with borders.

                // Using tImageHandle to save original to keep
                tImageHandle = RavenImaging.ImgDuplicate(ImageHandle);

                // Full + Photostat + Non SBB Thresholding
                if (NegativeImage == true && SBB == false)
                {
                    StatusUpdate("Photostat Threshold - please wait ... ");

                    // Refine threshold not supported for full photostat conversions (same as IET)
                    if (conversionSettings?.Type == "Refine")
                    {
                        MessageBox.Show("Refine Threshold is not supported for full photostat conversions.");
                        if (tImageHandle != 0) { RavenImaging.ImgDelete(tImageHandle); tImageHandle = 0; }
                        if (TifHandle    != 0) { RavenImaging.ImgDelete(TifHandle);    TifHandle    = 0; }
                        return;
                    }

                    int CopyOfImage = RavenImaging.ImgDuplicate(tImageHandle);
                    
                    RavenImaging.ImgAutoThreshold(CopyOfImage, 2);

                    // Round A: single LockBits+Copy for all 4 border calls
                    var aRes = RavenImaging.ImgFindBlackBorderBatch(CopyOfImage,
                        new[] { (0, 90.0, 1), (2, 90.0, 1), (1, 90.0, 1), (3, 90.0, 1) });
                    int aLeft = aRes[0]; int aTop = aRes[1]; int aRight = aRes[2]; int aBottom = aRes[3];

                    if ((aLeft <= aRight) && (aTop <= aBottom))
                    {
                        RavenImaging.ImgCropBorder(CopyOfImage, aLeft, aTop, aRight, aBottom);

                        //Copy of image has black interior photostat bitonal - we will invert this.
                        RavenImaging.ImgInvert(CopyOfImage);

                        // Round B: single LockBits+Copy for all 4 border calls
                        var bRes = RavenImaging.ImgFindBlackBorderBatch(CopyOfImage,
                            new[] { (0, 99.0, 1), (2, 99.0, 1), (1, 99.0, 1), (3, 99.0, 1) });
                        int bLeft = bRes[0]; int bTop = bRes[1]; int bRight = bRes[2]; int bBottom = bRes[3];

                        bLeft = bLeft + 20;
                        bRight = bRight - 20;

                        if (bLeft <= bRight && bTop <= bBottom)
                        {
                            RavenImaging.ImgCropBorder(CopyOfImage, bLeft, bTop, bRight, bBottom);

                            // Round C: single LockBits+Copy for all 4 border calls
                            var cRes = RavenImaging.ImgFindBlackBorderBatch(CopyOfImage,
                                new[] { (0, 80.0, 30), (2, 80.0, 100), (1, 80.0, 30), (3, 80.0, 100) });
                            int cLeft = cRes[0]; int cTop = cRes[1]; int cRight = cRes[2]; int cBottom = cRes[3];

                            RavenImaging.ImgDelete(CopyOfImage);

                            RavenImaging.ImgRemoveBleedThrough(tImageHandle, 1);

                            int Copy1 = 0;
                            int Copy2 = 0;

                            if (aLeft <= aRight && aTop <= aBottom)
                            {
                                Copy1 = RavenImaging.ImgCopy(tImageHandle, aLeft, aTop, aRight, aBottom);
                            }

                            //Remove border of the paper itself that is clean
                            if (bLeft <= bRight && bTop <= bBottom && Copy1 > 0)
                            {
                                Copy2 = RavenImaging.ImgCopy(Copy1, bLeft, bTop, bRight, bBottom);

                            }

                            //Get real image
                            int Photostat = RavenImaging.ImgCopy(Copy2, cLeft, cTop, cRight, cBottom);

                            RavenImaging.ImgInvert(Photostat);

                            // Refine threshold supported. 

                            if (RefineThreshold == true)
                            {
                                MessageBox.Show("Refine Threshold not supported for full image conversions.");                              
                            }
                            else
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                if (conversionSettings?.Type == "RDynamic")
                                    Photostat = OpenThresholdBridge.ApplyThreshold(Photostat, 7, 7, contrast, brightness);
                                else
                                    RavenImaging.ImgDynamicThresholdAverage(Photostat, 7, 7, contrast, brightness);
                                sw.Stop();
                                StatusUpdate($" | Threshold: {sw.ElapsedMilliseconds}ms ({conversionSettings?.Type})");
                            }

                            if (despeckle > 0 && RefineThreshold == false)
                            {
                                RavenImaging.ImgDespeckle(Photostat, despeckle, despeckle);
                            }

                            RavenImaging.ImgRemoveBlackWires(Photostat);

                            int PhHeight = RavenImaging.ImgGetHeight(Photostat) - 10;
                            int PhRatio = PhHeight / 5;
                            int phBreaks = PhHeight - 15000;

                            RavenImaging.ImgRemoveVerticalLines(Photostat, PhHeight, phBreaks, PhRatio, false, true);

                            RavenImaging.ImgAdaptiveThresholdAverage(tImageHandle, 7, 7, -1, -1);
                            RavenImaging.ImgAdaptiveThresholdAverage(Copy1, 7, 7, 40, 230);
                            RavenImaging.ImgAdaptiveThresholdAverage(Copy2, 7, 7, 40, 230);

                            // Put photostat back in 

                            RavenImaging.ImgAddCopy(Copy2, Photostat, cLeft, cTop);
                            RavenImaging.ImgAddCopy(Copy1, Copy2, bLeft, bTop);
                            RavenImaging.ImgAddCopy(tImageHandle, Copy1, aLeft, aTop);

                            RavenImaging.ImgDelete(Photostat);
                            RavenImaging.ImgDelete(Copy2);
                            RavenImaging.ImgDelete(Copy1);

                            StatusUpdate();
                        }
                    }
                }

                // Set Tif handle which will get saved as the thresholded image handle.

                // If no SBB - we set tImageHandle to our thresholded full image
                if (SBB == false) { TifHandle = RavenImaging.ImgCopy(tImageHandle, 0, 0, 0, 0); }

                if (SBB == true) 
                { 
                  if (!IsValidTif(inputSBB))
                    {
                        return;
                    }


                    try
                    {
                        // Skip over corrupted .ml1 files
                        TifHandle = RavenImaging.ImgOpen(inputSBB, 0);
                        if (NegativeImage == true)
                        {
                            RavenImaging.ImgInvert(TifHandle);
                        }
                    }
                    catch
                    {
                        return; 
                    }

                }
            }

            // If specific area is provided, threshold that area
            if (X1 < X2 && Y1 < Y2 && TifHandle != 0 && !SBB)
            {
                // Verify dimensions match
                if (!VerifyMatchingDimensions(outputTIF, inputJPG) || IsFineRotated(outputTIF))
                {
                    MessageBox.Show("JPG & TIF Image Dimensions don't match!");
                    ClearJPGCache();
                    RavenImaging.ImgDelete(TifHandle);

                    return;
                }
                // Create Checkpoin
                CreateCheckpoint(outputTIF);

                // Pure C# partial path — read JPG, crop, threshold, write TIF directly (RDynamic + Dynamic + Refine)
                if (conversionSettings?.Type == "RDynamic" || conversionSettings?.Type == "Dynamic" || conversionSettings?.Type == "Refine")
                {
                    if (NegativeImage)
                        OpenThresholdBridge.ApplyThresholdToFilePartialNegative(inputJPG, outputTIF,
                            X1, Y1, X2, Y2, 7, 7, contrast, brightness,
                            RefineThreshold, refinetolerance, despeckle);
                    else
                        OpenThresholdBridge.ApplyThresholdToFilePartial(inputJPG, outputTIF,
                            X1, Y1, X2, Y2, 7, 7, contrast, brightness,
                            RefineThreshold, refinetolerance, despeckle);
                    OpenThresholdBridge.WaitForPendingSave();
                    {
                        long total = OpenThresholdBridge.LastDecodeMs + OpenThresholdBridge.LastThresholdMs + Math.Max(0, OpenThresholdBridge.LastWriteMs);
                        string bits = Environment.Is64BitProcess ? "64-bit" : "32-bit";
                        _lastThresholdDetail = $"{total}ms {bits} (decode:{OpenThresholdBridge.LastDecodeMs} thresh:{OpenThresholdBridge.LastThresholdMs} save:{OpenThresholdBridge.LastWriteMs})";
                    }

                    if (tImageHandle != 0) { RavenImaging.ImgDelete(tImageHandle); tImageHandle = 0; }
                    if (TifHandle != 0) { RavenImaging.ImgDelete(TifHandle); TifHandle = 0; }
                    return;
                }

            }

            // Note: Removed JPG cropping during threshold operation
            // JPG should only be cropped during explicit crop operations, not during thresholding

            int sbbFull = 0;
            int sbbPartial = 0; 
            if (SBB == true)
            {
                if (!IsValidTif(inputSBB))
                {
                    return; 
                }

                sbbFull = RavenImaging.ImgOpen(inputSBB, 0);
                sbbPartial = RavenImaging.ImgCopy(sbbFull, X1, Y1, X2, Y2);
                
                if (NegativeImage == true)
                {
                    RavenImaging.ImgInvert(sbbPartial);
                }
                RavenImaging.ImgAddCopy(TifHandle, sbbPartial, X1, Y1);                

            }

            // Save the final thresholded TIF image
            {
                var swSave = System.Diagnostics.Stopwatch.StartNew();
                SaveImageVerified(TifHandle, outputTIF);
                swSave.Stop();
                if (_lastThresholdDetail != null)
                    _lastThresholdDetail = _lastThresholdDetail.Replace(")", $" save:{swSave.ElapsedMilliseconds})");
            }

            RavenImaging.ImgDelete(sbbFull);
            RavenImaging.ImgDelete(sbbPartial); 
            
            if (tImageHandle != 0) RavenImaging.ImgDelete(tImageHandle);
            if (TifHandle != 0) RavenImaging.ImgDelete(TifHandle); // Clean up TIF image handle if it was used
        }

        public bool IsValidTif(string filePath)
        {
            // Check if the file exists
            if (!File.Exists(filePath))
            {
                return false;
            }

            // Try opening the file as a TIFF
            using (Tiff tif = Tiff.Open(filePath, "r"))
            {
                if (tif == null)
                {
                    return false; // Not a valid TIFF file
                }
            }

            return true; // Valid TIFF file
        }

        private bool CreateCheckpoint(string tifImage)
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "Imaging_cs_DEMO");
                if (!Directory.Exists(tempPath))
                {
                    Directory.CreateDirectory(tempPath);
                }

                string baseFileName = Path.GetFileNameWithoutExtension(tifImage);
                string searchPattern = baseFileName + "_*.tif";
                var existingFiles = Directory.GetFiles(tempPath, searchPattern)
                                              .Select(Path.GetFileName)
                                              .OrderBy(f => f);

                int maxVersion = existingFiles.Select(file =>
                {
                    if (int.TryParse(file.Split('_').Last().Replace(".tif", ""), out int version))
                        return version;
                    return 0;
                }).DefaultIfEmpty(0).Max(); // Provide a default value of 0 if the sequence is empty

                // Adjusted for 40 iterations
                int newVersionNumber = maxVersion >= 40 ? 1 : maxVersion + 1;

                if (maxVersion >= 40)
                {
                    // Delete the oldest version and shift others to accommodate 40 versions
                    for (int i = 1; i < 40; i++) // Start from 1 to 39
                    {
                        string currentFile = Path.Combine(tempPath, $"{baseFileName}_{i}.tif");
                        string nextFile = Path.Combine(tempPath, $"{baseFileName}_{i + 1}.tif");
                        if (File.Exists(currentFile))
                        {
                            File.Delete(currentFile);
                        }
                        if (File.Exists(nextFile))
                        {
                            File.Move(nextFile, currentFile); // Move without overwrite param
                        }
                    }
                    string oldestFile = Path.Combine(tempPath, $"{baseFileName}_40.tif");
                    if (File.Exists(oldestFile))
                    {
                        File.Delete(oldestFile); // Ensure the last slot is available for new checkpoint
                    }
                }
                // Create new checkpoint
                string newFilePath = Path.Combine(tempPath, $"{baseFileName}_{newVersionNumber}.tif");
                File.Copy(tifImage, newFilePath, true); // Overwrite if needed

                return true;
            }
            catch (Exception ex) { return false; }

        }


        private void LoadPrevCheckpoint(string tifImage)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "Imaging_cs_DEMO");
            string baseFileName = Path.GetFileNameWithoutExtension(tifImage);
            string searchPattern = baseFileName + "_*.tif";
            var checkpoints = Directory.GetFiles(tempPath, searchPattern)
                                       .Select(f => new {
                                           FullPath = f,
                                           Version = int.Parse(Path.GetFileNameWithoutExtension(f).Split('_').Last())
                                       })
                                       .OrderBy(f => f.Version)
                                       .ToList();

            if (!checkpoints.Any())
            {
                MessageBox.Show("No checkpoints available.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Find the current version
            int currentVersion = checkpoints.FindIndex(c => File.GetLastWriteTime(c.FullPath) >= File.GetLastWriteTime(tifImage));
            if (currentVersion == -1) currentVersion = checkpoints.Count;

            // If we're at the original state, can't go back further
            if (currentVersion == 0)
            {
                MessageBox.Show("This is the first checkpoint. Can't go back further.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Load the previous checkpoint
            File.Copy(checkpoints[currentVersion - 1].FullPath, tifImage, true);
            DisplayImages("", tifImage, 2, true);
        }

        private void LoadNextCheckpoint(string tifImage)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "Imaging_cs_DEMO");
            string baseFileName = Path.GetFileNameWithoutExtension(tifImage);
            string searchPattern = baseFileName + "_*.tif";
            var checkpoints = Directory.GetFiles(tempPath, searchPattern)
                                       .Select(f => new {
                                           FullPath = f,
                                           Version = int.Parse(Path.GetFileNameWithoutExtension(f).Split('_').Last())
                                       })
                                       .OrderBy(f => f.Version)
                                       .ToList();

            if (!checkpoints.Any())
            {
                MessageBox.Show("No checkpoints available.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Find the current version
            int currentVersion = checkpoints.FindIndex(c => File.GetLastWriteTime(c.FullPath) >= File.GetLastWriteTime(tifImage));
            if (currentVersion == -1) currentVersion = checkpoints.Count;

            // If we're at the latest state, can't go forward
            if (currentVersion >= checkpoints.Count)
            {
                MessageBox.Show("This is the latest state. Can't go forward.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Load the next checkpoint
            File.Copy(checkpoints[currentVersion].FullPath, tifImage, true);
            DisplayImages("", tifImage, 2, true);
        }


        private void KeyboardShortcutHelp()
        {
            // Assuming 'ReadMe.txt' is in the same directory as your application's executable
            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            string readmePath = Path.Combine(rootPath, "ReadMe.txt");

            // Check if the file exists before trying to open it
            if (File.Exists(readmePath))
            {
                Process.Start("notepad.exe", readmePath);
            }
            else
            {
                MessageBox.Show("The file ReadMe.txt does not exist in the root folder.");
            }
        }

        private void ClearCheckpoints()
        {
            string tempCheckpointPath = Path.Combine(Path.GetTempPath(), "Imaging_cs_DEMO");
            if (Directory.Exists(tempCheckpointPath))
            {
                var tifFiles = Directory.GetFiles(tempCheckpointPath, "*.tif");
                foreach (var file in tifFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete temporary checkpoint file: {file}. Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void Form1_closing(object sender, FormClosingEventArgs e)
        {
            SaveCurrentPageSettings();
            SaveIniSettings();
        }

        private void SaveIniSettings()
        {
            F2Settings.WriteINI("F2");
            F3Settings.WriteINI("F3");
            F4Settings.WriteINI("F4");
            F5Settings.WriteINI("F5");
            F6Settings.WriteINI("F6");
        }

        public void ClearJPGCache(bool clearGrayscaleCache = false)
        {
            if (this.CachedJPG > 0)
            {
                RavenImaging.ImgDelete(CachedJPG);
                this.CachedJPG = 0;
            }
            if (this.CachedPartialImageHandle > 0)
            {
                RavenImaging.ImgDelete(CachedPartialImageHandle);
                this.CachedPartialImageHandle = 0; // Reset the handle
                this.CachedPartialImageDimensions = (0, 0, 0, 0);
            }
            if (clearGrayscaleCache)
                OpenThresholdBridge.ClearCache();
        }

        // Preload current image's JPEG into grayscale bytes in the background
        // so RDynamic threshold doesn't have to wait for the disk read.
        private void PreloadForOpenThreshold()
        {
            if (currentImageIndex >= 0 && currentImageIndex < ImagePairs.Count)
            {
                string jpg = ImagePairs[currentImageIndex].JPG;
                if (!string.IsNullOrEmpty(jpg) && File.Exists(jpg))
                {
                    Task.Run(() => OpenThresholdBridge.PreloadGrayscale(jpg));
                }
            }
        }

        /// <summary>
        /// Pre-decode adjacent images (±3) on background threads so paging feels instant.
        /// Called after DisplayImages — current image is already shown, this starts background work.
        /// </summary>
        private void PrefetchAdjacentImages()
        {
            if (!ImagePairs.Any()) return;

            var keepPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toPrefetch = new List<string>();

            for (int offset = -3; offset <= 3; offset++)
            {
                if (offset == 0) continue; // current image already displayed
                int idx = currentImageIndex + offset;
                if (idx < 0 || idx >= ImagePairs.Count) continue;

                var pair = ImagePairs[idx];
                if (isJPGVisible && !string.IsNullOrEmpty(pair.JPG) && File.Exists(pair.JPG))
                {
                    keepPaths.Add(pair.JPG);
                    toPrefetch.Add(pair.JPG);
                }
                if (!string.IsNullOrEmpty(pair.TIF) && File.Exists(pair.TIF))
                {
                    keepPaths.Add(pair.TIF);
                    toPrefetch.Add(pair.TIF);
                }
            }

            // Cancel stale prefetches, keep entries still in the ±3 window
            RavenPictureBox.RefreshPrefetch(keepPaths);
            // Start new prefetches for uncached adjacent images
            RavenPictureBox.PrefetchImages(toPrefetch);
        }

        private void UpdateLinePositionFromTag()
        {
            if (Special_DrawLineMode && currentImageIndex >= 0 && currentImageIndex < ImagePairs.Count)
            {
                // Try current image first
                decimal? currentPosition = ReadLinePositionTag(ImagePairs[currentImageIndex].TIF);
                if (currentPosition.HasValue)
                {
                    Special_DrawLineValue = currentPosition.Value;
                    return;
                }

                // If no current value, check previous matching pages
                int prevMatchingIndex = currentImageIndex - 2;
                while (prevMatchingIndex >= 0)
                {
                    decimal? savedPosition = ReadLinePositionTag(ImagePairs[prevMatchingIndex].TIF);
                    if (savedPosition.HasValue)
                    {
                        Special_DrawLineValue = savedPosition.Value;
                        break;
                    }
                    prevMatchingIndex -= 2;
                }
            }
        }

        private void SaveCurrentPageSettings()
        {
            if (ImagePairs.Count > 0 && currentImageIndex >= 0 && currentImageIndex < ImagePairs.Count)
            {
                // Only save if we actually changed the line position on this page
                if (Special_DrawLineMode)
                {
                    SaveLinePositionIfNeeded();
                }
            }
        }

        private void SaveLinePositionIfNeeded()
        {
            // Early return if basic conditions aren't met
            if (!Special_DrawLineMode ||
                Special_DrawLineValue <= 0 ||
                currentImageIndex < 0 ||
                currentImageIndex >= ImagePairs.Count)
            {
                return;
            }

            string currentTif = ImagePairs[currentImageIndex].TIF;

            // Read existing value
            decimal? existingPosition = ReadLinePositionTag(currentTif);

            // Only save if value has changed
            if (!existingPosition.HasValue || existingPosition.Value != Special_DrawLineValue)
            {
                WriteLinePositionTag(currentTif, Special_DrawLineValue);
                Console.WriteLine($"Saved new line position {Special_DrawLineValue} to {currentTif}");
            }
        }

        private void JumpTo(int jumpto)
        {
            SaveCurrentPageSettings(); 
            if (jumpto <= (ImagePairs.Count) && jumpto >= 0)
            {
                ClearJPGCache(clearGrayscaleCache: true);
                currentImageIndex = jumpto;
                var nextImagePair = ImagePairs[currentImageIndex];
                PreloadForOpenThreshold();
                DisplayImages(nextImagePair.JPG, nextImagePair.TIF);
                keyPicture2.ShowFileOk();
                PrefetchAdjacentImages();
            }

            if (Special_DrawLineMode)
            {
                UpdateLinePositionFromTag();
                DrawTop();
            }
        }

        private void PrevImage_Click(object sender, EventArgs e)
        {
            SaveCurrentPageSettings();
            ClearJPGCache(clearGrayscaleCache: true);

            //Clears variabls in case Darken / Lighten has been used back to what is on screen
            thresholdSettingsForm.SetVariables();

            if (ImagePairs.Any())
            {
                // Move to the previous image in the list
                currentImageIndex--;

                // Check if the index goes below 0, indicating we need to loop back to the last image
                if (currentImageIndex < 0)
                {
                    // Set to the last image in the list
                    currentImageIndex = 0;
                    return; 
                    // currentImageIndex = ImagePairs.Count - 1;
                }
                // Load and display the previous image pair
                var prevImagePair = ImagePairs[currentImageIndex];
                PreloadForOpenThreshold();
                DisplayImages(prevImagePair.JPG, prevImagePair.TIF);
                keyPicture2.ShowFileOk();
                PrefetchAdjacentImages();
            }
            else
            {
                MessageBox.Show("No images to display.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            this.activecropbox = false;
            this.BatchWhiteoutActive = false;

            if (Special_DrawLineMode)
            {
                UpdateLinePositionFromTag();
                DrawTop();
            }
        }


        private void NextImage_Click(object sender, EventArgs e)
        {
            if (this.activecropbox == true)
            {
                this.activecropbox = false;
            }
            if (this.BatchWhiteoutActive == true)
            {
                this.BatchWhiteoutActive = false;
            }

            SaveCurrentPageSettings();
            ClearJPGCache(clearGrayscaleCache: true);

            //Clears variabls in case Darken / Lighten has been used back to what is on screen
            thresholdSettingsForm.SetVariables();

            // Check if there are any images in the list
            if (ImagePairs.Any())
            {
                // Move to the next image in the list
                currentImageIndex++;

                // Check if the index exceeds the bounds of the list
                if (currentImageIndex >= ImagePairs.Count)
                {
                    // Loop back to the first image if we've reached the end of the list
                    // currentImageIndex = 0;
                    currentImageIndex--;
                    
                    return; 
                }

                // Load and display the next image pair
                var nextImagePair = ImagePairs[currentImageIndex];
                // Do auto line removal process
                if (LineRemovalList.Count > 0)
                {
                    AutoRemoveLines(nextImagePair.TIF);
                }
                // Start grayscale preload BEFORE display so OpenCV decode runs
                // in parallel with WIC decode + D2D render on the UI thread.
                PreloadForOpenThreshold();
                DisplayImages(nextImagePair.JPG, nextImagePair.TIF);
                keyPicture2.ShowFileOk();
                PrefetchAdjacentImages();
            }
            else
            {
                MessageBox.Show("No images to display.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }


            if (Special_DrawLineMode)
            {
                UpdateLinePositionFromTag();
                DrawTop();
            }
        }

        private void AutoRemoveLines(string TifImage)
        {
            foreach (int xCoordinate in LineRemovalList)
            {
                keyPicture2.RemoveDirtyLine(TifImage, 1, TifImage, 3, xCoordinate);
            }
        }



        private void TestMe_Click(object sender, EventArgs e)
        {
            KeyboardShortcutHelp();
        }





        private void LoadINISettings()
        {
            string iniFilePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "settings.ini");
            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(iniFilePath);

            Special_InsertBlankAbility = data["Special"]["InsertBlankAbility"] == "Y";
            Special_BatchWhiteoutAbility = data["Special"]["BatchWhiteoutAbility"] == "Y";
            Special_DrawLineValue = decimal.TryParse(data["Special"]["CanvasTop"], out decimal value) ? value : 0;
            Special_CanadianSetLine = data["Special"]["CanadianSetLine"] == "Y";
            Special_CropJPG = data["Special"]["CropJPG"] == "Y";
    }
    }
}
