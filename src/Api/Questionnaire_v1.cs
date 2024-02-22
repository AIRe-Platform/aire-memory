using System.Net;
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
using Aire.Sdk.Auth.Models;
using Aire.Sdk.Auth.Scopes;
using Aire.Sdk.Auth.Services;
using Aire.Sdk.Models.Resources;
using Aire.Sdk.Platform;
using Aire.Sdk.AI;
using Aire.Sdk.Models.Platform;
using Aire.Sdk.Models.Identity;
using System.Web.Http;

namespace Aire.Memory.Api
{
    public class Questionnaire_v1
    {
        private readonly DatabaseContext _db;
        private readonly IJwtTokenService _jwt;
        private readonly IAirePlatformService _platformService;
        private readonly IAireAiService _aiService;
        private readonly ILogger _log;

        private readonly PlatformConfiguration _platform;

        public Questionnaire_v1(
            DatabaseContext db,
            IJwtTokenService jwt,
            IAirePlatformService platformService,
            IAireAiService aiService,
            ILogger<Questionnaire_v1> log)
        {
            _db = db;
            _jwt = jwt;
            _platformService = platformService;
            _aiService = aiService;
            _log = log;

            _platform = _platformService.GetPlatformConfiguration()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
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
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/questionnaires")] HttpRequest req,
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
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/questionnaire/{id}")] HttpRequest req,
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
        [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(Questionnaire), Description = "Best matching questionnaire")]
        [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No results")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "Missing query")]
        [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Missing or insufficient authorization")]
        public async Task<IActionResult> QueryQuestionnaire(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/questionnaire")] HttpRequest req,
            [FromQuery(Name = "query")] string query,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.ReadQuestionnaire))
                return new UnauthorizedResult();

            if (string.IsNullOrWhiteSpace(query))
                return new BadRequestResult();

            var module = _platform.GetDefaultModuleOfType(ModuleType.AI);
            if (module == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }
            _aiService.UseModule(module, new UserServiceCredentials { Token = auth!.JwtEncodedToken });

            var queryWords = query.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var results = await _aiService.QueryQuestionnaires(queryWords);
            var id = results?.Results?.FirstOrDefault();

            if (id == null)
                return new NotFoundResult();

            var questionnaireId = Guid.Parse(id);

            var ent = await _db.Questionnaires
                .Where(x => x.Id == questionnaireId)
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
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/questionnaire")] HttpRequest req,
            FunctionContext context)
        {
            var auth = context.Features.Get<JwtAuthFeature>();
            if (!_jwt.CheckAuthorization(auth, requiredScopes: AireScopes.WriteQuestionnaire))
                return new UnauthorizedResult();

            var questionnaire = await req.ReadJson<Questionnaire>();
            if (questionnaire == null)
                return new BadRequestResult();

            var module = _platform.GetDefaultModuleOfType(ModuleType.AI);
            if (module == null)
            {
                _log.LogCritical("Default AI module not configured");
                return new InternalServerErrorResult();
            }
            _aiService.UseModule(module, new UserServiceCredentials { Token = auth!.JwtEncodedToken });

            var entity = new QuestionnaireEntity(questionnaire);
            questionnaire.Id = entity.Id;

            {
                var embedResult = await _aiService.EmbedQuestionnaire(questionnaire);
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
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "v1/questionnaire/{id}")] HttpRequest req,
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
                var module = _platform.GetDefaultModuleOfType(ModuleType.AI);
                if (module == null)
                {
                    _log.LogCritical("Default AI module not configured");
                    return new InternalServerErrorResult();
                }
                _aiService.UseModule(module, new UserServiceCredentials { Token = auth!.JwtEncodedToken });

                bool result = await _aiService.DeleteQuestionnaireEmbedding(ent.EmbeddingId.ToString()!);
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
