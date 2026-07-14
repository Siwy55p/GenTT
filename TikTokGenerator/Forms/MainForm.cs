using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Forms;

public partial class MainForm : Form
{
    private readonly TrendService _trendService;
    private readonly ScriptService _scriptService;
    private readonly VoiceService _voiceService;
    private readonly StockVideoService _stockVideoService;
    private readonly VideoService _videoService;
    private readonly BindingSource _trendBindingSource = new();

    public MainForm(
        TrendService trendService,
        ScriptService scriptService,
        VoiceService voiceService,
        StockVideoService stockVideoService,
        VideoService videoService)
    {
        _trendService = trendService;
        _scriptService = scriptService;
        _voiceService = voiceService;
        _stockVideoService = stockVideoService;
        _videoService = videoService;

        InitializeComponent();
        ConfigureControls();
    }

    private void ConfigureControls()
    {
        countryComboBox.SelectedIndex = 0;
        categoryComboBox.SelectedIndex = 0;

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

            progressBar.Value = 15;
            statusLabel.Text = "Przygotowuje scenariusz...";

            var trend = GetSelectedTrend(topic);
            var script = await _scriptService.GenerateScriptAsync(trend);
            progressBar.Value = 35;

            statusLabel.Text = "Przygotowuje lektora...";
            var outputRoot = Path.Combine(AppContext.BaseDirectory, "Output", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var voiceOverPath = await _voiceService.GenerateVoiceOverAsync(script, outputRoot);
            progressBar.Value = 50;

            statusLabel.Text = "Dobieram tlo...";
            var background = await _stockVideoService.FindBackgroundAsync(trend);
            progressBar.Value = 60;

            statusLabel.Text = "Renderuje short...";
            var project = await _videoService.GenerateShortAsync(
                trend,
                script,
                voiceOverPath,
                background,
                new Progress<int>(value => progressBar.Value = Math.Clamp(value, 0, 100)));

            statusLabel.Text = $"Gotowe: {project.OutputPath}";
            MessageBox.Show(
                this,
                $"Short zostal zapisany tutaj:{Environment.NewLine}{project.OutputPath}",
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
            DiscoveredAt: DateTimeOffset.Now);
    }

    private void TrendsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (trendsListBox.SelectedItem is Trend trend)
        {
            selectedTopicTextBox.Text = trend.Title;
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
        trendsListBox.Enabled = enabled;
    }
}
