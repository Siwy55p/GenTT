using TikTokGenerator.Models;
using TikTokGenerator.Services;
using System.Text.Json;

namespace TikTokGenerator.Forms;

public partial class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly TrendService _trendService;
    private readonly ShortGenerator _shortGenerator;
    private readonly AppSettings _appSettings;
    private readonly BindingSource _trendBindingSource = new();
    private string _lastGeneratedBriefJson = string.Empty;
    private bool _updatingBriefText;
    private bool _briefWasManuallyEdited;

    public MainForm(
        TrendService trendService,
        ShortGenerator shortGenerator,
        AppSettings appSettings)
    {
        _trendService = trendService;
        _shortGenerator = shortGenerator;
        _appSettings = appSettings;

        InitializeComponent();
        ConfigureControls();
    }

    private void ConfigureControls()
    {
        countryComboBox.SelectedIndex = 0;
        categoryComboBox.SelectedIndex = 0;
        pexelsApiKeyTextBox.Text = AppSettingsService.ResolvePexelsApiKey(_appSettings);
        pixabayApiKeyTextBox.Text = AppSettingsService.ResolvePixabayApiKey(_appSettings);

        trendsListBox.DataSource = _trendBindingSource;
        trendsListBox.DisplayMember = nameof(Trend.Title);
        trendsListBox.SelectedIndexChanged += TrendsListBox_SelectedIndexChanged;
        selectedTopicTextBox.TextChanged += (_, _) => UpdateBriefFromCurrentInputs(force: false);
        sourceTextTextBox.TextChanged += (_, _) => UpdateBriefFromCurrentInputs(force: false);
        categoryComboBox.SelectedIndexChanged += (_, _) => UpdateBriefFromCurrentInputs(force: false);
        briefTextBox.TextChanged += BriefTextBox_TextChanged;

        UpdateBriefFromCurrentInputs(force: true);
    }

    private async void findTrendsButton_Click(object sender, EventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            progressBar.Value = 10;
            statusLabel.Text = "Laduje tematy startowe...";

            var trends = await _trendService.FindPopularTopicsAsync(
                countryComboBox.Text,
                categoryComboBox.Text);

            _trendBindingSource.DataSource = trends;
            trendsListBox.SelectedIndex = trends.Count > 0 ? 0 : -1;
            if (trends.Count == 0)
            {
                UpdateBriefFromCurrentInputs(force: true);
            }

            progressBar.Value = 100;
            statusLabel.Text = $"Zaladowano {trends.Count} tematow startowych.";
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
                SourceUrl = sourceUrlTextBox.Text.Trim(),
                Brief = ParseBrief()
            };

            var options = AppSettingsService.CreateShortGeneratorOptions(
                _appSettings,
                pexelsApiKeyTextBox.Text.Trim(),
                pixabayApiKeyTextBox.Text.Trim());

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

    private ContentBrief ParseBrief()
    {
        try
        {
            var thematicFallback = ContentBriefService.CreateForTopic(
                selectedTopicTextBox.Text.Trim(),
                sourceTextTextBox.Text.Trim(),
                categoryComboBox.Text.Trim());
            var brief = JsonSerializer.Deserialize<ContentBrief>(briefTextBox.Text, JsonOptions)
                ?? thematicFallback;
            return ContentBriefService.FillMissing(brief, thematicFallback);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Brief JSON jest niepoprawny: {ex.Message}", ex);
        }
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
            UpdateBriefFromCurrentInputs(force: true);
        }
    }

    private void BriefTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (_updatingBriefText)
        {
            return;
        }

        _briefWasManuallyEdited = !briefTextBox.Text.Equals(_lastGeneratedBriefJson, StringComparison.Ordinal);
    }

    private void UpdateBriefFromCurrentInputs(bool force)
    {
        if (!force && _briefWasManuallyEdited)
        {
            return;
        }

        var brief = ContentBriefService.CreateForTopic(
            selectedTopicTextBox.Text.Trim(),
            sourceTextTextBox.Text.Trim(),
            categoryComboBox.Text.Trim(),
            GetCurrentBriefDuration());
        SetBriefText(brief);
    }

    private int GetCurrentBriefDuration()
    {
        try
        {
            var current = JsonSerializer.Deserialize<ContentBrief>(briefTextBox.Text, JsonOptions);
            return current?.DurationSeconds > 0 ? current.DurationSeconds : 25;
        }
        catch (JsonException)
        {
            return 25;
        }
    }

    private void SetBriefText(ContentBrief brief)
    {
        var json = JsonSerializer.Serialize(brief, JsonOptions);
        _updatingBriefText = true;
        try
        {
            briefTextBox.Text = json;
            _lastGeneratedBriefJson = json;
            _briefWasManuallyEdited = false;
        }
        finally
        {
            _updatingBriefText = false;
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
        briefTextBox.Enabled = enabled;
        pexelsApiKeyTextBox.Enabled = enabled;
        pixabayApiKeyTextBox.Enabled = enabled;
        trendsListBox.Enabled = enabled;
    }
}
