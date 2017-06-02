#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"
#load "../Shared/mediaServicesHelpers.csx"
#load "../Shared/copyBlobHelpers.csx"

using System;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Web;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.Azure.WebJobs;


// Read values from the App.config file.
private static readonly string _mediaServicesAccountName = Environment.GetEnvironmentVariable("AMSAccount");
private static readonly string _mediaServicesAccountKey = Environment.GetEnvironmentVariable("AMSKey");

static string _storageAccountName = Environment.GetEnvironmentVariable("MediaServicesStorageAccountName");
static string _storageAccountKey = Environment.GetEnvironmentVariable("MediaServicesStorageAccountKey");

// Field for service context.
private static CloudMediaContext _context = null;
private static MediaServicesCredentials _cachedCredentials = null;
private static CloudStorageAccount _destinationStorageAccount = null;

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info($"Webhook was triggered!");

    string jsonContent = await req.Content.ReadAsStringAsync();
    dynamic data = JsonConvert.DeserializeObject(jsonContent);

    log.Info(jsonContent);

    string locatorId = data.locatorId;

    if (data.assetId == null)
    {
        // for test
        // data.assetId = "nb:cid:UUID:c0d770b4-1a69-43c4-a4e6-bc60d20ab0b2";
        return req.CreateResponse(HttpStatusCode.BadRequest, new
        {
            error = "Please pass asset ID in the input object (assetId)"
        });
    }

    if (data.locatorId == null)
    {
        locatorId = Guid.NewGuid().ToString();
        log.Warning("No locatorId was provided for publishing the media asset, generating new Guid");
    }

    string playerUrl = "";
    string smoothUrl = "";
    string pathUrl = "";

    log.Info($"Using Azure Media Services account : {_mediaServicesAccountName}");

    try
    {
        // Create and cache the Media Services credentials in a static class variable.
        _cachedCredentials = new MediaServicesCredentials(
                        _mediaServicesAccountName,
                        _mediaServicesAccountKey);

        // Used the chached credentials to create CloudMediaContext.
        _context = new CloudMediaContext(_cachedCredentials);

        // Get the asset
        string assetid = data.assetId;
        var outputAsset = _context.Assets.Where(a => a.Id == assetid).FirstOrDefault();

        if (outputAsset == null)
        {
            log.Info($"Asset not found {assetid}");

            return req.CreateResponse(HttpStatusCode.BadRequest, new
            {
                error = "Asset not found"
            });
        }

        // The locatorId is unique so it must be removed from the old videoa sset, before it can be used to publish the new video asset
        ILocator locator = _context.Locators.SingleOrDefault(l => l.Id.IndexOf(locatorId, StringComparison.OrdinalIgnoreCase) >= 0);
        locator?.Delete();

        // publish with a streaming locator (10 years)
        IAccessPolicy readPolicy2 = _context.AccessPolicies.Create("readPolicy", TimeSpan.FromDays(365*10), AccessPermissions.Read);
        ILocator outputLocator2 = _context.Locators.CreateLocator(locatorId, LocatorType.OnDemandOrigin, outputAsset, readPolicy2, null);

        var publishurlsmooth = GetValidOnDemandURI(outputAsset);
        var publishurlpath = GetValidOnDemandPath(outputAsset);

        if (outputLocator2 != null && publishurlsmooth != null)
        {
            smoothUrl = publishurlsmooth.ToString();
            playerUrl = "http://ampdemo.azureedge.net/?url=" + System.Web.HttpUtility.UrlEncode(smoothUrl);
            log.Info($"smooth url : {smoothUrl}");
        }

        if (outputLocator2 != null && publishurlpath != null)
        {
            pathUrl = publishurlpath.ToString();
            log.Info($"path url : {pathUrl}");
        }
    }

    catch (Exception ex)
    {
        log.Info($"Exception {ex}");
        return req.CreateResponse(HttpStatusCode.InternalServerError, new
        {
            Error = ex.ToString()
        });
    }

    log.Info($"");
    return req.CreateResponse(HttpStatusCode.OK, new
    {
        playerUrl = playerUrl,
        smoothUrl = smoothUrl,
        pathUrl = pathUrl
    });
}
