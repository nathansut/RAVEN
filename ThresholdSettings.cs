using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Text.RegularExpressions;
using IniParser;
using IniParser.Model;
using System.Security.Cryptography.X509Certificates;

namespace RAVEN
{
    public partial class Settings : Form
    {

        // public string GroupFocus = "";
        public Control currentlyFocusedControl = null;
        private const int MouseWheelStartingTabIndex = 9;

        public List<(string JPG, string TIF)> ConvertImageList { get; private set; }

        public Settings()
        {
            InitializeComponent();
            LoadSettings();
            LoadLocation();


        }

        private void LoadLocation()
        {
            // Parse the saved location and set it to the form's location
            string savedLocation = Properties.Settings.Default.SettingsFormLocation;
            if (!string.IsNullOrEmpty(savedLocation))
            {
                string[] coords = savedLocation.Split(',');
                if (coords.Length == 2 && int.TryParse(coords[0], out int x) && int.TryParse(coords[1], out int y))
                {
                    this.Location = new Point(x, y);
                }
            }
        }

        private void LoadSettings()
        {
            string iniFilePath = System.IO.Path.Combine(Application.StartupPath, "settings.ini");

            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(iniFilePath); // Replace 'this.iniFilePath' with the actual path to your INI file

            // Load 'AreaConv' settings
            SetComboBoxSelectedItem(F2ActiveType, data["F2"]["Type"]);
            F2Negative.Checked = data["F2"]["NegativeImage"] == "Y";
            textBoxF2Contrast.Text = data["F2"]["Contrast"];
            textBoxF2Brightness.Text = data["F2"]["Brightness"];
            textBoxF2Despeckle.Text = data["F2"]["Despeckle"];
            textBoxF2FilterThresholdStepup.Text = data["F2"]["FilterThresholdStepup"];
            textBoxF2ToleranceFilter.Text = data["F2"]["Tolerance"];
            textBoxF2DespeckleFilter.Text = data["F2"]["DespeckleFilter"];

            // Load 'F3' settings
            SetComboBoxSelectedItem(F3ActiveType, data["F3"]["Type"]);
            F3Negative.Checked = data["F3"]["NegativeImage"] == "Y";
            textBoxF3Contrast.Text = data["F3"]["Contrast"];
            textBoxF3Brightness.Text = data["F3"]["Brightness"];
            textBoxF3Despeckle.Text = data["F3"]["Despeckle"];
            textBoxF3FilterThresholdStepup.Text = data["F3"]["FilterThresholdStepup"];
            textBoxF3ToleranceFilter.Text = data["F3"]["Tolerance"];
            textBoxF3DespeckleFilter.Text = data["F3"]["DespeckleFilter"];

            // Load 'F4' settings
            SetComboBoxSelectedItem(F4ActiveType, data["F4"]["Type"]);
            F4Negative.Checked = data["F4"]["NegativeImage"] == "Y";
            textBoxF4Contrast.Text = data["F4"]["Contrast"];
            textBoxF4Brightness.Text = data["F4"]["Brightness"];
            textBoxF4Despeckle.Text = data["F4"]["Despeckle"];
            textBoxF4FilterThresholdStepup.Text = data["F4"]["FilterThresholdStepup"];
            textBoxF4ToleranceFilter.Text = data["F4"]["Tolerance"];
            textBoxF4DespeckleFilter.Text = data["F4"]["DespeckleFilter"];

            // Load 'F5' settings
            SetComboBoxSelectedItem(F5ActiveType, data["F5"]["Type"]);
            F5Negative.Checked = data["F5"]["NegativeImage"] == "Y";
            textBoxF5Contrast.Text = data["F5"]["Contrast"];
            textBoxF5Brightness.Text = data["F5"]["Brightness"];
            textBoxF5Despeckle.Text = data["F5"]["Despeckle"];
            textBoxF5FilterThresholdStepup.Text = data["F5"]["FilterThresholdStepup"];
            textBoxF5ToleranceFilter.Text = data["F5"]["Tolerance"];
            textBoxF5DespeckleFilter.Text = data["F5"]["DespeckleFilter"];

            // Load 'F6' settings
            SetComboBoxSelectedItem(F6ActiveType, data["F6"]["Type"]);
            F6Negative.Checked = data["F6"]["NegativeImage"] == "Y";
            textBoxF6Contrast.Text = data["F6"]["Contrast"];
            textBoxF6Brightness.Text = data["F6"]["Brightness"];
            textBoxF6Despeckle.Text = data["F6"]["Despeckle"];
            textBoxF6FilterThresholdStepup.Text = data["F6"]["FilterThresholdStepup"];
            textBoxF6ToleranceFilter.Text = data["F6"]["Tolerance"];
            textBoxF6DespeckleFilter.Text = data["F6"]["DespeckleFilter"];

        }

        private void SetComboBoxSelectedItem(ComboBox comboBox, string value)
        {
            if (comboBox != null && value != null)
            {
                comboBox.SelectedItem = comboBox.Items.Cast<Object>().FirstOrDefault(item => item.ToString().Equals(value, StringComparison.OrdinalIgnoreCase));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Properties.Settings.Default.SettingsFormLocation = $"{this.Location.X},{this.Location.Y}";
            Properties.Settings.Default.Save();


            // Try to parse the text to an integer.
            if (this.Owner is Form1 mainForm)
            {


            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Cancel the closing of the form
                e.Cancel = true;

                // Hide the form instead of closing it
                this.Hide();
            }
            else
            {
                // If the form is closing for any other reason, allow it to close
                base.OnFormClosing(e);
            }


           

        }


        public void SetFirstContrast()
        {
            SetFieldFocus(this.textBoxF2Contrast);
        }

        public void SetSecondContrast()
        {
            SetFieldFocus(this.textBoxF3Contrast);
        }

        public void SetThirdContrast()
        {
            SetFieldFocus(this.textBoxF4Contrast);
        }

        public void SetFieldFocus(Control controlToFocus)
        {
            // ClearFieldFocus(); // Resets the visual state of previously focused controls. Don't think I need this - just reset the current control color back before going to next one. 
            if (controlToFocus != null)
            {
                
                // If control prev highlighted, refresh group & control color back to system color
                if (this.currentlyFocusedControl != null)
                {
                    this.currentlyFocusedControl.BackColor = SystemColors.Control;
                    this.currentlyFocusedControl.Refresh();

                    // Area Conv
                    if (this.currentlyFocusedControl.Name.Contains("F2"))
                    {
                        if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                        {
                            AreaConvDyncamicLabel.BackColor = SystemColors.Control; 
                            AreaConvDyncamicLabel.Refresh();
                        }
                        else
                        {
                            AreaConvRefineLabel.BackColor = SystemColors.Control;
                            AreaConvRefineLabel.Refresh();
                        }
                    }

                    // F3

                    if (this.currentlyFocusedControl.Name.Contains("F3"))
                    {
                        if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                        {
                            F3DyncamicLabel.BackColor = SystemColors.Control;
                            F3RefineLabel.Refresh();
                        }
                        else
                        {
                            F3RefineLabel.BackColor = SystemColors.Control;
                            F3RefineLabel.Refresh();
                        }
                    }

                    // F4
                    if (this.currentlyFocusedControl.Name.Contains("F4"))
                    {
                        if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                        {
                            F4DyncamicLabel.BackColor = SystemColors.Control;
                            F4RefineLabel.Refresh();
                        }
                        else
                        {
                            F4RefineLabel.BackColor = SystemColors.Control;
                            F4RefineLabel.Refresh();
                        }
                    }

                    // F5
                    if (this.currentlyFocusedControl.Name.Contains("F5"))
                    {
                        if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                        {
                            F5DyncamicLabel.BackColor = SystemColors.Control;
                            F4RefineLabel.Refresh();
                        }
                        else
                        {
                            F5RefineLabel.BackColor = SystemColors.Control;
                            F5RefineLabel.Refresh();
                        }
                    }

                    // F6
                    if (this.currentlyFocusedControl.Name.Contains("F6"))
                    {
                        if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                        {
                            F6DyncamicLabel.BackColor = SystemColors.Control;
                            F6RefineLabel.Refresh();
                        }
                        else
                        {
                            F6RefineLabel.BackColor = SystemColors.Control;
                            F6RefineLabel.Refresh();
                        }
                    }


                }


                //Set next control color
                this.currentlyFocusedControl = controlToFocus;               
                this.currentlyFocusedControl.BackColor = Color.FromArgb(217, 237, 247); // Highlight the control
                this.currentlyFocusedControl.Refresh();

                // Check for Group Highlighting
                if (this.currentlyFocusedControl.Name.Contains("AreaConv"))
                {
                    if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                    {
                        AreaConvDyncamicLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                    else
                    {
                        AreaConvRefineLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                }

                if (this.currentlyFocusedControl.Name.Contains("F3"))
                {
                    if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                    {
                        F3DyncamicLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                    else
                    {
                        F3RefineLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                }

                if (this.currentlyFocusedControl.Name.Contains("F4"))
                {
                    if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                    {
                        F4DyncamicLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                    else
                    {
                        F4RefineLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                }

                if (this.currentlyFocusedControl.Name.Contains("F5"))
                {
                    if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                    {
                        F5DyncamicLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                    else
                    {
                        F5RefineLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                }

                if (this.currentlyFocusedControl.Name.Contains("F6"))
                {
                    if (!this.currentlyFocusedControl.Name.Contains("Filter"))
                    {
                        F6DyncamicLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                    else
                    {
                        F6RefineLabel.BackColor = Color.FromArgb(217, 237, 247);
                    }
                }

            }


        }

        public void ClearFieldFocus()
        {
            this.textBoxF3Contrast.BackColor = Color.White;
            this.textBoxF3Brightness.BackColor = Color.White;
        }


        public void Settings_MouseWheel(object sender, MouseEventArgs e)
        {
            // Check if Alt is pressed and the currently focused control is a TextBox.
            
            if (currentlyFocusedControl != null && currentlyFocusedControl is TextBox textBox)
            {
                // Handle incrementing/decrementing the value if Alt is not pressed.
                if (int.TryParse(textBox.Text, out int number))
                {
                    number += e.Delta > 0 ? 1 : -1;
                    textBox.Text = number.ToString();
                    SetVariables();
                }
            }
        }


        private Control GetNextControl()
        {
            var allControls = GetAllControls(this)
                .Where(c => c.TabStop && c.CanSelect)
                .OrderBy(c => c.TabIndex)
                .ToList();

            var currentTabIndex = currentlyFocusedControl?.TabIndex ?? 0;
            var nextControl = allControls
                .FirstOrDefault(c => c.TabIndex > currentTabIndex);

            // If no next control is found, loop back to the first control in the tab order.
            // Ensure this selects the control with the smallest TabIndex that is valid.
            return nextControl ?? allControls.FirstOrDefault();
        }

        public void goNextControl()
        {
            Control nextControl = GetNextControl();
            SetFieldFocus(nextControl); 
        }

        public void goPrevControl()
        {
            Control nextControl = GetPreviousControl();
            SetFieldFocus(nextControl);
        }

        private Control GetPreviousControl()
        {
            var allControls = GetAllControls(this)
                .Where(c => c.TabStop && c.CanSelect)
                .OrderByDescending(c => c.TabIndex)
                .ToList();

            var currentTabIndex = currentlyFocusedControl?.TabIndex ?? MouseWheelStartingTabIndex;
            var prevControl = allControls
                .FirstOrDefault(c => c.TabIndex < currentTabIndex);

            return prevControl ?? allControls.LastOrDefault(c => c.TabIndex < MouseWheelStartingTabIndex);
        }


        private IEnumerable<Control> GetAllControls(Control container)
        {
            var controls = container.Controls.Cast<Control>();

            return controls.SelectMany(ctrl => GetAllControls(ctrl))
                           .Concat(controls);
        }



        private void ThresholdSettings_Load(object sender, EventArgs e)
        {
           
        }

        private void Negative_CheckedChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
            if (this.Owner is Form1 mainForm)
            {
                    // Because it inverts the JPG - have to clear the cache
                    mainForm.ClearJPGCache();               
            }
        }

        public async Task RunProcessAsync(string filePath, string arguments)
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = filePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.EnableRaisingEvents = true;

                var tcs = new TaskCompletionSource<bool>();

                process.Exited += (sender, args) =>
                {
                    tcs.SetResult(true);
                    process.Dispose();
                };

                process.Start();

                await tcs.Task;
                // Process has exited at this point
            }
        }
        private async void button1_Click(object sender, EventArgs e)
        {
            if (F2ActiveType.Text != "Dynamic" && F2ActiveType.Text != "ML1" && F2ActiveType.Text != "ML2")
            {
                MessageBox.Show("Convert Rest of Book only supports Dynamic/ML1/ML2 Threshold");
                return; 
            }

            
            

            // Where is the thing that figures out if this is DYnamic or other one? Does it just go w Dynamic all the time? If so need to say other one is not supported. Then also SBB not supported.
            // Then unsupport doing "areas" for both SBB & (maybe) Dynamic
            
            // Get the main form and relevant values
            var mainForm = this.Owner as Form1;
            var imagePairs = mainForm.ImagePairs;
            int currentImageIndex = mainForm.currentImageIndex;
            

            if (F2ActiveType.Text == "ML1" || F2ActiveType.Text == "ML2")
            {
                // Generate the list of files to convert without copying them
                for (int i = currentImageIndex; i < imagePairs.Count; i++)
                {
                    string originalJpgPath = imagePairs[i].JPG;
                    string originalTifPath = imagePairs[i].TIF;                   
                    mainForm.ThresholdMe(mainForm.F2Settings, - 1, -1, -1, -1, originalJpgPath, originalTifPath, false, true); 
                }
            }

            else if (F2ActiveType.Text == "Dynamic")
            {
                int contrast = int.Parse(textBoxF2Contrast.Text);
                int brightness = int.Parse(textBoxF2Brightness.Text);
                int despeckle = int.Parse(textBoxF2Despeckle.Text);

                // Define the paths
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string scriptFileName = this.F2Negative.Checked ? "PhotostatScript.ips" : "xLooseScript.ips"; // Choose the script based on the checkbox
                string scriptFilePath = Path.Combine(baseDirectory, scriptFileName);
                string tempDirectory = Path.Combine(Path.GetTempPath(), "Recog");
                Directory.CreateDirectory(tempDirectory); // Ensure temp directory exists

                // Copy and modify the script file
                string tempScriptFilePath = Path.Combine(tempDirectory, scriptFileName);
                File.Copy(scriptFilePath, tempScriptFilePath, overwrite: true);
                string scriptContent = File.ReadAllText(tempScriptFilePath);

                // Use Regex to replace, bulletproofing against varying spaces and different object names
                scriptContent = Regex.Replace(scriptContent,
                                              @"ImgDynamicThresholdAverage\s*\(\s*(Photostat|_CurrentImage)\s*,\s*7\s*,\s*7\s*,\s*\d+\s*,\s*\d+\s*\);",
                                              $"ImgDynamicThresholdAverage( $1, 7, 7, {contrast}, {brightness} );");

                scriptContent = Regex.Replace(scriptContent,
                                              @"ImgDespeckle\s*\(\s*(Photostat|_CurrentImage)\s*,\s*\d+\s*,\s*\d+\s*\);",
                                              $"ImgDespeckle( $1, {despeckle}, {despeckle} );");

                File.WriteAllText(tempScriptFilePath, scriptContent);

                // Create the launch file with attributes
                string launchFilePath = Path.Combine(tempDirectory, "DefaultBatchesQueue1.ipb");
                List<string> launchFileLines = new List<string>
                {
                "[Batches]",
                "Count=1",
                "[Batch 0]",
                "Description=Default",
                "Output Directory=*",
                "Watching Directory=",
                "Output Format=1",
                "TIFF Compression=0",
                "TIFF Rows per Strip=0",
                "TIFF Overwrite=1",
                "TIFF Author=Sutterfield Technologies",
                "PDF Rasterization=0",
                "PDFA=0",
                "PDF Resolution=300",
                "PDF Auto Color Reduction=1",
                "JPEG QFactor=80",
                "Agents=5",
                "Log=1",
                "Only Unchecked=1",
                "Script=" + tempScriptFilePath,
                "Files Count=" + (imagePairs.Count - currentImageIndex).ToString(),
                "[Batch 0 Files]"
                };

                // Generate the list of files to convert without copying them
                for (int i = currentImageIndex; i < imagePairs.Count; i++)
                {
                    string originalJpgPath = imagePairs[i].JPG;
                    launchFileLines.Add($"{i - currentImageIndex}={originalJpgPath}");
                }
                File.WriteAllLines(launchFilePath, launchFileLines);

                // Launch the image processing command
                string imageProcessorExePath = @"C:\Program Files (x86)\RecogniformTechnologies\ImageProcessor\ImageProcessor.exe";

                string commandLine = $"\"{imageProcessorExePath}\" -auto \"{launchFilePath}\" \"{tempScriptFilePath}\"";

                Clipboard.SetText(commandLine);

                await RunProcessAsync(imageProcessorExePath, $"-auto \"{launchFilePath}\"");

                
            }
            // Update UI to indicate completion
            this.Invoke((Action)(() =>
            {
                this.Text = "Conversion Complete";
                buConvertBook.Enabled = true;
            }));

        }


        private void RecogConvertPhotostat(string jpg, string tif, int contrast, int brightness)
        {

        }



        private void ConvertImage(string jpg, string tif, int contrast, int brightness)
        {
            //We only proceed to photostat convert if the "Negative" is checked
            if (this.F2Negative.Checked == true)
            {
                ConvertPhotostat(jpg, tif, contrast, brightness);
            }
        }

        private void ConvertPhotostat(string jpg, string tif, int contrast, int brightness)
        {
            bool Despeckle = true;
            bool CropOverscan = true;

            int Copy1 = 0;
            int Copy2 = 0; 


            int ImageHandle = RecoIP.ImgOpen(jpg, 0);

            // A = Overscan 
            // B = Page border outside of photostat
            // C = Photostat
            

            int CopyOfImage = RecoIP.ImgDuplicate(ImageHandle);

            double test = 80.0;

            // RecoIP.ImgBackTrackThresholdAverage(CopyOfImage, -1, -1, 80.0);
            RecoIP.ImgAdaptiveThresholdAverage(CopyOfImage, 7, 7, -1, -1);
            




            int aLeft = RecoIP.ImgFindBlackBorderLeft(CopyOfImage, 90.0, 1);
            int aTop = RecoIP.ImgFindBlackBorderTop(CopyOfImage, 90.0, 1);
            int aRight = RecoIP.ImgFindBlackBorderRight(CopyOfImage, 90.0, 1);
            int aBottom = RecoIP.ImgFindBlackBorderBottom(CopyOfImage, 90.0, 1);

            if (aLeft < aRight && aTop < aBottom)
            {
                RecoIP.ImgCropBorder(CopyOfImage, aLeft, aTop, aRight, aBottom);
            }

            RecoIP.ImgInvert(CopyOfImage);

            int bLeft = RecoIP.ImgFindBlackBorderLeft(CopyOfImage, 99.0, 1);
            int bTop = RecoIP.ImgFindBlackBorderTop(CopyOfImage, 99.0, 1);
            int bRight = RecoIP.ImgFindBlackBorderRight(CopyOfImage, 99.0, 1);
            int bBottom = RecoIP.ImgFindBlackBorderBottom(CopyOfImage, 99.0, 1);

            bLeft = bLeft + 20;
            bRight = bRight - 20;

            if (bLeft <= bRight && bTop <= bBottom)
            {
                RecoIP.ImgCropBorder(CopyOfImage, bLeft, bTop, bRight, bBottom);
            }

            int cLeft = RecoIP.ImgFindBlackBorderLeft(CopyOfImage, 80.0, 30);
            int cTop = RecoIP.ImgFindBlackBorderTop(CopyOfImage, 80.0, 100);
            int cRight = RecoIP.ImgFindBlackBorderRight(CopyOfImage, 80.0, 30);
            int cBottom = RecoIP.ImgFindBlackBorderBottom(CopyOfImage, 80.0, 100);

            cLeft = cLeft + 20;
            cRight = cRight - 20;
            cTop = cTop + 20;
            cBottom = cBottom - 20;

            RecoIP.ImgDelete(CopyOfImage);

            RecoIP.ImgRemoveBleedThrough(ImageHandle, 1);

            if (aLeft<=aRight && aTop <= aBottom)
            {
                Copy1 = RecoIP.ImgCopy(ImageHandle, aLeft, aTop, aRight, aBottom);
            }

            if (bLeft<=bRight && bTop <= bBottom)
            {
                Copy2 = RecoIP.ImgCopy(Copy1, bLeft, bTop, bRight, bBottom);
            }

            int Photostat = RecoIP.ImgCopy(Copy2, cLeft, cTop, cRight, cBottom);

            RecoIP.ImgInvert(Photostat);

            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (F2ActiveType.SelectedItem?.ToString() == "RDynamic")
                    Photostat = OpenThresholdBridge.ApplyThreshold(Photostat, 7, 7, contrast, brightness);
                else
                    RecoIP.ImgDynamicThresholdAverage(Photostat, 7, 7, contrast, brightness);
                sw.Stop();
                AddStatusUpdate($"Threshold: {sw.ElapsedMilliseconds}ms ({F2ActiveType.SelectedItem})");
            }

            if (Despeckle == true)
            {
                RecoIP.ImgDespeckle(Photostat, 3, 3);
            }

            RecoIP.ImgRemoveBlackWires(Photostat);

            int PhHeight = RecoIP.ImgGetHeight(Photostat) - 10;
            int PhRatio = PhHeight / 5;
            int PhBreaks = PhHeight - 15000;
            RecoIP.ImgRemoveVerticalLines(Photostat, PhHeight, PhBreaks, PhRatio, false, true);

            RecoIP.ImgAdaptiveThresholdAverage(ImageHandle, 7, 7, -1, -1);
            RecoIP.ImgAdaptiveThresholdAverage(Copy1, 7, 7, 40, 230);
            RecoIP.ImgAdaptiveThresholdAverage(Copy2, 7, 7, 40, 230);

            RecoIP.ImgAddCopy(Copy2, Photostat, cLeft, cTop);
            RecoIP.ImgAddCopy(Copy1, Copy2, bLeft, bTop);
            RecoIP.ImgAddCopy(ImageHandle, Copy1, aLeft, aTop);

            File.Delete(tif);
            RecoIP.ImgSaveAsTif(ImageHandle, tif, 5, 0);

            RecoIP.ImgDelete(Photostat);
            RecoIP.ImgDelete(Copy1);
            RecoIP.ImgDelete(Copy2);
            RecoIP.ImgDelete(ImageHandle);

        }

        private void ShowJPG_CheckedChanged(object sender, EventArgs e)
        {
            if (this.Owner is Form1 mainForm)
            {
                mainForm.isJPGVisible = ShowJPG.Checked;
                // Optionally, refresh the visibility of the JPG image in mainForm
                mainForm.RefreshJPGVisibility();
            }
        }

        private void Despeckle_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }


        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void F3Negative_CheckedChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
            if (this.Owner is Form1 mainForm)
            {
                // Because it inverts the JPG - have to clear the cache
                mainForm.ClearJPGCache();
            }
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            UpdateINIAndVariables();
        }

        public void UpdateINIAndVariables()
        {
            SetVariables();
            
        }

        public void SetVariables()
        {
            var mainForm = this.Owner as Form1;

            // 'F2' settings (replacement for 'AreaConv' settings)
            mainForm.F2Settings.Type = F2ActiveType.SelectedItem?.ToString() ?? "Dynamic";
            mainForm.F2Settings.NegativeImage = F2Negative.Checked;
            mainForm.F2Settings.Contrast = int.TryParse(textBoxF2Contrast.Text, out _) ? int.Parse(textBoxF2Contrast.Text) : 0;
            mainForm.F2Settings.Brightness = int.TryParse(textBoxF2Brightness.Text, out _) ? int.Parse(textBoxF2Brightness.Text) : 0;
            mainForm.F2Settings.Despeckle = int.TryParse(textBoxF2Despeckle.Text, out _) ? int.Parse(textBoxF2Despeckle.Text) : 0;
            mainForm.F2Settings.FilterThresholdStepup = int.TryParse(textBoxF2FilterThresholdStepup.Text, out _) ? int.Parse(textBoxF2FilterThresholdStepup.Text) : 0;
            mainForm.F2Settings.Tolerance = int.TryParse(textBoxF2ToleranceFilter.Text, out _) ? int.Parse(textBoxF2ToleranceFilter.Text) : 0;
            mainForm.F2Settings.DespeckleFilter = int.TryParse(textBoxF2DespeckleFilter.Text, out _) ? int.Parse(textBoxF2DespeckleFilter.Text) : 0;


            // Assuming 'mainForm' is an instance of a class where these properties are defined
            // and UI elements like 'Negative', 'textBoxContrast', etc., are from which values are fetched

            // 'F3' settings
            mainForm.F3Settings.Type = F3ActiveType.SelectedItem?.ToString() ?? "Dynamic";
            mainForm.F3Settings.NegativeImage = F3Negative.Checked;
            mainForm.F3Settings.Contrast = int.TryParse(textBoxF3Contrast.Text, out _) ? int.Parse(textBoxF3Contrast.Text) : 0;
            mainForm.F3Settings.Brightness = int.TryParse(textBoxF3Brightness.Text, out _) ? int.Parse(textBoxF3Brightness.Text) : 0;
            mainForm.F3Settings.Despeckle = int.TryParse(textBoxF3Despeckle.Text, out _) ? int.Parse(textBoxF3Despeckle.Text) : 0;
            mainForm.F3Settings.FilterThresholdStepup = int.TryParse(textBoxF3FilterThresholdStepup.Text, out _) ? int.Parse(textBoxF3FilterThresholdStepup.Text) : 0;
            mainForm.F3Settings.Tolerance = int.TryParse(textBoxF3ToleranceFilter.Text, out _) ? int.Parse(textBoxF3ToleranceFilter.Text) : 0;
            mainForm.F3Settings.DespeckleFilter = int.TryParse(textBoxF3DespeckleFilter.Text, out _) ? int.Parse(textBoxF3DespeckleFilter.Text) : 0;

            // 'F4' settings

            mainForm.F4Settings.Type = F4ActiveType.SelectedItem?.ToString() ?? "Dynamic";
            mainForm.F4Settings.NegativeImage = F4Negative.Checked;
            mainForm.F4Settings.Contrast = int.TryParse(textBoxF4Contrast.Text, out _) ? int.Parse(textBoxF4Contrast.Text) : 0;
            mainForm.F4Settings.Brightness = int.TryParse(textBoxF4Brightness.Text, out _) ? int.Parse(textBoxF4Brightness.Text) : 0;
            mainForm.F4Settings.Despeckle = int.TryParse(textBoxF4Despeckle.Text, out _) ? int.Parse(textBoxF4Despeckle.Text) : 0;
            mainForm.F4Settings.FilterThresholdStepup = int.TryParse(textBoxF4FilterThresholdStepup.Text, out _) ? int.Parse(textBoxF4FilterThresholdStepup.Text) : 0;
            mainForm.F4Settings.Tolerance = int.TryParse(textBoxF4ToleranceFilter.Text, out _) ? int.Parse(textBoxF4ToleranceFilter.Text) : 0;
            mainForm.F4Settings.DespeckleFilter = int.TryParse(textBoxF4DespeckleFilter.Text, out _) ? int.Parse(textBoxF4DespeckleFilter.Text) : 0;



            // 'F5' settings

            mainForm.F5Settings.Type = F5ActiveType.SelectedItem?.ToString() ?? "Dynamic";
            mainForm.F5Settings.NegativeImage = F5Negative.Checked;
            mainForm.F5Settings.Contrast = int.TryParse(textBoxF5Contrast.Text, out _) ? int.Parse(textBoxF5Contrast.Text) : 0;
            mainForm.F5Settings.Brightness = int.TryParse(textBoxF5Brightness.Text, out _) ? int.Parse(textBoxF5Brightness.Text) : 0;
            mainForm.F5Settings.Despeckle = int.TryParse(textBoxF5Despeckle.Text, out _) ? int.Parse(textBoxF5Despeckle.Text) : 0;
            mainForm.F5Settings.FilterThresholdStepup = int.TryParse(textBoxF5FilterThresholdStepup.Text, out _) ? int.Parse(textBoxF5FilterThresholdStepup.Text) : 0;
            mainForm.F5Settings.Tolerance = int.TryParse(textBoxF5ToleranceFilter.Text, out _) ? int.Parse(textBoxF5ToleranceFilter.Text) : 0;
            mainForm.F5Settings.DespeckleFilter = int.TryParse(textBoxF5DespeckleFilter.Text, out _) ? int.Parse(textBoxF5DespeckleFilter.Text) : 0;

            // 'F6' settings

            mainForm.F6Settings.Type = F6ActiveType.SelectedItem?.ToString() ?? "Dynamic";
            mainForm.F6Settings.NegativeImage = F6Negative.Checked;
            mainForm.F6Settings.Contrast = int.TryParse(textBoxF6Contrast.Text, out _) ? int.Parse(textBoxF6Contrast.Text) : 0;
            mainForm.F6Settings.Brightness = int.TryParse(textBoxF6Brightness.Text, out _) ? int.Parse(textBoxF6Brightness.Text) : 0;
            mainForm.F6Settings.Despeckle = int.TryParse(textBoxF6Despeckle.Text, out _) ? int.Parse(textBoxF6Despeckle.Text) : 0;
            mainForm.F6Settings.FilterThresholdStepup = int.TryParse(textBoxF6FilterThresholdStepup.Text, out _) ? int.Parse(textBoxF6FilterThresholdStepup.Text) : 0;
            mainForm.F6Settings.Tolerance = int.TryParse(textBoxF6ToleranceFilter.Text, out _) ? int.Parse(textBoxF6ToleranceFilter.Text) : 0;
            mainForm.F6Settings.DespeckleFilter = int.TryParse(textBoxF6DespeckleFilter.Text, out _) ? int.Parse(textBoxF6DespeckleFilter.Text) : 0;


        }

        public void AddStatusUpdate(string update, bool clearstatus = false)
        {
            if (clearstatus) { textboxStatusUpdates.Clear(); }

            textboxStatusUpdates.AppendText(update + Environment.NewLine);

            // Scroll to the bottom
            textboxStatusUpdates.SelectionStart = textboxStatusUpdates.Text.Length;
            textboxStatusUpdates.ScrollToCaret();
        }

        private Dictionary<string, string> GetOrderedSettings(
            ComboBox typeComboBox, // Use ComboBox instead of TextBox for Type
            TextBox brightnessBox, TextBox contrastBox, TextBox despeckleBox,
            TextBox filterThresholdStepupBox, TextBox toleranceBox, TextBox despeckleFilterBox,
            bool negative)
        {
            var settings = new Dictionary<string, string>();

            if (typeComboBox != null && typeComboBox.SelectedItem != null) // Check if typeComboBox is provided and has a selected item
            {
                settings["Type"] = typeComboBox.SelectedItem.ToString(); // Use SelectedItem.ToString() for ComboBox
            }

            settings["NegativeImage"] = negative ? "Y" : "N";
            settings["Contrast"] = contrastBox.Text;
            settings["Brightness"] = brightnessBox.Text;
            settings["Despeckle"] = despeckleBox.Text;
            settings["FilterThresholdStepup"] = filterThresholdStepupBox.Text;
            settings["Tolerance"] = toleranceBox.Text;
            settings["DespeckleFilter"] = despeckleFilterBox.Text;

            return settings;
        }



        private void UpdateOrAddSection(Dictionary<string, Dictionary<string, string>> sections,
                                 string sectionName, Dictionary<string, string> newSettings)
        {
            if (!sections.ContainsKey(sectionName))
                sections[sectionName] = new Dictionary<string, string>(); // Corrected line

            foreach (var setting in newSettings)
                sections[sectionName][setting.Key] = setting.Value;
        }

        private Dictionary<string, string> LoadSettingsFromIni(string section, string iniPath)
        {
            var dict = new Dictionary<string, string>();
            bool inCorrectSection = false;

            foreach (var line in File.ReadAllLines(iniPath))
            {
                // Check for section
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    inCorrectSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                }
                else if (inCorrectSection && line.Contains('='))
                {
                    var keyValue = line.Split(new[] { '=' }, 2);
                    if (keyValue.Length == 2)
                    {
                        dict[keyValue[0].Trim()] = keyValue[1].Trim();
                    }
                }
            }

            return dict;
        }

        private void F2ActiveType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }       
        }

        private void textBoxAreaConvContrast_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxAreaConvBrightness_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxAreaConvFilterThresholdStepup_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxAreaConvDespeckleFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxAreaConvToleranceFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void F3ActiveType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }


        private void textBoxF3Contrast_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF3Despeckle_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF3Brightness_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF3FilterThresholdStepup_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF3DespeckleFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF3ToleranceFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void F4ActiveType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF4Contrast_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF4Despeckle_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF4Brightness_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void F4Negative_CheckedChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
            if (this.Owner is Form1 mainForm)
            {
                // Because it inverts the JPG - have to clear the cache
                mainForm.ClearJPGCache();
            }
        }

        private void textBoxF4FilterThresholdStepup_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF4DespeckleFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF4ToleranceFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void AreaConvThresholdGroupbox_Enter(object sender, EventArgs e)
        {

        }

        private void F5ActiveType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void F5Negative_CheckedChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
            if (this.Owner is Form1 mainForm)
            {
                // Because it inverts the JPG - have to clear the cache
                mainForm.ClearJPGCache();
            }
        }

        private void F6Negative_CheckedChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
            if (this.Owner is Form1 mainForm)
            {
                // Because it inverts the JPG - have to clear the cache
                mainForm.ClearJPGCache();
            }
        }

        private void textBoxF5Contrast_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF5Despeckle_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF5Brightness_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF5FilterThresholdStepup_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF5DespeckleFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF5ToleranceFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void F6ActiveType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF6Contrast_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF6Despeckle_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF6Brightness_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF6FilterThresholdStepup_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF6DespeckleFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        private void textBoxF6ToleranceFilter_TextChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }
    }

}
