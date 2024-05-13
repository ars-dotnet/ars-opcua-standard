using ConsoleAppNew;
using Opc.Ua.Client;
using Opc.Ua;

namespace Ars.OpcUaTest
{
    public class UnitTest1
    {
        const string connectString = "opc.tcp://127.0.0.1:62541/Quickstarts/ReferenceServer";

        static string[] tags = new string[]
        {
            "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int64",
            "ns=2;Devices/WorkFactory01/WorkShop02/ModbusTcpTest/Int64",
            "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Float",
            "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Double",
            "ns=2;Devices/分厂二/车间三/ModbusTcp客户端02/温度"
        };

        /// <summary>
        /// 测试多节点写
        /// </summary>
        [Fact]
        public async void TestWriteMultipleNodes()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var data = opcUaClient.WriteNodes(
                tags,
                new object[]
                {
                    6665,
                    6666,
                    12345.6f,
                    345.6677,
                    38
                });

            //添加订阅
            //第一次读取会订阅到
            //PLC值发生变化会订阅到
            opcUaClient.AddSubscription("A", tags, SubCallback);

            opcUaClient.Disconnect();

            Assert.True(true == data);
        }

        /// <summary>
        /// 测试单节点写多数组
        /// </summary>
        [Fact]
        public async void TestWriteArray()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var data = opcUaClient.WriteNode(
                "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int16",
                new int[]
                {
                    6665,
                    6666,
                });

            opcUaClient.Disconnect();

            Assert.True(true == data);
        }

        [Fact]
        public async void TestRead()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var data = await opcUaClient.ReadNodeAsync<long>("ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int64");
            var data1 = await opcUaClient.ReadNodeAsync<long>("ns=2;Devices/WorkFactory01/WorkShop02/ModbusTcpTest/Int64");
            var data2 = await opcUaClient.ReadNodeAsync<short>("ns=2;Devices/分厂二/车间三/ModbusTcp客户端02/温度");

            opcUaClient.Disconnect();

            Assert.True(6665 == data);
            Assert.True(6666 == data1);
            Assert.True(38 == data2);
        }

        private static void SubCallback(string key, MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs args)
        {
            MonitoredItemNotification notification = args.NotificationValue as MonitoredItemNotification;
            if (notification != null)
            {
                var showValue = notification.Value.WrappedValue.Value;

                Console.WriteLine($"Key:{monitoredItem.StartNodeId} Value:{showValue}");
            }
        }
    }
}
