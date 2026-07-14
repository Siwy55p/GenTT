namespace TikTokGenerator;

using TikTokGenerator.Forms;
using TikTokGenerator.Services;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        using var httpClient = new HttpClient();

        var scriptService = new ScriptService(httpClient);
        var voiceService = new VoiceService();
        var stockVideoService = new StockVideoService(httpClient);
        var videoService = new VideoService();

        Application.Run(new MainForm(
            new TrendService(httpClient),
            new ShortGenerator(scriptService, voiceService, stockVideoService, videoService)));
    }    
}
