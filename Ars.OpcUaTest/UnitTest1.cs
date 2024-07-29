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
            "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int64", //D107
            "ns=2;Devices/WorkFactory01/WorkShop02/ModbusTcpTest/Int64", //7
            "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Float",//D115
            "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Double", //D117
            "ns=2;Devices/分厂二/车间三/ModbusTcp客户端02/温度" //250
        };

        /// <summary>
        /// 测试多节点写
        /// </summary>
        [Fact]
        public async void TestWriteMultipleNodes()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var datas = opcUaClient.WriteNodes(
                tags,
                new object[]
                {
                    (long)6665,
                    (long)6666,
                    12345.6f,
                    345.6677,
                    (short)6667
                });

            //添加订阅
            //第一次读取会订阅到
            //PLC值发生变化会订阅到
            opcUaClient.AddSubscription("A", tags, SubCallback);

            foreach (var data in datas)
            {
                Assert.True(data.Item1); 
            }

            var bbb = await opcUaClient.ReadNodesAsync(tags.Select(r => new NodeId(r)).ToArray());

            Assert.True((long)bbb[0].Value == 6665);
            Assert.True((long)bbb[1].Value == 6666);
            Assert.True((float)bbb[2].Value == 12345.6f);
            Assert.True((double)bbb[3].Value == 345.6677);
            Assert.True((short)bbb[4].Value == 6667);

            opcUaClient.Disconnect();
        }

        /// <summary>
        /// 测试单节点写多数组
        /// </summary>
        [Fact]
        public async void TestWriteArray()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var datas = opcUaClient.WriteNode(
                "ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int16",//D101
                new short[]
                {
                    6665,
                    6666,
                });

            Assert.True(true == datas.Item1);

            var aaa = await opcUaClient.ReadNodeAsync<short[]>("ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int16");

            Assert.True(aaa[0] == 6665);
            Assert.True(aaa[1] == 6666);
            Assert.True(aaa[2] == 0);

            opcUaClient.Disconnect();
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
