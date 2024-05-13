using Ars.Common.OpcUaTool.Core;
using Ars.Common.OpcUaTool.Node.Regular;
using Ars.Common.OpcUaTool.Node.Request;
using Ars.Common.OpcUaTool.Node.Server;
using Ars.OpcUaTool.Extensions;
using HslCommunication;
using HslCommunication.Core;
using Opc.Ua;
using Opc.Ua.Server;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Xml.Linq;
using Range = Opc.Ua.Range;

namespace Ars.Common.OpcUaTool.OpcUaCoreOverLoads
{
    /// <summary>
    /// 
    /// </summary>
    internal class ArsCustomNodeManager2 : CustomNodeManager2
    {

        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ArsCustomNodeManager2(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.ReferenceApplications)
        {
            SystemContext.NodeIdFactory = this;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<ReferenceServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new ReferenceServerConfiguration();
            }

            m_dynamicNodes = new List<BaseDataVariableState>();
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                string id = instance.Parent.NodeId.Identifier as string;

                if (id != null)
                {
                    return new NodeId(id + "_" + instance.SymbolicName, instance.Parent.NodeId.NamespaceIndex);
                }
            }

            return node.NodeId;
        }
        #endregion

        #region Private Helper Functions
        private static bool IsUnsignedAnalogType(BuiltInType builtInType)
        {
            if (builtInType == BuiltInType.Byte ||
                builtInType == BuiltInType.UInt16 ||
                builtInType == BuiltInType.UInt32 ||
                builtInType == BuiltInType.UInt64)
            {
                return true;
            }
            return false;
        }

        private static bool IsAnalogType(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Byte:
                case BuiltInType.UInt16:
                case BuiltInType.UInt32:
                case BuiltInType.UInt64:
                case BuiltInType.SByte:
                case BuiltInType.Int16:
                case BuiltInType.Int32:
                case BuiltInType.Int64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                    return true;
            }
            return false;
        }

        private static Range GetAnalogRange(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.UInt16:
                    return new Range(ushort.MaxValue, ushort.MinValue);
                case BuiltInType.UInt32:
                    return new Range(uint.MaxValue, uint.MinValue);
                case BuiltInType.UInt64:
                    return new Range(ulong.MaxValue, ulong.MinValue);
                case BuiltInType.SByte:
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
                case BuiltInType.Int16:
                    return new Range(short.MaxValue, short.MinValue);
                case BuiltInType.Int32:
                    return new Range(int.MaxValue, int.MinValue);
                case BuiltInType.Int64:
                    return new Range(long.MaxValue, long.MinValue);
                case BuiltInType.Float:
                    return new Range(float.MaxValue, float.MinValue);
                case BuiltInType.Double:
                    return new Range(double.MaxValue, double.MinValue);
                case BuiltInType.Byte:
                    return new Range(byte.MaxValue, byte.MinValue);
                default:
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
            }
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                LoadPredefinedNodes(SystemContext, externalReferences);

                IList<IReference>? references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }


                dict_BaseDataVariableState = new Dictionary<string, BaseDataVariableState>();
                dicRegularNodeId = new Dictionary<string, IList<string>>();
                try
                {
                    // =========================================================================================
                    // 
                    // 此处需要加载本地文件，并且创建对应的节点信息，
                    // 
                    // =========================================================================================
                    sharpNodeServer = new SharpNodeServer();
                    sharpNodeServer.WriteCustomerData = (deviceCore, name) => {
                        string opcNode = "ns=2;s=" + string.Join("/", deviceCore.DeviceNodes) + "/" + name;
                        lock (Lock)
                        {
                            if (dict_BaseDataVariableState.ContainsKey(opcNode))
                            {
                                //这里存放采集的数据
                                dict_BaseDataVariableState[opcNode].Value = deviceCore.GetDynamicValueByName(name);

                                dict_BaseDataVariableState[opcNode].ClearChangeMasks(SystemContext, false);
                            }
                        }
                    };

                    XElement element = XElement.Load("settings.xml");
                    dicRegularItemNode = Util.ParesRegular(element);

                    AddNodeClass(null, element, references);


                    // 加载配置文件之前设置写入方法
                    sharpNodeServer.LoadByXmlFile("settings.xml");

                    // 最后再启动服务器信息
                    sharpNodeServer.ServerStart(12345);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Error creating the address space.");

                    ExceptionDispatchInfo.Capture(e).Throw();
                }
            }
        }

        private void AddNodeClass(NodeState? parent, XElement nodeClass, IList<IReference> references)
        {
            foreach (var xmlNode in nodeClass.Elements())
            {
                if (xmlNode.Name == "NodeClass")
                {
                    Node.NodeBase.NodeClass nClass = new Node.NodeBase.NodeClass();
                    nClass.LoadByXmlElement(xmlNode);

                    FolderState son;
                    if (parent == null)
                    {
                        son = CreateFolder(null, nClass.Name);
                        son.Description = nClass.Description;
                        son.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                        references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, son.NodeId));
                        son.EventNotifier = EventNotifiers.SubscribeToEvents;
                        AddRootNotifier(son);

                        AddNodeClass(son, xmlNode, references);
                        AddPredefinedNode(SystemContext, son);
                    }
                    else
                    {
                        son = CreateFolder(parent, nClass.Name, nClass.Description);
                        AddNodeClass(son, xmlNode, references);
                    }
                }
                else if (xmlNode.Name == "DeviceNode")
                {
                    AddDeviceCore(parent, xmlNode);
                }
                else if (xmlNode.Name == "Server")
                {
                    AddServer(parent, xmlNode, references);
                }
            }
        }

        private void AddDeviceCore(NodeState parent, XElement device)
        {
            if (device.Name == "DeviceNode")
            {
                // 提取名称和描述信息
                string name = device.Attribute("Name").Value;
                string description = device.Attribute("Description").Value;

                // 创建OPC节点
                FolderState deviceFolder = CreateFolder(parent, device.Attribute("Name").Value, device.Attribute("Description").Value);
                // 添加Request
                foreach (var requestXml in device.Elements("DeviceRequest"))
                {
                    DeviceRequest deviceRequest = new DeviceRequest();
                    deviceRequest.LoadByXmlElement(requestXml);

                    AddDeviceRequest(deviceFolder, deviceRequest);
                }
            }
        }

        private void AddServer(NodeState parent, XElement xmlNode, IList<IReference> references)
        {
            int serverType = int.Parse(xmlNode.Attribute("ServerType").Value);
            if (serverType == ServerNode.ModbusServer)
            {
                NodeModbusServer serverNode = new NodeModbusServer();
                serverNode.LoadByXmlElement(xmlNode);

                FolderState son = CreateFolder(parent, serverNode.Name, serverNode.Description);
                AddNodeClass(son, xmlNode, references);
            }
            else if (serverType == ServerNode.AlienServer)
            {
                AlienServerNode alienNode = new AlienServerNode();
                alienNode.LoadByXmlElement(xmlNode);

                FolderState son = CreateFolder(parent, alienNode.Name, alienNode.Description);
                AddNodeClass(son, xmlNode, references);
            }
        }
        private void AddDeviceRequest(NodeState parent, DeviceRequest deviceRequest)
        {
            // 提炼真正的数据节点
            if (!dicRegularItemNode.ContainsKey(deviceRequest.PraseRegularCode)) return;
            List<RegularItemNode> regularNodes = dicRegularItemNode[deviceRequest.PraseRegularCode];

            BaseDataVariableState? dataVariableState;
            foreach (var regularNode in regularNodes)
            {
                dataVariableState = default;

                if (regularNode.RegularCode == RegularNodeTypeItem.Bool.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Boolean, ValueRanks.Scalar, default(bool));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Boolean, ValueRanks.OneDimension, new bool[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.Byte.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Byte, ValueRanks.Scalar, default(byte));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Byte, ValueRanks.OneDimension, new byte[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.Int16.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Int16, ValueRanks.Scalar, default(short));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Int16, ValueRanks.OneDimension, new short[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.UInt16.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.UInt16, ValueRanks.Scalar, default(ushort));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.UInt16, ValueRanks.OneDimension, new ushort[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.Int32.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Int32, ValueRanks.Scalar, default(int));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Int32, ValueRanks.OneDimension, new int[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.UInt32.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.UInt32, ValueRanks.Scalar, default(uint));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.UInt32, ValueRanks.OneDimension, new uint[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.Float.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Float, ValueRanks.Scalar, default(float));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Float, ValueRanks.OneDimension, new float[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.Int64.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Int64, ValueRanks.Scalar, default(long));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Int64, ValueRanks.OneDimension, new long[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.UInt64.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.UInt64, ValueRanks.Scalar, default(ulong));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.UInt64, ValueRanks.OneDimension, new ulong[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.Double.Code)
                {
                    if (regularNode.TypeLength == 1)
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Double, ValueRanks.Scalar, default(double));
                    }
                    else
                    {
                        dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.Double, ValueRanks.OneDimension, new double[regularNode.TypeLength]);
                    }
                }
                else if (regularNode.RegularCode == RegularNodeTypeItem.StringAscii.Code ||
                    regularNode.RegularCode == RegularNodeTypeItem.StringUnicode.Code ||
                    regularNode.RegularCode == RegularNodeTypeItem.StringUtf8.Code)
                {

                    dataVariableState = CreateBaseVariable(parent, regularNode.Name, regularNode.Description, DataTypeIds.String, ValueRanks.Scalar, "");
                }

                if (null != dataVariableState)
                {
                    dict_BaseDataVariableState.Add(dataVariableState.NodeId.ToString(), dataVariableState);

                    if (dicRegularNodeId.TryGetValue(deviceRequest.PraseRegularCode,out var values))
                    {
                        values.Add(dataVariableState.NodeId.ToString());
                    }
                    else
                    {
                        dicRegularNodeId.Add(
                            deviceRequest.PraseRegularCode,
                            new List<string> { dataVariableState.NodeId.ToString()}
                        );
                    }
                }
            }

        }


        /// <summary>
        /// 创建一个新的节点，节点名称为字符串
        /// </summary>
        protected FolderState CreateFolder(NodeState parent, string name)
        {
            return CreateFolder(parent, name, string.Empty);
        }

        /// <summary>
        /// 创建一个新的节点，节点名称为字符串
        /// </summary>
        protected FolderState CreateFolder(NodeState parent, string name, string description)
        {
            FolderState folder = new FolderState(parent);

            folder.SymbolicName = name;
            folder.ReferenceTypeId = ReferenceTypes.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;
            folder.Description = description;
            if (parent == null)
            {
                folder.NodeId = new NodeId(name, NamespaceIndex);
            }
            else
            {
                folder.NodeId = new NodeId(parent.NodeId.ToString() + "/" + name);
            }
            folder.BrowseName = new QualifiedName(name, NamespaceIndex);
            folder.DisplayName = new LocalizedText(name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        /// <summary>
        /// 创建一个值节点，类型需要在创建的时候指定
        /// </summary>
        protected BaseDataVariableState CreateBaseVariable(NodeState parent, string name, string description, NodeId dataType, int valueRank, object defaultValue)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            if (parent == null)
            {
                variable.NodeId = new NodeId(name, NamespaceIndex);
            }
            else
            {
                variable.NodeId = new NodeId(parent.NodeId.ToString() + "/" + name);
            }
            variable.Description = description;
            variable.BrowseName = new QualifiedName(name, NamespaceIndex);
            variable.DisplayName = new LocalizedText(name);
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = defaultValue;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.Now;
            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0, 0 });
            }

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private DataItemState CreateDataItemVariable(NodeState parent, string path, string name, BuiltInType dataType, int valueRank)
        {
            DataItemState variable = new DataItemState(parent);
            variable.ValuePrecision = new PropertyState<double>(variable);
            variable.Definition = new PropertyState<string>(variable);

            variable.Create(
                SystemContext,
                null,
                variable.BrowseName,
                null,
                true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.DataType = (uint)dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = TypeInfo.GetDefaultValue((uint)dataType, valueRank, Server.TypeTree);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0, 0 });
            }

            variable.ValuePrecision.Value = 2;
            variable.ValuePrecision.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.ValuePrecision.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Definition.Value = string.Empty;
            variable.Definition.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Definition.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        private DataItemState[] CreateDataItemVariables(NodeState parent, string path, string name, BuiltInType dataType, int valueRank, ushort numVariables)
        {
            List<DataItemState> itemsCreated = new List<DataItemState>();
            // create the default name first:
            itemsCreated.Add(CreateDataItemVariable(parent, path, name, dataType, valueRank));
            // now to create the remaining NUMBERED items
            for (uint i = 0; i < numVariables; i++)
            {
                string newName = string.Format("{0}{1}", name, i.ToString("000"));
                string newPath = string.Format("{0}/Mass/{1}", path, newName);
                itemsCreated.Add(CreateDataItemVariable(parent, newPath, newName, dataType, valueRank));
            }//for i
            return itemsCreated.ToArray();
        }

        private ServiceResult OnWriteDataItem(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            DataItemState variable = node as DataItemState;

            // verify data type.
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (typeInfo.BuiltInType != BuiltInType.DateTime)
            {
                double number = Convert.ToDouble(value);
                number = Math.Round(number, (int)variable.ValuePrecision.Value);
                value = TypeInfo.Cast(number, typeInfo.BuiltInType);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private AnalogItemState CreateAnalogItemVariable(NodeState parent, string path, string name, BuiltInType dataType, int valueRank)
        {
            return CreateAnalogItemVariable(parent, path, name, dataType, valueRank, null);
        }

        private AnalogItemState CreateAnalogItemVariable(NodeState parent, string path, string name, BuiltInType dataType, int valueRank, object initialValues)
        {
            return CreateAnalogItemVariable(parent, path, name, dataType, valueRank, initialValues, null);
        }

        private AnalogItemState CreateAnalogItemVariable(NodeState parent, string path, string name, BuiltInType dataType, int valueRank, object initialValues, Range customRange)
        {
            return CreateAnalogItemVariable(parent, path, name, (uint)dataType, valueRank, initialValues, customRange);
        }

        private AnalogItemState CreateAnalogItemVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank, object initialValues, Range customRange)
        {
            AnalogItemState variable = new AnalogItemState(parent);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.EngineeringUnits = new PropertyState<EUInformation>(variable);
            variable.InstrumentRange = new PropertyState<Range>(variable);

            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                null,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0, 0 });
            }

            BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType, Server.TypeTree);

            // Simulate a mV Voltmeter
            Range newRange = GetAnalogRange(builtInType);
            // Using anything but 120,-10 fails a few tests
            newRange.High = Math.Min(newRange.High, 120);
            newRange.Low = Math.Max(newRange.Low, -10);
            variable.InstrumentRange.Value = newRange;

            if (customRange != null)
            {
                variable.EURange.Value = customRange;
            }
            else
            {
                variable.EURange.Value = new Range(100, 0);
            }

            if (initialValues == null)
            {
                variable.Value = TypeInfo.GetDefaultValue(dataType, valueRank, Server.TypeTree);
            }
            else
            {
                variable.Value = initialValues;
            }

            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            // The latest UNECE version (Rev 11, published in 2015) is available here:
            // http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/rec20_latest_08052015.zip
            variable.EngineeringUnits.Value = new EUInformation("mV", "millivolt", "http://www.opcfoundation.org/UA/units/un/cefact");
            // The mapping of the UNECE codes to OPC UA(EUInformation.unitId) is available here:
            // http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/UNECE_to_OPCUA.csv
            variable.EngineeringUnits.Value.UnitId = 12890; // "2Z"
            variable.OnWriteValue = OnWriteAnalog;
            variable.EURange.OnWriteValue = OnWriteAnalogRange;
            variable.EURange.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EURange.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EngineeringUnits.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EngineeringUnits.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange.OnWriteValue = OnWriteAnalogRange;
            variable.InstrumentRange.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private DataItemState CreateTwoStateDiscreteItemVariable(NodeState parent, string path, string name, string trueState, string falseState)
        {
            TwoStateDiscreteState variable = new TwoStateDiscreteState(parent);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;

            variable.Create(
                SystemContext,
                null,
                variable.BrowseName,
                null,
                true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = DataTypeIds.Boolean;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = (bool)GetNewValue(variable);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            variable.TrueState.Value = trueState;
            variable.TrueState.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.TrueState.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            variable.FalseState.Value = falseState;
            variable.FalseState.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.FalseState.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private DataItemState CreateMultiStateDiscreteItemVariable(NodeState parent, string path, string name, params string[] values)
        {
            MultiStateDiscreteState variable = new MultiStateDiscreteState(parent);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;

            variable.Create(
                SystemContext,
                null,
                variable.BrowseName,
                null,
                true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = DataTypeIds.UInt32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = (uint)0;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            variable.OnWriteValue = OnWriteDiscrete;

            LocalizedText[] strings = new LocalizedText[values.Length];

            for (int ii = 0; ii < strings.Length; ii++)
            {
                strings[ii] = values[ii];
            }

            variable.EnumStrings.Value = strings;
            variable.EnumStrings.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EnumStrings.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        /// <summary>
        /// Creates a new UInt32 variable.
        /// </summary>
        private DataItemState CreateMultiStateValueDiscreteItemVariable(NodeState parent, string path, string name, params string[] enumNames)
        {
            return CreateMultiStateValueDiscreteItemVariable(parent, path, name, null, enumNames);
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private DataItemState CreateMultiStateValueDiscreteItemVariable(NodeState parent, string path, string name, NodeId nodeId, params string[] enumNames)
        {
            MultiStateValueDiscreteState variable = new MultiStateValueDiscreteState(parent);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;

            variable.Create(
                SystemContext,
                null,
                variable.BrowseName,
                null,
                true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = nodeId == null ? DataTypeIds.UInt32 : nodeId;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = (uint)0;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            variable.OnWriteValue = OnWriteValueDiscrete;

            // there are two enumerations for this type:
            // EnumStrings = the string representations for enumerated values
            // ValueAsText = the actual enumerated value

            // set the enumerated strings
            LocalizedText[] strings = new LocalizedText[enumNames.Length];
            for (int ii = 0; ii < strings.Length; ii++)
            {
                strings[ii] = enumNames[ii];
            }

            // set the enumerated values
            EnumValueType[] values = new EnumValueType[enumNames.Length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new EnumValueType();
                values[ii].Value = ii;
                values[ii].Description = strings[ii];
                values[ii].DisplayName = strings[ii];
            }
            variable.EnumValues.Value = values;
            variable.EnumValues.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EnumValues.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.ValueAsText.Value = variable.EnumValues.Value[0].DisplayName;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        private ServiceResult OnWriteDiscrete(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            MultiStateDiscreteState variable = node as MultiStateDiscreteState;

            // verify data type.
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            double number = Convert.ToDouble(value);

            if (number >= variable.EnumStrings.Value.Length | number < 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteValueDiscrete(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            MultiStateValueDiscreteState variable = node as MultiStateValueDiscreteState;

            TypeInfo typeInfo = TypeInfo.Construct(value);

            if (variable == null ||
                typeInfo == null ||
                typeInfo == TypeInfo.Unknown ||
                !TypeInfo.IsNumericType(typeInfo.BuiltInType))
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            int number = Convert.ToInt32(value);
            if (number >= variable.EnumValues.Value.Length || number < 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            if (!node.SetChildValue(context, BrowseNames.ValueAsText, variable.EnumValues.Value[number].DisplayName, true))
            {
                return StatusCodes.BadOutOfRange;
            }

            node.ClearChangeMasks(context, true);

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteAnalog(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            AnalogItemState variable = node as AnalogItemState;

            // verify data type.
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            // check index range.
            if (variable.ValueRank >= 0)
            {
                if (indexRange != NumericRange.Empty)
                {
                    object target = variable.Value;
                    ServiceResult result = indexRange.UpdateRange(ref target, value);

                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    value = target;
                }
            }

            // check instrument range.
            else
            {
                if (indexRange != NumericRange.Empty)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                double number = Convert.ToDouble(value);

                if (variable.InstrumentRange != null && (number < variable.InstrumentRange.Value.Low || number > variable.InstrumentRange.Value.High))
                {
                    return StatusCodes.BadOutOfRange;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteAnalogRange(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            PropertyState<Range> variable = node as PropertyState<Range>;
            ExtensionObject extensionObject = value as ExtensionObject;
            TypeInfo typeInfo = TypeInfo.Construct(value);

            if (variable == null ||
                extensionObject == null ||
                typeInfo == null ||
                typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            Range newRange = extensionObject.Body as Range;
            AnalogItemState parent = variable.Parent as AnalogItemState;
            if (newRange == null ||
                parent == null)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            TypeInfo parentTypeInfo = TypeInfo.Construct(parent.Value);
            Range parentRange = GetAnalogRange(parentTypeInfo.BuiltInType);
            if (parentRange.High < newRange.High ||
                parentRange.Low > newRange.Low)
            {
                return StatusCodes.BadOutOfRange;
            }

            value = newRange;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateVariable(NodeState parent, string path, string name, BuiltInType dataType, int valueRank)
        {
            return CreateVariable(parent, path, name, (uint)dataType, valueRank);
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = GetNewValue(variable);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0, 0 });
            }

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }



        private object GetNewValue(BaseVariableState variable)
        {
            if (m_generator == null)
            {
                m_generator = new Opc.Ua.Test.DataGenerator(null);
                m_generator.BoundaryValueFrequency = 0;
            }

            object value = null;

            while (value == null)
            {
                value = m_generator.GetRandom(variable.DataType, variable.ValueRank, new uint[] { 10 }, Server.TypeTree);
            }

            return value;
        }

        private void DoSimulation(object state)
        {
            try
            {
                lock (Lock)
                {
                    foreach (BaseDataVariableState variable in m_dynamicNodes)
                    {
                        variable.Value = GetNewValue(variable);
                        variable.Timestamp = DateTime.UtcNow;
                        variable.ClearChangeMasks(SystemContext, false);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error doing simulation.");
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace. 
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                if (!PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    return null;
                }

                NodeHandle handle = new NodeHandle();

                handle.NodeId = nodeId;
                handle.Node = node;
                handle.Validated = true;

                return handle;
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
           ServerSystemContext context,
           NodeHandle handle,
           IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            // TBD

            return null;
        }
        #endregion

        #region SharpNodeSettings Server

        private SharpNodeServer sharpNodeServer = null;
        private Dictionary<string, List<RegularItemNode>> dicRegularItemNode = null;
        private Dictionary<string, BaseDataVariableState> dict_BaseDataVariableState;    // 节点管理器
        private Dictionary<string, IList<string>> dicRegularNodeId; //regular-nodeid关系数据 

        #endregion

        #region Overrides

        //将值写入底层系统
        protected override void Write(ServerSystemContext context, IList<WriteValue> nodesToWrite, IList<ServiceResult> errors, List<NodeHandle> nodesToValidate, IDictionary<NodeId, NodeState> cache)
        {
            // validates the nodes (reads values from the underlying data source if required).
            for (int ii = 0; ii < nodesToValidate.Count; ii++)
            {
                NodeHandle handle = nodesToValidate[ii];

                lock (Lock)
                {
                    // validate node.
                    NodeState source = ValidateNode(context, handle, cache);

                    if (source == null)
                    {
                        Utils.LogError("writes the value to the underlying system error,nodeState not be null");

                        continue;
                    }

                    WriteValue nodeToWrite = nodesToWrite[handle.Index];

                    //获取到Device
                    var readWriteDevice = GetReadWriteNetByNodeId(nodeToWrite.NodeId.ToString(),out string address);

                    if (null == readWriteDevice)
                    {
                        Utils.LogError("writes the value to the underlying system error, readWriteDevice not be null");

                        continue;
                    }

                    //获取DB值，DeviceNode.Address + RegularNode.Index / 2
                    string? dbAddress = GetDbAddress(nodeToWrite.NodeId.ToString(), address);

                    if (string.IsNullOrEmpty(dbAddress))
                    {
                        Utils.LogError("writes the value to the underlying system error,dbAddress not be null");

                        continue;
                    }

                    //写PLC值
                    var writeMethod = readWriteDevice.GetType().GetMethod("Write", new Type[] {typeof(string), nodeToWrite.Value.Value.GetType() });

                    if (null == writeMethod)
                    {
                        Utils.LogError("writes the value to the underlying system error, writeMethod not be null");

                        continue;
                    }

                    var res = (OperateResult)writeMethod.Invoke(readWriteDevice, new object[] { dbAddress!, nodeToWrite.Value.Value });

                    Utils.LogInfo($"write nodeid:{nodeToWrite.NodeId} value {nodeToWrite.Value.Value} to the underlying system {(res.IsSuccess ? "success" : "failed")}");

                    // updates to source finished - report changes to monitored items.
                    source.ClearChangeMasks(context, false);
                }
            }
        }

        #endregion

        #region Private Fields
        private ReferenceServerConfiguration m_configuration;
        private Opc.Ua.Test.DataGenerator m_generator;
        private ushort m_simulationInterval = 1000;
        private bool m_simulationEnabled = true;
        private List<BaseDataVariableState> m_dynamicNodes;
        #endregion

        #region Private Function

        /// <summary>
        /// 获取设备访问实例
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        private IReadWriteNet? GetReadWriteNetByNodeId(string nodeId,out string address)
        {
            address = string.Empty;

            var regular = GetRegularName(nodeId);

            if (string.IsNullOrEmpty(regular))
                return null;

            var deviceCore = sharpNodeServer.deviceCores
                .Where(r => r.Requests.Any(t => t.PraseRegularCode.Equals(regular)))
                .FirstOrDefault();

            address = deviceCore.Requests[0].Address;

            return deviceCore?.ReadWriteDevice;
        }

        /// <summary>
        /// 获取规则名称
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        private string? GetRegularName(string nodeId)
        {
            var regular = dicRegularNodeId.Where(r => r.Value.Any(t => t.Equals(nodeId))).FirstOrDefault().Key;

            return regular;
        }

        /// <summary>
        /// 获取规则节点名称
        /// </summary>
        /// <returns></returns>
        private string? GetRegularItemNodeName(string nodeId)
        {
            int index = nodeId.LastIndexOf("/");

            return nodeId?.Substring(index + 1);
        }

        /// <summary>
        /// 获取当前节点写入PLC的点位
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="fromDbAddress"></param>
        /// <returns></returns>
        private string? GetDbAddress(string nodeId,string fromDbAddress)
        {
            string? dbAddress = null;

            string? regular = GetRegularName(nodeId);
            string? regularItemCodeName = GetRegularItemNodeName(nodeId);

            if (string.IsNullOrEmpty(regular) || string.IsNullOrEmpty(regularItemCodeName))
            {
                return dbAddress;
            }

            RegularItemNode? regularItemNode = dicRegularItemNode
                .FirstOrDefault(r => r.Key.Equals(regular))
                .Value
                .FirstOrDefault(r => r.Name.Equals(regularItemCodeName));

            if (null == regularItemNode)
            {
                return dbAddress;
            }

            return regularItemNode.GetFromDbIndex(fromDbAddress);
        }

        #endregion
    }
}
