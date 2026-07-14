using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Forms;

public partial class MainForm : Form
{
    private readonly TrendService _trendService;
    private readonly ShortGenerator _shortGenerator;
    private readonly BindingSource _trendBindingSource = new();

    public MainForm(
        TrendService trendService,
        ShortGenerator shortGenerator)
    {
        _trendService = trendService;
        _shortGenerator = shortGenerator;

        InitializeComponent();
        ConfigureControls();
    }

    private void ConfigureControls()
    {
        countryComboBox.SelectedIndex = 0;
        categoryComboBox.SelectedIndex = 0;
        pexelsApiKeyTextBox.Text = Environment.GetEnvironmentVariable("PEXELS_API_KEY")
            ?? Environment.GetEnvironmentVariable("PEXELS_API_KEY", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("PEXELS_API_KEY", EnvironmentVariableTarget.Machine)
            ?? string.Empty;

        trendsListBox.DataSource = _trendBindingSource;
        trendsListBox.DisplayMember = nameof(Trend.Title);
        trendsListBox.SelectedIndexChanged += TrendsListBox_SelectedIndexChanged;
    }

    private async void findTrendsButton_Click(object sender, EventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            progressBar.Value = 10;
            statusLabel.Text = "Szukam popularnych tematow...";

            var trends = await _trendService.FindPopularTopicsAsync(
                countryComboBox.Text,
                categoryComboBox.Text);

            _trendBindingSource.DataSource = trends;
            trendsListBox.SelectedIndex = trends.Count > 0 ? 0 : -1;
            progressBar.Value = 100;
            statusLabel.Text = $"Znaleziono {trends.Count} tematow.";
        });
    }

    private async void generateShortButton_Click(object sender, EventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            var topic = selectedTopicTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                MessageBox.Show(
                    this,
                    "Wybierz temat z listy albo wpisz wlasny temat.",
                    "Brak tematu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var sourceText = sourceTextTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                MessageBox.Show(
                    this,
                    "Wklej material zrodlowy. Model ma pisac tylko na podstawie zrodla, nie samego tytulu.",
                    "Brak materialu zrodlowego",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var selectedTopic = new SelectedTopic
            {
                Title = topic,
                SourceText = sourceText,
                SourceUrl = sourceUrlTextBox.Text.Trim()
            };

            var options = new ShortGeneratorOptions
            {
                PexelsApiKey = pexelsApiKeyTextBox.Text.Trim()
            };

            progressBar.Value = 0;
            statusLabel.Text = "Startuje generator...";

            var outputPath = await _shortGenerator.GenerateAsync(
                selectedTopic,
                options,
                new Progress<ShortGenerationProgress>(progress =>
                {
                    progressBar.Value = Math.Clamp(progress.Percent, 0, 100);
                    statusLabel.Text = progress.Message;
                }));

            statusLabel.Text = $"Gotowe: {outputPath}";
            MessageBox.Show(
                this,
                $"Short zostal zapisany tutaj:{Environment.NewLine}{outputPath}",
                "Gotowe",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private Trend GetSelectedTrend(string topic)
    {
        if (trendsListBox.SelectedItem is Trend selectedTrend &&
            selectedTrend.Title.Equals(topic, StringComparison.OrdinalIgnoreCase))
        {
            return selectedTrend;
        }

        return new Trend(
            Rank: 1,
            Title: topic,
            Country: countryComboBox.Text,
            Category: categoryComboBox.Text,
            Source: "Wpisane recznie",
            SourceText: sourceTextTextBox.Text.Trim(),
            SourceUrl: sourceUrlTextBox.Text.Trim(),
            DiscoveredAt: DateTimeOffset.Now);
    }

    private void TrendsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (trendsListBox.SelectedItem is Trend trend)
        {
            selectedTopicTextBox.Text = trend.Title;
            sourceTextTextBox.Text = trend.SourceText;
            sourceUrlTextBox.Text = trend.SourceUrl;
        }
    }

    private async Task RunUiTaskAsync(Func<Task> action)
    {
        ToggleActions(false);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Wystapil blad.";
            MessageBox.Show(this, ex.Message, "Blad", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleActions(true);
        }
    }

    private void ToggleActions(bool enabled)
    {
        findTrendsButton.Enabled = enabled;
        generateShortButton.Enabled = enabled;
        countryComboBox.Enabled = enabled;
        categoryComboBox.Enabled = enabled;
        selectedTopicTextBox.Enabled = enabled;
        sourceTextTextBox.Enabled = enabled;
        sourceUrlTextBox.Enabled = enabled;
        pexelsApiKeyTextBox.Enabled = enabled;
        trendsListBox.Enabled = enabled;
    }
}
