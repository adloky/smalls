using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace MvcApp.Controllers
{
    public class UtilsController : ApiController {

        [HttpPost]
        [Route("api/upload")]
        public IHttpActionResult Upload() {
            if (!Request.Content.IsMimeMultipartContent()) return BadRequest("Not multipart/form-data");

            var path = "d:/Projects/smalls/bins/uploads"; 
            var provider = new MultipartFormDataStreamProvider(path);
            Task.Run(() => Request.Content.ReadAsMultipartAsync(provider)).Wait();
            var fileData = provider.FileData.FirstOrDefault();

            if (fileData == null) return BadRequest("Not uploaded");

            var originalName = fileData.Headers.ContentDisposition.FileName.Trim('"');
            string ext = Path.GetExtension(originalName).ToLower();

            var fileInfo = new FileInfo(fileData.LocalFileName);
            if (fileInfo.Length > 5 * 1024 * 1024) {
                File.Delete(path);
                return BadRequest("Size > 5 Mb");
            }

            path = $"{path}/{Guid.NewGuid()}{ext}";
            File.Move(fileData.LocalFileName, path);

            var url = path.Replace("d:/Projects", "http://192.168.0.2");
            
            return Ok(new { url = url });
        }
    }
}
