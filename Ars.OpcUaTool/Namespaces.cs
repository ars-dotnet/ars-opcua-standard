using System;
using System.Collections.Generic;
using System.Text;

namespace Ars.Common.OpcUaTool
{
    /// <summary>
    /// Defines constants for namespaces used by the application.
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The namespace for the nodes provided by the server.
        /// </summary>
        public const string ReferenceApplications = "http://opcfoundation.org/Quickstarts/ReferenceApplications";

        /// <summary>
        /// The URI for the Alarms namespace (.NET code namespace is 'Alarms').
        /// </summary>
        public const string Alarms = "http://test.org/UA/Alarms/";
    }
}
