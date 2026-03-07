namespace RAVEN
{
    partial class AddText
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
            textBox2 = new System.Windows.Forms.TextBox();
            textBox3 = new System.Windows.Forms.TextBox();
            button1 = new System.Windows.Forms.Button();
            SuspendLayout();
            // 
            // textBox1
            // 
            textBox1.Location = new System.Drawing.Point(32, 45);
            textBox1.Name = "textBox1";
            textBox1.Size = new System.Drawing.Size(218, 23);
            textBox1.TabIndex = 0;
            // 
            // textBox2
            // 
            textBox2.Location = new System.Drawing.Point(32, 91);
            textBox2.Name = "textBox2";
            textBox2.Size = new System.Drawing.Size(218, 23);
            textBox2.TabIndex = 1;
            // 
            // textBox3
            // 
            textBox3.Location = new System.Drawing.Point(32, 140);
            textBox3.Name = "textBox3";
            textBox3.Size = new System.Drawing.Size(218, 23);
            textBox3.TabIndex = 2;
            // 
            // button1
            // 
            button1.Location = new System.Drawing.Point(32, 186);
            button1.Name = "button1";
            button1.Size = new System.Drawing.Size(218, 23);
            button1.TabIndex = 3;
            button1.Text = "Sync Text";
            button1.UseVisualStyleBackColor = true;
            // 
            // AddText
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(333, 298);
            Controls.Add(button1);
            Controls.Add(textBox3);
            Controls.Add(textBox2);
            Controls.Add(textBox1);
            Name = "AddText";
            Text = "AddText";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.TextBox textBox3;
        private System.Windows.Forms.Button button1;
    }
}