using ConsoleAppNew;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ars.OpcUaWebApi
{
    /// <summary>
    /// api
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        const string connectString = "opc.tcp://127.0.0.1:62541/Quickstarts/ReferenceServer";

        /// <summary>
        /// 读
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> ReadOpcUaData()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var data = await opcUaClient.ReadNodeAsync<short>("ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int16");

            opcUaClient.Disconnect();

            return Ok(data);
        }

        /// <summary>
        /// 写
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> WriteOpcUaData()
        {
            OpcUaClient opcUaClient = new OpcUaClient();

            await opcUaClient.ConnectServer(connectString);

            var data = await opcUaClient.WriteNodeAsync<short>("ns=2;Devices/WorkFactory01/WorkShop01/MelsecTest/Int16",(short)8888);

            opcUaClient.Disconnect();

            return Ok(data);
        }
    }
}
