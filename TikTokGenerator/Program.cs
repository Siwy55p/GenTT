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

        Application.Run(new MainForm(
            new TrendService(httpClient),
            new ScriptService(httpClient),
            new VoiceService(httpClient),
            new StockVideoService(httpClient),
            new VideoService()));
    }    
}
