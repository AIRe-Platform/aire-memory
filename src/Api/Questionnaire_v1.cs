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

namespace Aire.Memory.Api
{
    public class Questionnaire_v1
    {
        private readonly DatabaseContext _db;
        private readonly IJwtTokenService _jwt;
        private readonly IAireClientFactory _clientFactory;
        private readonly ILogger _log;

        public Questionnaire_v1(
            DatabaseContext db,
            IJwtTokenService jwt,
            IAireClientFactory clientFactory,
            ILogger<Questionnaire_v1> log)
        {
            _db = db;
            _jwt = jwt;
            _clientFactory = clientFactory;
            _log = log;
        }

        [Function("GetQuestionnaires_v1")]
        [OpenApiOperation(
            operationId: "getQuestionnaires",
            tags: ["questionnaire"],
            Summary = "Get a list of questionnaires")]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> GetQuestionnaires(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaires")] HttpRequest req,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
                return new UnauthorizedResult();

            var list = await _db.Questionnaires
                .ToListAsync();

            var questionnaires = new List<Questionnaire>();
            foreach (var item in list)
            {
                questionnaires.Add(item.ToModel());
            }

            return new ObjectResult(questionnaires);
        }

        [Function("GetQuestionnaireWithId_v1")]
        [OpenApiOperation(
            operationId: "getQuestionnaireWithId",
            tags: ["questionnaire"],
            Summary = "Retrieve a questionnaire")]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiParameter("id", Description = "Questionnaire identifier", In = ParameterLocation.Path, Required = true)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(List<Questionnaire>), Description = "List of questionnaires")]
        [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The questionnaire was not found.")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid parameter")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> GetQuestionnaireWithId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
                return new UnauthorizedResult();

            if (!Guid.TryParse(id, out Guid questionnaireId))
                return new BadRequestResult();

            var ent = await _db.Questionnaires
                .Where(x => x.Id == questionnaireId)
                .FirstOrDefaultAsync();

            if (ent == null)
                return new NotFoundResult();

            var questionnaire = ent.ToModel();

            return new ObjectResult(questionnaire);
        }

        [Function("QueryQuestionnaire_v1")]
        [OpenApiOperation(
            operationId: "queryQuestionnaire",
            tags: ["questionnaire"],
            Summary = "Query questionnaires"
        )]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
        [OpenApiParameter("query", 
            CollectionDelimiter = OpenApiParameterCollectionDelimiterType.Comma, 
            In = ParameterLocation.Query, 
            Required = true,
            Description = "List of keywords separated by commas")]
        [OpenApiParameter("lang",
            In = ParameterLocation.Query,
            Required = false,
            Description = "Set to return questionnaires in a specific language")]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Best matching questionnaire")]
        [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No results")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> QueryQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context,
            [FromQuery] string query,
            [FromQuery] string? lang = null)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
                return new UnauthorizedResult();

            if (string.IsNullOrWhiteSpace(query))
                return new BadRequestResult();

            var aiService = await _clientFactory.CreateAiClient(auth!.JwtEncodedToken);
            if (aiService == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            var queryWords = query.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var queryResponse = await aiService.QueryQuestionnaires(queryWords);

            if (queryResponse == null || queryResponse.Results == null)
                return new NotFoundResult();

            var questionnaireId = queryResponse.Results
                .Where(x => x.Relevance.HasValue && x.Relevance.Value > 0.7)
                .Where(x => string.IsNullOrEmpty(lang) || lang == x.Language)
                .Select(x => x.Source)
                .FirstOrDefault();

            if(questionnaireId == null)
                return new NotFoundResult();

            var entId = Guid.Parse(questionnaireId);
            var ent = await _db.Questionnaires
                .Where(x => x.Id == entId)
                .FirstOrDefaultAsync();

            if (ent == null)
                return new NotFoundResult();

            var questionnaire = ent.ToModel();

            return new ObjectResult(questionnaire);
        }


        [Function("PostQuestionnaire_v1")]
        [OpenApiOperation(
            operationId: "postQuestionnaire",
            tags: ["questionnaire"],
            Summary = "Store new questionnaire")]
        [OpenApiSecurity(
            schemeName: "bearer_auth",
            schemeType: SecuritySchemeType.Http,
            Scheme = OpenApiSecuritySchemeType.Bearer,
            BearerFormat = "JWT",
            Description = "User token")]
        [OpenApiRequestBody("application/json", typeof(Questionnaire), Description = "A questionnaire", Required = true)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Saved questionnaire")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Invalid body")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> PostQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire))
                return new UnauthorizedResult();

            var questionnaire = await req.ReadJson<Questionnaire>();
            if (questionnaire == null)
                return new BadRequestResult();

            var aiService = await _clientFactory.CreateAiClient(auth!.JwtEncodedToken);
            if (aiService == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }

            var entity = new QuestionnaireEntity(questionnaire);
            questionnaire.Id = entity.Id;

            {
                var embedResult = await aiService.EmbedQuestionnaire(questionnaire);
                var embedId = embedResult?.Ids?.FirstOrDefault();
                if (embedId == null)
                {
                    _log.LogCritical("Failed to create embeddings for the questionnaire");
                    return new InternalServerErrorResult();
                }
                entity.EmbeddingId = Guid.Parse(embedId);
            }

            var add = await _db.Questionnaires.AddAsync(entity);
            await add.Context.SaveChangesAsync();

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
        public async Task<IActionResult> DeleteQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/questionnaire/{id}")] HttpRequest req,
            FunctionContext context,
            string id)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.DeleteQuestionnaire))
                return new UnauthorizedResult();

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
}
