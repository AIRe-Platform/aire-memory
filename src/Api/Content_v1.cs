using System.Net;
using System.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Platform.Clients;
using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Azure;
namespace Aire.Memory.Api;


public class Content_v1
{
    private readonly ITableStorageService _storage;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger _log;
    private readonly BlobContainerClient _container;


    public Content_v1(
        IAzureClientFactory<BlobServiceClient> clientFactory,
        ITableStorageService storage,
        IJwtTokenService jwt,
        ILogger<Content_v1> log)
    {
        _container = clientFactory
            .CreateClient("blob-client")
            .GetBlobContainerClient("content-media");
       
        _container.CreateIfNotExists();
       
        _storage = storage;
        _jwt = jwt;
        _log = log;
       
    }

    [Function("GetContents_v1")]
    [OpenApiOperation(
        operationId: "getContents",
        tags: ["content"],
        Summary = "Get a list of contents")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Content>), Description = "List of contents")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> GetContents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/contents")] HttpRequest req,
        FunctionContext context)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadContent))
             return new ForbiddenResult();
        
        var all = await _storage.All<ContentEntity>();
        
        foreach (var contentEntity in all){

            if(contentEntity.BlobName != null){
                var blobSasBuilder = new BlobSasBuilder()
                {
                    BlobContainerName =  "content-media",//_container.BlobContainerName,
                    ExpiresOn = DateTime.UtcNow.AddMinutes(15),
                };
                BlobClient blobClient = _container.GetBlobClient(contentEntity.BlobName);
                blobSasBuilder.SetPermissions(BlobSasPermissions.Read | BlobSasPermissions.Write);
                
                var sasUri = blobClient.GenerateSasUri(blobSasBuilder);
                contentEntity.Url = sasUri.ToString();
            }
        }
        var list = all.Select(x => x.ToModel()).ToList();
        
        return new ObjectResult(list);
    }


    [Function("PostContent_v1")]
    [OpenApiOperation(
        operationId: "postContent",
        tags: ["content"],
        Summary = "Store new content")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiRequestBody("application/json", typeof(Content), Description = "A new content", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Saved content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> PostContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/content")] HttpRequest req,
        FunctionContext context)
    {
        
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();
        
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteContent))
            return new ForbiddenResult();

        var formData = await req.ReadFormAsync();

        if (formData == null)
            return new BadRequestResult();
        
        var blobName = Guid.NewGuid().ToString();
        // Get a reference to a blob with unique id
        BlobClient blobClient = _container.GetBlobClient(blobName);

        Console.WriteLine("Uploading to Blob storage as blob:\n\t {0}\n", blobClient.Uri);

        string URI = "";

        if(req.Form.Files.Count > 0 ){
            var file = req.Form.Files[0];

            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, true);
                URI = blobClient.Uri.AbsoluteUri;
            }
        }
        
        formData.TryGetValue("json", out var json);

        var content = json.ToString().JsonToObject<Content>();
 
        content.Url = URI;
        if (content != null)
        { 
            content.Id = Guid.NewGuid();
            var entity = new ContentEntity(content);
            entity.BlobName = blobClient.Name;
            var add = await _storage.UpsertAsync(entity);
            if (!add)
                return new InternalServerErrorResult(); 
        }
        else
        {
            return new InternalServerErrorResult();
        }

        return new ObjectResult(content);
    }


    [Function("PutContent_v1")]
    [OpenApiOperation(
            operationId: "putContent",
            tags: ["Content"],
            Summary = "Edit existing Content")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(Content), Description = "Content", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Content), Description = "Content")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The Content was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> PutContent(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/content/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteContent))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid contentId))
            return new BadRequestResult();
        
        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();

        var formData = await req.ReadFormAsync();

        if (formData == null)
            return new BadRequestResult();
        string blobName = "";
        formData.TryGetValue("json", out var json);      
        var content = json.ToString().JsonToObject<Content>();
        string URI = "";

        if(req.Form.Files.Count > 0 )
        {
            //remove the old media file, dosent matter if it is the same
            if(entity.BlobName != null)
                _container.DeleteBlobAsync(entity.BlobName);

            var file = req.Form.Files[0];

            using (var stream = file.OpenReadStream())
            {
                blobName = Guid.NewGuid().ToString();
                // Get a reference to a blob with unique id
                BlobClient blobClient = _container.GetBlobClient(blobName);
                await blobClient.UploadAsync(stream, true);
                entity.Url = blobClient.Uri.AbsoluteUri;
                entity.BlobName = blobName;
            }
        }
        

        // Update entity data
        if (content.Name != null)
            entity.Name = content.Name;

        if (content.Description != null)
            entity.Description =  content.Description;

        if (content.Hidden != null)
            entity.Hidden = content.Hidden;

        if (content.Type != null)
            entity.Type = content.Type;

        if (content.Url != null)
            entity.Url = content.Url;
        else{
            entity.Url = "";
            content.Url = "";
        }
            

        if (content.ViewsCount != null)
            entity.ViewsCount = content.ViewsCount;

        if (content.ViewersRating != null)
            entity.ViewersRating = content.ViewersRating;

        if (content.InjuredType != null)
            entity.InjuredType = content.InjuredType;
        
        if (content.Keywords != null)
            entity.Keywords = String.Join(",", content.Keywords);

        // Apply edits
        var save = await _storage.UpsertAsync(entity);
        if (!save)
            return new InternalServerErrorResult();

        return new ObjectResult(content);
    }


    [Function("DeleteContent_v1")]
    [OpenApiOperation(
        operationId: "deleteContentWithId",
        tags: ["content"],
        Summary = "Delete a content")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The content was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteContent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/content/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteContent))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid contentId))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();
    
        if(entity.BlobName != null)
            _container.DeleteBlobAsync(entity.BlobName);

        var delete = await _storage.DeleteAsync(entity);
        if (!delete)
            return new InternalServerErrorResult();
        
        return new NoContentResult();
    }



     [Function("DeleteMedia_v1")]
    [OpenApiOperation(
        operationId: "deleteMediaWithId",
        tags: ["content"],
        Summary = "Delete a Media")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Content identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The content was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteMedia(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/content-Media/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteContent))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid contentId))
            return new BadRequestResult();

        var entity = await _storage.RetrieveAsync<ContentEntity>(id);
        if (entity == null)
            return new NotFoundResult();
    
        if(entity.BlobName != null)
            _container.DeleteBlobAsync(entity.BlobName);

        return new NoContentResult();
    }
}


 /*
        if(file != null){
            //if there is file, we have to update the file
            if(entity.BlobName != null){
                await _container.DeleteBlobAsync(entity.BlobName);
            }
        }
        else //check if file is the same, if not remove old file
        {
            if(entity.BlobName != content.Url){
                await _container.DeleteBlobAsync(entity.BlobName);
            }
        }
        */