namespace FLScanIE
{
    partial class MainWindow
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
            System.Windows.Forms.GroupBox groupBoxFilter;
            System.Windows.Forms.Label label2;
            System.Windows.Forms.Label label1;
            this.checkedListBoxChecks = new System.Windows.Forms.CheckedListBox();
            this.checkedListBoxLogLevel = new System.Windows.Forms.CheckedListBox();
            this.checkedListBoxFilter = new System.Windows.Forms.CheckedListBox();
            this.textBoxFilterExclude = new System.Windows.Forms.TextBox();
            this.infocardsBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.checkVanillaFilesExist = new System.Windows.Forms.CheckBox();
            this.checkDisableUTF = new System.Windows.Forms.CheckBox();
            this.buttonBrowseFLPath = new System.Windows.Forms.Button();
            this.buttonScan = new System.Windows.Forms.Button();
            this.buttonAbout = new System.Windows.Forms.Button();
            this.richTextBox = new System.Windows.Forms.RichTextBox();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.textBoxFlPath = new System.Windows.Forms.TextBox();
            groupBoxFilter = new System.Windows.Forms.GroupBox();
            label2 = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            groupBoxFilter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.infocardsBindingSource)).BeginInit();
            this.tabPage1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBoxFilter
            // 
            groupBoxFilter.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            groupBoxFilter.Controls.Add(this.checkedListBoxChecks);
            groupBoxFilter.Controls.Add(label2);
            groupBoxFilter.Controls.Add(this.checkedListBoxLogLevel);
            groupBoxFilter.Controls.Add(this.checkedListBoxFilter);
            groupBoxFilter.Controls.Add(this.textBoxFilterExclude);
            groupBoxFilter.Location = new System.Drawing.Point(12, 102);
            groupBoxFilter.Margin = new System.Windows.Forms.Padding(4);
            groupBoxFilter.Name = "groupBoxFilter";
            groupBoxFilter.Padding = new System.Windows.Forms.Padding(4);
            groupBoxFilter.Size = new System.Drawing.Size(709, 154);
            groupBoxFilter.TabIndex = 20;
            groupBoxFilter.TabStop = false;
            groupBoxFilter.Text = "Filter";
            // 
            // checkedListBoxChecks
            // 
            this.checkedListBoxChecks.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.checkedListBoxChecks.FormattingEnabled = true;
            this.checkedListBoxChecks.Location = new System.Drawing.Point(108, 23);
            this.checkedListBoxChecks.Margin = new System.Windows.Forms.Padding(4);
            this.checkedListBoxChecks.Name = "checkedListBoxChecks";
            this.checkedListBoxChecks.Size = new System.Drawing.Size(143, 106);
            this.checkedListBoxChecks.TabIndex = 8;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(425, 23);
            label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(58, 16);
            label2.TabIndex = 7;
            label2.Text = "Exclude:";
            // 
            // checkedListBoxLogLevel
            // 
            this.checkedListBoxLogLevel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.checkedListBoxLogLevel.FormattingEnabled = true;
            this.checkedListBoxLogLevel.Items.AddRange(new object[] {
            "Info",
            "Warning",
            "Error",
            "Fatal"});
            this.checkedListBoxLogLevel.Location = new System.Drawing.Point(8, 23);
            this.checkedListBoxLogLevel.Margin = new System.Windows.Forms.Padding(4);
            this.checkedListBoxLogLevel.Name = "checkedListBoxLogLevel";
            this.checkedListBoxLogLevel.Size = new System.Drawing.Size(91, 106);
            this.checkedListBoxLogLevel.TabIndex = 6;
            // 
            // checkedListBoxFilter
            // 
            this.checkedListBoxFilter.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.checkedListBoxFilter.FormattingEnabled = true;
            this.checkedListBoxFilter.Items.AddRange(new object[] {
            "Setting not found",
            "File not found",
            "Hardpoint not found",
            "ID not found",
            "Wrong Value-Count"});
            this.checkedListBoxFilter.Location = new System.Drawing.Point(260, 23);
            this.checkedListBoxFilter.Margin = new System.Windows.Forms.Padding(4);
            this.checkedListBoxFilter.Name = "checkedListBoxFilter";
            this.checkedListBoxFilter.Size = new System.Drawing.Size(160, 106);
            this.checkedListBoxFilter.TabIndex = 5;
            this.checkedListBoxFilter.SelectedIndexChanged += new System.EventHandler(this.checkedListBoxFilter_SelectedIndexChanged);
            // 
            // textBoxFilterExclude
            // 
            this.textBoxFilterExclude.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxFilterExclude.Location = new System.Drawing.Point(429, 43);
            this.textBoxFilterExclude.Margin = new System.Windows.Forms.Padding(4);
            this.textBoxFilterExclude.Multiline = true;
            this.textBoxFilterExclude.Name = "textBoxFilterExclude";
            this.textBoxFilterExclude.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxFilterExclude.Size = new System.Drawing.Size(271, 95);
            this.textBoxFilterExclude.TabIndex = 4;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(8, 16);
            label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(102, 16);
            label1.TabIndex = 12;
            label1.Text = "Freelancer Path";
            // 
            // infocardsBindingSource
            // 
            this.infocardsBindingSource.DataMember = "Infocards";
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.checkVanillaFilesExist);
            this.tabPage1.Controls.Add(this.checkDisableUTF);
            this.tabPage1.Controls.Add(groupBoxFilter);
            this.tabPage1.Controls.Add(this.buttonBrowseFLPath);
            this.tabPage1.Controls.Add(this.buttonScan);
            this.tabPage1.Controls.Add(this.buttonAbout);
            this.tabPage1.Controls.Add(this.richTextBox);
            this.tabPage1.Controls.Add(this.textBoxFlPath);
            this.tabPage1.Controls.Add(label1);
            this.tabPage1.Location = new System.Drawing.Point(4, 25);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage1.Size = new System.Drawing.Size(732, 468);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Commands";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // checkVanillaFilesExist
            // 
            this.checkVanillaFilesExist.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.checkVanillaFilesExist.AutoSize = true;
            this.checkVanillaFilesExist.Location = new System.Drawing.Point(352, 71);
            this.checkVanillaFilesExist.Margin = new System.Windows.Forms.Padding(4);
            this.checkVanillaFilesExist.Name = "checkVanillaFilesExist";
            this.checkVanillaFilesExist.Size = new System.Drawing.Size(177, 20);
            this.checkVanillaFilesExist.TabIndex = 21;
            this.checkVanillaFilesExist.Text = "Assume vanilla files exist";
            this.toolTip.SetToolTip(this.checkVanillaFilesExist, "Loading all UTF-Files can take some time..");
            this.checkVanillaFilesExist.UseVisualStyleBackColor = true;
            this.checkVanillaFilesExist.CheckedChanged += new System.EventHandler(this.checkVanillaFilesExist_CheckedChanged);
            // 
            // checkDisableUTF
            // 
            this.checkDisableUTF.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.checkDisableUTF.AutoSize = true;
            this.checkDisableUTF.Location = new System.Drawing.Point(567, 71);
            this.checkDisableUTF.Margin = new System.Windows.Forms.Padding(4);
            this.checkDisableUTF.Name = "checkDisableUTF";
            this.checkDisableUTF.Size = new System.Drawing.Size(155, 20);
            this.checkDisableUTF.TabIndex = 8;
            this.checkDisableUTF.Text = "Disable UTF-Checks";
            this.toolTip.SetToolTip(this.checkDisableUTF, "Loading all UTF-Files can take some time..");
            this.checkDisableUTF.UseVisualStyleBackColor = true;
            this.checkDisableUTF.CheckedChanged += new System.EventHandler(this.checkDisableUTF_CheckedChanged);
            // 
            // buttonBrowseFLPath
            // 
            this.buttonBrowseFLPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonBrowseFLPath.Location = new System.Drawing.Point(689, 34);
            this.buttonBrowseFLPath.Margin = new System.Windows.Forms.Padding(4);
            this.buttonBrowseFLPath.Name = "buttonBrowseFLPath";
            this.buttonBrowseFLPath.Size = new System.Drawing.Size(32, 25);
            this.buttonBrowseFLPath.TabIndex = 19;
            this.buttonBrowseFLPath.Text = "...";
            this.buttonBrowseFLPath.UseVisualStyleBackColor = true;
            this.buttonBrowseFLPath.Click += new System.EventHandler(this.buttonBrowseFLPath_Click);
            // 
            // buttonScan
            // 
            this.buttonScan.Location = new System.Drawing.Point(12, 66);
            this.buttonScan.Margin = new System.Windows.Forms.Padding(4);
            this.buttonScan.Name = "buttonScan";
            this.buttonScan.Size = new System.Drawing.Size(100, 28);
            this.buttonScan.TabIndex = 18;
            this.buttonScan.Text = "Scan";
            this.buttonScan.UseVisualStyleBackColor = true;
            this.buttonScan.Click += new System.EventHandler(this.buttonScan_Click);
            // 
            // buttonAbout
            // 
            this.buttonAbout.Location = new System.Drawing.Point(120, 66);
            this.buttonAbout.Margin = new System.Windows.Forms.Padding(4);
            this.buttonAbout.Name = "buttonAbout";
            this.buttonAbout.Size = new System.Drawing.Size(100, 28);
            this.buttonAbout.TabIndex = 17;
            this.buttonAbout.Text = "Help";
            this.buttonAbout.UseVisualStyleBackColor = true;
            this.buttonAbout.Click += new System.EventHandler(this.buttonAbout_Click);
            // 
            // richTextBox
            // 
            this.richTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.richTextBox.Font = new System.Drawing.Font("Consolas", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.richTextBox.Location = new System.Drawing.Point(12, 263);
            this.richTextBox.Margin = new System.Windows.Forms.Padding(4);
            this.richTextBox.Name = "richTextBox";
            this.richTextBox.Size = new System.Drawing.Size(708, 194);
            this.richTextBox.TabIndex = 16;
            this.richTextBox.Text = "";
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Location = new System.Drawing.Point(17, 16);
            this.tabControl1.Margin = new System.Windows.Forms.Padding(4);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(740, 497);
            this.tabControl1.TabIndex = 0;
            // 
            // textBoxFlPath
            // 
            this.textBoxFlPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxFlPath.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::FLScanIE.Properties.Settings.Default, "setFlDir", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            this.textBoxFlPath.Location = new System.Drawing.Point(11, 36);
            this.textBoxFlPath.Margin = new System.Windows.Forms.Padding(4);
            this.textBoxFlPath.Name = "textBoxFlPath";
            this.textBoxFlPath.Size = new System.Drawing.Size(669, 22);
            this.textBoxFlPath.TabIndex = 11;
            this.textBoxFlPath.Text = global::FLScanIE.Properties.Settings.Default.setFlDir;
            this.textBoxFlPath.TextChanged += new System.EventHandler(this.textBoxFlPath_TextChanged);
            // 
            // MainWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(773, 528);
            this.Controls.Add(this.tabControl1);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(639, 469);
            this.Name = "MainWindow";
            this.ShowIcon = false;
            this.Text = "FLScanII";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainWindow_FormClosing);
            this.Load += new System.EventHandler(this.MainWindow_Load);
            groupBoxFilter.ResumeLayout(false);
            groupBoxFilter.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.infocardsBindingSource)).EndInit();
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.BindingSource infocardsBindingSource;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.Button buttonAbout;
        private System.Windows.Forms.RichTextBox richTextBox;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.Button buttonScan;
        private System.Windows.Forms.TextBox textBoxFlPath;
        private System.Windows.Forms.Button buttonBrowseFLPath;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
        private System.Windows.Forms.TextBox textBoxFilterExclude;
        private System.Windows.Forms.CheckedListBox checkedListBoxFilter;
        private System.Windows.Forms.CheckedListBox checkedListBoxLogLevel;
        private System.Windows.Forms.CheckBox checkDisableUTF;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.CheckedListBox checkedListBoxChecks;
        private System.Windows.Forms.CheckBox checkVanillaFilesExist;
    }
}

