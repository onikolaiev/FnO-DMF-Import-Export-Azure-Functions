using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Primitives;
using System.Linq;
using System;
using System.Xml.Linq;

namespace AzureFunctions
{
    public static class ParseFnOExportPackage
    {
        private const string FilesToSkipHeader = "FilesToSkip";
        private const string FilesContentFormatHeader = "FilesOutputContentFormat";

        [FunctionName("ParseFnOExportPackage")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            try
            {
                log.LogInformation("C# HTTP trigger function processed a request.");

                string json = "";
                var filesToSkipHeader = req.Headers?.Keys?.Contains(FilesToSkipHeader) == true
                    ? req.Headers[FilesToSkipHeader] : StringValues.Empty;
                var filesToSkip = filesToSkipHeader.Count > 0 ?
                        filesToSkipHeader.ToString().Split(",") : null;
                var filesContentFormatHeader = req.Headers?.Keys?.Contains(FilesContentFormatHeader) == true
                    ? req.Headers[FilesContentFormatHeader] : StringValues.Empty;
                var filesContentFormat = filesContentFormatHeader.Count > 0 ?
                         filesContentFormatHeader.ToString().Split(";") : null;

                log.LogInformation("Starting decompress function.");
                using (Stream receiveStream = req.Body)
                {
                    json = Decompress(receiveStream, filesToSkip, filesContentFormat, log);
                }
                if (json != "")
                {
                    return new JsonResult(json);
                }
                else
                {
                    return new BadRequestObjectResult("Decompression failed!");
                }
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.ToString());
            }

        }

        public static string Decompress(Stream _stream, string[] filesToSkip, string[] filesContentFormat, ILogger log)
        {
            List<FileContent> data = new List<FileContent>();
            string result = "";
            byte[] bytes;
            Stream fileStream;

            Files files = new Files();

            var filesNameAndFormat = ParseFilesContentFormat(filesContentFormat, log);

            using (ZipArchive archive = new ZipArchive(_stream, ZipArchiveMode.Read, true))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (filesToSkip != null && filesToSkip.Contains(entry.FullName))
                    {
                        log.LogInformation($"File {entry.FullName} was skipped.");
                        continue;
                    }

                    filesNameAndFormat.TryGetValue(entry.FullName, out var currentFileFormat);
                    if (string.IsNullOrEmpty(currentFileFormat))
                    {
                        log.LogInformation($"File {entry.FullName} was skipped.");
                        continue;
                    }

                    log.LogInformation($"Processing {entry.FullName} file.");

                    FileContent file = new FileContent();
                    bytes = null;
                    file.name = entry.FullName;
                    fileStream = entry.Open();
                    using (var ms = new MemoryStream())
                    {
                        fileStream.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    var xmlContent = Encoding.Default.GetString(bytes);

                    string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
                    if (xmlContent.StartsWith(_byteOrderMarkUtf8))
                        xmlContent = xmlContent.Remove(0, _byteOrderMarkUtf8.Length);

                    xmlContent = xmlContent.Replace(@"<?xml version=""1.0"" encoding=""utf-8""?>", "");
                    var doc = XElement.Parse(xmlContent);

                    var node_cdata = doc.DescendantNodes().OfType<XCData>().ToList();
                    foreach (var node in node_cdata)
                    {
                        if (string.IsNullOrEmpty(node.Value))
                        {

                        }
                        node.Parent.Add(node.Value);

                        node.Remove();
                    }

                    var convertedContent = "";
                    switch (currentFileFormat)
                    {
                        case "json":
                            {
                                convertedContent = JsonConvert.SerializeXNode(doc, Formatting.None, false);
                                break;
                            }
                        case "xml":
                            {
                                convertedContent = doc.ToString();
                                break;
                            }
                        case "csv":
                            {
                                convertedContent = ConvertXElementToCsvString(doc);
                                break;
                            }

                        default:
                            break;
                    }

                    bytes = Encoding.Default.GetBytes(convertedContent);
                    file.content = Convert.ToBase64String(bytes);

                    result += entry.FullName;
                    files.files.Add(file);
                }
            }
            result = JsonConvert.SerializeObject(files);

            return result;
        }

        private static Dictionary<string, string> ParseFilesContentFormat(string[] filesContentFormat, ILogger log)
        {
            var result = new Dictionary<string, string>();

            foreach (var fileContentFormat in filesContentFormat)
            {
                var details = fileContentFormat.Split(",");

                if (!string.IsNullOrEmpty(details[0]))
                {
                    var fileName = details[0];
                    var outputFormat = details.Count() > 1
                            ? details[1] : "xml";

                    result.Add(fileName, outputFormat);
                }
            }

            return result;
        }

        private static string ConvertXElementToCsvString(XElement doc)
        {
            var sb = new StringBuilder();
            var headerRow = true;

            foreach (XElement element in doc.Elements())
            {
                if (headerRow)
                {
                    string[] headers = element.Elements().Select(x => x.Name.LocalName).ToArray();
                    sb.AppendLine(string.Join(",", headers));
                    headerRow = false;
                }
                string[] row = element.Elements().Select(x => (string)x).ToArray();
                sb.AppendLine(string.Join(",", row));
            }
            return sb.ToString();
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