using Ars.Common.OpcUaTool.OpcUaCoreOverLoads;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Quickstarts;
using System.Globalization;

TextWriter output = Console.Out;
output.WriteLine("{0} OPC UA Reference Server", Utils.IsRunningOnMono() ? "Mono" : ".NET Core");

output.WriteLine("OPC UA library: {0} @ {1} -- {2}",
    Utils.GetAssemblyBuildNumber(),
    Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
    Utils.GetAssemblySoftwareVersion());

// The application name and config file names
var applicationName = Utils.IsRunningOnMono() ? "MonoReferenceServer" : "ConsoleReferenceServer";
var configSectionName = Utils.IsRunningOnMono() ? "Quickstarts.MonoReferenceServer" : "Quickstarts.ReferenceServer";

// command line options
bool showHelp = false;
bool autoAccept = false;
bool logConsole = false;
bool appLog = false;
bool renewCertificate = false;
bool shadowConfig = false;
bool cttMode = false;
string password = null;
int timeout = -1;

var usage = Utils.IsRunningOnMono() ? $"Usage: mono {applicationName}.exe [OPTIONS]" : $"Usage: dotnet {applicationName}.dll [OPTIONS]";
Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                usage,
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "c|console", "log to console", c => logConsole = c != null },
                { "l|log", "log app output", c => appLog = c != null },
                { "p|password=", "optional password for private key", (string p) => password = p },
                { "r|renew", "renew application certificate", r => renewCertificate = r != null },
                { "t|timeout=", "timeout in seconds to exit application", (int t) => timeout = t * 1000 },
                { "s|shadowconfig", "create configuration in pki root", s => shadowConfig = s != null },
                { "ctt", "CTT mode, use to preset alarms for CTT testing.", c => cttMode = c != null },
            };

try
{
    // parse command line and set options
    ConsoleUtils.ProcessCommandLine(output, args, options, ref showHelp, "REFSERVER");

    if (logConsole && appLog)
    {
        output = new LogWriter();
    }

    // create the UA server
    var server = new UAServer<CustomSessionServer>(output)
    {
        AutoAccept = autoAccept,
        Password = password
    };

    // load the server configuration, validate certificates
    output.WriteLine("Loading configuration from {0}.", configSectionName);
    await server.LoadAsync(applicationName, configSectionName).ConfigureAwait(false);

    // use the shadow config to map the config to an externally accessible location
    if (shadowConfig)
    {
        output.WriteLine("Using shadow configuration.");
        var shadowPath = Directory.GetParent(
            Path.GetDirectoryName(
                Utils.ReplaceSpecialFolderNames(server.Configuration.TraceConfiguration.OutputFilePath)!)!)!.FullName;

        var shadowFilePath = Path.Combine(shadowPath, Path.GetFileName(server.Configuration.SourceFilePath));

        if (!File.Exists(shadowFilePath))
        {
            output.WriteLine("Create a copy of the config in the shadow location.");
            File.Copy(server.Configuration.SourceFilePath, shadowFilePath, true);
        }
        output.WriteLine("Reloading configuration from {0}.", shadowFilePath);
        await server.LoadAsync(applicationName, Path.Combine(shadowPath, configSectionName)).ConfigureAwait(false);
    }

    // setup the logging
    ConsoleUtils.ConfigureLogging(server.Configuration, applicationName, logConsole, LogLevel.Information);

    // check or renew the certificate
    output.WriteLine("Check the certificate.");
    await server.CheckCertificateAsync(renewCertificate).ConfigureAwait(false);

    // Create and add the node managers
    //var nodeManagerFactories = Ars.Common.OpcUaTool.Util.GetNodeManagerFactories();
    //if (nodeManagerFactories?.Any() ?? false) 
    //{
    //    server.Create(nodeManagerFactories!);
    //}

    // start the server
    output.WriteLine("Start the server.");
    await server.StartAsync().ConfigureAwait(false);

    // Apply custom settings for CTT testing
    if (cttMode)
    {
        output.WriteLine("Apply settings for CTT.");
        // start Alarms and other settings for CTT test
        Ars.Common.OpcUaTool.Util.ApplyCTTMode(output, server.Server);
    }

    output.WriteLine("Server started. Press Ctrl-C to exit...");

    // wait for timeout or Ctrl-C
    var quitCTS = new CancellationTokenSource();
    var quitEvent = ConsoleUtils.CtrlCHandler(quitCTS);
    bool ctrlc = quitEvent.WaitOne(timeout);

    // stop server. May have to wait for clients to disconnect.
    output.WriteLine("Server stopped. Waiting for exit...");
    await server.StopAsync().ConfigureAwait(false);

    return (int)ExitCode.Ok;
}
catch (ErrorExitException eee)
{
    output.WriteLine("The application exits with error: {0}", eee.Message);
    return (int)eee.ExitCode;
}
