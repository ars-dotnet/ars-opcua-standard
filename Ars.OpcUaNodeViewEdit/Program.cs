
using SharpNodeSettings.View;

class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.Run(new FormNodeSetting("settings.xml"));
    }
}
