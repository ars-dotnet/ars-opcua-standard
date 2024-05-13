
using Ars.Common.OpcUaTool.Device;
using Ars.Common.OpcUaTool.Node.Device;
using Ars.Common.OpcUaTool.Node.Regular;
using Opc.Ua.Server;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Reflection;

namespace Ars.Common.OpcUaTool
{
    /// <summary>
    /// 节点配置类的工具辅助类
    /// </summary>
    public class Util
    {
        #region Static Method

        /// <summary>
        /// 解析一个配置文件中的所有的规则解析，并返回一个词典信息
        /// </summary>
        /// <param name="nodeClass">配置文件的根信息</param>
        /// <returns>词典</returns>
        public static Dictionary<string, List<RegularItemNode>> ParesRegular( XElement nodeClass )
        {
            Dictionary<string, List<RegularItemNode>>  regularkeyValuePairs = new Dictionary<string, List<RegularItemNode>>( );
            foreach (var xmlNode in nodeClass.Elements( ))
            {
                if (xmlNode.Attribute( "Name" ).Value == "Regular")
                {
                    foreach (XElement element in xmlNode.Elements( "RegularNode" ))
                    {
                        List<RegularItemNode> itemNodes = new List<RegularItemNode>( );
                        foreach (XElement xmlItemNode in element.Elements( "RegularItemNode" ))
                        {
                            RegularItemNode regularItemNode = new RegularItemNode( );
                            regularItemNode.LoadByXmlElement( xmlItemNode );
                            itemNodes.Add( regularItemNode );
                        }

                        if (regularkeyValuePairs.ContainsKey( element.Attribute( "Name" ).Value ))
                        {
                            regularkeyValuePairs[element.Attribute( "Name" ).Value] = itemNodes;
                        }
                        else
                        {
                            regularkeyValuePairs.Add( element.Attribute( "Name" ).Value, itemNodes );
                        }
                    }
                }
            }
            return regularkeyValuePairs;
        }


        /// <summary>
        /// 通过真实配置的设备信息，来创建一个真实的设备，如果类型不存在，将返回null
        /// </summary>
        /// <param name="device">设备的配置信息</param>
        /// <returns>真实的设备对象</returns>
        public static DeviceCore CreateFromXElement( XElement device )
        {
            int deviceType = int.Parse( device.Attribute( "DeviceType" ).Value );

            if (deviceType == DeviceNode.ModbusTcpAlien)
            {
                return new DeviceModbusTcpAlien( device );
            }
            else if (deviceType == DeviceNode.ModbusTcpClient)
            {
                return new DeviceModbusTcp( device );
            }
            else if (deviceType == DeviceNode.MelsecMcQna3E)
            {
                return new DeviceMelsecMc( device );
            }
            else if (deviceType == DeviceNode.Omron)
            {
                return new DeviceOmron( device );
            }
            else if (deviceType == DeviceNode.Siemens)
            {
                return new DeviceSiemens( device );
            }
            else
            {
                return null;
            }
        }

        #endregion

        public static IList<INodeManagerFactory?> GetNodeManagerFactories()
        {
            var assembly = typeof(Util).Assembly;
            var nodeManagerFactories = assembly.GetExportedTypes().Select(type => IsINodeManagerFactoryType(type)).Where(type => type != null);
            
            return nodeManagerFactories.ToList();
        }

        /// <summary>
        /// Helper to determine the INodeManagerFactory by reflection.
        /// </summary>
        private static INodeManagerFactory? IsINodeManagerFactoryType(Type type)
        {
            var nodeManagerTypeInfo = type.GetTypeInfo();
            if (nodeManagerTypeInfo.IsAbstract || !typeof(INodeManagerFactory).IsAssignableFrom(type))
            {
                return null;
            }
            return Activator.CreateInstance(type) as INodeManagerFactory;
        }

        public static void ApplyCTTMode(TextWriter output, StandardServer server)
        {
            var methodsToCall = new CallMethodRequestCollection();
            var index = server.CurrentInstance.NamespaceUris.GetIndex(Namespaces.Alarms);
            if (index > 0)
            {
                try
                {
                    methodsToCall.Add(
                        // Start the Alarms with infinite runtime
                        new CallMethodRequest
                        {
                            MethodId = new NodeId("Alarms.Start", (ushort)index),
                            ObjectId = new NodeId("Alarms", (ushort)index),
                            InputArguments = new VariantCollection() { new Variant((UInt32)UInt32.MaxValue) }
                        });
                    var requestHeader = new RequestHeader()
                    {
                        Timestamp = DateTime.UtcNow,
                        TimeoutHint = 10000
                    };
                    var context = new OperationContext(requestHeader, RequestType.Call);
                    server.CurrentInstance.NodeManager.Call(context, methodsToCall, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos);
                    foreach (var result in results)
                    {
                        if (ServiceResult.IsBad(result.StatusCode))
                        {
                            Opc.Ua.Utils.LogError("Error calling method {0}.", result.StatusCode);
                        }
                    }
                    output.WriteLine("The Alarms for CTT mode are active.");
                    return;
                }
                catch (Exception ex)
                {
                    Opc.Ua.Utils.LogError(ex, "Failed to start alarms for CTT.");
                }
            }
            output.WriteLine("The alarms could not be enabled for CTT, the namespace does not exist.");
        }
    }
}
