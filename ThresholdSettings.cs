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

        // Maps each F-key section to its corresponding UI controls.
        private (ComboBox type, CheckBox negative, TextBox contrast, TextBox brightness,
                 TextBox despeckle, TextBox filterStepup, TextBox tolerance, TextBox despeckleFilter)
            GetControlsForKey(string key)
        {
            return key switch
            {
                "F2" => (F2ActiveType, F2Negative, textBoxF2Contrast, textBoxF2Brightness,
                         textBoxF2Despeckle, textBoxF2FilterThresholdStepup, textBoxF2ToleranceFilter, textBoxF2DespeckleFilter),
                "F3" => (F3ActiveType, F3Negative, textBoxF3Contrast, textBoxF3Brightness,
                         textBoxF3Despeckle, textBoxF3FilterThresholdStepup, textBoxF3ToleranceFilter, textBoxF3DespeckleFilter),
                "F4" => (F4ActiveType, F4Negative, textBoxF4Contrast, textBoxF4Brightness,
                         textBoxF4Despeckle, textBoxF4FilterThresholdStepup, textBoxF4ToleranceFilter, textBoxF4DespeckleFilter),
                "F5" => (F5ActiveType, F5Negative, textBoxF5Contrast, textBoxF5Brightness,
                         textBoxF5Despeckle, textBoxF5FilterThresholdStepup, textBoxF5ToleranceFilter, textBoxF5DespeckleFilter),
                "F6" => (F6ActiveType, F6Negative, textBoxF6Contrast, textBoxF6Brightness,
                         textBoxF6Despeckle, textBoxF6FilterThresholdStepup, textBoxF6ToleranceFilter, textBoxF6DespeckleFilter),
                _ => throw new ArgumentException($"Unknown key: {key}")
            };
        }

        private static readonly string[] FKeys = { "F2", "F3", "F4", "F5", "F6" };

        private void LoadSettings()
        {
            string iniFilePath = System.IO.Path.Combine(Application.StartupPath, "settings.ini");

            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(iniFilePath);

            foreach (string key in FKeys)
            {
                var c = GetControlsForKey(key);
                SetComboBoxSelectedItem(c.type, data[key]["Type"]);
                c.negative.Checked = data[key]["NegativeImage"] == "Y";
                c.contrast.Text = data[key]["Contrast"];
                c.brightness.Text = data[key]["Brightness"];
                c.despeckle.Text = data[key]["Despeckle"];
                c.filterStepup.Text = data[key]["FilterThresholdStepup"];
                c.tolerance.Text = data[key]["Tolerance"];
                c.despeckleFilter.Text = data[key]["DespeckleFilter"];
            }
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

        // Maps F-key name patterns (used in control names) to their Dynamic/Refine labels.
        // F2 controls use "F2" or "AreaConv" in their names.
        private (Label dynamic, Label refine) GetLabelsForKey(string keyPattern)
        {
            return keyPattern switch
            {
                "F2" => (AreaConvDyncamicLabel, AreaConvRefineLabel),
                "F3" => (F3DyncamicLabel, F3RefineLabel),
                "F4" => (F4DyncamicLabel, F4RefineLabel),
                "F5" => (F5DyncamicLabel, F5RefineLabel),
                "F6" => (F6DyncamicLabel, F6RefineLabel),
                _ => (null, null)
            };
        }

        // Returns the F-key pattern that matches a control name, or null.
        private string GetKeyPatternForControl(Control ctrl)
        {
            if (ctrl == null) return null;
            string name = ctrl.Name;
            // F2 controls may use "F2" or "AreaConv" prefix
            if (name.Contains("F2") || name.Contains("AreaConv")) return "F2";
            foreach (string key in FKeys)
                if (name.Contains(key)) return key;
            return null;
        }

        private void SetLabelColor(string keyPattern, bool isFilter, Color color)
        {
            var (dynamic, refine) = GetLabelsForKey(keyPattern);
            if (dynamic == null) return;
            if (!isFilter)
            {
                dynamic.BackColor = color;
                dynamic.Refresh();
            }
            else
            {
                refine.BackColor = color;
                refine.Refresh();
            }
        }

        public void SetFieldFocus(Control controlToFocus)
        {
            if (controlToFocus == null) return;

            // Un-highlight previous control and its group label
            if (this.currentlyFocusedControl != null)
            {
                this.currentlyFocusedControl.BackColor = SystemColors.Control;
                this.currentlyFocusedControl.Refresh();

                string prevKey = GetKeyPatternForControl(this.currentlyFocusedControl);
                if (prevKey != null)
                {
                    bool isFilter = this.currentlyFocusedControl.Name.Contains("Filter");
                    SetLabelColor(prevKey, isFilter, SystemColors.Control);
                }
            }

            // Highlight new control
            this.currentlyFocusedControl = controlToFocus;
            this.currentlyFocusedControl.BackColor = Color.FromArgb(217, 237, 247);
            this.currentlyFocusedControl.Refresh();

            // Highlight group label
            string newKey = GetKeyPatternForControl(this.currentlyFocusedControl);
            if (newKey != null)
            {
                bool isFilter = this.currentlyFocusedControl.Name.Contains("Filter");
                SetLabelColor(newKey, isFilter, Color.FromArgb(217, 237, 247));
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




        // Shared handler for all TextChanged / SelectedIndexChanged events
        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (this.IsHandleCreated)
            {
                UpdateINIAndVariables();
            }
        }

        // Shared handler for all Negative checkbox CheckedChanged events
        private void OnNegativeChanged(object sender, EventArgs e)
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

        private void buBatch_Click(object sender, EventArgs e)
        {
            if (this.Owner is Form1 mainForm)
            {
                mainForm.BatchModeToggle();
            }
        }

        private void buBatchProcess_Click(object sender, EventArgs e)
        {
            if (this.Owner is Form1 mainForm)
            {
                mainForm.BatchProcess();
            }
        }

        private void ShowJPG_CheckedChanged(object sender, EventArgs e)
        {
            if (this.Owner is Form1 mainForm)
            {
                mainForm.isJPGVisible = ShowJPG.Checked;
                mainForm.RefreshJPGVisibility();
            }
        }

        public void UpdateINIAndVariables()
        {
            SetVariables();
            
        }

        private static int ParseIntOrZero(TextBox tb)
        {
            return int.TryParse(tb.Text, out int val) ? val : 0;
        }

        private Form1.ConversionSettings GetSettingsForKey(string key)
        {
            var mainForm = this.Owner as Form1;
            return key switch
            {
                "F2" => mainForm.F2Settings,
                "F3" => mainForm.F3Settings,
                "F4" => mainForm.F4Settings,
                "F5" => mainForm.F5Settings,
                "F6" => mainForm.F6Settings,
                _ => null
            };
        }

        public void SetVariables()
        {
            var mainForm = this.Owner as Form1;
            if (mainForm == null) return;

            foreach (string key in FKeys)
            {
                var c = GetControlsForKey(key);
                var s = GetSettingsForKey(key);
                s.Type = c.type.SelectedItem?.ToString() ?? "Dynamic";
                s.NegativeImage = c.negative.Checked;
                s.Contrast = ParseIntOrZero(c.contrast);
                s.Brightness = ParseIntOrZero(c.brightness);
                s.Despeckle = ParseIntOrZero(c.despeckle);
                s.FilterThresholdStepup = ParseIntOrZero(c.filterStepup);
                s.Tolerance = ParseIntOrZero(c.tolerance);
                s.DespeckleFilter = ParseIntOrZero(c.despeckleFilter);
            }
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

    }

}
