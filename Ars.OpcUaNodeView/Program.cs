using Ars.OpcUaNodeView;

class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FormNodeView("127.0.0.1", 12345));
    }
}
