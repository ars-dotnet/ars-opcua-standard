using Ars.Common.OpcUaTool.Node.NodeBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ars.Common.OpcUaTool.Node.Server
{
    public class NodeModbusServer : ServerNode
    {

        /// <summary>
        /// 实例化一个Modbus服务器的节点对象
        /// </summary>
        public NodeModbusServer( )
        {
            Name = "Modbus 服务器";
            Description = "这是一个Modbus服务器";
            ServerType = ServerNode.ModbusServer;
        }



    }
}
