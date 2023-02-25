﻿namespace ShareX.ScreenCaptureLib
{
    partial class ScrollingCaptureLightForm
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
            this.components = new System.ComponentModel.Container();
            this.btnCapture = new System.Windows.Forms.Button();
            this.pOutput = new System.Windows.Forms.Panel();
            this.pbOutput = new System.Windows.Forms.PictureBox();
            this.tCapture = new System.Windows.Forms.Timer(this.components);
            this.btnOptions = new System.Windows.Forms.Button();
            this.pOutput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbOutput)).BeginInit();
            this.SuspendLayout();
            // 
            // btnCapture
            // 
            this.btnCapture.Location = new System.Drawing.Point(8, 8);
            this.btnCapture.Name = "btnCapture";
            this.btnCapture.Size = new System.Drawing.Size(144, 32);
            this.btnCapture.TabIndex = 0;
            this.btnCapture.Text = "Capture...";
            this.btnCapture.UseVisualStyleBackColor = true;
            this.btnCapture.Click += new System.EventHandler(this.btnCapture_Click);
            // 
            // pOutput
            // 
            this.pOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pOutput.AutoScroll = true;
            this.pOutput.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pOutput.Controls.Add(this.pbOutput);
            this.pOutput.Location = new System.Drawing.Point(8, 48);
            this.pOutput.Name = "pOutput";
            this.pOutput.Size = new System.Drawing.Size(968, 605);
            this.pOutput.TabIndex = 2;
            // 
            // pbOutput
            // 
            this.pbOutput.Location = new System.Drawing.Point(0, 0);
            this.pbOutput.Name = "pbOutput";
            this.pbOutput.Size = new System.Drawing.Size(100, 100);
            this.pbOutput.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.pbOutput.TabIndex = 0;
            this.pbOutput.TabStop = false;
            // 
            // tCapture
            // 
            this.tCapture.Tick += new System.EventHandler(this.tCapture_Tick);
            // 
            // btnOptions
            // 
            this.btnOptions.Location = new System.Drawing.Point(160, 8);
            this.btnOptions.Name = "btnOptions";
            this.btnOptions.Size = new System.Drawing.Size(144, 32);
            this.btnOptions.TabIndex = 1;
            this.btnOptions.Text = "Options...";
            this.btnOptions.UseVisualStyleBackColor = true;
            this.btnOptions.Click += new System.EventHandler(this.btnOptions_Click);
            // 
            // ScrollingCaptureLightForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(984, 661);
            this.Controls.Add(this.btnOptions);
            this.Controls.Add(this.pOutput);
            this.Controls.Add(this.btnCapture);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "ScrollingCaptureLightForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "ShareX - Scrolling capture";
            this.pOutput.ResumeLayout(false);
            this.pOutput.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbOutput)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button btnCapture;
        private System.Windows.Forms.Panel pOutput;
        private System.Windows.Forms.PictureBox pbOutput;
        private System.Windows.Forms.Timer tCapture;
        private System.Windows.Forms.Button btnOptions;
    }
}