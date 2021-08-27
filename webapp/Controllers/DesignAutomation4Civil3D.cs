/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Autodesk.Forge;
using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;

namespace forgeSample.Controllers
{
  public class DesignAutomation4Civil3D
  {
    protected string AppName { get; set; }
    protected string AppBundleName { get; set; }
    protected string ActivityName { get; set; }
    protected string ResultName { get; set; }
    protected string Script { get; set; }
    private const string ENGINE_NAME = "Autodesk.AutoCAD+24";

    /// NickName.AppBundle+Alias
    protected string AppBundleFullName { get { return string.Format("{0}.{1}+{2}", Utils.NickName, AppName, Alias); } }
    /// NickName.Activity+Alias
    protected string ActivityFullName { get { return string.Format("{0}.{1}+{2}", Utils.NickName, ActivityName, Alias); } }
    /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
    protected static string Alias { get { return "dev"; } }
    // Design Automation v3 API
    protected DesignAutomationClient _designAutomation;

    protected DesignAutomation4Civil3D()
    {
      // need to initialize manually as this class runs in background
      ForgeService service =
        new ForgeService(
          new HttpClient(
            new ForgeHandler(Microsoft.Extensions.Options.Options.Create(new ForgeConfiguration()
            {
              ClientId = Credentials.GetAppSetting("FORGE_CLIENT_ID"),
              ClientSecret = Credentials.GetAppSetting("FORGE_CLIENT_SECRET")
            }))
            {
              InnerHandler = new HttpClientHandler()
            })
          );
      _designAutomation = new DesignAutomationClient(service);
      //_designAutomation.Configuration.BaseAddress = new Uri("https://developer.api.autodesk.com/preview.da/us-east/");
    }

    public async Task ClearAccount()
    {
      await _designAutomation.DeleteForgeAppAsync("me");
    }

    public async Task EnsureAppBundle(string contentRootPath)
    {
      // get the list and check for the name
      Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();
      bool existAppBundle = false;
      foreach (string appName in appBundles.Data)
      {
        if (appName.Contains(AppBundleFullName))
        {
          existAppBundle = true;
          continue;
        }
      }

      if (!existAppBundle)
      {
        // check if ZIP with bundle is here
        string packageZipPath = Path.Combine(contentRootPath + "/bundles/", AppBundleName);
        if (!File.Exists(packageZipPath)) throw new Exception(AppBundleName + " not found at " + packageZipPath);

        AppBundle appBundleSpec = new AppBundle()
        {
          Package = AppName,
          Engine = ENGINE_NAME,
          Id = AppName,
          Description = string.Format("Description for {0}", AppBundleName),

        };
        AppBundle newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
        if (newAppVersion == null) throw new Exception("Cannot create new app");

        // create alias pointing to v1
        Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
        Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(AppName, aliasSpec);

        // upload the zip with .bundle
        RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
        RestRequest request = new RestRequest(string.Empty, Method.POST);
        request.AlwaysMultipartFormData = true;
        foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
        request.AddFile("file", packageZipPath);
        request.AddHeader("Cache-Control", "no-cache");
        await uploadClient.ExecuteTaskAsync(request);
      }
    }

    public async Task EnsureActivity()
    {
      Page<string> activities = await _designAutomation.GetActivitiesAsync();

      bool existActivity = false;
      foreach (string activity in activities.Data)
      {
        if (activity.Contains(ActivityFullName))
        {
          existActivity = true;
          continue;
        }
      }

      if (!existActivity)
      {
        string commandLine = string.Format("$(engine.path)\\accoreconsole.exe /i \"$(args[inputFile].path)\" /al \"$(appbundles[{0}].path)\" /s \"$(settings[script].path)\"", AppName);
        Activity activitySpec = new Activity()
        {
          Id = ActivityName,
          Appbundles = new List<string>() { AppBundleFullName },
          CommandLine = new List<string>() { commandLine },
          Engine = ENGINE_NAME,
          Parameters = new Dictionary<string, Parameter>()
          {
              { "inputFile", new Parameter() { Description = "Input Civil 3D File", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
              { "result", new Parameter() { Description = "Resulting File", LocalName = ResultName, Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } }
          },
          Settings = new Dictionary<string, ISetting>()
          {
              { "script", new StringSetting(){ Value = Script } }
          }
        };
        Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

        // specify the alias for this Activity
        Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
        Alias newAlias = await _designAutomation.CreateActivityAliasAsync(ActivityName, aliasSpec);
      }
    }

    protected async Task<XrefTreeArgument> BuildDownloadURL(string userAccessToken, string projectId, string versionId)
    {
      VersionsApi versionApi = new VersionsApi();
      versionApi.Configuration.AccessToken = userAccessToken;
      dynamic version = await versionApi.GetVersionAsync(projectId, versionId);
      dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

      string[] versionItemParams = ((string)version.data.relationships.storage.data.id).Split('/');
      string[] bucketKeyParams = versionItemParams[versionItemParams.Length - 2].Split(':');
      string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
      string objectName = versionItemParams[versionItemParams.Length - 1];
      string downloadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, objectName);

      return new XrefTreeArgument()
      {
        Url = downloadUrl,
        Verb = Verb.Get,
        Headers = new Dictionary<string, string>()
        {
            { "Authorization", "Bearer " + userAccessToken }
        }
      };
    }

    protected async Task<XrefTreeArgument> BuildUploadURL(string resultFilename)
    {
      BucketsApi buckets = new BucketsApi();
      dynamic token = await Credentials.Get2LeggedTokenAsync(new Scope[] { Scope.BucketCreate, Scope.DataWrite });
      buckets.Configuration.AccessToken = token.access_token;
      PostBucketsPayload bucketPayload = new PostBucketsPayload(Utils.BucketName, null, PostBucketsPayload.PolicyKeyEnum.Transient);
      try
      {
        await buckets.CreateBucketAsync(bucketPayload, "US");
      }
      catch { }

      ObjectsApi objects = new ObjectsApi();
      dynamic signedUrl = await objects.CreateSignedResourceAsyncWithHttpInfo(Utils.BucketName, resultFilename, new PostBucketsSigned(5), "readwrite");

      return new XrefTreeArgument()
      {
        Url = (string)(signedUrl.Data.signedUrl),
        Verb = Verb.Put
      };
    }

    protected async Task<WorkItemStatus> SubmitWorkitem(Credentials credentials, string projectId, string versionId, string resultFilename, string callbackUrl)
    {
      WorkItem workItemSpec = new WorkItem()
      {
        ActivityId = ActivityFullName,
        Arguments = new Dictionary<string, IArgument>()
        {
            { "inputFile", await BuildDownloadURL(credentials.TokenInternal, projectId, versionId) },
            { "result",  await BuildUploadURL(resultFilename) },
            { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
        }
      };
      return await _designAutomation.CreateWorkItemAsync(workItemSpec);
    }
  }

  public class ExtractStyles : DesignAutomation4Civil3D
  {
    public ExtractStyles()
    {
      AppName = "ExtractStyles";
      AppBundleName = "ExtractStyles.zip";
      ActivityName = "ExtractStylesActivity";
      ResultName = "result.json";
      Script = "extractStyles\n";
    }

    public async Task StartExtractStyles(Credentials credentials, string projectId, string versionId, string connectionId, string contentRootPath)
    {
      await EnsureAppBundle(contentRootPath);
      await EnsureActivity();

      string resultFilename = versionId.Base64Encode() + ".json";
      string callbackUrl = string.Format("{0}/api/forge/callback/designautomation/extractstyles/{1}/{2}", Credentials.GetAppSetting("FORGE_WEBHOOK_URL"), connectionId, resultFilename);

      await SubmitWorkitem(credentials, projectId, versionId, resultFilename, callbackUrl);
    }
  }
}