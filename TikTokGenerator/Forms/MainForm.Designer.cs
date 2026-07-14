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
    private Label sourceUrlLabel;
    private TextBox sourceUrlTextBox;
    private Label sourceTextLabel;
    private TextBox sourceTextTextBox;
    private Label briefLabel;
    private TextBox briefTextBox;
    private Label pexelsApiKeyLabel;
    private TextBox pexelsApiKeyTextBox;
    private Label pixabayApiKeyLabel;
    private TextBox pixabayApiKeyTextBox;
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
        sourceUrlLabel = new Label();
        sourceUrlTextBox = new TextBox();
        sourceTextLabel = new Label();
        sourceTextTextBox = new TextBox();
        briefLabel = new Label();
        briefTextBox = new TextBox();
        pexelsApiKeyLabel = new Label();
        pexelsApiKeyTextBox = new TextBox();
        pixabayApiKeyLabel = new Label();
        pixabayApiKeyTextBox = new TextBox();
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
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
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
        rootLayout.Controls.Add(sourceUrlLabel, 0, 6);
        rootLayout.Controls.Add(sourceUrlTextBox, 1, 6);
        rootLayout.Controls.Add(sourceTextLabel, 0, 7);
        rootLayout.Controls.Add(sourceTextTextBox, 1, 7);
        rootLayout.Controls.Add(briefLabel, 0, 8);
        rootLayout.Controls.Add(briefTextBox, 1, 8);
        rootLayout.Controls.Add(pexelsApiKeyLabel, 0, 9);
        rootLayout.Controls.Add(pexelsApiKeyTextBox, 1, 9);
        rootLayout.Controls.Add(pixabayApiKeyLabel, 0, 10);
        rootLayout.Controls.Add(pixabayApiKeyTextBox, 1, 10);
        rootLayout.Controls.Add(generateShortButton, 1, 11);
        rootLayout.Controls.Add(progressLabel, 0, 12);
        rootLayout.Controls.Add(progressBar, 1, 12);
        rootLayout.Controls.Add(statusLabel, 1, 13);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(24);
        rootLayout.RowCount = 14;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        rootLayout.Size = new Size(760, 900);
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
        titleLabel.Size = new Size(706, 52);
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
        countryLabel.Size = new Size(144, 42);
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
        countryComboBox.Location = new Point(177, 79);
        countryComboBox.Name = "countryComboBox";
        countryComboBox.Size = new Size(556, 33);
        countryComboBox.TabIndex = 2;
        // 
        // categoryLabel
        // 
        categoryLabel.AutoSize = true;
        categoryLabel.Dock = DockStyle.Fill;
        categoryLabel.Location = new Point(27, 118);
        categoryLabel.Name = "categoryLabel";
        categoryLabel.Size = new Size(144, 42);
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
        categoryComboBox.Location = new Point(177, 121);
        categoryComboBox.Name = "categoryComboBox";
        categoryComboBox.Size = new Size(556, 33);
        categoryComboBox.TabIndex = 4;
        // 
        // findTrendsButton
        // 
        findTrendsButton.AutoSize = true;
        findTrendsButton.Dock = DockStyle.Left;
        findTrendsButton.Location = new Point(177, 171);
        findTrendsButton.Margin = new Padding(3, 11, 3, 8);
        findTrendsButton.Name = "findTrendsButton";
        findTrendsButton.Size = new Size(197, 33);
        findTrendsButton.TabIndex = 5;
        findTrendsButton.Text = "Pokaz tematy startowe";
        findTrendsButton.UseVisualStyleBackColor = true;
        findTrendsButton.Click += findTrendsButton_Click;
        // 
        // trendsListBox
        // 
        trendsListBox.Dock = DockStyle.Fill;
        trendsListBox.FormattingEnabled = true;
        trendsListBox.ItemHeight = 25;
        trendsListBox.Location = new Point(177, 215);
        trendsListBox.Name = "trendsListBox";
        trendsListBox.Size = new Size(556, 119);
        trendsListBox.TabIndex = 6;
        // 
        // selectedTopicLabel
        // 
        selectedTopicLabel.AutoSize = true;
        selectedTopicLabel.Dock = DockStyle.Fill;
        selectedTopicLabel.Location = new Point(27, 340);
        selectedTopicLabel.Name = "selectedTopicLabel";
        selectedTopicLabel.Size = new Size(144, 42);
        selectedTopicLabel.TabIndex = 7;
        selectedTopicLabel.Text = "Wybrany temat:";
        selectedTopicLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // selectedTopicTextBox
        // 
        selectedTopicTextBox.Dock = DockStyle.Fill;
        selectedTopicTextBox.Location = new Point(177, 345);
        selectedTopicTextBox.Margin = new Padding(3, 5, 3, 3);
        selectedTopicTextBox.Name = "selectedTopicTextBox";
        selectedTopicTextBox.Size = new Size(556, 31);
        selectedTopicTextBox.TabIndex = 8;
        // 
        // sourceUrlLabel
        // 
        sourceUrlLabel.AutoSize = true;
        sourceUrlLabel.Dock = DockStyle.Fill;
        sourceUrlLabel.Location = new Point(27, 382);
        sourceUrlLabel.Name = "sourceUrlLabel";
        sourceUrlLabel.Size = new Size(144, 42);
        sourceUrlLabel.TabIndex = 9;
        sourceUrlLabel.Text = "URL zrodla:";
        sourceUrlLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // sourceUrlTextBox
        // 
        sourceUrlTextBox.Dock = DockStyle.Fill;
        sourceUrlTextBox.Location = new Point(177, 387);
        sourceUrlTextBox.Margin = new Padding(3, 5, 3, 3);
        sourceUrlTextBox.Name = "sourceUrlTextBox";
        sourceUrlTextBox.Size = new Size(556, 31);
        sourceUrlTextBox.TabIndex = 10;
        // 
        // sourceTextLabel
        // 
        sourceTextLabel.AutoSize = true;
        sourceTextLabel.Dock = DockStyle.Fill;
        sourceTextLabel.Location = new Point(27, 424);
        sourceTextLabel.Name = "sourceTextLabel";
        sourceTextLabel.Size = new Size(144, 128);
        sourceTextLabel.TabIndex = 11;
        sourceTextLabel.Text = "Material zrodlowy:";
        sourceTextLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // sourceTextTextBox
        // 
        sourceTextTextBox.AcceptsReturn = true;
        sourceTextTextBox.Dock = DockStyle.Fill;
        sourceTextTextBox.Location = new Point(177, 429);
        sourceTextTextBox.Margin = new Padding(3, 5, 3, 5);
        sourceTextTextBox.Multiline = true;
        sourceTextTextBox.Name = "sourceTextTextBox";
        sourceTextTextBox.ScrollBars = ScrollBars.Vertical;
        sourceTextTextBox.Size = new Size(556, 118);
        sourceTextTextBox.TabIndex = 12;
        // 
        // briefLabel
        // 
        briefLabel.AutoSize = true;
        briefLabel.Dock = DockStyle.Fill;
        briefLabel.Location = new Point(27, 552);
        briefLabel.Name = "briefLabel";
        briefLabel.Size = new Size(144, 128);
        briefLabel.TabIndex = 13;
        briefLabel.Text = "Brief JSON:";
        briefLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // briefTextBox
        // 
        briefTextBox.AcceptsReturn = true;
        briefTextBox.Dock = DockStyle.Fill;
        briefTextBox.Location = new Point(177, 557);
        briefTextBox.Margin = new Padding(3, 5, 3, 5);
        briefTextBox.Multiline = true;
        briefTextBox.Name = "briefTextBox";
        briefTextBox.ScrollBars = ScrollBars.Vertical;
        briefTextBox.Size = new Size(556, 118);
        briefTextBox.TabIndex = 14;
        // 
        // pexelsApiKeyLabel
        // 
        pexelsApiKeyLabel.AutoSize = true;
        pexelsApiKeyLabel.Dock = DockStyle.Fill;
        pexelsApiKeyLabel.Location = new Point(27, 680);
        pexelsApiKeyLabel.Name = "pexelsApiKeyLabel";
        pexelsApiKeyLabel.Size = new Size(144, 42);
        pexelsApiKeyLabel.TabIndex = 15;
        pexelsApiKeyLabel.Text = "Pexels API:";
        pexelsApiKeyLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // pexelsApiKeyTextBox
        // 
        pexelsApiKeyTextBox.Dock = DockStyle.Fill;
        pexelsApiKeyTextBox.Location = new Point(177, 685);
        pexelsApiKeyTextBox.Margin = new Padding(3, 5, 3, 3);
        pexelsApiKeyTextBox.Name = "pexelsApiKeyTextBox";
        pexelsApiKeyTextBox.PasswordChar = '*';
        pexelsApiKeyTextBox.Size = new Size(556, 31);
        pexelsApiKeyTextBox.TabIndex = 16;
        // 
        // pixabayApiKeyLabel
        // 
        pixabayApiKeyLabel.AutoSize = true;
        pixabayApiKeyLabel.Dock = DockStyle.Fill;
        pixabayApiKeyLabel.Location = new Point(27, 722);
        pixabayApiKeyLabel.Name = "pixabayApiKeyLabel";
        pixabayApiKeyLabel.Size = new Size(144, 42);
        pixabayApiKeyLabel.TabIndex = 17;
        pixabayApiKeyLabel.Text = "Pixabay API:";
        pixabayApiKeyLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // pixabayApiKeyTextBox
        // 
        pixabayApiKeyTextBox.Dock = DockStyle.Fill;
        pixabayApiKeyTextBox.Location = new Point(177, 727);
        pixabayApiKeyTextBox.Margin = new Padding(3, 5, 3, 3);
        pixabayApiKeyTextBox.Name = "pixabayApiKeyTextBox";
        pixabayApiKeyTextBox.PasswordChar = '*';
        pixabayApiKeyTextBox.Size = new Size(556, 31);
        pixabayApiKeyTextBox.TabIndex = 18;
        // 
        // generateShortButton
        // 
        generateShortButton.AutoSize = true;
        generateShortButton.Dock = DockStyle.Left;
        generateShortButton.Location = new Point(177, 775);
        generateShortButton.Margin = new Padding(3, 11, 3, 8);
        generateShortButton.Name = "generateShortButton";
        generateShortButton.Size = new Size(139, 33);
        generateShortButton.TabIndex = 19;
        generateShortButton.Text = "Wygeneruj short";
        generateShortButton.UseVisualStyleBackColor = true;
        generateShortButton.Click += generateShortButton_Click;
        // 
        // progressLabel
        // 
        progressLabel.AutoSize = true;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.Location = new Point(27, 816);
        progressLabel.Name = "progressLabel";
        progressLabel.Size = new Size(144, 42);
        progressLabel.TabIndex = 20;
        progressLabel.Text = "Postep:";
        progressLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // progressBar
        // 
        progressBar.Dock = DockStyle.Fill;
        progressBar.Location = new Point(177, 825);
        progressBar.Margin = new Padding(3, 9, 3, 9);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(556, 24);
        progressBar.TabIndex = 21;
        // 
        // statusLabel
        // 
        statusLabel.AutoEllipsis = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Location = new Point(177, 858);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(556, 44);
        statusLabel.TabIndex = 22;
        statusLabel.Text = "Gotowe do pracy.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(10F, 25F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 900);
        Controls.Add(rootLayout);
        Font = new Font("Segoe UI", 10.5F);
        MinimumSize = new Size(720, 820);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Generator TikTokow";
        rootLayout.ResumeLayout(false);
        rootLayout.PerformLayout();
        ResumeLayout(false);
    }
}
