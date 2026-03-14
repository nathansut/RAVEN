using System;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace RAVEN
{
    public partial class Memo : Form
    {
        private string _imageFilePath;

        public Memo(string imageFilePath)
        {
            InitializeComponent();
            this.KeyPreview = true;
            _imageFilePath = imageFilePath;
            this.Text = _imageFilePath;
            this.Shown += Memo_Shown; // Attach the Shown event handler.
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Memo_Save(); // Call SaveComment when form is closing
            base.OnFormClosing(e);
            
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Check if F12 or Esc is pressed
            if (keyData == Keys.F12 || keyData == Keys.Escape)
            {
                this.Close(); // Call SaveComment
                return true; // Indicate that the key press has been handled
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Memo_Shown(object sender, EventArgs e)
        {
            textBox1.Focus();
            textBox1.SelectAll(); // Also select all text in textBox1 for overwriting.
        }

        private void Memo_Save()
        {
            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            string commentsFilePath = Path.Combine(rootPath, "Comments.txt");
            bool commentUpdatedOrAdded = false;
            string _comment = textBox1.Text.Replace("\"", "\"\"").Replace("'", "").Replace(",", "");

            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(commentsFilePath))
                {
                    lines = File.ReadAllLines(commentsFilePath).ToList();

                    // Iterate through the lines to find if a comment for the current image exists
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var parts = lines[i].Split(new[] { ',' }, 2);
                        if (parts.Length == 2)
                        {
                            string imagePart = parts[0].Trim();
                            if (string.Equals(imagePart, _imageFilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                // If the comment is not blank, update the existing comment
                                lines[i] = $"{_imageFilePath},\"{_comment}\"";
                                commentUpdatedOrAdded = true;
                                break;
                            }
                        }
                    }
                }

                // If a comment for the current image wasn't found, or if it's a deletion (blank comment), add/update the line
                if (!commentUpdatedOrAdded)
                {
                    lines.Add($"{_imageFilePath},\"{_comment}\"");
                }

                // Write the updated list back to the file
                File.WriteAllLines(commentsFilePath, lines);               
            }
            catch (IOException ex)
            {
                MessageBox.Show($"There was a problem saving the comment. Please try again.\nError: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        
        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Memo_Load(object sender, EventArgs e)
        {
            // Call LoadCommentForImage and set textBox1.Text with the result
            string comment = LoadCommentForImage(_imageFilePath);
            textBox1.Text = comment; // Assuming textBox1 is your TextBox for displaying the comment
        }


        private string LoadCommentForImage(string imagePath)
        {
            // Determine the root path of the program
            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            string commentsFilePath = Path.Combine(rootPath, "Comments.txt");

            // Check if the Comments.txt file exists
            if (File.Exists(commentsFilePath))
            {
                // Read all lines from the file
                var lines = File.ReadAllLines(commentsFilePath);

                // Loop through each line in the file
                foreach (var line in lines)
                {
                    // Split the line by comma, taking into account potential commas within the comment
                    var parts = line.Split(new[] { ',' }, 2);
                    if (parts.Length == 2)
                    {
                        string imagePart = parts[0].Trim();
                        string commentPart = parts[1].Trim();

                        // Remove quotes from the comment part if present
                        if (commentPart.StartsWith("\"") && commentPart.EndsWith("\""))
                        {
                            commentPart = commentPart.Substring(1, commentPart.Length - 2);
                        }

                        // Check if the current line's image matches the imagePath
                        if (string.Equals(imagePart, imagePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return commentPart;
                        }
                    }
                }
            }

            // Return an empty string if no comment is found or the file doesn't exist
            return string.Empty;
        }
    }
}