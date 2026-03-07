namespace RAVEN
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            textBox1 = new System.Windows.Forms.TextBox();
            button_go = new System.Windows.Forms.Button();
            btnThresholdSettings = new System.Windows.Forms.Button();
            fileSystemWatcher1 = new System.IO.FileSystemWatcher();
            keyPicture2 = new KeyPicture();
            keyPicture1 = new KeyPicture();
            NextImage = new System.Windows.Forms.Button();
            PrevImage = new System.Windows.Forms.Button();
            buHelp = new System.Windows.Forms.Button();
            buPrecalcBorders = new System.Windows.Forms.Button();
            button1 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)fileSystemWatcher1).BeginInit();
            SuspendLayout();
            // 
            // textBox1
            // 
            textBox1.Location = new System.Drawing.Point(3, 5);
            textBox1.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            textBox1.Name = "textBox1";
            textBox1.Size = new System.Drawing.Size(1191, 31);
            textBox1.TabIndex = 16;
            textBox1.TextChanged += textBox1_TextChanged;
            // 
            // button_go
            // 
            button_go.Location = new System.Drawing.Point(1206, 5);
            button_go.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            button_go.Name = "button_go";
            button_go.Size = new System.Drawing.Size(79, 45);
            button_go.TabIndex = 15;
            button_go.Text = "Go";
            button_go.UseVisualStyleBackColor = true;
            button_go.Click += button5_Click;
            // 
            // btnThresholdSettings
            // 
            btnThresholdSettings.Location = new System.Drawing.Point(1629, 5);
            btnThresholdSettings.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            btnThresholdSettings.Name = "btnThresholdSettings";
            btnThresholdSettings.Size = new System.Drawing.Size(211, 45);
            btnThresholdSettings.TabIndex = 21;
            btnThresholdSettings.Text = "ThresholdSettings";
            btnThresholdSettings.UseVisualStyleBackColor = true;
            btnThresholdSettings.Click += buttonx_Click;
            // 
            // fileSystemWatcher1
            // 
            fileSystemWatcher1.EnableRaisingEvents = true;
            fileSystemWatcher1.SynchronizingObject = this;
            // 
            // keyPicture2
            // 
            keyPicture2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            keyPicture2.Image = null;
            keyPicture2.Location = new System.Drawing.Point(1337, 62);
            keyPicture2.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            keyPicture2.Name = "keyPicture2";
            keyPicture2.Size = new System.Drawing.Size(1165, 1424);
            keyPicture2.TabIndex = 26;
            // 
            // keyPicture1
            // 
            keyPicture1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            keyPicture1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            keyPicture1.Image = null;
            keyPicture1.Location = new System.Drawing.Point(71, 62);
            keyPicture1.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            keyPicture1.Name = "keyPicture1";
            keyPicture1.Size = new System.Drawing.Size(1210, 1424);
            keyPicture1.TabIndex = 20;
            keyPicture1.Paint += keyPicture1_Paint;
            // 
            // NextImage
            // 
            NextImage.Location = new System.Drawing.Point(1383, 5);
            NextImage.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            NextImage.Name = "NextImage";
            NextImage.Size = new System.Drawing.Size(79, 45);
            NextImage.TabIndex = 27;
            NextImage.Text = "Next";
            NextImage.UseVisualStyleBackColor = true;
            NextImage.Click += NextImage_Click;
            // 
            // PrevImage
            // 
            PrevImage.Location = new System.Drawing.Point(1293, 5);
            PrevImage.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            PrevImage.Name = "PrevImage";
            PrevImage.Size = new System.Drawing.Size(79, 45);
            PrevImage.TabIndex = 28;
            PrevImage.Text = "Prev";
            PrevImage.UseVisualStyleBackColor = true;
            PrevImage.Click += PrevImage_Click;
            // 
            // buHelp
            // 
            buHelp.Location = new System.Drawing.Point(1471, 5);
            buHelp.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            buHelp.Name = "buHelp";
            buHelp.Size = new System.Drawing.Size(147, 45);
            buHelp.TabIndex = 31;
            buHelp.Text = "Help [F1]";
            buHelp.UseVisualStyleBackColor = true;
            buHelp.Click += TestMe_Click;
            // 
            // buPrecalcBorders
            // 
            buPrecalcBorders.Location = new System.Drawing.Point(2280, 2);
            buPrecalcBorders.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            buPrecalcBorders.Name = "buPrecalcBorders";
            buPrecalcBorders.Size = new System.Drawing.Size(159, 45);
            buPrecalcBorders.TabIndex = 33;
            buPrecalcBorders.Text = "Precalc Borders";
            buPrecalcBorders.UseVisualStyleBackColor = true;
            buPrecalcBorders.Visible = false;
            buPrecalcBorders.Click += button2_Click;
            // 
            // button1
            // 
            button1.AllowDrop = true;
            button1.Location = new System.Drawing.Point(1866, 7);
            button1.Margin = new System.Windows.Forms.Padding(6, 5, 6, 5);
            button1.Name = "button1";
            button1.Size = new System.Drawing.Size(60, 45);
            button1.TabIndex = 34;
            button1.Text = "Test";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click_2;
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(10F, 25F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(3061, 1760);
            Controls.Add(button1);
            Controls.Add(buPrecalcBorders);
            Controls.Add(PrevImage);
            Controls.Add(NextImage);
            Controls.Add(btnThresholdSettings);
            Controls.Add(buHelp);
            Controls.Add(keyPicture1);
            Controls.Add(keyPicture2);
            Controls.Add(textBox1);
            Controls.Add(button_go);
            Name = "Form1";
            Text = "RAVEN - Restoration And Visual ENhancement";
            FormClosing += Form1_closing;
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)fileSystemWatcher1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Button button_go;
        private System.Windows.Forms.Button btnThresholdSettings;
        private System.IO.FileSystemWatcher fileSystemWatcher1;
        private KeyPicture keyPicture2;
        private System.Windows.Forms.Button PrevImage;
        private System.Windows.Forms.Button NextImage;
        private KeyPicture keyPicture1;
        private System.Windows.Forms.Button buHelp;
        private System.Windows.Forms.Button buPrecalcBorders;
        private System.Windows.Forms.Button button1;
    }
}

