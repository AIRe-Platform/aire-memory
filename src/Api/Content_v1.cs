using System.Net;
using System.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Aire.Memory.Models;
using Aire.Sdk.AspNetCore;
using Aire.Sdk.Auth;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Platform.Clients;

namespace Aire.Memory.Api;

public class Content_v1
{
    private readonly DatabaseContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IAireClientFactory _clientFactory;
    private readonly ILogger _log;

    public Content_v1(
        DatabaseContext db,
        IJwtTokenService jwt,
        IAireClientFactory clientFactory,
        ILogger<Content_v1> log)
    {
        _db = db;
        _jwt = jwt;
        _clientFactory = clientFactory;
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
        /* var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
             return new ForbiddenResult();
        */
        var contents = new List<Content>();
        contents.Add( new Content{
            Name = "Juan",
            Description = "Description Juan",
            Hidden = false,
            Type = "image",
            URL = "/src/assets/images/meme.jpg",
            Age = "5-15",
            Gender = "all",
            Injured_type = "head",
            Views_count = 21,
            Viewers_rating = 4,
        });
        

        return new ObjectResult(contents);
    }

    

    [Function("PostContent_v1")]
    [OpenApiOperation(
        operationId: "postContent",
        tags: ["questionnaire"],
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
       /*  var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();
 */
        /* if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire))
            return new ForbiddenResult(); */

        var content = await req.ReadJson<Content>();
        if (content == null)
            return new BadRequestResult();

       
        return new ObjectResult(content);
    }


    [Function("PutQuestionnaire_v1")]
    [OpenApiOperation(
            operationId: "putQuestionnaire",
            tags: ["questionnaire"],
            Summary = "Edit existing questionnaire")]
    [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", Required = true)]
    [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "Questionnaire", Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Questionnaire")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body or param")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    public async Task<IActionResult> PutQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/questionnaire/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire))
            return new UnauthorizedResult();

        if (!Guid.TryParse(id, out Guid questionnaireId))
            return new BadRequestResult();

        var questionnaire = await req.ReadJson<Questionnaire>();
        if (questionnaire == null)
            return new BadRequestResult();

        var entity = await _db.Questionnaires
            .Where(x => x.Id == questionnaireId)
            .FirstOrDefaultAsync();

        if (entity == null)
            return new NotFoundResult();

        var questionnaireEntity = new QuestionnaireEntity(questionnaire);
        // Updating existing tracked entity in database with new entity with same id will cause error, so clear tracker.
        _db.ChangeTracker.Clear();

        var update = _db.Questionnaires.Update(questionnaireEntity);
        await update.Context.SaveChangesAsync();

        return new ObjectResult(questionnaire);
    }


    [Function("DeleteQuestionnaire_v1")]
    [OpenApiOperation(
        operationId: "deleteQuestionnaireWithId",
        tags: ["questionnaire"],
        Summary = "Delete a questionnaire")]
    [OpenApiSecurity(
        schemeName: "bearer_auth",
        schemeType: SecuritySchemeType.Http,
        Scheme = OpenApiSecuritySchemeType.Bearer,
        BearerFormat = "JWT",
        Description = "User token")]
    [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Operation was successful")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Forbidden, Description = "Access denied")]
    public async Task<IActionResult> DeleteQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/questionnaire/{id}")] HttpRequest req,
        FunctionContext context,
        string id)
    {
        var auth = context.Features.Get<JwtAuthFeature>();
        if (auth == null)
            return new UnauthorizedResult();

        if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteQuestionnaire))
            return new ForbiddenResult();

        if (!Guid.TryParse(id, out Guid questionnaireId))
            return new BadRequestResult();

        var ent = await _db.Questionnaires
            .Where(x => x.Id == questionnaireId)
            .FirstOrDefaultAsync();

        if (ent == null)
            return new NotFoundResult();

        if (ent.EmbeddingId != null)
        {
            var aiService = await _clientFactory.CreateAiClient(auth!.JwtEncodedToken);
            if (aiService == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            bool result = await aiService.DeleteQuestionnaireEmbedding(ent.EmbeddingId.ToString()!);
            if (!result)
            {
                _log.LogCritical("Failed to delete questionnaire embeddings");
                return new InternalServerErrorResult();
            }
        }

        _db.Questionnaires.Remove(ent);
        await _db.SaveChangesAsync();

        return new NoContentResult();
    }
}
