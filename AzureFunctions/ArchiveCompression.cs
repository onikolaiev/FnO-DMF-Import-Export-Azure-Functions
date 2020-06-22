using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AzureFunctions
{
    public static class ArchiveCompression
    {
        [FunctionName("ArchiveCompression")]
        public static async Task<IActionResult> Run(
                    [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req, ILogger log)
        {
            log.LogInformation("Starting compress function.");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            dynamic data = JsonConvert.DeserializeObject(requestBody);
            log.LogInformation("Deserializing JSON.");

            List<ArchiveFile> files = new List<ArchiveFile>();
            var objs = data.files;

            foreach (var obj in objs)
            {
                ArchiveFile file = new ArchiveFile();

                file.Name = obj.name;
                file.Data = obj.content;


                var nameOfProperty = "insertIfEmpty";
                var propertyInfo = obj.GetType().GetProperty(nameOfProperty);
                if (propertyInfo != null)
                {
                    var value = propertyInfo.GetValue(obj, null);
                    file.InsertIfEmpty = value;
                }

                string temp = obj.name;
                int fileExtPos = temp.LastIndexOf(".");
                if (fileExtPos >= 0)
                {
                    file.Name = temp.Substring(0, fileExtPos);
                    file.Extension = temp.Substring(fileExtPos + 1);
                }

                files.Add(file);
            }

            byte[] arr = null;
            if (files.Count > 0)
            {
                arr = GeneratePackage(files, log);
            }

            if (arr != null)
            {
                return new FileContentResult(arr, "application/octet-stream");
            }
            else
            {
                return new BadRequestObjectResult("Zip archive was not created!");
            }
        }

        public static byte[] GeneratePackage(List<ArchiveFile> fileList, ILogger log)
        {
            byte[] result;
            log.LogInformation("Generating zip archive.");

            using (var packageStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, true))
                {
                    foreach (var virtualFile in fileList)
                    {
                        if (virtualFile.InsertIfEmpty == true ||
                            (virtualFile.InsertIfEmpty == false && !(virtualFile.Data == "" || virtualFile.Data == null)))
                        {
                            log.LogInformation($"Processing {virtualFile.Name} file.");
                            var zipFile = archive.CreateEntry(virtualFile.Name + "." + virtualFile.Extension);

                            byte[] array = Encoding.UTF8.GetBytes(virtualFile.Data);
                            using (var sourceFileStream = new MemoryStream(array))

                            using (var zipEntryStream = zipFile.Open())
                            {
                                sourceFileStream.CopyTo(zipEntryStream);
                            }
                        }
                    }
                }

                result = packageStream.ToArray();

            }
            return result;
        }

        public class ArchiveFile
        {
            public string Name;
            public string Extension;
            public string Data;
            public bool InsertIfEmpty;
        }
    }
}
