using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.DesignAutomation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json.Linq;
using BlockConfigurator.Models;
using System.Reflection.Metadata.Ecma335;
using Autodesk.Oss.Model;
using System.Net;
using Microsoft.Extensions.Options;
using System.Reflection.Emit;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System;

namespace BlockConfigurator.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DesignAutomationController : ControllerBase
    {
        // Used to access the application folder (temp location for files & bundles)
        private IWebHostEnvironment _env;
        // used to access the SignalR Hub
        private IHubContext<DesignAutomationHub> _hubContext;      
        // Local folder for bundles
        public string LocalBundlesFolder { get { return Path.Combine(_env.WebRootPath, "bundles"); } }
        /// Prefix for AppBundles and Activities
        public static string NickName { get { return "blkconfig"; } }
        /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
        public static string Alias { get { return "dev"; } }
      
        // Design Automation v3 API
        private readonly DesignAutomationClient _designAutomation;
        public readonly APS _aps;
        // Constructor, where env and hubContext are specified
        public DesignAutomationController(IWebHostEnvironment env, IHubContext<DesignAutomationHub> hubContext, DesignAutomationClient api, APS aps)
        {
            _designAutomation = api;
            _env = env;
            _hubContext = hubContext;
            _aps = aps;           
        }

        // **********************************
        //
        // Next we will add the methods here
        //
        // **********************************


        /// <summary>
        /// Names of app bundles on this project
        /// </summary>
        [HttpGet("apps")]
       
        public string?[] GetLocalBundles()
        {
            // this folder is placed under the public folder, which may expose the bundles
            // but it was defined this way so it be published on most hosts easily
            if (Directory.Exists(LocalBundlesFolder))
            {
                var files = Directory.GetFiles(LocalBundlesFolder, "*.zip");
                if (files.Length == 0)
                    return ["No bundles available"];
                if(files is null) throw new NullReferenceException();
                return files.Select(Path.GetFileNameWithoutExtension).ToArray();

            }
            else
                return [];
        }
               
           

        /// <summary>
        /// Return a list of available engines
        /// </summary>
        [HttpGet("engines")]     
        public async Task<List<string>> GetAvailableEngines()
        {
            List<string> allEngines = [];
            // define Engines API
            string? paginationToken = null;
            while (true)
            {
                Page<string> engines = await _designAutomation.GetEnginesAsync(paginationToken);
                allEngines.AddRange(engines.Data);
                if (engines.PaginationToken == null)
                    break;
                paginationToken = engines.PaginationToken;
            }
            allEngines.Sort();
            return allEngines; // return list of engines
        }

        /// <summary>
        /// Define a new appbundle
        /// </summary>
        [HttpPost("appbundles")]
        public async Task<IActionResult> CreateAppBundle([FromBody] JObject appBundleSpecs)
        {

            await SetupOwnerAsync();          
            // basic input validation
            string? zipFileName = appBundleSpecs.Value<string>("zipFileName");
            string? engineName = appBundleSpecs.Value<string>("engine");
            // standard name for this sample
            string? appBundleName = zipFileName;
            // check if ZIP with bundle is here
            string packageZipPath = Path.Combine(LocalBundlesFolder, zipFileName + ".zip");
            if (!System.IO.File.Exists(packageZipPath)) throw new Exception("Appbundle not found at " + packageZipPath);
            string qualifiedAppBundleId = string.Format("{0}.{1}+{2}", NickName, appBundleName, Alias);

            var appResponse = await _designAutomation.AppBundlesApi.GetAppBundleAsync(qualifiedAppBundleId, throwOnError: false);
            var app = new AppBundle()
            {
                Engine = engineName,
                Id = appBundleName
            };
            int version = 1;
            if (appResponse.HttpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"\tCreating appbundle {qualifiedAppBundleId}...");
                await _designAutomation.CreateAppBundleAsync(app, Alias, packageZipPath);
                return Ok(new { AppBundle = qualifiedAppBundleId, Version = version });
            }
            await appResponse.HttpResponse.EnsureSuccessStatusCodeAsync();
            Console.WriteLine("\tFound existing appbundle...");
            if (!await EqualsAsync(packageZipPath, appResponse.Content.Package))
            {
                Console.WriteLine($"\tUpdating appbundle {qualifiedAppBundleId}...");
                version = await _designAutomation.UpdateAppBundleAsync(app, Alias, packageZipPath);
            }
            return Ok(new { AppBundle = qualifiedAppBundleId, Version = version });

            async Task<bool> EqualsAsync(string a, string b)
            {
                Console.Write("\tComparing bundles...");
                using var aStream = System.IO.File.OpenRead(a);
                var bLocal = await DownloadToDocsAsync(b, "das-appbundle.zip");
                using var bStream = System.IO.File.OpenRead(bLocal);
                using var hasher = SHA256.Create();
                var res = hasher.ComputeHash(aStream).SequenceEqual(hasher.ComputeHash(bStream));
                Console.WriteLine(res ? "Same." : "Different");
                return res;
            }

        }

        /// <summary>
        /// Helps identify the engine
        /// </summary>
        private dynamic EngineAttributes(string engine)
        {
            if (engine.Contains("AutoCAD")) return new { commandLine = "$(engine.path)\\accoreconsole.exe /i \"$(args[inputFile].path)\" /al \"$(appbundles[{0}].path)\" /s $(settings[script].path)", extension = "dwg", script = "UpdateParam\n" };
            throw new Exception("Invalid engine");
        }

        /// <summary>
        /// Define a new activity
        /// </summary>
        [HttpPost("activities")]       
        public async Task<IActionResult> CreateActivity([FromBody] JObject activitySpecs)
        {
           
            // basic input validation
            string? zipFileName = activitySpecs.Value<string>("zipFileName");
            string? engineName = activitySpecs.Value<string>("engine");
            int version = 1;
            // standard name for this sample
            string? appBundleName = zipFileName;
            string? activityName = $"{zipFileName}activity";
            string qualifiedActivityId = string.Format("{0}.{1}+{2}", NickName, activityName, Alias);
            var activityResp = await _designAutomation.ActivitiesApi.GetActivityAsync(qualifiedActivityId, throwOnError: false);
            Activity activitySpec = GetUpdateParamActivity(engineName, appBundleName, activityName);
            if (activityResp.HttpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"\tCreating activity {qualifiedActivityId}...");                
                await _designAutomation.CreateActivityAsync(activitySpec, Alias);
                return Ok(new { Activity = qualifiedActivityId });
            }

            Console.WriteLine($"\tFound existing activity {qualifiedActivityId}...");
            await activityResp.HttpResponse.EnsureSuccessStatusCodeAsync();
            if (!Equals(activitySpec, activityResp.Content))
            {
                Console.WriteLine($"\tUpdating activity {qualifiedActivityId}...");
                await _designAutomation.UpdateActivityAsync(activitySpec, Alias);
            }
            return Ok(new { Activity = qualifiedActivityId, Version = version });

            bool Equals(Autodesk.Forge.DesignAutomation.Model.Activity a, Autodesk.Forge.DesignAutomation.Model.Activity b)
            {
                Console.Write("\tComparing activities...");
                //ignore id and version
                b.Id = a.Id;
                b.Version = a.Version;
                var res = a.ToString() == b.ToString();
                Console.WriteLine(res ? "Same." : "Different");
                return res;
            }
            
        }

        private Activity GetUpdateParamActivity(string? engineName, string? appBundleName, string? activityName)
        {
            dynamic engineAttributes = EngineAttributes(engineName ?? string.Empty);
            string commandLine = string.Format(engineAttributes.commandLine, appBundleName);
            Activity activitySpec = new Activity()
            {
                Id = activityName,
                Appbundles = [string.Format("{0}.{1}+{2}", NickName, appBundleName, Alias)],
                CommandLine = [commandLine],
                Engine = engineName,
                Parameters = new Dictionary<string, Parameter>()
                    {
                        { "inputFile", new Parameter() { Description = "input file", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "inputJson", new Parameter() { Description = "input json", LocalName = "params.json", Ondemand = false, Required = false, Verb = Verb.Get, Zip = false } },
                        { "outputFile", new Parameter() { Description = "output file", LocalName = "outputFile." + engineAttributes.extension, Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } }
                     },
                Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting(){ Value = engineAttributes.script } }
                     }
            };
            return activitySpec;
        }

        /// <summary>
        /// Get all Activities defined for this account
        /// </summary>
        [HttpGet("activities")]      
        public async Task<List<string>> GetDefinedActivities()
        {
            // filter list of 
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            List<string> definedActivities = new List<string>();
            foreach (string activity in activities.Data)
                if (activity.StartsWith(NickName) && activity.IndexOf("$LATEST") == -1)
                    definedActivities.Add(activity.Replace(NickName + ".", string.Empty));

            return definedActivities;
        }


        /// <summary>
        /// Start a new workitem
        /// </summary>
        [HttpPost("workitems")]        
        public async Task<IActionResult> StartWorkitem([FromForm] StartWorkitemInput input)
        {

           
            // basic input validation
            if (input.InputFile == null) throw new Exception("Missing inputFile");
            if (string.IsNullOrWhiteSpace(input.Data)) throw new Exception("Missing data");

            JObject workItemData = JObject.Parse(input.Data);

            string? widthParam = workItemData.Value<string>("width");
            string? heigthParam = workItemData.Value<string>("height");
            string? activityName = workItemData.Value<string>("activityName");
            string qualifiedActivityId = $"{NickName}.{activityName}";

            string? browserConnectionId = workItemData.Value<string>("browserConnectionId");

            // save the file on the server
            var fileSavePath = Path.Combine(_env.ContentRootPath, Path.GetFileName(input.InputFile.FileName));
            using (var stream = new FileStream(fileSavePath, FileMode.Create))
            { 
                await input.InputFile.CopyToAsync(stream); 
            }

            // OAuth token
            dynamic oauth = await _aps.GetInternalToken(); ;

            // avoid overriding
            string inputFileNameOSS = $"input_{Path.GetFileName(input.InputFile.FileName)}";

            dynamic inputFileDetails = await _aps.GetObjectId(inputFileNameOSS, fileSavePath, false);

            // prepare workitem arguments
            // 1. input file
            XrefTreeArgument inputFileArgument = new XrefTreeArgument()
            {
                Url = inputFileDetails.ObjectId,
                Headers = new Dictionary<string, string>(){
                    { "Authorization", "Bearer " + oauth.AccessToken} }
            };

            // 2. input json
            var inputJson = new JObject
            {
                { "Width", widthParam },
                { "Height", heigthParam }
            };
            var url = "data:application/json, " + ((JObject)inputJson).ToString(Formatting.None).Replace("\"", "'");


            XrefTreeArgument inputJsonArgument = new XrefTreeArgument()
            {
                Url = url
            };
            // 3. output file
            string outputFileNameOSS = $"{DateTimeOffset.Now.ToUnixTimeMilliseconds()}_output_{Path.GetFileName(input.InputFile.FileName)}";    
            dynamic outputFileDetails = await _aps.GetObjectId(outputFileNameOSS, _env.ContentRootPath, true);
            XrefTreeArgument outputFileArgument = new XrefTreeArgument()
            {
                Url = outputFileDetails.ObjectId,
                Headers = new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + oauth.AccessToken}
                },
                Verb = Verb.Put
            };

            if (System.IO.File.Exists(fileSavePath))
            {
                System.IO.File.Delete(fileSavePath);
            }

            // prepare & submit workitem            
            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = qualifiedActivityId,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "inputFile", inputFileArgument },
                    { "inputJson",  inputJsonArgument },
                    { "outputFile", outputFileArgument },
                    {
                        "adskDebug", new DebugArgument()
                        {
                            UploadJobFolder = true
                        }
                    }

                }
            };
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemAsync(workItemSpec);
            _ = MonitorWorkitem(oauth, browserConnectionId, workItemStatus, outputFileDetails);
            return Ok(new { WorkItemId = workItemStatus.Id });
        }

        private async Task MonitorWorkitem(dynamic oauth, string browserConnectionId, WorkItemStatus workItemStatus, ObjectDetails obj)
        {
            try
            {

                while (!workItemStatus.Status.IsDone())
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    workItemStatus = await _designAutomation.GetWorkitemStatusAsync(workItemStatus.Id);
                    await _hubContext.Clients.Client(browserConnectionId).SendAsync("onComplete", workItemStatus.ToString());
                }
                using (var httpClient = new HttpClient())
                {
                    byte[] bs = await httpClient.GetByteArrayAsync(workItemStatus.ReportUrl);
                    string report = System.Text.Encoding.Default.GetString(bs);
                    await _hubContext.Clients.Client(browserConnectionId).SendAsync("onComplete", report);
                }

                if (workItemStatus.Status == Status.Success)
                {
                    var dlink = await _aps.GetSignedS3DownloadLink(obj.ObjectKey);
                    await _hubContext.Clients.Client(browserConnectionId).SendAsync("downloadResult", dlink);
                    Console.WriteLine("Congrats!");

                    var job = await _aps.TranslateModel(obj.ObjectId, string.Empty);
                    Console.WriteLine("Translation job submitted: " + job.Urn);
                    var status = await _aps.GetTranslationStatus(job.Urn);                    

                    while (status.Progress != "complete")
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        status = await _aps.GetTranslationStatus(job.Urn);
                    }
                    await _hubContext.Clients.Client(browserConnectionId).SendAsync("onComplete", "Translation job completed.");
                    await _hubContext.Clients.Client(browserConnectionId).SendAsync("onTranslation", job.Urn);
                }

            }
            catch (Exception ex)
            {
                await _hubContext.Clients.Client(browserConnectionId).SendAsync("onComplete", ex.Message);
                Console.WriteLine(ex.Message);
            }
        }
        private async Task<bool> SetupOwnerAsync()
        {
            Console.WriteLine("Setting up owner...");

            var nickname = await _designAutomation.GetNicknameAsync("me");
            if (nickname == _aps.GetClientId())
            {
                Console.WriteLine("\tNo nickname for this clientId yet. Attempting to create one...");
                HttpResponseMessage resp;
                resp = await _designAutomation.ForgeAppsApi.CreateNicknameAsync("me", new NicknameRecord() { Nickname = NickName }, throwOnError: false);
                if (resp.StatusCode == HttpStatusCode.Conflict)
                {
                    Console.WriteLine("\tThere are already resources associated with this clientId or nickname is in use. Please use a different clientId or nickname.");
                    return false;
                }
                await resp.EnsureSuccessStatusCodeAsync();
            }
            return true;
        }
        public async Task<string> DownloadToDocsAsync(string url, string fn)
        {
            string zipFile = Path.Combine(_env.ContentRootPath, fn) ;
            if (System.IO.File.Exists(zipFile))
            {
                System.IO.File.Delete(zipFile);
            }

            using var client = new HttpClient();           
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using (var fs = new FileStream(zipFile, FileMode.CreateNew))
            {
                await response.Content.CopyToAsync(fs);
            }

            return zipFile;
        }





        /// <summary>
        /// Clear the accounts (for debugging purposes)
        /// </summary>
        [HttpDelete("account")]      
        public async Task<IActionResult> ClearAccount()
        {
            // clear account
            await _designAutomation.DeleteForgeAppAsync("me");
            return Ok();
        }



        /// <summary>
        /// Input for StartWorkitem
        /// </summary>
        public class StartWorkitemInput
        {
            public IFormFile? InputFile { get; set; }
            public string? Data { get; set; }
        }
    }
    public class DesignAutomationHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public string GetConnectionId() { return Context.ConnectionId; }
    }
    public class DebugArgument : IArgument
    {
        [DataMember(Name = "uploadJobFolder", EmitDefaultValue = false)]
        public bool UploadJobFolder { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}
