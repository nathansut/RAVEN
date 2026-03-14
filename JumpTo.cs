using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace RAVEN
{
    public partial class JumpTo : Form
    {
        // Public property to store the JumpToPage value
        public int? JumpToPage { get; private set; } = null;

        public JumpTo()
        {
            InitializeComponent();
            this.KeyPreview = true; // Enable key preview to capture key presses
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Enter)
            {
                // Set JumpToPage to some value. Here, just an example value is used.
                // You would actually get this value from the user input within this form.
                JumpToPage = int.Parse(Regex.Replace(textBox1.Text, @"[^\d]", "")); // Example value, replace with actual logic to get the user's input
                
                this.DialogResult = DialogResult.OK; // Indicate success
                this.Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel; // Indicate cancellation
                this.Close();
            }
        }

        private void JumpTo_Load(object sender, EventArgs e)
        {
            // Load event logic (if any) goes here
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }
        /*
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // JumpTo
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "JumpTo";
            this.Load += new System.EventHandler(this.JumpTo_Load_1);
            this.ResumeLayout(false);

        }

        private void JumpTo_Load_1(object sender, EventArgs e)
        {

        }*/ 
    }
}
