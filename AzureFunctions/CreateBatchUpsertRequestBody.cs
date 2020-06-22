using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System;
using Newtonsoft.Json;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AzureFunctionss
{
    public static class CreateBatchUpsertRequestBody
    {
        private const string BatchSizeHeader = "BatchSize";
        private const string EnvironmentURLHeader = "EnvironmentURL";
        private const string BatchQueryParamsHeader = "BatchQueryParams";
        private const string EntityNameHeader = "EntityName";
        private const string ContentTypeHeader = 
@"
Content-Type: application/json
OData-MaxVersion:4.0
OData-Version:4.0
";

        private static string elementHeader =
@"
--changeset_{0}
Content-Type:application/http
Content-Transfer-Encoding:binary
Content-ID: {1}
OData-MaxVersion:4.0
OData-Version:4.0

PATCH {2}/api/data/v9.1/{3}{4} HTTP/1.1";

        private static string elementFooter =
@"

--changeset_{0}--
--batch_{1}--";

        private static Guid changeSetNumber = Guid.NewGuid();

        [FunctionName("CreateBatchUpsertRequestBody")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var environmentURL = GetHeaderValue(req, EnvironmentURLHeader, true);
            if (environmentURL.EndsWith("/"))
                environmentURL = environmentURL.Remove(environmentURL.Length - 1, 1);

            var entityName = GetHeaderValue(req, EntityNameHeader, true);
            var barchSize = int.TryParse(GetHeaderValue(req, BatchSizeHeader, false), out int batch) && batch > 0
                ? batch : 100;

            var batchQueryParamsHeaderValue = GetHeaderValue(req, BatchQueryParamsHeader, false);
            var batchQueryParams = batchQueryParamsHeaderValue != null
                ? batchQueryParamsHeaderValue.Split(new char[] { ',' })
                : null;

            object[] batchBodyElements = DecodeRequestBody(req);

            List<string> result =
                GerenateBody(environmentURL, entityName, barchSize, batchQueryParams, batchBodyElements);

            return new OkObjectResult(result);
        }

        private static object[] DecodeRequestBody(HttpRequest req)
        {
            object[] batchBodyElements;
            using (Stream receiveStream = req.Body)
            {
                var bytes = ReadToEnd(receiveStream);
                var json = Encoding.Default.GetString(bytes);
                batchBodyElements = JsonConvert.DeserializeObject<object[]>(json);
            }

            return batchBodyElements;
        }

        private static List<string> GerenateBody(string environmentURL, string entityName, int barchSize, string[] batchQueryParams, object[] batchBodyElements)
        {
            var result = new List<string>();
            var body = new StringBuilder();
            var contentID = 1;
            for (int i = 0; i < batchBodyElements.Length; i++)
            {
                if (i % barchSize == 0)
                {
                    if (body.Length > 0)
                    {
                        body.Append(string.Format(elementFooter,
                            $"{changeSetNumber.ToString()}",
                            $"{entityName}"));

                        var bytes = Encoding.Default.GetBytes(body.ToString());
                        var decodedContent = Convert.ToBase64String(bytes);

                        result.Add(decodedContent);
                        body.Clear();
                        changeSetNumber = Guid.NewGuid();
                        contentID = 1;
                    }
                }
                AppendBatchElementBody(environmentURL, entityName, batchQueryParams, batchBodyElements, result, body, i, contentID++);
            }

            return result;
        }

        private static void AppendBatchElementBody(
            string environmentURL, 
            string entityName, 
            string[] batchQueryParams, 
            object[] batchBodyElements, 
            List<string> result, 
            StringBuilder body, 
            int index,
            int contentID)
        {
            if (body.Length == 0)
            {
                body.Append(
$@"--batch_{entityName}
Content-Type: multipart/mixed;boundary=changeset_{changeSetNumber.ToString()}
");
            }

            body.Append(
                string.Format(elementHeader,
                    changeSetNumber.ToString(),
                    contentID,
                    environmentURL,
                    entityName,
                    AddRequestParams(batchBodyElements[index] as JObject, batchQueryParams)));
            body.Append(ContentTypeHeader + Environment.NewLine);
            body.Append(batchBodyElements[index].ToString());
        }

        private static string AddRequestParams(JObject body, string[] parameters)
        {
            var result = new StringBuilder();
            if (parameters != null && parameters.Length > 0)
            {
                result.Append("(");
            }
            else
                return string.Empty;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (body[parameters[i]] != null)
                    result.Append($"{parameters[i]}='{body[parameters[i]].ToString()}',");
            }

            result.Remove(result.Length - 1, 1).Append(")");
            return result.ToString();
        }

        private static string GetHeaderValue(HttpRequest request, string headerName, bool throwException)
        {
            if (request.Headers[headerName] != StringValues.Empty &&
                request.Headers[headerName].Count > 0 &&
                !string.IsNullOrEmpty(request.Headers[headerName][0]))
            {
                return request.Headers[headerName][0];
            }

            if (throwException)
            {
                throw new MissingMemberException($"{headerName} is null or empty.");
            }

            return null;
        }

        public static byte[] ReadToEnd(Stream stream)
        {
            long originalPosition = 0;

            if (stream.CanSeek)
            {
                originalPosition = stream.Position;
                stream.Position = 0;
            }

            try
            {
                byte[] readBuffer = new byte[4096];

                int totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = stream.Read(readBuffer, totalBytesRead, readBuffer.Length - totalBytesRead)) > 0)
                {
                    totalBytesRead += bytesRead;

                    if (totalBytesRead == readBuffer.Length)
                    {
                        int nextByte = stream.ReadByte();
                        if (nextByte != -1)
                        {
                            byte[] temp = new byte[readBuffer.Length * 2];
                            Buffer.BlockCopy(readBuffer, 0, temp, 0, readBuffer.Length);
                            Buffer.SetByte(temp, totalBytesRead, (byte)nextByte);
                            readBuffer = temp;
                            totalBytesRead++;
                        }
                    }
                }

                byte[] buffer = readBuffer;
                if (readBuffer.Length != totalBytesRead)
                {
                    buffer = new byte[totalBytesRead];
                    Buffer.BlockCopy(readBuffer, 0, buffer, 0, totalBytesRead);
                }
                return buffer;
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = originalPosition;
                }
            }
        }
    }
}