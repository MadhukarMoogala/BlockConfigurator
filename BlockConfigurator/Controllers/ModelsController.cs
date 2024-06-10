using BlockConfigurator.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlockConfigurator.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ModelsController : ControllerBase
    {
        public record BucketObject(string name, string urn);
        private readonly APS _aps;
        public ModelsController(APS aps)
        {
            _aps = aps;
        }
        [HttpGet()]
        public async Task<IEnumerable<BucketObject>> GetModels()
        {
            var objects = await _aps.GetObjects();
            return from o in objects
                   select new BucketObject(o.ObjectKey, APS.Base64Encode(o.ObjectId));
        }

        [HttpGet("{urn}/status")]
        public async Task<TranslationStatus> GetModelStatus(string urn)
        {
            var status = await _aps.GetTranslationStatus(urn);
            if (status.Status.Equals("n/a"))
            {
                await _aps.TranslateModel(urn);
            }

            while (status.Progress != "complete")
            {
                await Task.Delay(2000);
                status = await _aps.GetTranslationStatus(urn);
            }
            return status;
        }
        public class UploadModelForm
        {
            [FromForm(Name = "model-zip-entrypoint")]
            public string? Entrypoint { get; set; }

            [FromForm(Name = "model-file")]
            public IFormFile? File { get; set; }
        }

        [HttpPost(), DisableRequestSizeLimit]
        public async Task<BucketObject> UploadAndTranslateModel([FromForm] UploadModelForm form)
        {
            if (form.File == null)
            {
                throw new ArgumentException("No file uploaded");
            }
            var tempFilePath = Path.GetTempFileName();
            using (var stream = System.IO.File.Create(tempFilePath))
            {
                await form.File.CopyToAsync(stream);
            }
            var obj = await _aps.UploadModel(form.File.FileName, tempFilePath);
            var job = await _aps.TranslateModel(obj.ObjectId, form.Entrypoint ?? string.Empty);
            return new BucketObject(obj.ObjectKey, job.Urn);
        }
    }
}
