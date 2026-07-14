namespace TikTokGenerator.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private TableLayoutPanel rootLayout;
    private Label titleLabel;
    private Label countryLabel;
    private ComboBox countryComboBox;
    private Label categoryLabel;
    private ComboBox categoryComboBox;
    private Button findTrendsButton;
    private ListBox trendsListBox;
    private Label selectedTopicLabel;
    private TextBox selectedTopicTextBox;
    private Button generateShortButton;
    private Label progressLabel;
    private ProgressBar progressBar;
    private Label statusLabel;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _trendBindingSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        titleLabel = new Label();
        countryLabel = new Label();
        countryComboBox = new ComboBox();
        categoryLabel = new Label();
        categoryComboBox = new ComboBox();
        findTrendsButton = new Button();
        trendsListBox = new ListBox();
        selectedTopicLabel = new Label();
        selectedTopicTextBox = new TextBox();
        generateShortButton = new Button();
        progressLabel = new Label();
        progressBar = new ProgressBar();
        statusLabel = new Label();
        rootLayout.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 2;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(titleLabel, 0, 0);
        rootLayout.Controls.Add(countryLabel, 0, 1);
        rootLayout.Controls.Add(countryComboBox, 1, 1);
        rootLayout.Controls.Add(categoryLabel, 0, 2);
        rootLayout.Controls.Add(categoryComboBox, 1, 2);
        rootLayout.Controls.Add(findTrendsButton, 1, 3);
        rootLayout.Controls.Add(trendsListBox, 1, 4);
        rootLayout.Controls.Add(selectedTopicLabel, 0, 5);
        rootLayout.Controls.Add(selectedTopicTextBox, 1, 5);
        rootLayout.Controls.Add(generateShortButton, 1, 6);
        rootLayout.Controls.Add(progressLabel, 0, 7);
        rootLayout.Controls.Add(progressBar, 1, 7);
        rootLayout.Controls.Add(statusLabel, 1, 8);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(24);
        rootLayout.RowCount = 9;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.Size = new Size(620, 560);
        rootLayout.TabIndex = 0;
        // 
        // titleLabel
        // 
        titleLabel.AutoSize = true;
        rootLayout.SetColumnSpan(titleLabel, 2);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
        titleLabel.Location = new Point(27, 24);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(566, 52);
        titleLabel.TabIndex = 0;
        titleLabel.Text = "Generator TikTokow";
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // countryLabel
        // 
        countryLabel.AutoSize = true;
        countryLabel.Dock = DockStyle.Fill;
        countryLabel.Location = new Point(27, 76);
        countryLabel.Name = "countryLabel";
        countryLabel.Size = new Size(124, 42);
        countryLabel.TabIndex = 1;
        countryLabel.Text = "Kraj:";
        countryLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // countryComboBox
        // 
        countryComboBox.Dock = DockStyle.Fill;
        countryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        countryComboBox.FormattingEnabled = true;
        countryComboBox.Items.AddRange(new object[] { "Polska", "USA", "Niemcy", "Wielka Brytania" });
        countryComboBox.Location = new Point(157, 79);
        countryComboBox.Name = "countryComboBox";
        countryComboBox.Size = new Size(436, 33);
        countryComboBox.TabIndex = 2;
        // 
        // categoryLabel
        // 
        categoryLabel.AutoSize = true;
        categoryLabel.Dock = DockStyle.Fill;
        categoryLabel.Location = new Point(27, 118);
        categoryLabel.Name = "categoryLabel";
        categoryLabel.Size = new Size(124, 42);
        categoryLabel.TabIndex = 3;
        categoryLabel.Text = "Kategoria:";
        categoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // categoryComboBox
        // 
        categoryComboBox.Dock = DockStyle.Fill;
        categoryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        categoryComboBox.FormattingEnabled = true;
        categoryComboBox.Items.AddRange(new object[] { "Technologia", "Biznes", "Lifestyle", "Edukacja" });
        categoryComboBox.Location = new Point(157, 121);
        categoryComboBox.Name = "categoryComboBox";
        categoryComboBox.Size = new Size(436, 33);
        categoryComboBox.TabIndex = 4;
        // 
        // findTrendsButton
        // 
        findTrendsButton.AutoSize = true;
        findTrendsButton.Dock = DockStyle.Left;
        findTrendsButton.Location = new Point(157, 171);
        findTrendsButton.Margin = new Padding(3, 11, 3, 8);
        findTrendsButton.Name = "findTrendsButton";
        findTrendsButton.Size = new Size(197, 33);
        findTrendsButton.TabIndex = 5;
        findTrendsButton.Text = "Znajdz popularne tematy";
        findTrendsButton.UseVisualStyleBackColor = true;
        findTrendsButton.Click += findTrendsButton_Click;
        // 
        // trendsListBox
        // 
        trendsListBox.Dock = DockStyle.Fill;
        trendsListBox.FormattingEnabled = true;
        trendsListBox.ItemHeight = 25;
        trendsListBox.Location = new Point(157, 215);
        trendsListBox.Name = "trendsListBox";
        trendsListBox.Size = new Size(436, 150);
        trendsListBox.TabIndex = 6;
        // 
        // selectedTopicLabel
        // 
        selectedTopicLabel.AutoSize = true;
        selectedTopicLabel.Dock = DockStyle.Fill;
        selectedTopicLabel.Location = new Point(27, 368);
        selectedTopicLabel.Name = "selectedTopicLabel";
        selectedTopicLabel.Size = new Size(124, 42);
        selectedTopicLabel.TabIndex = 7;
        selectedTopicLabel.Text = "Wybrany temat:";
        selectedTopicLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // selectedTopicTextBox
        // 
        selectedTopicTextBox.Dock = DockStyle.Fill;
        selectedTopicTextBox.Location = new Point(157, 373);
        selectedTopicTextBox.Margin = new Padding(3, 5, 3, 3);
        selectedTopicTextBox.Name = "selectedTopicTextBox";
        selectedTopicTextBox.Size = new Size(436, 31);
        selectedTopicTextBox.TabIndex = 8;
        // 
        // generateShortButton
        // 
        generateShortButton.AutoSize = true;
        generateShortButton.Dock = DockStyle.Left;
        generateShortButton.Location = new Point(157, 421);
        generateShortButton.Margin = new Padding(3, 11, 3, 8);
        generateShortButton.Name = "generateShortButton";
        generateShortButton.Size = new Size(139, 33);
        generateShortButton.TabIndex = 9;
        generateShortButton.Text = "Wygeneruj short";
        generateShortButton.UseVisualStyleBackColor = true;
        generateShortButton.Click += generateShortButton_Click;
        // 
        // progressLabel
        // 
        progressLabel.AutoSize = true;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.Location = new Point(27, 462);
        progressLabel.Name = "progressLabel";
        progressLabel.Size = new Size(124, 42);
        progressLabel.TabIndex = 10;
        progressLabel.Text = "Postep:";
        progressLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // progressBar
        // 
        progressBar.Dock = DockStyle.Fill;
        progressBar.Location = new Point(157, 471);
        progressBar.Margin = new Padding(3, 9, 3, 9);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(436, 24);
        progressBar.TabIndex = 11;
        // 
        // statusLabel
        // 
        statusLabel.AutoEllipsis = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Location = new Point(157, 504);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(436, 42);
        statusLabel.TabIndex = 12;
        statusLabel.Text = "Gotowe do pracy.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(10F, 25F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 560);
        Controls.Add(rootLayout);
        Font = new Font("Segoe UI", 10.5F);
        MinimumSize = new Size(560, 500);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Generator TikTokow";
        rootLayout.ResumeLayout(false);
        rootLayout.PerformLayout();
        ResumeLayout(false);
    }
}
