using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO.Compression;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AzureFunctions
{
    public static class Decompress
    {
        [FunctionName("Decompress")]
        public static async Task<IActionResult> Run(
             [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, ILogger log)
        {
            string json = "";
            log.LogInformation("Starting decompress function.");

            using (Stream receiveStream = req.Body)
            {
                json = DecompressData(receiveStream, log);
            }

            if (json == "")
            {
                return new BadRequestObjectResult("Decompression failed!");
            }
            else
            {
                return new JsonResult(json);//JsonResult//ObjectResult
            }
        }

        public static string DecompressData(Stream _stream, ILogger log)
        {
            List<FileContent> data = new List<FileContent>();
            string result = "";

            byte[] bytes;
            Stream fileStream;
            Files files = new Files();

            using (ZipArchive archive = new ZipArchive(_stream, ZipArchiveMode.Read, true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    log.LogInformation($"Processing {entry.FullName} file.");

                    //if (entry.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))//If need to set extension
                    {

                        FileContent file = new FileContent();
                        bytes = null;
                        file.name = entry.FullName;
                        fileStream = entry.Open();

                        using (var ms = new MemoryStream())
                        {
                            fileStream.CopyTo(ms);
                            bytes = ms.ToArray();
                        }

                        file.content = Convert.ToBase64String(bytes);
                        result += entry.FullName;
                        files.files.Add(file);
                    }
                }
            }

            result = JsonConvert.SerializeObject(files);

            return result;
        }
        public class Files
        {
            public List<FileContent> files = new List<FileContent>();
        }

        public class FileContent
        {
            public string name { get; set; }
            public string content { get; set; }
        }
    }
}