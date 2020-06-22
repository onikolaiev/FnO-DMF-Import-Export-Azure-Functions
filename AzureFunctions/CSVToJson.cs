using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AzureFunctions
{
    public static class CSVToJson
    {
        [FunctionName("CSVToJson")]
        public static async Task<IActionResult> Run(
                    [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, ILogger log)
        {
            List<string[]> csvList = new List<string[]>();
            List<string> csvList2 = new List<string>();
            string json = "";
            string csv = "";

            using (Stream receiveStream = req.Body)
            {
                StreamReader reader = new StreamReader(receiveStream);
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    csvList.Add(line.Split(','));

                    //used for 1 column

                    //csvList.Add(new string[]{line});
                    if (csvList.Count > 1)
                    {
                        csv += line + "\r\n";
                    }
                }
            }
            var data = CsvToJson(csv, csvList[0]);
            return (ActionResult)new OkObjectResult(data);
        }

        public static List<object> CsvToJson(string body, string[] column)
        {
            if (string.IsNullOrEmpty(body)) return null;

            string[] rowSeparators = new string[] { "\r\n" };
            string[] rows = body.Split(rowSeparators, StringSplitOptions.None);
            body = null;

            if (rows == null || (rows != null && rows.Length == 0)) return null;
            string[] cellSeparator = new string[] { "," };//\r\n
            List<object> data = new List<object>();

            int clen = column.Length;
            rows.Select(row =>
            {
                if (string.IsNullOrEmpty(row)) return row;
                string[] cells = row.Trim().Split(cellSeparator, StringSplitOptions.None);
                if (cells == null) return row;
                if (cells.Length < clen) return row;

                Dictionary<object, object> jrows = new Dictionary<object, object>();
                for (int i = 0; i < clen; i++)
                {
                    jrows.Add(column[i], cells[i]?.Trim());
                }

                data.Add(jrows);

                return row;

            }).ToList();

            rowSeparators = null; rows = null;
            cellSeparator = null;
            return data;
        }
    }
}